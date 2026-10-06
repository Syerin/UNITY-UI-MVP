using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSample.Presentation
{
    /// <summary>행 하나. 같은 값이 다시 오면 아무것도 하지 않는다(record 값 비교).</summary>
    public sealed class QuestCellView : MonoBehaviour
    {
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text rewardText;
        [SerializeField] TMP_Text progressText;
        [SerializeField] RectTransform progressFill;
        [SerializeField] Button receiveButton;
        [SerializeField] TMP_Text receiveButtonText;
        [SerializeField] GameObject receivedOverlay;

        QuestRowData _current;

        public Observable<string> OnReceiveClicked => receiveButton.OnClickAsObservable().Select(_ => _current.Id);

        public void Render(QuestRowData row)
        {
            if (row.Equals(_current))
            {
                return;
            }

            _current = row;
            titleText.text = row.Title;
            rewardText.text = row.RewardText;
            progressText.text = row.ProgressText;
            progressFill.anchorMax = new Vector2(row.ProgressRatio, 1f);
            receiveButton.interactable = row.CanReceive;
            receiveButtonText.text = row.ButtonText;
            receivedOverlay.SetActive(row.IsReceived);
        }
    }
}
