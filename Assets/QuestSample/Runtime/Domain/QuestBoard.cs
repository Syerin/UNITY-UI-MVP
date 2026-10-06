using System;
using System.Collections.Generic;

namespace QuestSample.Domain
{
    /// <summary>
    /// 서버가 내려준 한 시점의 스냅샷. 클라이언트는 이 값을 고치지 않고 통째로 바꾼다.
    /// 두 컨텐츠는 모양이 달라도(임무별 진행도 / 공유 포인트) QuestsOf에서 같은 Quest 모양이 된다.
    /// 시각은 서버가 ms로 주는 값을 UnixTime으로 받은 것이다. 초기화 시각은 담지 않는다 — 클라와 서버가 같은 일정(ResetSchedule)을 쓴다.
    /// </summary>
    public sealed class QuestBoard
    {
        public static readonly QuestBoard Empty = new QuestBoard(
            Array.Empty<DailyMission>(), 0, Array.Empty<PassTier>(), UnixTime.Epoch);

        public QuestBoard(
            IReadOnlyList<DailyMission> missions,
            long seasonPoints,
            IReadOnlyList<PassTier> passTiers,
            UnixTime serverTime)
        {
            Missions = missions;
            SeasonPoints = seasonPoints;
            PassTiers = passTiers;
            ServerTime = serverTime;
        }

        public IReadOnlyList<DailyMission> Missions { get; }
        public long SeasonPoints { get; }
        public IReadOnlyList<PassTier> PassTiers { get; }

        /// <summary>서버가 이 스냅샷을 만든 시각. 이 시각까지의 초기화는 스냅샷에 이미 반영돼 있다.</summary>
        public UnixTime ServerTime { get; }

        /// <summary>
        /// 클라가 먼저 하는 일일 초기화: 일일 임무의 진행도와 받음을 되돌린 새 스냅샷. 시즌 패스는 그대로다.
        /// 서버 규칙(접속하면 접속 임무 완료 등)은 모르므로 모두 0으로 되돌리고, 다음 서버 응답이 이 값을 덮어쓴다.
        /// </summary>
        public QuestBoard WithDailyReset()
        {
            var missions = new DailyMission[Missions.Count];
            for (var i = 0; i < missions.Length; i++)
            {
                missions[i] = Missions[i] with { Progress = 0, Received = false };
            }

            return new QuestBoard(missions, SeasonPoints, PassTiers, ServerTime);
        }

        /// <summary>
        /// 클라가 먼저 하는 월간 초기화(새 시즌): 시즌 포인트와 단계 받음을 되돌린 새 스냅샷. 일일 임무는 그대로다.
        /// 다음 서버 응답이 새 시즌의 목록으로 덮어쓴다.
        /// </summary>
        public QuestBoard WithSeasonReset()
        {
            var tiers = new PassTier[PassTiers.Count];
            for (var i = 0; i < tiers.Length; i++)
            {
                tiers[i] = PassTiers[i] with { Received = false };
            }

            return new QuestBoard(Missions, 0, tiers, ServerTime);
        }

        public IReadOnlyList<Quest> QuestsOf(QuestGroup group)
        {
            switch (group)
            {
                case QuestGroup.DailyMission:
                {
                    var quests = new Quest[Missions.Count];
                    for (var i = 0; i < quests.Length; i++)
                    {
                        quests[i] = Missions[i].ToQuest();
                    }

                    return quests;
                }
                case QuestGroup.SeasonPass:
                {
                    var quests = new Quest[PassTiers.Count];
                    for (var i = 0; i < quests.Length; i++)
                    {
                        quests[i] = PassTiers[i].ToQuest(SeasonPoints);
                    }

                    return quests;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(group), group, null);
            }
        }
    }
}
