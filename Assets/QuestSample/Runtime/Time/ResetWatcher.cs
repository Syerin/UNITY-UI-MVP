using System;
using System.Collections.Generic;

namespace QuestSample.Timing
{
    /// <summary>
    /// 초기화 신호 하나. 여러 초기화가 한 시각에 겹칠 수 있어(예: 매월 1일 10시는 일간 · 월간) 한 신호에 함께 담는다.
    /// 받는 쪽은 자기에게 해당하는 플래그만 보고, 한 번에 처리한다.
    /// </summary>
    public readonly struct ResetSignal
    {
        public ResetSignal(bool daily, bool monthly, UnixTime at)
        {
            Daily = daily;
            Monthly = monthly;
            At = at;
        }

        public bool Daily { get; }
        public bool Monthly { get; }

        // 넘어간 초기화 시각(가장 최근 것)
        public UnixTime At { get; }

        public bool Any => Daily || Monthly;

        public override string ToString()
        {
            return "초기화 " + At + (Daily ? " 일간" : "") + (Monthly ? " 월간" : "");
        }
    }

    /// <summary>
    /// 초기화 시각 규칙. 둘 다 같은 시각(기본: 한국 시간 10시)에 일어나고, 월간은 날짜가 더 붙는다.
    /// </summary>
    public readonly struct ResetSchedule
    {
        // 매일 10시, 매월 1일 10시, 한국 시간
        public static readonly ResetSchedule Default = new ResetSchedule(10, 0, 1, UnixTimeExtensions.Kst);

        public ResetSchedule(int hour, int minute, int monthlyDay, UnixSpan offset)
        {
            Hour = hour;
            Minute = minute;
            MonthlyDay = monthlyDay;
            Offset = offset;
        }

        public int Hour { get; }
        public int Minute { get; }
        public int MonthlyDay { get; }
        public UnixSpan Offset { get; }

        // 지금 기간이 시작된 시각(가장 최근 초기화)
        public UnixTime LastDaily(UnixTime now) => now.LastDaily(Hour, Minute, Offset);
        public UnixTime LastMonthly(UnixTime now) => now.LastMonthly(MonthlyDay, Hour, Minute, Offset);

        // 다음 초기화 시각(남은 시간 표시용)
        public UnixTime NextDaily(UnixTime now) => now.NextDaily(Hour, Minute, Offset);
        public UnixTime NextMonthly(UnixTime now) => now.NextMonthly(MonthlyDay, Hour, Minute, Offset);
    }

    /// <summary>
    /// 초기화를 감시한다. Watch로 100ms 시계를 구독하면 틱마다 Check하고, 마지막으로 반영한 기간이 끝났을 때 신호를 한 번 낸다.
    /// "지금이 10시 정각인가"가 아니라 "마지막 반영 이후 초기화 시각을 넘었는가"를 본다 —
    /// 프레임이 밀리거나 앱이 백그라운드에 있다 돌아와도 넘어간 초기화를 한 신호로 한 번만 알린다.
    /// 서버 응답을 받으면 SyncToServer로 기준을 옮긴다 — 응답에는 서버 시각까지의 초기화가 이미 들어 있어서,
    /// 같은 초기화를 클라가 뒤늦게 다시 알리지 않는다.
    /// 클라는 초기화했다고 서버에 알리지 않는다 — 공격 지점이 되고 불필요한 호출이 생긴다.
    /// </summary>
    public sealed partial class ResetWatcher
    {
        readonly ResetSchedule _schedule;
        UnixTime _dailyHandled;
        UnixTime _monthlyHandled;
        bool _started;

        public ResetWatcher(ResetSchedule schedule)
        {
            _schedule = schedule;
        }

        // 받는 쪽마다 따로 부른다(Raise). 하나가 예외를 던져도 나머지는 이 신호를 받는다.
        public event Action<ResetSignal> Reset;

        public ResetSchedule Schedule => _schedule;

        // 서버 응답을 반영했다. 서버 시각이 속한 기간까지는 반영된 것으로 보고 기준을 옮긴다(앞으로만).
        // 처음 부를 때 감시가 시작된다 — 서버 데이터를 받기 전에는 무엇을 되돌릴지 기준이 없다.
        public void SyncToServer(UnixTime serverTime)
        {
            MarkHandled(_schedule.LastDaily(serverTime), _schedule.LastMonthly(serverTime));
            _started = true;
        }

        // 한 번 확인한다(시계를 구독하면 틱마다 불린다). 초기화 시각을 넘었으면 신호를 내고 돌려준다. 넘지 않았으면 null.
        public ResetSignal? Check(UnixTime now)
        {
            if (!_started)
            {
                return null;
            }

            var daily = _schedule.LastDaily(now);
            var monthly = _schedule.LastMonthly(now);

            // 둘 다 같은 시각에 일어나므로, 무엇이 넘어갔든 가장 최근 초기화 시각은 일간 시각이다.
            var signal = new ResetSignal(daily > _dailyHandled, monthly > _monthlyHandled, daily);
            if (!signal.Any)
            {
                return null;
            }

            MarkHandled(daily, monthly);
            Raise(signal);
            return signal;
        }

        // 받는 쪽마다 따로 부른다. 하나가 예외를 던져도 나머지는 이 신호를 받는다 —
        // 기준(MarkHandled)은 이미 옮겼으므로, 여기서 멈추면 뒤의 구독자는 이 초기화를 다시 받을 길이 없다.
        // 예외는 모두 부른 뒤에 모아서 던진다(시계 구독에서는 R3가 로그로 남긴다).
        void Raise(ResetSignal signal)
        {
            var handlers = Reset;
            if (handlers == null)
            {
                return;
            }

            List<Exception> errors = null;
            foreach (Action<ResetSignal> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(signal);
                }
                catch (Exception e)
                {
                    (errors ??= new List<Exception>()).Add(e);
                }
            }

            if (errors != null)
            {
                throw new AggregateException(errors);
            }
        }

        // 기준은 앞으로만 간다. 서버 시각이 클라 시각보다 늦어도 되돌아가지 않는다.
        void MarkHandled(UnixTime daily, UnixTime monthly)
        {
            if (daily > _dailyHandled)
            {
                _dailyHandled = daily;
            }

            if (monthly > _monthlyHandled)
            {
                _monthlyHandled = monthly;
            }
        }
    }
}
