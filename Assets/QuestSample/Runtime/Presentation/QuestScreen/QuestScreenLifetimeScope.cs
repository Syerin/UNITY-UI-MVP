using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace QuestSample.Presentation
{
    /// <summary>
    /// 임무 화면 스코프. 화면이 열려 있는 동안만 있고, 닫히면 Presenter와 구독이 함께 정리된다(남은 시간 표시는 앱의 100ms 시계를 구독할 뿐이다).
    /// </summary>
    public sealed class QuestScreenLifetimeScope : LifetimeScope
    {
        [SerializeField] QuestScreenView view;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponent(view);
            builder.RegisterEntryPoint<QuestScreenPresenter>();
        }
    }
}
