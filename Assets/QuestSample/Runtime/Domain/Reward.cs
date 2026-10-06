namespace QuestSample.Domain
{
    public enum RewardKind
    {
        Gold,
        Gem,
        SeasonPoint,
    }

    public sealed record Reward(RewardKind Kind, long Amount);
}
