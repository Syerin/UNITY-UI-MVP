namespace QuestSample.Domain
{
    /// <summary>
    /// 진행도를 따로 갖지 않는다. 시즌 포인트 하나를 모든 단계가 함께 쓴다.
    /// 일일 임무와 다른 점은 이것과 끝나는 시점(시즌 종료)뿐이다.
    /// </summary>
    public sealed record PassTier(string Id, int Level, long RequiredPoints, bool Received, Reward Reward)
    {
        public Quest ToQuest(long seasonPoints)
        {
            return new Quest(Id, "Lv." + Level, seasonPoints, RequiredPoints, Received, Reward);
        }
    }
}
