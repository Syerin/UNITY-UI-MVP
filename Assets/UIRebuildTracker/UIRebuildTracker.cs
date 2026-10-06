using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class UIRebuildTracker : MonoBehaviour
{
	public enum RebuildType { Graphic, Layout }

	public class RebuildInfo
	{
		public Queue<int> FrameTimestamps = new Queue<int>(64);

		public float HoldTimer;
		public float Intensity = 1.0f;
		public RebuildType Type;
		public string ComponentName;

		public int RebuildCountInWindow => FrameTimestamps.Count;
		public bool IsHighFrequencySpike => FrameTimestamps.Count >= 30;

		public void RecordRebuild(int currentFrame)
		{
			if (FrameTimestamps.Count == 0 || FrameTimestamps.Peek() != currentFrame)
			{
				FrameTimestamps.Enqueue(currentFrame);
			}
		}

		public void CleanOldFrames(int currentFrame)
		{
			while (FrameTimestamps.Count > 0 && (currentFrame - FrameTimestamps.Peek()) > 60)
			{
				FrameTimestamps.Dequeue();
			}
		}
	}

	public static UIRebuildTracker Instance { get; private set; }
	public Dictionary<RectTransform, RebuildInfo> ActiveData { get; } = new Dictionary<RectTransform, RebuildInfo>(128);

	[Header("Visualization Settings")]
	[SerializeField] private float _holdTime = 0.4f;
	[FormerlySerializedAs("_decaySpeed")]
	[SerializeField] private float _highlightFadeSpeed = 3.0f;
	[SerializeField] private bool _enableInRuntime = true;

	private object _registryInstance;
	private FieldInfo _layoutQueueField;
	private FieldInfo _graphicQueueField;
	private readonly List<RectTransform> _removeBuffer = new List<RectTransform>(64);

	private static readonly Vector3[] _corners = new Vector3[4];
	private Texture2D _whiteTex;
	private GUIStyle _labelStyle;

	private void OnEnable()
	{
		Instance = this;
		InitReflection();
#if UNITY_EDITOR
		EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
	}

	private void OnDisable()
	{
		if (Instance == this) Instance = null;
		ActiveData.Clear();
	}
#if UNITY_EDITOR
	private void OnDestroy()
	{
		EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
	}

	private void OnPlayModeStateChanged(PlayModeStateChange state)
	{
		// 플레이 모드가 바뀌거나 멈출 때 데이터 잔상 완전 초기화
		if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
		{
			Cleanup();
		}
	}
#endif
	private void Cleanup()
	{
		if (Instance == this) Instance = null;
		ActiveData.Clear();
	}
	private void InitReflection()
	{
		Type registryType = typeof(CanvasUpdateRegistry);
		PropertyInfo instanceProp = registryType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		_registryInstance = instanceProp?.GetValue(null);

		_layoutQueueField = registryType.GetField("m_LayoutRebuildQueue", BindingFlags.NonPublic | BindingFlags.Instance);
		_graphicQueueField = registryType.GetField("m_GraphicRebuildQueue", BindingFlags.NonPublic | BindingFlags.Instance);
	}

	private void LateUpdate()
	{
		if (!Application.isPlaying && !Application.isEditor) return;
		if (Application.isPlaying && !_enableInRuntime) return;

		CollectQueue(_graphicQueueField, RebuildType.Graphic);
		CollectQueue(_layoutQueueField, RebuildType.Layout);
	}

	private void CollectQueue(FieldInfo fieldInfo, RebuildType type)
	{
		if (fieldInfo == null || _registryInstance == null) return;

		object queueObj = fieldInfo.GetValue(_registryInstance);
		if (queueObj == null) return;

		FieldInfo listField = queueObj.GetType().GetField("m_List", BindingFlags.NonPublic | BindingFlags.Instance);
		if (listField != null && listField.GetValue(queueObj) is IList innerList)
		{
			int currentFrame = Time.frameCount;
			int count = innerList.Count;

			for (int i = 0; i < count; i++)
			{
				if (innerList[i] is ICanvasElement element && element.transform is RectTransform rect)
				{
					if (rect.transform == transform || rect.transform.IsChildOf(transform)) continue;

					if (!ActiveData.TryGetValue(rect, out var info))
					{
						info = new RebuildInfo();
						info.ComponentName = element.GetType().Name;
						ActiveData[rect] = info;
					}

					info.RecordRebuild(currentFrame);
					info.HoldTimer = _holdTime;
					info.Intensity = 1.0f;
					info.Type = type;
				}
			}
		}
	}

	private void Update()
	{
		if (ActiveData.Count == 0) return;

		float deltaTime = Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
		int currentFrame = Time.frameCount;
		_removeBuffer.Clear();

		foreach (var kvp in ActiveData)
		{
			var rect = kvp.Key;
			var info = kvp.Value;

			if (rect == null)
			{
				_removeBuffer.Add(rect);
				continue;
			}

			info.CleanOldFrames(currentFrame);

			if (info.HoldTimer > 0f)
			{
				info.HoldTimer -= deltaTime;
			}
			else
			{
				info.Intensity -= deltaTime * _highlightFadeSpeed;
				if (info.Intensity <= 0f && info.RebuildCountInWindow == 0)
				{
					_removeBuffer.Add(rect);
				}
			}
		}

		for (int i = 0; i < _removeBuffer.Count; i++)
		{
			ActiveData.Remove(_removeBuffer[i]);
		}
	}

	private void OnGUI()
	{
		if (ActiveData.Count == 0) return;

		if (_whiteTex == null) _whiteTex = Texture2D.whiteTexture;

		int scaledFontSize = Mathf.Max(12, Mathf.RoundToInt(16f * ((float)Screen.height / 1080f)));

		if (_labelStyle == null || _labelStyle.fontSize != scaledFontSize)
		{
			_labelStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = scaledFontSize,
				fontStyle = FontStyle.Bold,
				normal = { textColor = Color.white }
			};
		}

		foreach (var kvp in ActiveData)
		{
			RectTransform rect = kvp.Key;
			RebuildInfo info = kvp.Value;
			if (rect == null) continue;

			rect.GetWorldCorners(_corners);

			Canvas canvas = rect.GetComponentInParent<Canvas>();
			Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

			Vector2 screen0 = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
			Vector2 screen2 = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);

			screen0.y = Screen.height - screen0.y;
			screen2.y = Screen.height - screen2.y;

			float xMin = Mathf.Min(screen0.x, screen2.x);
			float xMax = Mathf.Max(screen0.x, screen2.x);
			float yMin = Mathf.Min(screen0.y, screen2.y);
			float yMax = Mathf.Max(screen0.y, screen2.y);

			Rect guiRect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);

			int rebuildCount = info.RebuildCountInWindow;
			bool isCritical = info.IsHighFrequencySpike;
			float heatRatio = Mathf.Clamp01(rebuildCount / 30f);

			Color outlineColor;
			if (info.Type == RebuildType.Graphic)
			{
				outlineColor = Color.Lerp(Color.yellow, Color.red, heatRatio);
			}
			else
			{
				outlineColor = Color.Lerp(Color.green, Color.red, heatRatio);
			}

			outlineColor.a = isCritical ? (Mathf.PingPong(Time.unscaledTime * 8f, 0.6f) + 0.4f) : (info.Intensity * 0.8f);
			int outlineThickness = isCritical ? 4 : 2;
			DrawOutline(guiRect, outlineColor, outlineThickness);

			string labelText = $"{info.ComponentName} ({rebuildCount}/60f)";
			if (isCritical) labelText = $"⚠ {labelText}";

			GUI.color = new Color(0f, 0f, 0f, 0.8f);
			Rect labelRect = new Rect(xMin + 2, yMin + 2, 300, scaledFontSize * 2);
			GUI.Label(new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width, labelRect.height), labelText, _labelStyle);

			GUI.color = isCritical ? Color.red : new Color(1f, 1f, 1f, info.Intensity);
			GUI.Label(labelRect, labelText, _labelStyle);
		}
	}

	private void DrawOutline(Rect rect, Color color, int thickness)
	{
		GUI.color = color;
		GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _whiteTex);
		GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _whiteTex);
		GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _whiteTex);
		GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _whiteTex);
	}
}