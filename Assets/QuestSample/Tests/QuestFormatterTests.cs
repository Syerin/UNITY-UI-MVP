using System;
using System.Linq;
using NUnit.Framework;
using QuestSample.Domain;
using QuestSample.Presentation;

namespace QuestSample.Tests
{
    public sealed class QuestFormatterTests
    {
        [Test]
        public void 받을수있는것이_위로_받은것이_아래로_간다()
        {
            var quests = new[] { Quest("진행 중", 0, 3, false), Quest("완료", 3, 3, true), Quest("받기", 3, 3, false) };

            var titles = QuestFormatter.ToRows(quests).Select(row => row.Title);

            Assert.That(titles, Is.EqualTo(new[] { "받기", "진행 중", "완료" }));
        }

        [Test]
        public void 진행도는_목표를_넘어도_목표까지만_보인다()
        {
            var row = QuestFormatter.ToRow(Quest("Lv.4", 1200, 400, false));

            Assert.That(row.ProgressText, Is.EqualTo("400 / 400"));
            Assert.That(row.ProgressRatio, Is.EqualTo(1f));
            Assert.That(row.CanReceive, Is.True);
        }

        [Test]
        public void 같은_종류의_보상은_합쳐서_보여준다()
        {
            var rewards = new[]
            {
                new Reward(RewardKind.SeasonPoint, 100),
                new Reward(RewardKind.SeasonPoint, 150),
                new Reward(RewardKind.Gold, 1000),
            };

            Assert.That(QuestFormatter.GrantedText(rewards), Is.EqualTo("보상 획득 · 시즌 포인트 250, 골드 1,000"));
        }

        [Test]
        public void 모두_받기에서_받지_못한_것이_있으면_개수를_붙인다()
        {
            var text = QuestFormatter.GrantedText(new[] { new Reward(RewardKind.Gold, 1000) }, 1);

            Assert.That(text, Is.EqualTo("보상 획득 · 골드 1,000 (1개는 받지 못했습니다)"));
        }

        [TestCase(1, 2, 3, 4, "초기화까지 1일 02:03:04")]
        [TestCase(0, 2, 3, 4, "초기화까지 02:03:04")]
        [TestCase(0, 0, 0, -5, "초기화까지 00:00:00")]
        public void 남은_시간은_하루가_넘으면_일수를_붙인다(int days, int hours, int minutes, int seconds, string expected)
        {
            var now = new UnixTime(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            var left = UnixSpan.FromDays(days) + UnixSpan.FromHours(hours) + UnixSpan.FromMinutes(minutes) + UnixSpan.FromSeconds(seconds);

            Assert.That(QuestFormatter.CountdownText(QuestGroup.DailyMission, now + left, now), Is.EqualTo(expected));
        }

        static Quest Quest(string title, long progress, long target, bool received)
        {
            return new Quest(title, title, progress, target, received, new Reward(RewardKind.Gold, 1));
        }
    }
}
