using NUnit.Framework;
using QuestSample.Domain;

namespace QuestSample.Tests
{
    public sealed class QuestRulesTests
    {
        [TestCase(0, 3, false, QuestState.Progress)]
        [TestCase(3, 3, false, QuestState.Receivable)]
        [TestCase(5, 3, false, QuestState.Receivable)]
        [TestCase(0, 3, true, QuestState.Complete)]
        public void 상태는_진행도와_목표와_받음여부로만_정해진다(long progress, long target, bool received, QuestState expected)
        {
            Assert.That(QuestRules.StateOf(progress, target, received), Is.EqualTo(expected));
        }

        [Test]
        public void 시즌패스_단계는_시즌포인트를_진행도로_쓴다()
        {
            var tier = new PassTier("tier2", 2, 200, false, new Reward(RewardKind.Gold, 1000));

            Assert.That(tier.ToQuest(150).State, Is.EqualTo(QuestState.Progress));
            Assert.That(tier.ToQuest(200).State, Is.EqualTo(QuestState.Receivable));
        }

        [Test]
        public void 받기가능이_하나라도_있으면_레드닷이다()
        {
            var inProgress = Quest("a", 0, 1, false);
            var received = Quest("b", 1, 1, true);
            var receivable = Quest("c", 2, 2, false);

            Assert.That(QuestRules.HasReceivable(new[] { inProgress, received, receivable }), Is.True);
            Assert.That(QuestRules.HasReceivable(new[] { inProgress, received }), Is.False);
        }

        static Quest Quest(string id, long progress, long target, bool received)
        {
            return new Quest(id, id, progress, target, received, new Reward(RewardKind.Gold, 1));
        }
    }
}
