using NUnit.Framework;
using QuestSample.Timing;

namespace QuestSample.Tests
{
    public sealed class RecoveryTests
    {
        static readonly UnixTime Anchor = UnixTime.FromUnixMilliseconds(1_790_000_000_000);
        static readonly UnixSpan FiveMinutes = UnixSpan.FromMinutes(5);

        [Test]
        public void 간격마다_하나씩_차고_최대치에서_멈춘다()
        {
            var stamina = new Recovery(3, 5, FiveMinutes, Anchor);

            Assert.That(stamina.ValueAt(Anchor + UnixSpan.FromMinutes(4)), Is.EqualTo(3));
            Assert.That(stamina.ValueAt(Anchor + UnixSpan.FromMinutes(5)), Is.EqualTo(4));
            Assert.That(stamina.ValueAt(Anchor + UnixSpan.FromHours(3)), Is.EqualTo(5)); // 백그라운드에 오래 있다 와도 최대치까지만
        }

        [Test]
        public void 다음_회복과_가득_차는_시각을_계산한다()
        {
            var stamina = new Recovery(1, 5, FiveMinutes, Anchor);
            var now = Anchor + UnixSpan.FromMinutes(7); // 2개

            Assert.That(stamina.NextAt(now), Is.EqualTo(Anchor + UnixSpan.FromMinutes(10)));
            Assert.That(stamina.FullAt(now), Is.EqualTo(Anchor + UnixSpan.FromMinutes(20)));

            var full = Anchor + UnixSpan.FromMinutes(20);
            Assert.That(stamina.NextAt(full), Is.Null);
            Assert.That(stamina.FullAt(full), Is.Null);
        }

        [Test]
        public void 이미_가득이거나_넘치면_시간으로_늘지_않는다()
        {
            var tickets = new Recovery(7, 5, UnixSpan.FromMinutes(7), Anchor); // 보상으로 최대치를 넘긴 경우

            Assert.That(tickets.ValueAt(Anchor + UnixSpan.FromDays(1)), Is.EqualTo(7));
            Assert.That(tickets.NextAt(Anchor), Is.Null);
            Assert.That(tickets.FullAt(Anchor), Is.Null);
        }

        [Test]
        public void 기준_시각보다_이르면_서버가_준_값_그대로다()
        {
            // 기기 시계가 서버보다 늦은 경우. 보정하지 않고, 거꾸로 줄이지도 않는다.
            var stamina = new Recovery(3, 5, FiveMinutes, Anchor);

            Assert.That(stamina.ValueAt(Anchor - UnixSpan.FromMinutes(12)), Is.EqualTo(3));
            Assert.That(stamina.NextAt(Anchor - UnixSpan.FromMinutes(12)), Is.EqualTo(Anchor + FiveMinutes));
        }
    }
}
