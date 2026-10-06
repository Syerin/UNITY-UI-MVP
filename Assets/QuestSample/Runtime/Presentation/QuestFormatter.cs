using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuestSample.Domain;
using QuestSample.Server;

namespace QuestSample.Presentation
{
    /// <summary>화면에 보일 문구와 순서를 정한다. 순수 함수라서 Unity 없이 테스트한다.</summary>
    public static class QuestFormatter
    {
        public static IReadOnlyList<QuestRowData> ToRows(IReadOnlyList<Quest> quests)
        {
            // 받을 수 있는 것을 위로, 받은 것을 아래로. 같은 단계 안에서는 서버 순서를 지킨다(OrderBy는 안정 정렬).
            return quests.OrderBy(quest => SortOrder(quest.State)).Select(ToRow).ToArray();
        }

        public static QuestRowData ToRow(Quest quest)
        {
            var state = quest.State;
            var shown = Math.Min(quest.Progress, quest.Target);
            return new QuestRowData(
                quest.Id,
                quest.Title,
                Number(shown) + " / " + Number(quest.Target),
                quest.Target <= 0 ? 1f : (float)shown / quest.Target,
                RewardText(quest.Reward),
                ButtonText(state),
                state == QuestState.Receivable,
                state == QuestState.Complete);
        }

        public static string RewardText(Reward reward)
        {
            switch (reward.Kind)
            {
                case RewardKind.Gold: return "골드 " + Number(reward.Amount);
                case RewardKind.Gem: return "보석 " + Number(reward.Amount);
                case RewardKind.SeasonPoint: return "시즌 포인트 " + Number(reward.Amount);
                default: throw new ArgumentOutOfRangeException(nameof(reward));
            }
        }

        /// <summary>같은 종류는 합쳐서 한 줄로 보여 준다. 모두 받기에서 받지 못한 것이 있으면 그 개수를 붙인다.</summary>
        public static string GrantedText(IReadOnlyList<Reward> rewards, int rejected = 0)
        {
            var merged = rewards
                .GroupBy(reward => reward.Kind)
                .Select(group => RewardText(new Reward(group.Key, group.Sum(reward => reward.Amount))));
            var text = "보상 획득 · " + string.Join(", ", merged);
            return rejected > 0 ? text + " (" + rejected + "개는 받지 못했습니다)" : text;
        }

        public static string ErrorText(QuestError error)
        {
            switch (error)
            {
                case QuestError.AlreadyReceived: return "이미 받은 보상입니다";
                case QuestError.NotCompleted: return "아직 조건을 채우지 않았습니다";
                case QuestError.Expired: return "초기화되어 받을 수 없습니다. 목록을 새로 불러왔습니다";
                case QuestError.NothingToReceive: return "받을 보상이 없습니다";
                default: return "통신 오류입니다. 잠시 후 다시 시도해 주세요";
            }
        }

        public static string SeasonPointsText(long points)
        {
            return "시즌 포인트 " + Number(points);
        }

        // 남은 시간은 UnixTime의 문구를 그대로 쓴다. 하루가 넘으면 일수를 붙이고, 지났으면 00:00:00.
        public static string CountdownText(QuestGroup group, UnixTime resetAt, UnixTime now)
        {
            var remaining = UnixTimeExtensions.FormatRemaining(resetAt - now);
            return group == QuestGroup.DailyMission ? "초기화까지 " + remaining : "시즌 종료까지 " + remaining;
        }

        static string ButtonText(QuestState state)
        {
            switch (state)
            {
                case QuestState.Receivable: return "받기";
                case QuestState.Complete: return "완료";
                default: return "진행 중";
            }
        }

        static int SortOrder(QuestState state)
        {
            switch (state)
            {
                case QuestState.Receivable: return 0;
                case QuestState.Progress: return 1;
                default: return 2;
            }
        }

        static string Number(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
