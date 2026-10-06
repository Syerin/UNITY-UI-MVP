#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(UIRebuildTracker))]
public class UIRebuildTrackerEditor : Editor
{
	private static readonly Vector3[] WorldCorners = new Vector3[4];
	private static GUIStyle _badgeStyle;

	// 새 하이어라키(UI Toolkit)용 배지
	private const string BadgeElementName = "ui-rebuild-tracker-badge";
	private static readonly HashSet<Label> _boundBadges = new HashSet<Label>();
	private static readonly List<Label> _deadBadges = new List<Label>();
	private static bool _hadDataLastFrame;

	[InitializeOnLoadMethod]
	private static void InitEditorEvents()
	{
		SceneView.duringSceneGui += OnSceneGUIGlobal;

		// [레거시 하이어라키] Project Settings > Editor > Hierarchy > Use Legacy Hierarchy 켰을 때
		// Unity 6.6 에서 instanceID(int) 계열이 EntityId 계열로 바뀌었다.
		// 옛 hierarchyWindowItemOnGUI 는 obsolete 를 넘어 컴파일 에러다.
		EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnLegacyHierarchyGUI;

		// [새 하이어라키] UI Toolkit 기반 — IMGUI 콜백이 호출되지 않으므로
		// 각 행(HierarchyViewItem)에 Label 배지를 붙였다 뗐다 한다.
		HierarchyWindow.BindViewItem += OnBindViewItem;
		HierarchyWindow.UnbindViewItem += OnUnbindViewItem;

		// 매 프레임 하이어라키 창을 다시 그려 잔상이 남지 않게 한다.
		EditorApplication.update += OnEditorUpdate;

		// 도메인 리로드 전에 이미 그려져 있던 행에도 배지를 붙인다.
		EditorApplication.delayCall += AttachToExistingItems;
	}

	private static void OnEditorUpdate()
	{
		var tracker = UIRebuildTracker.Instance;
		bool hasData = tracker != null && tracker.ActiveData.Count > 0;

		if (hasData)
		{
			EditorApplication.RepaintHierarchyWindow();
		}

		// 데이터가 있거나, 방금 사라진 프레임이면 새 하이어라키 배지 갱신(숨김 처리 포함)
		if (hasData || _hadDataLastFrame)
		{
			UpdateAllBadges();
		}
		_hadDataLastFrame = hasData;
	}

	private static void OnSceneGUIGlobal(SceneView sceneView)
	{
		var tracker = UIRebuildTracker.Instance;
		if (tracker == null || tracker.ActiveData.Count == 0) return;

		foreach (var (rect, info) in tracker.ActiveData)
		{
			if (rect == null || info.Intensity <= 0.01f) continue;

			rect.GetWorldCorners(WorldCorners);

			Color outlineColor = info.Type == UIRebuildTracker.RebuildType.Layout ? Color.red : Color.yellow;
			outlineColor.a = info.Intensity;

			Handles.DrawSolidRectangleWithOutline(WorldCorners, new Color(0, 0, 0, 0), outlineColor);
		}

		sceneView.Repaint();
	}

	// ------------------------------------------------------------------------
	// 공통
	// ------------------------------------------------------------------------
	private static bool TryGetInfo(GameObject go, out UIRebuildTracker.RebuildInfo info)
	{
		info = null;
		var tracker = UIRebuildTracker.Instance;
		if (tracker == null || tracker.ActiveData.Count == 0 || go == null) return false;
		if (!go.TryGetComponent<RectTransform>(out var rect)) return false;
		if (!tracker.ActiveData.TryGetValue(rect, out info)) return false;

		// 강도가 0 이면 그리지 않고 빠진다 — 잔상 방지.
		return info.Intensity > 0.01f;
	}

	private static string GetBadgeText(UIRebuildTracker.RebuildInfo info)
	{
		return info.Type == UIRebuildTracker.RebuildType.Layout ? "LAYOUT" : "GRAPHIC";
	}

	private static Color GetBadgeColor(UIRebuildTracker.RebuildInfo info)
	{
		return info.Type == UIRebuildTracker.RebuildType.Layout
			? new Color(0.9f, 0.1f, 0.2f, info.Intensity)
			: new Color(0.9f, 0.75f, 0.0f, info.Intensity);
	}

	// ------------------------------------------------------------------------
	// 레거시 하이어라키 (IMGUI)
	// ------------------------------------------------------------------------
	private static void OnLegacyHierarchyGUI(EntityId entityId, Rect selectionRect)
	{
		var go = EditorUtility.EntityIdToObject(entityId) as GameObject;
		if (!TryGetInfo(go, out var info)) return;

		if (_badgeStyle == null)
		{
			_badgeStyle = new GUIStyle(EditorStyles.miniButton)
			{
				fontSize = 10,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter
			};
		}

		Rect badgeRect = new Rect(selectionRect.xMax - 75, selectionRect.y, 70, selectionRect.height);

		Color prevColor = GUI.color;
		GUI.color = GetBadgeColor(info);
		GUI.Box(badgeRect, GetBadgeText(info), _badgeStyle);
		GUI.color = prevColor;
	}

	// ------------------------------------------------------------------------
	// 새 하이어라키 (UI Toolkit)
	// ------------------------------------------------------------------------
	private static void AttachToExistingItems()
	{
		foreach (var window in Resources.FindObjectsOfTypeAll<HierarchyWindow>())
		{
			if (window == null || window.rootVisualElement == null) continue;
			window.rootVisualElement.Query<HierarchyViewItem>().ForEach(item => OnBindViewItem(window, window.View, item));
		}
	}

	private static void OnBindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
	{
		if (item == null || item.RightCustomContainer == null) return;

		var badge = item.RightCustomContainer.Q<Label>(BadgeElementName);

		// GameObject 행이 아니면(씬 헤더 등) 배지 제거
		if (!(item.Handler is HierarchyGameObjectHandler goHandler))
		{
			RemoveBadge(badge);
			return;
		}

		if (badge == null)
		{
			badge = CreateBadge();
			item.RightCustomContainer.Add(badge);
		}

		badge.userData = goHandler.GetGameObject(item.Node);
		_boundBadges.Add(badge);
		UpdateBadge(badge);
	}

	private static void OnUnbindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
	{
		if (item == null || item.RightCustomContainer == null) return;
		RemoveBadge(item.RightCustomContainer.Q<Label>(BadgeElementName));
	}

	private static Label CreateBadge()
	{
		var badge = new Label { name = BadgeElementName, pickingMode = PickingMode.Ignore };
		badge.style.display = DisplayStyle.None;
		badge.style.minWidth = 60;
		badge.style.height = 14;
		badge.style.alignSelf = Align.Center;
		badge.style.marginRight = 4;
		badge.style.paddingLeft = 4;
		badge.style.paddingRight = 4;
		badge.style.paddingTop = 0;
		badge.style.paddingBottom = 0;
		badge.style.fontSize = 9;
		badge.style.unityFontStyleAndWeight = FontStyle.Bold;
		badge.style.unityTextAlign = TextAnchor.MiddleCenter;
		badge.style.color = Color.white;
		badge.style.borderTopLeftRadius = 3;
		badge.style.borderTopRightRadius = 3;
		badge.style.borderBottomLeftRadius = 3;
		badge.style.borderBottomRightRadius = 3;
		return badge;
	}

	private static void RemoveBadge(Label badge)
	{
		if (badge == null) return;
		_boundBadges.Remove(badge);
		badge.RemoveFromHierarchy();
	}

	private static void UpdateAllBadges()
	{
		_deadBadges.Clear();
		foreach (var badge in _boundBadges)
		{
			if (badge.panel == null)
			{
				_deadBadges.Add(badge); // 창이 닫혔거나 행이 사라짐
				continue;
			}
			UpdateBadge(badge);
		}
		for (int i = 0; i < _deadBadges.Count; i++) _boundBadges.Remove(_deadBadges[i]);
	}

	private static void UpdateBadge(Label badge)
	{
		if (!TryGetInfo(badge.userData as GameObject, out var info))
		{
			badge.style.display = DisplayStyle.None;
			return;
		}

		string text = GetBadgeText(info);
		if (badge.text != text) badge.text = text;

		badge.style.backgroundColor = GetBadgeColor(info);
		badge.style.color = new Color(1f, 1f, 1f, info.Intensity);
		badge.style.display = DisplayStyle.Flex;
	}

	// ------------------------------------------------------------------------
	// Inspector
	// ------------------------------------------------------------------------
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var tracker = (UIRebuildTracker)target;
		EditorGUILayout.Space(10);
		EditorGUILayout.LabelField("Tracking UI Count", tracker.ActiveData.Count.ToString(), EditorStyles.boldLabel);
	}
}
#endif