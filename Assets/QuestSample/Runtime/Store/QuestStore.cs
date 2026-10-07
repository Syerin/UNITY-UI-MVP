using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using QuestSample.Domain;
using QuestSample.Server;
using QuestSample.Timing;
using R3;

namespace QuestSample.Store
{
    /// <summary>
    /// 앱이 살아 있는 동안 하나. 상태는 서버의 성공 응답이 정한다.
    /// 서버 요청은 한 번에 하나다(받기 · 모두 받기 · 목록 받기). 화면을 닫았다 다시 열어도, 진행 중인 요청이 끝나기 전에는 다음 요청을 보내지 않는다.
    /// 응답은 리비전이 지금보다 낮으면(늦게 도착한 옛 스냅샷) 버린다.
    /// 목록 받기가 실패하면 1초부터 두 배씩, 최대 30초 간격으로 다시 받는다(지터 포함).
    /// 클라가 먼저 하는 일은 초기화 하나뿐이고(일일 임무는 매일, 시즌 패스는 매월), 초기화 시각에서 0~60초 무작위로 늦춰
    /// 목록을 한 번 다시 받아 서버 규칙(접속하면 접속 임무 완료 등)으로 바로잡는다.
    /// 받은 목록의 서버 시각이 그 초기화 전이면(기기 시계가 빠름, 초기화 직전 요청의 늦은 응답) 서버가 초기화를 넘긴 뒤 한 번 더 받는다.
    /// 받기가 실패하면 목록을 서버 상태로 맞춘다 — 거절이면 바로 새로 받고, 통신 오류면(서버가 처리했을 수 있다) 다시 받기를 예약한다.
    /// 초기화 시각을 넘었는지는 ResetWatcher 하나가 정한다 — 일간 · 월간이 겹쳐도 한 신호로 온다.
    /// 화면은 여기서 만든 값을 구독하기만 하고, 받을 수 있는지 다시 계산하지 않는다.
    /// </summary>
    public sealed class QuestStore : IDisposable
    {
        readonly IQuestServer _server;
        readonly Clock100ms _clock;
        readonly ResetWatcher _resets;
        readonly QuestStoreOptions _options;
        readonly ReactiveProperty<QuestBoard> _board = new ReactiveProperty<QuestBoard>(QuestBoard.Empty);
        readonly ReactiveProperty<bool> _isRequesting = new ReactiveProperty<bool>(false);
        readonly Subject<UnixSpan> _refreshRetryScheduled = new Subject<UnixSpan>();
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly CompositeDisposable _disposables = new CompositeDisposable();
        bool _loaded;

        // 목록을 다시 받을 시각. 처음 받기, 실패 뒤 재시도, 초기화 뒤 바로잡기가 모두 이것 하나로 예약된다.
        UnixTime? _refreshDueAt;
        int _refreshFailures;

        // 클라가 먼저 넘긴 초기화 시각 중 서버 응답으로 아직 확인하지 못한 것. 서버 시각이 이 시각을 넘긴 목록을 받으면 지운다.
        UnixTime? _unconfirmedResetAt;

        public QuestStore(IQuestServer server, Clock100ms clock, ResetWatcher resets, QuestStoreOptions options)
        {
            _server = server;
            _clock = clock;
            _resets = resets;
            _options = options;

            DailyMissions = FromBoard(board => board.QuestsOf(QuestGroup.DailyMission));
            PassTiers = FromBoard(board => board.QuestsOf(QuestGroup.SeasonPass));
            SeasonPoints = FromBoard(board => board.SeasonPoints);

            // 레드닷 규칙은 QuestRules 하나. ReadOnlyReactiveProperty는 값이 같으면 다시 알리지 않는다.
            HasReceivableMission = DailyMissions.Select(QuestRules.HasReceivable).ToReadOnlyReactiveProperty().AddTo(_disposables);
            HasReceivablePass = PassTiers.Select(QuestRules.HasReceivable).ToReadOnlyReactiveProperty().AddTo(_disposables);
            HasAnyReceivable = HasReceivableMission
                .CombineLatest(HasReceivablePass, (mission, pass) => mission || pass)
                .ToReadOnlyReactiveProperty()
                .AddTo(_disposables);

            // 예약한 목록 받기는 시계가 틱마다 확인해 보낸다. 처음 목록은 앱이 시작할 때 바로 받는다(구독하자마자 지금 시각이 온다).
            _refreshDueAt = clock.Now.CurrentValue;
            clock.Now
                .Where(now => _refreshDueAt.HasValue && now >= _refreshDueAt.Value && !_isRequesting.Value)
                .SubscribeAwait((_, cancellationToken) => RefreshScheduledAsync(cancellationToken), AwaitOperation.Drop)
                .AddTo(_disposables);

            // 그 밖에는 버튼 없이 서버에 묻지 않는다. 초기화 시각을 넘기면 ResetWatcher가 신호를 내고, 클라에서 먼저 되돌린 뒤
            // 잠시 뒤 한 번 다시 받는다. 화면이 닫혀 있어도 한다 — 로비 레드닷이 맞아야 하므로.
            _resets.Reset += OnReset;
        }

        public ReadOnlyReactiveProperty<IReadOnlyList<Quest>> DailyMissions { get; }
        public ReadOnlyReactiveProperty<IReadOnlyList<Quest>> PassTiers { get; }
        public ReadOnlyReactiveProperty<long> SeasonPoints { get; }
        public ReadOnlyReactiveProperty<bool> HasReceivableMission { get; }
        public ReadOnlyReactiveProperty<bool> HasReceivablePass { get; }
        public ReadOnlyReactiveProperty<bool> HasAnyReceivable { get; }

        /// <summary>서버 요청이 진행 중인지. 화면은 이 값이 true면 받기를 보내지 않는다(Store도 막는다).</summary>
        public ReadOnlyReactiveProperty<bool> IsRequesting => _isRequesting;

        /// <summary>목록 받기가 실패해 다시 시도를 예약했을 때, 그때까지 기다릴 시간이 온다.</summary>
        public Observable<UnixSpan> RefreshRetryScheduled => _refreshRetryScheduled;

        /// <summary>그 목록이 다음에 초기화되는 시각. 일일 임무는 매일, 시즌 패스는 매월 — ResetWatcher의 일정 하나를 같이 쓴다.</summary>
        public UnixTime NextReset(QuestGroup group, UnixTime now)
        {
            return group == QuestGroup.DailyMission ? _resets.Schedule.NextDaily(now) : _resets.Schedule.NextMonthly(now);
        }

        public Observable<IReadOnlyList<Quest>> Quests(QuestGroup group)
        {
            return group == QuestGroup.DailyMission ? DailyMissions : PassTiers;
        }

        public Observable<bool> HasReceivable(QuestGroup group)
        {
            return group == QuestGroup.DailyMission ? HasReceivableMission : HasReceivablePass;
        }

        public UniTask RefreshAsync(CancellationToken cancellationToken)
        {
            BeginRequest();
            return EndRequestAfter(FetchBoardAsync(cancellationToken));
        }

        // 받기는 화면 수명과 상관없이 끝까지 간다. 서버가 이미 처리했을 수 있으므로 응답은 반드시 반영한다.
        // 그래서 취소 토큰을 받지 않는다 — 앱이 끝날 때(Store가 사라질 때)만 멈춘다.
        public UniTask<ReceiveResponse> ReceiveAsync(QuestGroup group, string questId)
        {
            BeginRequest();
            return EndRequestAfter(ReceiveCoreAsync(() => _server.ReceiveAsync(group, questId, _lifetime.Token)));
        }

        // 모두 받기: 받을 수 있다고 보이는 Id만 모아 보낸다(QuestRules). 서버는 목록을 탐색하지 않고 보낸 Id만 검증해,
        // 통과한 것만 주고 걸린 Id는 응답의 Rejected로 돌려준다.
        public UniTask<ReceiveResponse> ReceiveAllAsync(QuestGroup group)
        {
            var questIds = new List<string>();
            foreach (var quest in _board.Value.QuestsOf(group))
            {
                if (quest.State == QuestState.Receivable)
                {
                    questIds.Add(quest.Id);
                }
            }

            BeginRequest();
            return EndRequestAfter(ReceiveCoreAsync(() => _server.ReceiveAllAsync(group, questIds, _lifetime.Token)));
        }

        public void Dispose()
        {
            _resets.Reset -= OnReset;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _disposables.Dispose();
            _board.Dispose();
            _isRequesting.Dispose();
            _refreshRetryScheduled.Dispose();
        }

        // 요청은 한 번에 하나. 화면이 IsRequesting을 보고 막으므로, 여기서 걸리면 부르는 쪽의 실수다.
        // 플래그는 부르는 즉시(같은 프레임 안에서) 켠다 — 연타의 두 번째 입력도 바로 막힌다.
        void BeginRequest()
        {
            if (_isRequesting.Value)
            {
                throw new InvalidOperationException("서버 요청이 이미 진행 중이다. IsRequesting을 보고 부른다.");
            }

            _isRequesting.Value = true;
        }

        async UniTask EndRequestAfter(UniTask request)
        {
            try
            {
                await request;
            }
            finally
            {
                _isRequesting.Value = false;
            }
        }

        async UniTask<T> EndRequestAfter<T>(UniTask<T> request)
        {
            try
            {
                return await request;
            }
            finally
            {
                _isRequesting.Value = false;
            }
        }

        async UniTask<ReceiveResponse> ReceiveCoreAsync(Func<UniTask<ReceiveResponse>> request)
        {
            try
            {
                var response = await Send(request);
                Accept(response.Board);
                return response;
            }
            catch (QuestServerException e) when (e.Error == QuestError.Network)
            {
                // 서버가 처리했는지 알 수 없다(처리한 뒤 응답만 유실됐을 수 있다). 다음 틱에 목록을 다시 받아 서버 상태로 맞춘다.
                // 연결이 끊긴 상태라면 예약한 받기가 실패하며 간격을 늘려 다시 시도한다. 사용자에게는 통신 오류를 알린다.
                ScheduleRefresh(_clock.Now.CurrentValue);
                throw;
            }
            catch (QuestServerException)
            {
                // 서버가 거절했다(초기화가 지난 목록 · 이미 받음 · 조건 미달 · 받을 것 없음). 어느 쪽이든 화면의 목록이 서버와 어긋났다는 뜻이라
                // 목록을 새로 받고, 거절 이유는 그대로 올려 보낸다.
                // 새로 받기마저 실패하면 예약해 두고(다음 틱부터, 실패하면 간격을 늘려) 사용자에게는 원래 이유를 알린다.
                try
                {
                    await FetchBoardAsync(_lifetime.Token);
                }
                catch (QuestServerException)
                {
                    ScheduleRefresh(_clock.Now.CurrentValue);
                }

                throw;
            }
        }

        async UniTask FetchBoardAsync(CancellationToken cancellationToken)
        {
            Accept(await Send(() => _server.GetBoardAsync(cancellationToken)));
        }

        // 예약한 목록 받기. 실패하면 간격을 늘려 다시 예약하고 알린다(화면은 토스트, 데모 패널은 상태 문구).
        async ValueTask RefreshScheduledAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshAsync(cancellationToken);

                // 받은 목록이 옛 스냅샷이라 버려졌으면 예약이 그대로 남는다. 틱마다 다시 묻지 않도록 실패처럼 간격을 늘린다(알리지는 않는다).
                if (_refreshDueAt.HasValue && _refreshDueAt.Value <= _clock.Now.CurrentValue)
                {
                    _refreshFailures++;
                    _refreshDueAt = _clock.Now.CurrentValue + _options.RetryDelay(_refreshFailures);
                }
            }
            catch (QuestServerException)
            {
                _refreshFailures++;
                var delay = _options.RetryDelay(_refreshFailures);
                _refreshDueAt = _clock.Now.CurrentValue + delay;
                _refreshRetryScheduled.OnNext(delay);
            }
        }

        // 이미 더 이른 예약이 있으면 그대로 둔다.
        void ScheduleRefresh(UnixTime at)
        {
            if (!_refreshDueAt.HasValue || at < _refreshDueAt.Value)
            {
                _refreshDueAt = at;
            }
        }

        // 서버 경계: 거절(QuestServerException)과 취소는 그대로 올리고, 전송 오류(타임아웃 · 연결 끊김)만 통신 오류로 바꾼다.
        // 화면은 QuestServerException 하나만 보면 된다. 그 밖의 예외(코드의 실수)는 통신 오류로 덮지 않고 그대로 올린다.
        static async UniTask<T> Send<T>(Func<UniTask<T>> request)
        {
            try
            {
                return await request();
            }
            catch (Exception e) when (IsTransportError(e))
            {
                throw new QuestServerException(QuestError.Network, e);
            }
        }

        // .NET 기본 전송 예외만 본다. 다른 통신 계층을 쓰면 IQuestServer 구현이 자기 예외를 이 중 하나나 Network 거절로 바꿔 던진다.
        static bool IsTransportError(Exception e)
        {
            return e is TimeoutException
                || e is System.IO.IOException
                || e is System.Net.WebException
                || e is System.Net.Sockets.SocketException;
        }

        // 서버 응답은 모두 여기로 들어온다. 리비전이 지금보다 낮으면 늦게 도착한 옛 스냅샷이라 버린다(같으면 같은 상태다).
        void Accept(QuestBoard board)
        {
            if (_loaded && board.Revision < _board.Value.Revision)
            {
                // 버린 응답으로 예약을 지우지 않는다 — 초기화 뒤 다시 받기가 남아 있을 수 있다.
                return;
            }

            _refreshFailures = 0;

            // 서버 시각까지의 초기화는 이 응답에 이미 반영돼 있다. 목록을 통째로 바꾸니 신호는 필요 없고, 기준만 맞춘다.
            // 기준은 앞으로만 가므로(ResetWatcher), 기기 시계가 빨라도(보정하지 않는다) 같은 초기화를 되풀이하지 않는다.
            _resets.SyncToServer(board.ServerTime);
            _loaded = true;
            _board.Value = board;

            // 클라가 먼저 넘긴 초기화보다 이른 서버 시각의 목록이다 — 기기 시계가 빠르거나, 초기화 직전에 보낸 요청의 응답이 늦게 왔다.
            // 서버는 아직 그 초기화 전이니, 서버가 그 시각을 넘긴 뒤(남은 차이 + 0~60초) 목록을 한 번 더 받는다.
            if (_unconfirmedResetAt.HasValue && board.ServerTime < _unconfirmedResetAt.Value)
            {
                _refreshDueAt = _clock.Now.CurrentValue + (_unconfirmedResetAt.Value - board.ServerTime) + _options.RefetchDelay();
                return;
            }

            // 서버에서 목록을 받았으니(받기 응답도 전체 목록이다) 예약해 둔 다시 받기는 필요 없다.
            _unconfirmedResetAt = null;
            _refreshDueAt = null;
        }

        // 초기화 시각을 넘겼다는 신호. 서버에 묻지 않고 먼저 되돌린다 — 일일 임무는 일간, 시즌 패스는 월간 신호로.
        void OnReset(ResetSignal signal)
        {
            // 겹쳐 와도(예: 1일 10시) 목록은 한 번만 바꾼다.
            var board = _board.Value;
            if (signal.Daily)
            {
                board = board.WithDailyReset();
            }

            if (signal.Monthly)
            {
                board = board.WithSeasonReset();
            }

            _board.Value = board;

            // 클라는 서버 규칙(접속하면 접속 임무 완료 등)을 모르니, 목록을 한 번 다시 받아 바로잡는다.
            // 모든 기기가 초기화 시각에 한꺼번에 묻지 않도록 초기화 시각에서 0~60초 사이로 퍼뜨린다(이미 지났으면 바로).
            // 서버 시각이 이 초기화를 넘긴 목록을 받을 때까지는 확인되지 않은 초기화로 둔다(Accept).
            _unconfirmedResetAt = signal.At;
            ScheduleRefresh(signal.At + _options.RefetchDelay());
        }

        // 보드에서 값을 뽑아 Store의 프로퍼티로 만든다. 값이 같으면 다시 알리지 않는다(목록은 매번 새 배열이라 셀이 record로 비교한다).
        ReadOnlyReactiveProperty<T> FromBoard<T>(Func<QuestBoard, T> selector)
        {
            return _board.Select(selector).ToReadOnlyReactiveProperty().AddTo(_disposables);
        }
    }
}
