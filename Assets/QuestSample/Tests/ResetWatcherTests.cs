using System;
using System.Collections.Generic;
using NUnit.Framework;
using QuestSample.Timing;

namespace QuestSample.Tests
{
    /// <summary>
    /// ResetWatcher만 따로 본다. Store와 묶은 경우(클라가 먼저 되돌리기 등)는 QuestStoreTests에 있다.
    /// </summary>
    public sealed class ResetWatcherTests
    {
        // 2026-10-05(월) 낮 12시. 그날 10시 일일 초기화와 1일 10시 월간 초기화는 이미 지났다.
        static readonly UnixTime Noon = KstTime(2026, 10, 5, 12);

        ResetWatcher _watcher;
        List<ResetSignal> _signals;

        [SetUp]
        public void SetUp()
        {
            _watcher = new ResetWatcher(ResetSchedule.Default);
            _signals = new List<ResetSignal>();
            _watcher.Reset += signal => _signals.Add(signal);
        }

        [Test]
        public void 서버_데이터를_받기_전에는_신호를_내지_않는다()
        {
            Assert.That(_watcher.Check(Noon + UnixSpan.FromDays(2)).HasValue, Is.False);
            Assert.That(_signals, Is.Empty);
        }

        [Test]
        public void 며칠을_건너뛰어도_신호는_한_번이다()
        {
            // 앱이 백그라운드에서 사흘 있다 돌아온 경우. 넘어간 초기화 세 번을 한 신호로 알린다.
            _watcher.SyncToServer(Noon);

            var signal = _watcher.Check(Noon + UnixSpan.FromDays(3));

            Assert.That(signal.HasValue, Is.True);
            Assert.That(signal.Value.Daily && !signal.Value.Monthly, Is.True);
            Assert.That(signal.Value.At, Is.EqualTo(KstTime(2026, 10, 8, 10)));
            Assert.That(_watcher.Check(Noon + UnixSpan.FromDays(3) + UnixSpan.FromHours(1)).HasValue, Is.False);
            Assert.That(_signals.Count, Is.EqualTo(1));
        }

        [Test]
        public void 서버_시각이_클라보다_늦어도_기준은_되돌아가지_않는다()
        {
            _watcher.SyncToServer(Noon);
            _watcher.Check(Noon + UnixSpan.FromDays(1)); // 클라가 먼저 다음 날로 넘어갔다

            _watcher.SyncToServer(Noon); // 서버 응답의 시각은 아직 오늘

            Assert.That(_watcher.Check(Noon + UnixSpan.FromDays(1) + UnixSpan.FromHours(1)).HasValue, Is.False);
            Assert.That(_signals.Count, Is.EqualTo(1));
        }

        [Test]
        public void 서버가_이미_넘긴_초기화는_클라가_다시_알리지_않는다()
        {
            _watcher.SyncToServer(Noon);
            _watcher.SyncToServer(Noon + UnixSpan.FromDays(1)); // 다음 날 받은 응답에 그날 초기화가 들어 있다

            Assert.That(_watcher.Check(Noon + UnixSpan.FromDays(1)).HasValue, Is.False);
            Assert.That(_signals, Is.Empty);
        }

        [Test]
        public void 월말을_넘기면_일간과_월간이_한_신호로_온다()
        {
            _watcher.SyncToServer(KstTime(2026, 10, 31, 12));

            var signal = _watcher.Check(KstTime(2026, 11, 1, 10));

            Assert.That(signal.Value.Daily && signal.Value.Monthly, Is.True);
            Assert.That(signal.Value.At, Is.EqualTo(KstTime(2026, 11, 1, 10)));
        }

        static UnixTime KstTime(int year, int month, int day, int hour)
        {
            return new UnixTime(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc)) - UnixTimeExtensions.Kst;
        }
    }
}
