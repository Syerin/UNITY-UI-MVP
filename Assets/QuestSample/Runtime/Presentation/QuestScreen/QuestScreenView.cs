using QuestSample.Domain;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSample.Presentation
{
    /// <summary>받은 값을 넣기만 한다. 무엇을 보여 줄지는 정하지 않는다.</summary>
    public sealed class QuestScreenView : MonoBehaviour
    {
        [SerializeField] Button closeButton;
        [SerializeField] Button missionTabButton;
        [SerializeField] Button passTabButton;
        [SerializeField] GameObject missionTabSelected;
        [SerializeField] GameObject passTabSelected;
        [SerializeField] GameObject missionBadge;
        [SerializeField] GameObject passBadge;
        [SerializeField] TMP_Text countdownText;
        [SerializeField] GameObject seasonPointsRoot;
        [SerializeField] TMP_Text seasonPointsText;
        [SerializeField] Button receiveAllButton;
        [SerializeField] QuestListView list;
        [SerializeField] ToastView toast;

        public QuestListView List => list;
        public ToastView Toast => toast;

        public Observable<Unit> OnCloseClicked => closeButton.OnClickAsObservable();
        public Observable<Unit> OnReceiveAllClicked => receiveAllButton.OnClickAsObservable();

        public Observable<QuestGroup> OnTabClicked => Observable.Merge(
            missionTabButton.OnClickAsObservable().Select(_ => QuestGroup.DailyMission),
            passTabButton.OnClickAsObservable().Select(_ => QuestGroup.SeasonPass));

        public void SetTabSelected(bool missionSelected)
        {
            missionTabSelected.SetActive(missionSelected);
            passTabSelected.SetActive(!missionSelected);
        }

        public void SetMissionBadge(bool visible) => missionBadge.SetActive(visible);

        public void SetPassBadge(bool visible) => passBadge.SetActive(visible);

        public void SetReceiveAllInteractable(bool interactable) => receiveAllButton.interactable = interactable;

        public void SetCountdown(string text) => countdownText.text = text;

        public void SetSeasonPointsVisible(bool visible) => seasonPointsRoot.SetActive(visible);

        public void SetSeasonPoints(string text) => seasonPointsText.text = text;
    }
}
