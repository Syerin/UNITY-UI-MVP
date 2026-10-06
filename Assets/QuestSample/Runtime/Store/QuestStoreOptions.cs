using System;
using QuestSample.Timing;

namespace QuestSample.Store
{
    /// <summary>
    /// Store가 목록을 다시 받는 간격. 앱은 Default를 쓰고, 테스트는 무작위를 고정해 넘긴다.
    /// </summary>
    public sealed class QuestStoreOptions
    {
        readonly Func<double> _random01;

        public QuestStoreOptions(UnixSpan retryFirst, UnixSpan retryMax, UnixSpan refetchWindow, Func<double> random01)
        {
            RetryFirst = retryFirst;
            RetryMax = retryMax;
            RefetchWindow = refetchWindow;
            _random01 = random01;
        }

        /// <summary>실패하면 1초 뒤부터 두 배씩 최대 30초 간격으로, 초기화 뒤 다시 받기는 0~60초 사이로 퍼뜨린다.</summary>
        public static QuestStoreOptions Default => new QuestStoreOptions(
            UnixSpan.FromSeconds(1), UnixSpan.FromSeconds(30), UnixSpan.FromSeconds(60), new Random().NextDouble);

        public UnixSpan RetryFirst { get; }
        public UnixSpan RetryMax { get; }
        public UnixSpan RefetchWindow { get; }

        /// <summary>
        /// failures번째 실패 뒤 기다릴 시간. RetryFirst부터 두 배씩 늘리고 RetryMax에서 멈춘다.
        /// 여러 기기가 같은 순간에 다시 몰리지 않도록 50~100% 사이로 흔든다.
        /// </summary>
        public UnixSpan RetryDelay(int failures)
        {
            var delay = RetryFirst;
            for (var i = 1; i < failures && delay < RetryMax; i++)
            {
                delay = delay * 2;
            }

            if (delay > RetryMax)
            {
                delay = RetryMax;
            }

            return new UnixSpan(delay.Ticks - (long)(delay.Ticks * 0.5 * _random01()));
        }

        /// <summary>초기화 시각에서 목록을 다시 받기까지. 모든 기기가 초기화 시각에 한꺼번에 묻지 않도록 0 ~ RefetchWindow 사이로 퍼뜨린다.</summary>
        public UnixSpan RefetchDelay()
        {
            return new UnixSpan((long)(RefetchWindow.Ticks * _random01()));
        }
    }
}
