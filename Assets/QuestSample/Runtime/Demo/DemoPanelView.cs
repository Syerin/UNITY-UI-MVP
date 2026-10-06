using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSample.Demo
{
    public sealed class DemoPanelView : MonoBehaviour
    {
        [SerializeField] Button winBattleButton;
        [SerializeField] Button enhanceButton;
        [SerializeField] Button skipDayButton;
        [SerializeField] Button failNextButton;
        [SerializeField] TMP_Text statusText;

        public Observable<Unit> OnWinBattle => winBattleButton.OnClickAsObservable();
        public Observable<Unit> OnEnhance => enhanceButton.OnClickAsObservable();
        public Observable<Unit> OnSkipDay => skipDayButton.OnClickAsObservable();
        public Observable<Unit> OnFailNext => failNextButton.OnClickAsObservable();

        public void SetStatus(string text)
        {
            statusText.text = text;
        }
    }
}
