using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using QuestSample.Domain;
using QuestSample.Server;
using QuestSample.Store;
using QuestSample.Timing;
using R3;

namespace QuestSample.Tests
{
    /// <summary>
    /// 가짜 서버(지연 0)와 100ms 시계, ResetWatcher로 Store를 검증한다.
    /// 지금(UnixTime.Now, 서버가 보는 시각)은 가짜 TimeProvider로 옮기고, 클라 시계에는 시각을 직접 넣는다(Set).
    /// 모든 요청이 동기로 끝나서 Unity 없이 돈다.
    /// </summary>
    public sealed class QuestStoreTests
    {
        // 일일 초기화(한국 시간 오전 10시) 정각에서 시작한다. 다음 초기화는 24시간 뒤다.
        // 2026-10-01은 목요일이고 1일이라, 이 시각은 일간 · 주간 · 월간 초기화가 겹치는 시각이기도 하다.
        static readonly UnixTime Start = KstTime(2026, 10, 1, 10);

        static readonly UnixSpan Tick = Clock100ms.TickInterval;

        static readonly FakeQuestServerOptions Options = new FakeQuestServerOptions(UnixSpan.Zero);

        TimeProvider _previousTime;
        FakeTimeProvider _serverTime;
        Clock100ms _clock;
        ResetWatcher _resets;
        IDisposable _watching;
        List<ResetSignal> _signals;
        FakeQuestServer _server;
        CountingServer _counting;
        QuestStore _store;

        [SetUp]
        public void SetUp()
        {
            _previousTime = UnixTime.TimeProvider;
            Open(Start);
        }

        [TearDown]
        public void TearDown()
        {
            _store.Dispose();
            _watching.Dispose();
            _clock.Dispose();
            UnixTime.TimeProvider = _previousTime;
        }

        [Test]
        public void 임무를_받으면_시즌포인트가_올라_패스_레드닷이_켜진다()
        {
            // 접속 임무(시즌 포인트 100)는 들어오자마자 받을 수 있다.
            Assert.That(_store.HasReceivableMission.CurrentValue, Is.True);
            Assert.That(_store.HasReceivablePass.CurrentValue, Is.False);

            Wait(_store.ReceiveAsync(QuestGroup.DailyMission, MissionId("login")));

            Assert.That(_store.SeasonPoints.CurrentValue, Is.EqualTo(100));
            Assert.That(_store.HasReceivablePass.CurrentValue, Is.True); // Lv.1(100P)
            Assert.That(_store.HasAnyReceivable.CurrentValue, Is.True);
        }

        [Test]
        public void 서버가_거절하면_상태가_그대로다()
        {
            var before = _store.DailyMissions.CurrentValue;
            _server.FailNextRequest(QuestError.Network);

            var error = Assert.Throws<QuestServerException>(
                () => Wait(_store.ReceiveAsync(QuestGroup.DailyMission, MissionId("login"))));

            Assert.That(error.Error, Is.EqualTo(QuestError.Network));
            Assert.That(_store.DailyMissions.CurrentValue, Is.SameAs(before));
        }

        [Test]
        public void 초기화_시각이_지나면_서버에_묻지_않고_클라에서_먼저_초기화한다()
        {
            Wait(_store.ReceiveAsync(QuestGroup.DailyMission, MissionId("login")));

            Advance(UnixSpan.FromDays(1));

            Assert.That(_counting.BoardRequests, Is.EqualTo(1)); // 처음 받은 한 번뿐
            Assert.That(_store.DailyMissions.CurrentValue.All(quest => quest.State == QuestState.Progress), Is.True); // 받은 것까지 되돌렸다
            Assert.That(_signals.Count, Is.EqualTo(1)); // 클라가 낸 일일 신호 하나
            Assert.That(_signals[0].Daily && !_signals[0].Weekly && !_signals[0].Monthly && !_signals[0].FromServer, Is.True);
            Assert.That(_signals[0].At, Is.EqualTo(KstTime(2026, 10, 2, 10)));
        }

        [Test]
        public void 클라가_먼저_초기화한_목록은_다음_서버_응답이_덮어쓴다()
        {
            Advance(UnixSpan.FromDays(1));
            Assert.That(_store.HasReceivableMission.CurrentValue, Is.False); // 클라는 서버 규칙(접속하면 완료)을 모른다

            Wait(_store.RefreshAsync(CancellationToken.None)); // 버튼으로 서버를 부른 경우(예: 전투 결과)

            Assert.That(_store.HasReceivableMission.CurrentValue, Is.True); // 서버의 새 날: 접속 임무 완료
        }

        [Test]
        public void 기기_시계가_빨라도_서버에_되풀이해_묻지_않는다()
        {
            // 기기 시각은 보정하지 않는다. 기기가 25시간 빠르면 서버는 아직 오늘인데 클라는 초기화 시각을 넘긴다.
            _clock.Set(Start + UnixSpan.FromHours(25));
            Ticks(10);

            Assert.That(_counting.BoardRequests, Is.EqualTo(1)); // 처음 받은 한 번뿐
            Assert.That(_store.HasReceivableMission.CurrentValue, Is.False); // 클라가 먼저 비웠다

            // 버튼으로 서버를 부르면 서버의 오늘 목록이 온다. 클라가 넘긴 초기화 기준은 되돌아가지 않아서,
            // 틱이 계속 돌아도 같은 초기화로 서버 목록을 다시 비우지 않는다.
            Wait(_store.RefreshAsync(CancellationToken.None));
            Ticks(10);

            Assert.That(_store.HasReceivableMission.CurrentValue, Is.True); // 서버의 목록(접속 임무 완료)이 그대로다
            Assert.That(_signals.Count, Is.EqualTo(1));
        }

        [Test]
        public void 일일_초기화는_한국_시간_오전_10시다()
        {
            // 10시 바로 전 틱까지는 초기화하지 않고, 10시가 되는 틱에 한 번 초기화한다.
            Advance(UnixSpan.FromDays(1) - Tick);
            Assert.That(_signals, Is.Empty);

            Advance(Tick);
            Assert.That(_signals.Count, Is.EqualTo(1));
            Assert.That(_signals[0].At, Is.EqualTo(KstTime(2026, 10, 2, 10)));

            // 10시 정각이면 이미 초기화된 것으로 보고, 다음 초기화는 다음 날 10시다.
            Assert.That(_store.NextReset(QuestGroup.DailyMission, _clock.Now.CurrentValue), Is.EqualTo(KstTime(2026, 10, 3, 10)));
        }

        [Test]
        public void 시즌_패스는_매월_1일_10시에_새로_시작한다()
        {
            // 지금(10월 1일 10시)의 다음 시즌은 11월 1일 10시에 시작한다.
            var nextSeason = KstTime(2026, 11, 1, 10);
            Assert.That(_store.NextReset(QuestGroup.SeasonPass, _clock.Now.CurrentValue), Is.EqualTo(nextSeason));

            Wait(_store.ReceiveAsync(QuestGroup.DailyMission, MissionId("login"))); // 시즌 포인트 100 → Lv.1을 받을 수 있다
            Assert.That(_store.HasReceivablePass.CurrentValue, Is.True);

            Advance(nextSeason - _clock.Now.CurrentValue);

            // 서버에 묻지 않고 시즌 패스를 먼저 되돌렸다(월간 신호).
            Assert.That(_counting.BoardRequests, Is.EqualTo(1));
            Assert.That(_signals.Count, Is.EqualTo(1));
            Assert.That(_signals[0].Monthly, Is.True);
            Assert.That(_store.SeasonPoints.CurrentValue, Is.EqualTo(0));
            Assert.That(_store.HasReceivablePass.CurrentValue, Is.False);

            // 지난 시즌의 단계 Id로 받으면 서버가 거절하고, Store는 새 시즌 목록을 받는다.
            var staleTierId = _store.PassTiers.CurrentValue[0].Id;
            var error = Assert.Throws<QuestServerException>(
                () => Wait(_store.ReceiveAsync(QuestGroup.SeasonPass, staleTierId)));

            Assert.That(error.Error, Is.EqualTo(QuestError.Expired));
            Assert.That(_store.PassTiers.CurrentValue[0].Id, Is.Not.EqualTo(staleTierId));
        }

        [Test]
        public void 처음_받기에_실패하면_다음_틱에_다시_받는다()
        {
            var server = new FakeQuestServer(Options);
            server.FailNextRequest(QuestError.Network);
            using var store = new QuestStore(server, _clock, _resets);
            Assert.That(store.DailyMissions.CurrentValue, Is.Empty);

            Advance(Tick);

            Assert.That(store.DailyMissions.CurrentValue, Is.Not.Empty);
        }

        [Test]
        public void 초기화가_지난_목록으로_받으면_거절되고_목록을_새로_받는다()
        {
            var staleId = MissionId("login");
            _server.SkipDay();

            var error = Assert.Throws<QuestServerException>(
                () => Wait(_store.ReceiveAsync(QuestGroup.DailyMission, staleId)));

            Assert.That(error.Error, Is.EqualTo(QuestError.Expired));
            Assert.That(MissionId("login"), Is.Not.EqualTo(staleId));
        }

        [Test]
        public void 레드닷은_값이_같으면_다시_알리지_않는다()
        {
            var notified = 0;
            using var subscription = _store.HasAnyReceivable.Subscribe(_ => notified++);

            _server.AddProgress(FakeQuestServer.BattleMission, 1);
            Wait(_store.RefreshAsync(CancellationToken.None));
            _server.AddProgress(FakeQuestServer.BattleMission, 1);
            Wait(_store.RefreshAsync(CancellationToken.None));

            // 목록은 두 번 바뀌었지만 '받을 것이 있다'는 그대로라서 구독할 때 한 번만 알렸다.
            Assert.That(notified, Is.EqualTo(1));
        }

        [Test]
        public void 모두받기는_받을_수_있는_Id만_보낸다()
        {
            _server.AddProgress(FakeQuestServer.BattleMission, 3);
            Wait(_store.RefreshAsync(CancellationToken.None));

            var response = Wait(_store.ReceiveAllAsync(QuestGroup.DailyMission));

            // 진행 중인 강화 · 모집은 보내지 않았다. 서버는 목록을 뒤지지 않고 이 두 개만 검증했다.
            Assert.That(_counting.SentIds, Is.EqualTo(new[] { MissionId("login"), MissionId("battle") }));
            Assert.That(response.Granted.Count, Is.EqualTo(2));
            Assert.That(response.Rejected, Is.Empty);
            Assert.That(_store.SeasonPoints.CurrentValue, Is.EqualTo(250));
            Assert.That(_store.HasReceivableMission.CurrentValue, Is.False);
        }

        [Test]
        public void 모두받기는_받을_수_없는_Id를_빼고_준다()
        {
            // 다른 기기에서 접속 임무를 먼저 받았다. 이 기기의 목록은 아직 받을 수 있다고 보여 준다.
            _server.AddProgress(FakeQuestServer.BattleMission, 3);
            Wait(_store.RefreshAsync(CancellationToken.None));
            Wait(_server.ReceiveAsync(QuestGroup.DailyMission, MissionId("login"), CancellationToken.None));

            var response = Wait(_store.ReceiveAllAsync(QuestGroup.DailyMission));

            Assert.That(response.Granted.Count, Is.EqualTo(1)); // 전투만 받았다
            Assert.That(response.Rejected, Is.EqualTo(new[] { MissionId("login") }));
            Assert.That(_store.SeasonPoints.CurrentValue, Is.EqualTo(250)); // 다른 기기의 100 + 전투 150
            Assert.That(_store.HasReceivableMission.CurrentValue, Is.False); // 응답의 목록으로 바로잡혔다
        }

        [Test]
        public void 모두받기가_하나도_못_주면_그_이유로_거절된다()
        {
            // 서버만 다음 날로 넘어가 이 기기의 임무 Id가 모두 어제 것이 됐다.
            var staleId = MissionId("login");
            _server.SkipDay();

            var error = Assert.Throws<QuestServerException>(
                () => Wait(_store.ReceiveAllAsync(QuestGroup.DailyMission)));

            Assert.That(error.Error, Is.EqualTo(QuestError.Expired));
            Assert.That(MissionId("login"), Is.Not.EqualTo(staleId)); // 목록을 새로 받았다
        }

        [Test]
        public void 일간_주간_월간이_겹치면_한_신호로_받는다()
        {
            // 2026-10-01 10시는 일간 · 주간(목요일) · 월간(1일) 초기화가 겹친다. 그 한 틱 전에 앱을 켠다.
            TearDown();
            Open(Start - Tick);

            Advance(Tick);

            Assert.That(_signals.Count, Is.EqualTo(1));
            var signal = _signals[0];
            Assert.That(signal.Daily && signal.Weekly && signal.Monthly, Is.True);
            Assert.That(signal.FromServer, Is.False);
            Assert.That(signal.At, Is.EqualTo(Start));
            Assert.That(_store.HasReceivableMission.CurrentValue, Is.False); // 일일 임무는 클라가 먼저 비웠다
        }

        [Test]
        public void 초기화를_받는_쪽_하나가_예외를_던져도_나머지는_받는다()
        {
            // 시계 구독자가 여럿이어도(초기화 · 회복 · 표시) 하나의 고장이 다른 쪽을 막으면 안 된다.
            // R3는 구독할 때의 예외 처리기를 쓰므로, 처리기를 바꾼 뒤에 앱을 다시 켠다.
            var errors = new List<Exception>();
            var previous = ObservableSystem.GetUnhandledExceptionHandler();
            ObservableSystem.RegisterUnhandledExceptionHandler(errors.Add);
            try
            {
                TearDown();
                Open(Start);
                var after = 0;
                _resets.Reset += _ => throw new InvalidOperationException("고장 난 구독자");
                _resets.Reset += _ => after++;

                Advance(UnixSpan.FromDays(1));

                Assert.That(after, Is.EqualTo(1)); // 고장 난 구독자 뒤에 붙은 쪽도 이 초기화를 받았다
                Assert.That(_store.HasReceivableMission.CurrentValue, Is.False); // Store도 먼저 초기화했다
                Assert.That(errors.Count, Is.EqualTo(1)); // 예외는 삼키지 않고 R3가 넘겼다(게임에서는 콘솔 로그)
            }
            finally
            {
                ObservableSystem.RegisterUnhandledExceptionHandler(previous);
            }
        }

        // start 시각에 앱을 켠 것처럼 만든다. 게임의 AppLifetimeScope처럼 ResetWatcher가 시계를 구독한다.
        void Open(UnixTime start)
        {
            _serverTime = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(start.ToUnixMilliseconds()));
            UnixTime.TimeProvider = _serverTime;
            _clock = new Clock100ms(start);
            _resets = new ResetWatcher(ResetSchedule.Default);
            _watching = _resets.Watch(_clock.Now);
            _signals = new List<ResetSignal>();
            _resets.Reset += signal => _signals.Add(signal);
            _server = new FakeQuestServer(Options);
            _counting = new CountingServer(_server);
            // Store는 만들어지자마자 목록을 받는다. 가짜 서버는 지연이 0이라 여기서 끝난다.
            _store = new QuestStore(_counting, _clock, _resets);
        }

        // 서버와 클라의 시계를 같이 옮긴다(시계가 맞는 기기).
        void Advance(UnixSpan by)
        {
            _serverTime.Advance(by.ToTimeSpan());
            _clock.Set(_clock.Now.CurrentValue + by);
        }

        // 클라 시계만 한 틱씩 여러 번 옮긴다.
        void Ticks(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _clock.Set(_clock.Now.CurrentValue + Tick);
            }
        }

        // 한국 시간으로 읽은 시각. 달력 날짜를 UnixTime으로 바꿀 때만 DateTime을 거친다.
        static UnixTime KstTime(int year, int month, int day, int hour)
        {
            return new UnixTime(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc)) - UnixTimeExtensions.Kst;
        }

        string MissionId(string key)
        {
            return _store.DailyMissions.CurrentValue.First(quest => quest.Id.StartsWith(key + "@", StringComparison.Ordinal)).Id;
        }

        static void Wait(UniTask task)
        {
            task.GetAwaiter().GetResult();
        }

        static T Wait<T>(UniTask<T> task)
        {
            return task.GetAwaiter().GetResult();
        }

        /// <summary>목록 요청 횟수와 모두 받기로 보낸 Id를 기록한다. 나머지는 그대로 넘긴다.</summary>
        sealed class CountingServer : IQuestServer
        {
            readonly IQuestServer _inner;

            public CountingServer(IQuestServer inner)
            {
                _inner = inner;
            }

            public int BoardRequests { get; private set; }

            public IReadOnlyList<string> SentIds { get; private set; } = Array.Empty<string>();

            public UniTask<QuestBoard> GetBoardAsync(CancellationToken cancellationToken)
            {
                BoardRequests++;
                return _inner.GetBoardAsync(cancellationToken);
            }

            public UniTask<ReceiveResponse> ReceiveAsync(QuestGroup group, string questId, CancellationToken cancellationToken)
            {
                return _inner.ReceiveAsync(group, questId, cancellationToken);
            }

            public UniTask<ReceiveResponse> ReceiveAllAsync(QuestGroup group, IReadOnlyList<string> questIds, CancellationToken cancellationToken)
            {
                SentIds = questIds.ToArray();
                return _inner.ReceiveAllAsync(group, questIds, cancellationToken);
            }
        }
    }
}
