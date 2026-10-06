using QuestSample.Timing;
using R3;
using TMPro;
using UnityEngine;

namespace QuestSample.Presentation
{
    /// <summary>짧은 알림. 새 알림이 오면 이전 것을 바로 덮는다.</summary>
    public sealed class ToastView : MonoBehaviour
    {
        static readonly UnixSpan Duration = UnixSpan.FromMilliseconds(1800);

        [SerializeField] GameObject root;
        [SerializeField] TMP_Text messageText;

        readonly SerialDisposable _hide = new SerialDisposable();

        public void Show(string message)
        {
            messageText.text = message;
            root.SetActive(true);
            // timeScale과 무관하게 닫는다. 일시정지(timeScale 0) 중에 뜬 알림이 남아 있지 않도록.
            _hide.Disposable = Observable.Timer(Duration.ToTimeSpan(), UnityTimeProvider.UpdateIgnoreTimeScale)
                .Subscribe(_ => root.SetActive(false));
        }

        void OnDestroy()
        {
            _hide.Dispose();
        }
    }
}
