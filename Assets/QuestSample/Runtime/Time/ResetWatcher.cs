using System;
using System.Collections.Generic;

/// <summary>
/// 초기화 신호 하나. 여러 초기화가 한 시각에 겹칠 수 있어(예: 1일이 목요일이면 일간 · 주간 · 월간) 한 신호에 함께 담는다.
/// 받는 쪽은 자기에게 해당하는 플래그만 보고, 한 번에 처리한다.
/// </summary>
public readonly struct ResetSignal
{
	public ResetSignal(bool daily, bool weekly, bool monthly, bool fromServer, UnixTime at)
	{
		Daily = daily;
		Weekly = weekly;
		Monthly = monthly;
		FromServer = fromServer;
		At = at;
	}

	public bool Daily { get; }
	public bool Weekly { get; }
	public bool Monthly { get; }

	// true: 서버가 알려 줬다 — 받은 서버 데이터로 모두 덮어쓴다. false: 클라가 시각을 보고 먼저 초기화했다.
	public bool FromServer { get; }

	// 클라 신호는 넘어간 초기화 시각, 서버 신호는 서버가 알려 준 시각
	public UnixTime At { get; }

	public bool Any => Daily || Weekly || Monthly;

	public override string ToString()
	{
		return (FromServer ? "서버" : "클라") + " 초기화 " + At
			+ (Daily ? " 일간" : "") + (Weekly ? " 주간" : "") + (Monthly ? " 월간" : "");
	}
}

/// <summary>
/// 초기화 시각 규칙. 셋 다 같은 시각(기본: 한국 시간 10시)에 일어나고, 주간은 요일, 월간은 날짜가 더 붙는다.
/// </summary>
public readonly struct ResetSchedule
{
	// 매일 10시, 주간은 목요일, 월간은 1일, 한국 시간
	public static readonly ResetSchedule Default = new ResetSchedule(10, 0, Weekday.Thursday, 1, UnixTimeExtensions.Kst);

	public ResetSchedule(int hour, int minute, Weekday weeklyDay, int monthlyDay, UnixSpan offset)
	{
		Hour = hour;
		Minute = minute;
		WeeklyDay = weeklyDay;
		MonthlyDay = monthlyDay;
		Offset = offset;
	}

	public int Hour { get; }
	public int Minute { get; }
	public Weekday WeeklyDay { get; }
	public int MonthlyDay { get; }
	public UnixSpan Offset { get; }

	// 지금 기간이 시작된 시각(가장 최근 초기화)
	public UnixTime LastDaily(UnixTime now) => now.LastDaily(Hour, Minute, Offset);
	public UnixTime LastWeekly(UnixTime now) => now.LastWeekly(WeeklyDay, Hour, Minute, Offset);
	public UnixTime LastMonthly(UnixTime now) => now.LastMonthly(MonthlyDay, Hour, Minute, Offset);

	// 다음 초기화 시각(남은 시간 표시용)
	public UnixTime NextDaily(UnixTime now) => now.NextDaily(Hour, Minute, Offset);
	public UnixTime NextWeekly(UnixTime now) => now.NextWeekly(WeeklyDay, Hour, Minute, Offset);
	public UnixTime NextMonthly(UnixTime now) => now.NextMonthly(MonthlyDay, Hour, Minute, Offset);
}

/// <summary>
/// 초기화를 감시한다. Watch로 100ms 시계를 구독하면 틱마다 Check하고, 마지막으로 반영한 기간이 끝났을 때 클라 초기화 신호를 한 번 낸다.
/// "지금이 10시 정각인가"가 아니라 "마지막 반영 이후 초기화 시각을 넘었는가"를 본다 —
/// 프레임이 밀리거나 앱이 백그라운드에 있다 돌아와도 넘어간 초기화를 한 신호로 한 번만 알린다.
/// 서버가 초기화를 알리면 ApplyServer로 넘긴다. 서버 신호는 항상 내보내고(받는 쪽이 데이터를 덮어씀),
/// 같은 초기화를 클라가 뒤늦게 다시 알리지 않게 기준을 옮긴다.
/// 클라는 초기화했다고 서버에 알리지 않는다 — 공격 지점이 되고 불필요한 호출이 생긴다. 서버 호출은 상호작용이 있을 때만 한다.
/// </summary>
public sealed partial class ResetWatcher
{
	private readonly ResetSchedule _schedule;
	private UnixTime _dailyHandled;
	private UnixTime _weeklyHandled;
	private UnixTime _monthlyHandled;
	private bool _started;

	public ResetWatcher(ResetSchedule schedule)
	{
		_schedule = schedule;
	}

	// 받는 쪽마다 따로 부른다(Raise). 하나가 예외를 던져도 나머지는 이 신호를 받는다.
	public event Action<ResetSignal> Reset;

	public ResetSchedule Schedule => _schedule;

	// 서버 데이터를 받은 시각(서버 시각)으로 기준을 잡는다. 그 시각이 속한 기간은 이미 반영된 것으로 본다.
	public void Begin(UnixTime dataTime)
	{
		_dailyHandled = _schedule.LastDaily(dataTime);
		_weeklyHandled = _schedule.LastWeekly(dataTime);
		_monthlyHandled = _schedule.LastMonthly(dataTime);
		_started = true;
	}

	// 한 번 확인한다(시계를 구독하면 틱마다 불린다). 초기화 시각을 넘었으면 클라 신호를 내고 돌려준다. 넘지 않았으면 null.
	public ResetSignal? Check(UnixTime now)
	{
		if (!_started)
		{
			return null;
		}

		var daily = _schedule.LastDaily(now);
		var weekly = _schedule.LastWeekly(now);
		var monthly = _schedule.LastMonthly(now);

		// 셋 다 같은 시각에 일어나므로, 무엇이 넘어갔든 가장 최근 초기화 시각은 일간 시각이다.
		var signal = new ResetSignal(daily > _dailyHandled, weekly > _weeklyHandled, monthly > _monthlyHandled, false, daily);
		if (!signal.Any)
		{
			return null;
		}

		MarkHandled(daily, weekly, monthly);
		Raise(signal);
		return signal;
	}

	// 서버가 초기화됐다고 알렸다(버튼 응답 등). 받는 쪽은 서버 데이터로 모두 덮어쓴다.
	public ResetSignal ApplyServer(bool daily, bool weekly, bool monthly, UnixTime serverTime)
	{
		// 서버 시각이 속한 기간까지 반영한 것으로 본다 — 클라가 같은 초기화를 뒤늦게 다시 알리지 않는다.
		MarkHandled(_schedule.LastDaily(serverTime), _schedule.LastWeekly(serverTime), _schedule.LastMonthly(serverTime));
		_started = true;

		var signal = new ResetSignal(daily, weekly, monthly, true, serverTime);
		if (signal.Any)
		{
			Raise(signal);
		}

		return signal;
	}

	// 받는 쪽마다 따로 부른다. 하나가 예외를 던져도 나머지는 이 신호를 받는다 —
	// 기준(MarkHandled)은 이미 옮겼으므로, 여기서 멈추면 뒤의 구독자는 이 초기화를 다시 받을 길이 없다.
	// 예외는 모두 부른 뒤에 모아서 던진다(시계 구독에서는 R3가 로그로 남긴다).
	private void Raise(ResetSignal signal)
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

	// 기준은 앞으로만 간다. 서버 시각이 클라 시각보다 조금 늦어도 되돌아가지 않는다.
	private void MarkHandled(UnixTime daily, UnixTime weekly, UnixTime monthly)
	{
		if (daily > _dailyHandled)
		{
			_dailyHandled = daily;
		}

		if (weekly > _weeklyHandled)
		{
			_weeklyHandled = weekly;
		}

		if (monthly > _monthlyHandled)
		{
			_monthlyHandled = monthly;
		}
	}
}
