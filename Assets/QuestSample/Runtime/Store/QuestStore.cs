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
    /// 클라가 먼저 하는 일은 초기화 하나뿐이고(일일 임무는 매일, 시즌 패스는 매월), 그것도 다음 서버 응답이 덮어쓴다. 버튼 없이는 서버에 묻지 않는다.
    /// 초기화 시각을 넘었는지는 ResetWatcher 하나가 정한다 — 일간 · 주간 · 월간이 겹쳐도 한 신호로 온다.
    /// 화면은 여기서 만든 값을 구독하기만 하고, 받을 수 있는지 다시 계산하지 않는다.
    /// </summary>
    public sealed class QuestStore : IDisposable
    {
        readonly IQuestServer _server;
        readonly ResetWatcher _resets;
        readonly ReactiveProperty<QuestBoard> _board = new ReactiveProperty<QuestBoard>(QuestBoard.Empty);
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly CompositeDisposable _disposables = new CompositeDisposable();
        bool _loaded;

        public QuestStore(IQuestServer server, Clock100ms clock, ResetWatcher resets)
        {
            _server = server;
            _resets = resets;

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

            // 처음 목록은 앱이 시작할 때 받는다. 목록이 없으면 아무것도 할 수 없으니, 실패하면 다음 틱에 다시 받는다.
            clock.Now
                .Where(_ => !_loaded)
                .SubscribeAwait((_, cancellationToken) => LoadAsync(cancellationToken), AwaitOperation.Drop)
                .AddTo(_disposables);

            // 그 뒤로는 버튼 없이 서버에 묻지 않는다. 초기화 시각을 넘기면 ResetWatcher가 신호를 내고,
            // 클라에서 먼저 초기화한 뒤 다음 서버 응답이 덮어쓴다. 화면이 닫혀 있어도 한다 — 로비 레드닷이 맞아야 하므로.
            _resets.Reset += OnReset;
        }

        public ReadOnlyReactiveProperty<IReadOnlyList<Quest>> DailyMissions { get; }
        public ReadOnlyReactiveProperty<IReadOnlyList<Quest>> PassTiers { get; }
        public ReadOnlyReactiveProperty<long> SeasonPoints { get; }
        public ReadOnlyReactiveProperty<bool> HasReceivableMission { get; }
        public ReadOnlyReactiveProperty<bool> HasReceivablePass { get; }
        public ReadOnlyReactiveProperty<bool> HasAnyReceivable { get; }

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

        public async UniTask RefreshAsync(CancellationToken cancellationToken)
        {
            Accept(await _server.GetBoardAsync(cancellationToken));
        }

        // 받기는 화면 수명과 상관없이 끝까지 간다. 서버가 이미 처리했을 수 있으므로 응답은 반드시 반영한다.
        // 그래서 취소 토큰을 받지 않는다 — 앱이 끝날 때(Store가 사라질 때)만 멈춘다.
        public UniTask<ReceiveResponse> ReceiveAsync(QuestGroup group, string questId)
        {
            return ReceiveCoreAsync(_server.ReceiveAsync(group, questId, _lifetime.Token));
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

            return ReceiveCoreAsync(_server.ReceiveAllAsync(group, questIds, _lifetime.Token));
        }

        public void Dispose()
        {
            _resets.Reset -= OnReset;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _disposables.Dispose();
            _board.Dispose();
        }

        async UniTask<ReceiveResponse> ReceiveCoreAsync(UniTask<ReceiveResponse> request)
        {
            try
            {
                var response = await request;
                Accept(response.Board);
                return response;
            }
            catch (QuestServerException e) when (e.Error == QuestError.Expired)
            {
                // 초기화가 지난 목록으로 요청했다. 목록을 새로 받고, 거절 이유는 그대로 올려 보낸다.
                await RefreshAsync(_lifetime.Token);
                throw;
            }
        }

        async ValueTask LoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (QuestServerException)
            {
                // 다음 틱에 다시 받는다.
            }
        }

        // 서버 응답은 모두 여기로 들어온다.
        void Accept(QuestBoard board)
        {
            // 서버 시각까지의 초기화는 이 응답에 이미 반영돼 있다. 목록을 통째로 바꾸니 신호는 필요 없고, 기준만 맞춘다.
            // 기준은 앞으로만 가므로(ResetWatcher), 기기 시계가 빨라도(보정하지 않는다) 같은 초기화를 되풀이하지 않는다.
            if (_loaded)
            {
                _resets.ApplyServer(false, false, false, board.ServerTime);
            }
            else
            {
                _resets.Begin(board.ServerTime);
            }

            _loaded = true;
            _board.Value = board;
        }

        // 초기화 시각을 넘겼다는 신호. 서버에 묻지 않고 먼저 되돌린다 — 일일 임무는 일간, 시즌 패스는 월간 신호로.
        void OnReset(ResetSignal signal)
        {
            if (signal.FromServer)
            {
                return; // 서버가 알린 초기화는 응답이 이미 목록을 덮어썼다.
            }

            // 겹쳐 와도(예: 1일 10시) 목록은 한 번만 바꾼다. 주간 주기로 초기화되는 컨텐츠는 이 샘플에 없다.
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
        }

        // 보드에서 값을 뽑아 Store의 프로퍼티로 만든다. 값이 같으면 다시 알리지 않는다.
        ReadOnlyReactiveProperty<T> FromBoard<T>(Func<QuestBoard, T> selector)
        {
            return _board.Select(selector).ToReadOnlyReactiveProperty().AddTo(_disposables);
        }
    }
}
