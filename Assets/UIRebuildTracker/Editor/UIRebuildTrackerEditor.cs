#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_6_OR_NEWER
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEngine.UIElements;
#endif

namespace Syerin.UIRebuild
{
    /// <summary>
    /// UIRebuildTracker의 기록을 Scene 뷰(외곽선) · Hierarchy(배지) · Inspector(추적 개수)에 그린다.
    /// Hierarchy는 Unity 6.6부터 EntityId 기반 콜백과 UI Toolkit 기반 새 창을 쓰고, 그 전 버전은 instanceID 기반 콜백을 쓴다.
    /// </summary>
    [CustomEditor(typeof(UIRebuildTracker))]
    public class UIRebuildTrackerEditor : Editor
    {
        static readonly Vector3[] WorldCorners = new Vector3[4];
        static GUIStyle s_badgeStyle;

        [InitializeOnLoadMethod]
        static void InitEditorEvents()
        {
            SceneView.duringSceneGui += OnSceneGUIGlobal;

            // 기록이 있는 동안 매 프레임 Hierarchy를 다시 그려 잔상이 남지 않게 한다.
            EditorApplication.update += OnEditorUpdate;

#if UNITY_6000_6_OR_NEWER
            // [레거시 Hierarchy] Project Settings > Editor > Hierarchy > Use Legacy Hierarchy를 켰을 때.
            // Unity 6.6에서 instanceID(int) 계열이 EntityId 계열로 바뀌었고, 옛 hierarchyWindowItemOnGUI는 컴파일 에러다.
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnLegacyHierarchyGUI;

            // [새 Hierarchy] UI Toolkit 기반이라 IMGUI 콜백이 불리지 않는다. 각 행(HierarchyViewItem)에 Label 배지를 붙였다 뗐다 한다.
            HierarchyWindow.BindViewItem += OnBindViewItem;
            HierarchyWindow.UnbindViewItem += OnUnbindViewItem;

            // 도메인 리로드 전에 이미 그려져 있던 행에도 배지를 붙인다.
            EditorApplication.delayCall += AttachToExistingItems;
#else
            // Unity 6.6 이전: instanceID 기반 IMGUI 콜백 하나로 그린다.
            EditorApplication.hierarchyWindowItemOnGUI += OnLegacyHierarchyGUI;
#endif
        }

        static void OnEditorUpdate()
        {
            var tracker = UIRebuildTracker.Instance;
            var hasData = tracker != null && tracker.ActiveData.Count > 0;
            if (hasData)
            {
                EditorApplication.RepaintHierarchyWindow();
            }

#if UNITY_6000_6_OR_NEWER
            // 기록이 있거나 방금 사라진 프레임이면 새 Hierarchy 배지를 갱신한다(숨김 처리 포함).
            if (hasData || s_hadDataLastFrame)
            {
                UpdateAllBadges();
            }

            s_hadDataLastFrame = hasData;
#endif
        }

        static void OnSceneGUIGlobal(SceneView sceneView)
        {
            var tracker = UIRebuildTracker.Instance;
            if (tracker == null || tracker.ActiveData.Count == 0)
            {
                return;
            }

            foreach (var pair in tracker.ActiveData)
            {
                var rect = pair.Key;
                var info = pair.Value;
                if (rect == null || info.Intensity <= 0.01f)
                {
                    continue;
                }

                rect.GetWorldCorners(WorldCorners);
                var outline = info.HasLayout ? Color.red : Color.yellow;
                outline.a = info.Intensity;
                Handles.DrawSolidRectangleWithOutline(WorldCorners, new Color(0, 0, 0, 0), outline);
            }

            sceneView.Repaint();
        }

        // ------------------------------------------------------------------------
        // 공통
        // ------------------------------------------------------------------------
        static bool TryGetInfo(GameObject go, out UIRebuildTracker.RebuildInfo info)
        {
            info = null;
            var tracker = UIRebuildTracker.Instance;
            if (tracker == null || tracker.ActiveData.Count == 0 || go == null)
            {
                return false;
            }

            if (!go.TryGetComponent<RectTransform>(out var rect) || !tracker.ActiveData.TryGetValue(rect, out info))
            {
                return false;
            }

            // 다 흐려졌으면 그리지 않는다 — 잔상 방지.
            return info.Intensity > 0.01f;
        }

        // 둘 다 잡혔으면 LAYOUT+G (Layout 리빌드가 Graphic 리빌드를 함께 부르는 경우가 많다)
        static string BadgeText(UIRebuildTracker.RebuildInfo info)
        {
            return info.HasLayout ? (info.HasGraphic ? "LAYOUT+G" : "LAYOUT") : "GRAPHIC";
        }

        static Color BadgeColor(UIRebuildTracker.RebuildInfo info)
        {
            return info.HasLayout
                ? new Color(0.9f, 0.1f, 0.2f, info.Intensity)
                : new Color(0.9f, 0.75f, 0.0f, info.Intensity);
        }

        // ------------------------------------------------------------------------
        // 레거시 Hierarchy (IMGUI)
        // ------------------------------------------------------------------------
#if UNITY_6000_6_OR_NEWER
        static void OnLegacyHierarchyGUI(EntityId entityId, Rect selectionRect)
        {
            DrawLegacyBadge(EditorUtility.EntityIdToObject(entityId) as GameObject, selectionRect);
        }
#else
        static void OnLegacyHierarchyGUI(int instanceID, Rect selectionRect)
        {
            DrawLegacyBadge(EditorUtility.InstanceIDToObject(instanceID) as GameObject, selectionRect);
        }
#endif

        static void DrawLegacyBadge(GameObject go, Rect selectionRect)
        {
            if (!TryGetInfo(go, out var info))
            {
                return;
            }

            if (s_badgeStyle == null)
            {
                s_badgeStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            var badgeRect = new Rect(selectionRect.xMax - 75, selectionRect.y, 70, selectionRect.height);
            var previousColor = GUI.color;
            GUI.color = BadgeColor(info);
            GUI.Box(badgeRect, BadgeText(info), s_badgeStyle);
            GUI.color = previousColor;
        }

#if UNITY_6000_6_OR_NEWER
        // ------------------------------------------------------------------------
        // 새 Hierarchy (UI Toolkit, Unity 6.6+)
        // ------------------------------------------------------------------------
        const string BadgeElementName = "ui-rebuild-tracker-badge";
        static bool s_hadDataLastFrame;
        static readonly HashSet<Label> s_boundBadges = new HashSet<Label>();
        static readonly List<Label> s_deadBadges = new List<Label>();

        static void AttachToExistingItems()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HierarchyWindow>())
            {
                if (window == null || window.rootVisualElement == null)
                {
                    continue;
                }

                window.rootVisualElement.Query<HierarchyViewItem>().ForEach(item => OnBindViewItem(window, window.View, item));
            }
        }

        static void OnBindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            if (item == null || item.RightCustomContainer == null)
            {
                return;
            }

            var badge = item.RightCustomContainer.Q<Label>(BadgeElementName);

            // GameObject 행이 아니면(씬 헤더 등) 배지를 뗀다.
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
            s_boundBadges.Add(badge);
            UpdateBadge(badge);
        }

        static void OnUnbindViewItem(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            if (item == null || item.RightCustomContainer == null)
            {
                return;
            }

            RemoveBadge(item.RightCustomContainer.Q<Label>(BadgeElementName));
        }

        static Label CreateBadge()
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

        static void RemoveBadge(Label badge)
        {
            if (badge == null)
            {
                return;
            }

            s_boundBadges.Remove(badge);
            badge.RemoveFromHierarchy();
        }

        static void UpdateAllBadges()
        {
            s_deadBadges.Clear();
            foreach (var badge in s_boundBadges)
            {
                if (badge.panel == null)
                {
                    s_deadBadges.Add(badge); // 창이 닫혔거나 행이 사라졌다
                    continue;
                }

                UpdateBadge(badge);
            }

            for (var i = 0; i < s_deadBadges.Count; i++)
            {
                s_boundBadges.Remove(s_deadBadges[i]);
            }
        }

        static void UpdateBadge(Label badge)
        {
            if (!TryGetInfo(badge.userData as GameObject, out var info))
            {
                badge.style.display = DisplayStyle.None;
                return;
            }

            var text = BadgeText(info);
            if (badge.text != text)
            {
                badge.text = text;
            }

            badge.style.backgroundColor = BadgeColor(info);
            badge.style.color = new Color(1f, 1f, 1f, info.Intensity);
            badge.style.display = DisplayStyle.Flex;
        }
#endif

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
}
#endif
