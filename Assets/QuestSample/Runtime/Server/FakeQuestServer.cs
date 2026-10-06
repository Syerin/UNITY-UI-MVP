using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QuestSample.Domain;

namespace QuestSample.Server
{
    public sealed class FakeQuestServerOptions
    {
        public FakeQuestServerOptions(UnixSpan latency)
        {
            Latency = latency;
        }

        public UnixSpan Latency { get; }
    }

    /// <summary>
    /// 실제 서버 대신 쓰는 메모리 서버. 응답 지연 · 실패를 바꿀 수 있어 데모와 테스트에 같이 쓴다.
    /// 초기화 규칙은 클라와 같은 ResetSchedule.Default다 — 일일 임무는 매일 한국 시간 오전 10시, 시즌 패스는 매월 1일 10시에 새로 시작한다.
    /// </summary>
    public sealed class FakeQuestServer : IQuestServer
    {
        public const string BattleMission = "battle";
        public const string EnhanceMission = "enhance";
        const string LoginMission = "login";

        static readonly ResetSchedule Schedule = ResetSchedule.Default;

        static readonly MissionSpec[] Missions =
        {
            new MissionSpec(LoginMission, "게임 접속하기", 1, new Reward(RewardKind.SeasonPoint, 100)),
            new MissionSpec(BattleMission, "전투에서 3회 승리", 3, new Reward(RewardKind.SeasonPoint, 150)),
            new MissionSpec(EnhanceMission, "장비 2회 강화", 2, new Reward(RewardKind.SeasonPoint, 100)),
            new MissionSpec("recruit", "동료 1회 모집", 1, new Reward(RewardKind.SeasonPoint, 50)),
        };

        static readonly TierSpec[] Tiers =
        {
            new TierSpec(1, 100, new Reward(RewardKind.Gold, 1000)),
            new TierSpec(2, 200, new Reward(RewardKind.Gem, 30)),
            new TierSpec(3, 300, new Reward(RewardKind.Gold, 2000)),
            new TierSpec(4, 400, new Reward(RewardKind.Gem, 50)),
            new TierSpec(5, 500, new Reward(RewardKind.Gold, 3000)),
            new TierSpec(6, 600, new Reward(RewardKind.Gem, 80)),
            new TierSpec(7, 700, new Reward(RewardKind.Gold, 5000)),
            new TierSpec(8, 800, new Reward(RewardKind.Gem, 150)),
        };

        readonly FakeQuestServerOptions _options;
        readonly Dictionary<string, long> _progress = new Dictionary<string, long>();
        readonly HashSet<string> _receivedMissions = new HashSet<string>();
        readonly HashSet<string> _receivedTiers = new HashSet<string>();
        long _seasonPoints;
        long _day = -1;
        long _season = -1;
        long _skippedDays;
        QuestError? _failNext;

        public FakeQuestServer(FakeQuestServerOptions options)
        {
            _options = options;
        }

        // 서버의 지금: 앱과 같은 UnixTime.Now를 본다(테스트에서는 가짜 시계). 데모의 '서버만 다음 날로'는 이 시계만 하루씩 앞당긴다.
        UnixTime Now => UnixTime.Now + SkippedTime;

        UnixSpan SkippedTime => UnixSpan.FromDays(_skippedDays);

        // 데모 도구가 부르는 것. 실제 게임에서는 전투 · 강화 결과가 서버에 기록된다.
        public void AddProgress(string missionKey, long amount)
        {
            Sync();
            _progress[missionKey] = ProgressOf(missionKey) + amount;
        }

        /// <summary>서버만 다음 날로 넘긴다. 클라이언트는 아직 어제 목록을 들고 있다.</summary>
        public void SkipDay()
        {
            _skippedDays++;
        }

        public void FailNextRequest(QuestError error)
        {
            _failNext = error;
        }

        public async UniTask<QuestBoard> GetBoardAsync(CancellationToken cancellationToken)
        {
            await BeginRequestAsync(cancellationToken);
            return Snapshot();
        }

        public async UniTask<ReceiveResponse> ReceiveAsync(QuestGroup group, string questId, CancellationToken cancellationToken)
        {
            await BeginRequestAsync(cancellationToken);
            var reward = Receive(group, questId);
            return new ReceiveResponse(Snapshot(), new[] { reward }, Array.Empty<string>());
        }

        public async UniTask<ReceiveResponse> ReceiveAllAsync(QuestGroup group, IReadOnlyList<string> questIds, CancellationToken cancellationToken)
        {
            await BeginRequestAsync(cancellationToken);

            // 목록을 탐색하지 않고 보낸 Id만 하나씩 검증한다. 통과한 것만 주고, 걸린 Id는 따로 돌려준다.
            // 같은 Id가 두 번 오면 두 번째는 이미 받은 것으로 걸린다.
            var granted = new List<Reward>();
            var rejected = new List<string>();
            QuestError? firstError = null;
            foreach (var questId in questIds)
            {
                try
                {
                    granted.Add(Receive(group, questId));
                }
                catch (QuestServerException e)
                {
                    rejected.Add(questId);
                    firstError ??= e.Error;
                }
            }

            // 하나도 주지 못했으면 그 이유로 거절한다. 초기화가 지난 목록이면 Expired라서 클라가 목록을 새로 받는다.
            if (granted.Count == 0)
            {
                throw new QuestServerException(firstError ?? QuestError.NothingToReceive);
            }

            return new ReceiveResponse(Snapshot(), granted, rejected);
        }

        async UniTask BeginRequestAsync(CancellationToken cancellationToken)
        {
            if (_options.Latency > UnixSpan.Zero)
            {
                await UniTask.Delay(_options.Latency.ToTimeSpan(), ignoreTimeScale: true, cancellationToken: cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_failNext.HasValue)
            {
                var error = _failNext.Value;
                _failNext = null;
                throw new QuestServerException(error);
            }

            Sync();
        }

        Reward Receive(QuestGroup group, string questId)
        {
            return group == QuestGroup.DailyMission ? ReceiveMission(questId) : ReceiveTier(questId);
        }

        Reward ReceiveMission(string questId)
        {
            // 임무 Id에는 날짜가 붙어 있다. 어제 목록으로 요청하면 거절한다.
            var spec = FindMission(KeyOf(questId, _day));
            ThrowIfNotReceivable(QuestRules.StateOf(ProgressOf(spec.Key), spec.Target, _receivedMissions.Contains(spec.Key)));
            _receivedMissions.Add(spec.Key);
            return Grant(spec.Reward);
        }

        Reward ReceiveTier(string questId)
        {
            // 단계 Id에는 시즌이 붙어 있다. 지난 시즌 목록으로 요청하면 거절한다.
            var spec = FindTier(KeyOf(questId, _season));
            ThrowIfNotReceivable(QuestRules.StateOf(_seasonPoints, spec.RequiredPoints, _receivedTiers.Contains(spec.Key)));
            _receivedTiers.Add(spec.Key);
            return Grant(spec.Reward);
        }

        // Id 끝의 @번호가 지금의 날(시즌)과 같을 때만 앞의 키를 돌려준다. 다르면 초기화가 지난 목록이다.
        static string KeyOf(string questId, long current)
        {
            var separator = questId.LastIndexOf('@');
            if (separator < 0 || questId.Substring(separator + 1) != current.ToString())
            {
                throw new QuestServerException(QuestError.Expired);
            }

            return questId.Substring(0, separator);
        }

        static void ThrowIfNotReceivable(QuestState state)
        {
            switch (state)
            {
                case QuestState.Complete:
                    throw new QuestServerException(QuestError.AlreadyReceived);
                case QuestState.Progress:
                    throw new QuestServerException(QuestError.NotCompleted);
            }
        }

        Reward Grant(Reward reward)
        {
            // 골드 · 보석 지갑은 이 샘플의 범위 밖이다. 시즌 포인트만 쌓는다.
            if (reward.Kind == RewardKind.SeasonPoint)
            {
                _seasonPoints += reward.Amount;
            }

            return reward;
        }

        void Sync()
        {
            // 몇째 날인지: 가장 최근 일일 초기화 시각의 날짜 번호(한국 시간). 임무 Id에 붙여 날짜가 바뀐 목록을 가려낸다.
            var day = Schedule.LastDaily(Now).DayNumber();
            if (day != _day)
            {
                _day = day;
                _progress.Clear();
                _receivedMissions.Clear();
                _progress[LoginMission] = 1; // 접속하면 접속 임무는 바로 완료된다.
            }

            // 몇째 시즌인지: 가장 최근 월간 초기화 시각의 날짜 번호. 단계 Id에 붙여 시즌이 바뀐 목록을 가려낸다.
            var season = Schedule.LastMonthly(Now).DayNumber();
            if (season != _season)
            {
                _season = season;
                _seasonPoints = 0;
                _receivedTiers.Clear();
            }
        }

        QuestBoard Snapshot()
        {
            var missions = new DailyMission[Missions.Length];
            for (var i = 0; i < missions.Length; i++)
            {
                var spec = Missions[i];
                missions[i] = new DailyMission(
                    spec.Key + "@" + _day, spec.Title, ProgressOf(spec.Key), spec.Target, _receivedMissions.Contains(spec.Key), spec.Reward);
            }

            var tiers = new PassTier[Tiers.Length];
            for (var i = 0; i < tiers.Length; i++)
            {
                var spec = Tiers[i];
                tiers[i] = new PassTier(spec.Key + "@" + _season, spec.Level, spec.RequiredPoints, _receivedTiers.Contains(spec.Key), spec.Reward);
            }

            // 시각은 클라이언트 시계 기준으로 보낸다(앞당긴 날만큼 뺀다).
            return new QuestBoard(missions, _seasonPoints, tiers, Now - SkippedTime);
        }

        long ProgressOf(string missionKey)
        {
            return _progress.TryGetValue(missionKey, out var progress) ? progress : 0;
        }

        static MissionSpec FindMission(string key)
        {
            foreach (var spec in Missions)
            {
                if (spec.Key == key)
                {
                    return spec;
                }
            }

            throw new InvalidOperationException("Unknown mission: " + key);
        }

        static TierSpec FindTier(string key)
        {
            foreach (var spec in Tiers)
            {
                if (spec.Key == key)
                {
                    return spec;
                }
            }

            throw new InvalidOperationException("Unknown tier: " + key);
        }

        sealed record MissionSpec(string Key, string Title, long Target, Reward Reward);

        sealed record TierSpec(int Level, long RequiredPoints, Reward Reward)
        {
            public string Key => "tier" + Level;
        }
    }
}
