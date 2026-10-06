using System.Collections.Generic;

namespace QuestSample.Domain
{
    /// <summary>
    /// 받을 수 있는지는 여기서만 정한다.
    /// 목록의 받기 버튼, 탭 레드닷, 로비 레드닷, 모두 받기 버튼이 모두 이 두 함수의 결과를 쓴다.
    /// </summary>
    public static class QuestRules
    {
        public static QuestState StateOf(long progress, long target, bool received)
        {
            if (received)
            {
                return QuestState.Complete;
            }

            return progress >= target ? QuestState.Receivable : QuestState.Progress;
        }

        public static bool HasReceivable(IReadOnlyList<Quest> quests)
        {
            for (var i = 0; i < quests.Count; i++)
            {
                if (quests[i].State == QuestState.Receivable)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
