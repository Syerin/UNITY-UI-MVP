using System;
using NUnit.Framework;
using QuestSample.Timing;

namespace QuestSample.Tests
{
    /// <summary>
    /// 시간 계산은 경계에서 틀린다. 정각 · 월말 · 연말 · 1970년 이전처럼 어긋나기 쉬운 곳만 확인한다.
    /// 시각은 모두 한국 시간으로 적고 KstTime으로 바꾼다.
    /// </summary>
    public sealed class UnixTimeExtensionsTests
    {
        [Test]
        public void 일일_초기화_정각이면_이미_지난_것으로_본다()
        {
            var now = KstTime(2026, 10, 1, 10, 0, 0);

            Assert.That(now.NextDaily(10), Is.EqualTo(KstTime(2026, 10, 2, 10, 0, 0)));
            Assert.That(now.LastDaily(10), Is.EqualTo(now));
        }

        [Test]
        public void 일일_초기화_직전이면_오늘_10시가_다음이다()
        {
            var now = KstTime(2026, 10, 1, 10, 0, 0) - UnixSpan.FromMilliseconds(1);

            Assert.That(now.NextDaily(10), Is.EqualTo(KstTime(2026, 10, 1, 10, 0, 0)));
            Assert.That(now.LastDaily(10), Is.EqualTo(KstTime(2026, 9, 30, 10, 0, 0)));
        }

        [Test]
        public void 월간_초기화는_해를_넘어간다()
        {
            var now = KstTime(2026, 12, 15, 0, 0, 0);

            Assert.That(now.NextMonthly(1, 10), Is.EqualTo(KstTime(2027, 1, 1, 10, 0, 0)));
            Assert.That(now.LastMonthly(1, 10), Is.EqualTo(KstTime(2026, 12, 1, 10, 0, 0)));
        }

        [Test]
        public void 매월_1일_초기화_전이면_지난달_것이_마지막이다()
        {
            var now = KstTime(2027, 1, 1, 9, 59, 59);

            Assert.That(now.LastMonthly(1, 10), Is.EqualTo(KstTime(2026, 12, 1, 10, 0, 0)));
            Assert.That(now.NextMonthly(1, 10), Is.EqualTo(KstTime(2027, 1, 1, 10, 0, 0)));
        }

        [Test]
        public void 날짜는_한국_시간으로_센다()
        {
            // UTC 2026-09-30 15:00은 한국 시간 2026-10-01 00:00이다.
            var utc = new UnixTime(new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc));

            Assert.That(utc.DayNumber(), Is.EqualTo(utc.DayNumber(UnixSpan.Zero) + 1));
            Assert.That(utc.TimeOfDay(), Is.EqualTo(UnixSpan.Zero));
        }

        [Test]
        public void 유닉스_원점_이전도_날짜_번호는_내림이다()
        {
            var justBeforeEpoch = new UnixTime(-1);

            Assert.That(justBeforeEpoch.DayNumber(UnixSpan.Zero), Is.EqualTo(-1));
            Assert.That(justBeforeEpoch.TimeOfDay(UnixSpan.Zero), Is.EqualTo(UnixSpan.FromDays(1) - new UnixSpan(1)));
        }

        [Test]
        public void 밀리초로_주고받아도_값이_그대로다()
        {
            foreach (var milliseconds in new[] { 1_759_280_400_123L, 0L, -1L, -86_400_001L })
            {
                Assert.That(UnixTime.FromUnixMilliseconds(milliseconds).ToUnixMilliseconds(), Is.EqualTo(milliseconds));
            }
        }

        static UnixTime KstTime(int year, int month, int day, int hour, int minute, int second)
        {
            return new UnixTime(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc)) - UnixTimeExtensions.Kst;
        }
    }
}
