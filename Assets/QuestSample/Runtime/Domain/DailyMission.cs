namespace QuestSample.Domain
{
    /// <summary>진행도를 임무마다 따로 가진다. 매일 초기화된다.</summary>
    public sealed record DailyMission(string Id, string Title, long Progress, long Target, bool Received, Reward Reward)
    {
        public Quest ToQuest()
        {
            return new Quest(Id, Title, Progress, Target, Received, Reward);
        }
    }
}
