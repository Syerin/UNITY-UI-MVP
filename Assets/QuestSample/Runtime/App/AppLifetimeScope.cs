using QuestSample.Demo;
using QuestSample.Presentation;
using QuestSample.Server;
using QuestSample.Store;
using QuestSample.Timing;
using R3;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace QuestSample.App
{
    /// <summary>
    /// 앱 스코프: 앱이 살아 있는 동안 하나씩 있는 것 — 시간(100ms 시계 · 초기화 감시) · 서버 · Store · 로비.
    /// 임무 화면은 열 때마다 이 스코프의 자식 스코프로 만든다(QuestScreenNavigator).
    /// 캔버스는 씬에 두지 않고 Resources/UI에서 불러온다.
    /// </summary>
    public sealed class AppLifetimeScope : LifetimeScope
    {
        const string LobbyCanvasPath = "UI/LobbyCanvas";
        const string DemoCanvasPath = "UI/DemoCanvas";
        const string QuestScreenPath = "UI/QuestScreen";

        [Header("테스트: 응답 지연")]
        [SerializeField] float latencySeconds = 0.4f;

        protected override void Configure(IContainerBuilder builder)
        {
            // 지금은 UnixTime.Now 하나로 읽는다(뒤에는 UnixTime.TimeProvider — 기본은 기기 시각, 보정하지 않는다).
            // 시계는 하나: 100ms마다 지금을 한 번 읽는다. 초 단위로 세는 것(초기화 감시, 남은 시간 표시)은 모두 이것을 구독한다.
            builder.Register(_ => new Clock100ms(UnixTime.Now), Lifetime.Singleton);

            // 초기화 판단은 ResetWatcher 하나: 매일 한국 시간 10시, 월간은 1일 10시. 시계를 구독해 틱마다 확인한다.
            builder.RegisterInstance(new ResetWatcher(ResetSchedule.Default));
            builder.RegisterBuildCallback(resolver =>
            {
                var clock = resolver.Resolve<Clock100ms>();
                resolver.Resolve<ResetWatcher>().Watch(clock.Now).AddTo(this);
                clock.Run().AddTo(this);
            });

            var latency = UnixSpan.FromMilliseconds((long)(latencySeconds * 1000));
            builder.RegisterInstance(new FakeQuestServer(new FakeQuestServerOptions(latency))).AsSelf().As<IQuestServer>();
            builder.RegisterInstance(QuestStoreOptions.Default);
            builder.Register<QuestStore>(Lifetime.Singleton);
            builder.RegisterInstance(new QuestScreenNavigator(this, Resources.Load<QuestScreenLifetimeScope>(QuestScreenPath)));

            // 로비와 데모 캔버스는 앱 스코프 아래에 만든다. 앱 스코프가 없어지면 함께 없어진다.
            builder.RegisterComponent(Instantiate(Resources.Load<LobbyView>(LobbyCanvasPath), transform));
            builder.RegisterEntryPoint<LobbyPresenter>();
            builder.RegisterComponent(Instantiate(Resources.Load<GameObject>(DemoCanvasPath), transform).GetComponentInChildren<DemoPanelView>());
            builder.RegisterEntryPoint<DemoPanelPresenter>();
        }
    }
}
