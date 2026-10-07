using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Syerin.UIRebuild
{
    /// <summary>
    /// uGUI 리빌드 큐(CanvasUpdateRegistry)를 프레임마다 읽어, 어떤 UI가 얼마나 자주 다시 빌드되는지 기록하고 Game 뷰에 그린다.
    /// Scene 뷰 외곽선 · Hierarchy 배지 · Inspector는 UIRebuildTrackerEditor가 같은 데이터로 그린다.
    /// 레이아웃 큐에는 요소 대신 레이아웃 루트(LayoutRebuilder)가 들어가므로, 같은 프레임에 그 루트 안에서
    /// Graphic 리빌드된 요소를 원인 후보(CAUSE)로 함께 표시한다.
    /// 원인 찾기용이고 시간(ms)은 재지 않는다. 릴리스 빌드에서는 스스로 꺼진다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    // 큐는 모든 LateUpdate가 끝난 뒤(Canvas.willRenderCanvases) 처리되고 비워진다.
    // 다른 스크립트의 LateUpdate가 만든 리빌드까지 보려면 가장 늦게 읽어야 한다.
    [DefaultExecutionOrder(32000)]
    public class UIRebuildTracker : MonoBehaviour
    {
        [Flags]
        public enum RebuildType
        {
            None = 0,
            Graphic = 1 << 0,
            Layout = 1 << 1,
        }

        // 집계 창: 최근 60프레임(지금 프레임 포함)
        public const int WindowFrames = 60;

        // 창 안에서 이만큼 이상 리빌드되면 경고(⚠ + 깜빡임)
        public const int SpikeThreshold = 30;

        public class RebuildInfo
        {
            readonly Queue<int> _frames = new Queue<int>(WindowFrames);
            int _lastFrame = -1;
            int _labelCount = -1;
            bool _labelCause;
            string _label;

            public float HoldTimer;
            public float Intensity = 1.0f;

            // 지금 묶음(유지 시간 안)에서 잡힌 종류. Graphic과 Layout이 둘 다 잡히면 둘 다 켜진다.
            public RebuildType Types;

            // 지금 묶음에서, 이 요소를 품은 레이아웃 루트가 같은 프레임에 레이아웃 리빌드됐다 — 레이아웃을 흔든 원인 후보.
            public bool IsLayoutCause;

            public string ComponentName;

            // OnGUI에서 요소마다 GetComponentInParent를 부르지 않도록 처음 볼 때 한 번 찾아 둔다.
            public Canvas Canvas;

            public int RebuildCountInWindow => _frames.Count;
            public bool IsHighFrequencySpike => _frames.Count >= SpikeThreshold;
            public bool HasLayout => (Types & RebuildType.Layout) != 0;
            public bool HasGraphic => (Types & RebuildType.Graphic) != 0;

            // 한 프레임에 두 큐에 다 있어도 한 번으로 센다.
            public void RecordRebuild(int frame)
            {
                if (_lastFrame == frame)
                {
                    return;
                }

                _lastFrame = frame;
                _frames.Enqueue(frame);
            }

            public void DropOldFrames(int currentFrame)
            {
                while (_frames.Count > 0 && currentFrame - _frames.Peek() >= WindowFrames)
                {
                    _frames.Dequeue();
                }
            }

            // Game 뷰 라벨. 횟수나 원인 표시가 바뀔 때만 문자열을 만든다 — OnGUI가 프레임마다 그려도 할당하지 않게.
            // 창 안 횟수가 0이면(창을 벗어나 흐려지는 중) 횟수는 빼고 이름만 둔다.
            public string Label
            {
                get
                {
                    var count = _frames.Count;
                    if (_label == null || count != _labelCount || IsLayoutCause != _labelCause)
                    {
                        _labelCount = count;
                        _labelCause = IsLayoutCause;
                        _label = (count >= SpikeThreshold ? "⚠ " : "")
                            + (IsLayoutCause ? "CAUSE · " : "")
                            + ComponentName
                            + (count > 0 ? " (" + count + "/" + WindowFrames + "f)" : "");
                    }

                    return _label;
                }
            }
        }

        static readonly Vector3[] Corners = new Vector3[4];

        public static UIRebuildTracker Instance { get; private set; }

        public Dictionary<RectTransform, RebuildInfo> ActiveData { get; } = new Dictionary<RectTransform, RebuildInfo>(128);

        [Header("Visualization Settings")]
        [SerializeField] float _holdTime = 0.4f;
        [FormerlySerializedAs("_decaySpeed")]
        [SerializeField] float _highlightFadeSpeed = 3.0f;
        [SerializeField] bool _enableInRuntime = true;

        // 리플렉션은 켜질 때 큐 객체를 찾는 데 한 번만 쓴다. 큐(IndexedSet)는 IList를 구현하므로, 매 프레임에는
        // 리플렉션 없이 Count(활성 요소 수)와 인덱서로 읽는다. IndexedSet의 열거자는 지원되지 않아 foreach는 쓰지 않는다.
        IList<ICanvasElement> _graphicQueue;
        IList<ICanvasElement> _layoutQueue;
        bool _reflectionReady;
        static bool s_warnedReflection;

        // 이번 프레임에 레이아웃 리빌드된 루트. Graphic 리빌드 요소가 이 안에 있으면 원인 후보로 표시한다.
        readonly HashSet<RectTransform> _layoutRoots = new HashSet<RectTransform>();

        readonly List<RectTransform> _removeBuffer = new List<RectTransform>(64);
        Texture2D _whiteTex;
        GUIStyle _labelStyle;

        void OnEnable()
        {
            // 디버그 전용. 개발 빌드와 에디터에서만 돈다 — 릴리스 빌드에 남아 있어도 스스로 꺼진다.
            if (!Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }

            Instance = this;
            InitReflection();
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            Cleanup();
        }

#if UNITY_EDITOR
        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // 플레이 모드를 오갈 때 남은 기록(잔상)을 지운다.
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                ActiveData.Clear();
            }
        }
#endif

        void Cleanup()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            ActiveData.Clear();
        }

        void InitReflection()
        {
            if (_reflectionReady)
            {
                return;
            }

            const BindingFlags instanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var registryType = typeof(CanvasUpdateRegistry);
            var registry = registryType.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            _graphicQueue = registry == null ? null : registryType.GetField("m_GraphicRebuildQueue", instanceFlags)?.GetValue(registry) as IList<ICanvasElement>;
            _layoutQueue = registry == null ? null : registryType.GetField("m_LayoutRebuildQueue", instanceFlags)?.GetValue(registry) as IList<ICanvasElement>;
            _reflectionReady = _graphicQueue != null && _layoutQueue != null;

            // uGUI 내부 필드 이름이 바뀌면 아무것도 표시되지 않는다. 조용히 넘어가지 않고 한 번 알린다.
            if (!_reflectionReady && !s_warnedReflection)
            {
                s_warnedReflection = true;
                Debug.LogWarning("UIRebuildTracker: uGUI 내부 필드(CanvasUpdateRegistry의 리빌드 큐)를 찾지 못해 동작하지 않습니다. uGUI 버전을 확인하세요.");
            }
        }

        void LateUpdate()
        {
            if (!_reflectionReady || (Application.isPlaying && !_enableInRuntime))
            {
                return;
            }

            // 레이아웃 큐를 먼저 읽어 이번 프레임의 레이아웃 루트를 모은 뒤, Graphic 리빌드 중 그 루트 안에 있는 요소를 원인 후보로 표시한다.
            var frame = Time.frameCount;
            _layoutRoots.Clear();
            Collect(_layoutQueue, RebuildType.Layout, frame);
            Collect(_graphicQueue, RebuildType.Graphic, frame);
        }

        void Collect(IList<ICanvasElement> queue, RebuildType type, int frame)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                var element = queue[i];
                if (element == null || !(element.transform is RectTransform rect))
                {
                    continue;
                }

                // 트래커 자신과 그 자식은 빼고 본다.
                if (rect == transform || rect.IsChildOf(transform))
                {
                    continue;
                }

                if (!ActiveData.TryGetValue(rect, out var info))
                {
                    info = new RebuildInfo
                    {
                        ComponentName = NameOf(element, rect),
                        Canvas = rect.GetComponentInParent<Canvas>(),
                    };
                    ActiveData[rect] = info;
                }

                // 지난 묶음이 끝난 뒤(유지 시간이 지난 뒤) 다시 잡혔으면 종류와 원인 표시를 새로 센다.
                if (info.HoldTimer <= 0f)
                {
                    info.Types = RebuildType.None;
                    info.IsLayoutCause = false;
                }

                info.Types |= type;
                info.RecordRebuild(frame);
                info.HoldTimer = _holdTime;
                info.Intensity = 1.0f;

                if (type == RebuildType.Layout)
                {
                    _layoutRoots.Add(rect);
                }
                else if (_layoutRoots.Count > 0 && IsInsideLayoutRoot(rect))
                {
                    info.IsLayoutCause = true;
                }
            }
        }

        // 레이아웃 큐에는 요소 대신 LayoutRebuilder가 들어 있고, 그 transform은 레이아웃 루트다(리빌드를 요청한 요소가 아니다).
        // 이름은 루트에 붙은 레이아웃 컴포넌트(LayoutGroup · ContentSizeFitter 등)로 보여 준다.
        static string NameOf(ICanvasElement element, RectTransform rect)
        {
            if (element is Component)
            {
                return element.GetType().Name;
            }

            var controller = rect.GetComponent<ILayoutController>() as Component;
            return controller != null ? controller.GetType().Name : rect.name;
        }

        // 부모 쪽에 이번 프레임의 레이아웃 루트가 있는가. 자기 자신이 루트면 이미 LAYOUT+G로 보이므로 부모부터 본다.
        bool IsInsideLayoutRoot(RectTransform rect)
        {
            for (var parent = rect.parent; parent != null; parent = parent.parent)
            {
                if (parent is RectTransform parentRect && _layoutRoots.Contains(parentRect))
                {
                    return true;
                }
            }

            return false;
        }

        void Update()
        {
            if (ActiveData.Count == 0)
            {
                return;
            }

            var deltaTime = Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
            var currentFrame = Time.frameCount;
            _removeBuffer.Clear();

            foreach (var pair in ActiveData)
            {
                var rect = pair.Key;
                var info = pair.Value;
                if (rect == null)
                {
                    _removeBuffer.Add(rect);
                    continue;
                }

                info.DropOldFrames(currentFrame);
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

            for (var i = 0; i < _removeBuffer.Count; i++)
            {
                ActiveData.Remove(_removeBuffer[i]);
            }
        }

        void OnGUI()
        {
            // OnGUI는 이벤트마다(레이아웃 · 입력 · 그리기) 불린다. 그리는 일은 Repaint에서 한 번만 한다.
            if (Event.current.type != EventType.Repaint || ActiveData.Count == 0)
            {
                return;
            }

            if (_whiteTex == null)
            {
                _whiteTex = Texture2D.whiteTexture;
            }

            var fontSize = Mathf.Max(12, Mathf.RoundToInt(16f * (Screen.height / 1080f)));
            if (_labelStyle == null || _labelStyle.fontSize != fontSize)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white },
                };
            }

            var previousColor = GUI.color;
            foreach (var pair in ActiveData)
            {
                var rect = pair.Key;
                var info = pair.Value;
                if (rect == null)
                {
                    continue;
                }

                rect.GetWorldCorners(Corners);
                var canvas = info.Canvas;
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var screen0 = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
                var screen2 = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
                screen0.y = Screen.height - screen0.y;
                screen2.y = Screen.height - screen2.y;

                var xMin = Mathf.Min(screen0.x, screen2.x);
                var yMin = Mathf.Min(screen0.y, screen2.y);
                var guiRect = new Rect(xMin, yMin, Mathf.Max(screen0.x, screen2.x) - xMin, Mathf.Max(screen0.y, screen2.y) - yMin);

                var isCritical = info.IsHighFrequencySpike;
                var heat = Mathf.Clamp01(info.RebuildCountInWindow / (float)SpikeThreshold);
                // 레이아웃 루트는 초록 → 빨강, 원인 후보는 하늘색 → 빨강, 그 밖의 Graphic 리빌드는 노랑 → 빨강
                var baseColor = info.HasLayout ? Color.green : info.IsLayoutCause ? Color.cyan : Color.yellow;
                var outlineColor = Color.Lerp(baseColor, Color.red, heat);
                outlineColor.a = isCritical ? Mathf.PingPong(Time.unscaledTime * 8f, 0.6f) + 0.4f : info.Intensity * 0.8f;
                DrawOutline(guiRect, outlineColor, isCritical ? 4 : 2);

                var labelRect = new Rect(xMin + 2, yMin + 2, 300, fontSize * 2);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width, labelRect.height), info.Label, _labelStyle);
                GUI.color = isCritical ? Color.red : new Color(1f, 1f, 1f, info.Intensity);
                GUI.Label(labelRect, info.Label, _labelStyle);
            }

            GUI.color = previousColor;
        }

        void DrawOutline(Rect rect, Color color, int thickness)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _whiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), _whiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _whiteTex);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), _whiteTex);
        }
    }
}
