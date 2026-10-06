using R3;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSample.Presentation
{
    public sealed class LobbyView : MonoBehaviour
    {
        [SerializeField] Button questButton;
        [SerializeField] GameObject questBadge;

        public Observable<Unit> OnQuestClicked => questButton.OnClickAsObservable();

        public void SetQuestBadge(bool visible)
        {
            questBadge.SetActive(visible);
        }
    }
}
