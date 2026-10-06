using System;
using System.Globalization;

/// <summary>
/// 길이(기간). 시점인 UnixTime과 짝이다: 시점 − 시점 = 길이, 시점 + 길이 = 시점, 시점 + 시점은 컴파일 에러.
/// 단위는 UnixTime과 같은 틱이다. "1초는 몇 틱" 같은 단위 약속은 길이에 대한 것이라 여기에 둔다(.NET의 TimeSpan과 같은 자리).
/// </summary>
public readonly struct UnixSpan : IEquatable<UnixSpan>, IComparable<UnixSpan>
{
	// 1초 = 1,000 Ticks. 틱의 해상도는 이 값 하나로 정한다(1,000 ~ 10,000,000 사이의 10의 거듭제곱).
	public const long TickPerSecond = 1_000L;

	// 1밀리초 = 1,000 틱 / 1,000
	public const long TickPerMillisecond = TickPerSecond / 1_000L; // 1L

	// 1분 = 60초 * 1,000 틱
	public const long TickPerMinute = 60 * TickPerSecond; // 60,000L

	// 1000초 = 1,000초 * 1,000 틱
	public const long TickPer1000Seconds = 1000 * TickPerSecond; // 1,000,000L

	// 1시간 = 3,600초 * 1,000 틱
	public const long TickPerHour = 3600 * TickPerSecond; // 3,600,000L

	// 1일 = 86,400초 * 1,000 틱
	public const long TickPerDay = 86400 * TickPerSecond; // 86,400,000L

	// 1주 = 7일
	public const int DaysPerWeek = 7;

	// 1주 = 7일 * 86,400,000 틱
	public const long TickPerWeek = DaysPerWeek * TickPerDay; // 604,800,000L

	public static readonly UnixSpan Zero = new UnixSpan(0L);

	public long Ticks { get; }

	public UnixSpan(long ticks)
	{
		Ticks = ticks;
	}

	// 서버가 주는 기간(ms)은 이것으로 받는다.
	public static UnixSpan FromMilliseconds(long milliseconds) => new UnixSpan(milliseconds * TickPerMillisecond);
	public static UnixSpan FromSeconds(long seconds) => new UnixSpan(seconds * TickPerSecond);
	public static UnixSpan FromMinutes(long minutes) => new UnixSpan(minutes * TickPerMinute);
	public static UnixSpan FromHours(long hours) => new UnixSpan(hours * TickPerHour);
	public static UnixSpan FromDays(long days) => new UnixSpan(days * TickPerDay);
	public static UnixSpan FromWeeks(long weeks) => new UnixSpan(weeks * TickPerWeek);

	// 전체를 한 단위로 센 값. 모자라는 부분은 0 쪽으로 버린다. 예: 90분 → TotalHours 1
	public long TotalMilliseconds => Ticks / TickPerMillisecond;
	public long TotalSeconds => Ticks / TickPerSecond;
	public long TotalMinutes => Ticks / TickPerMinute;
	public long TotalHours => Ticks / TickPerHour;
	public long TotalDays => Ticks / TickPerDay;

	// 시계처럼 나눈 자리. 예: 1일 02:03:04.005 → TotalDays 1, Hours 2, Minutes 3, Seconds 4, Milliseconds 5
	public int Hours => (int)(Ticks / TickPerHour % 24);
	public int Minutes => (int)(Ticks / TickPerMinute % 60);
	public int Seconds => (int)(Ticks / TickPerSecond % 60);
	public int Milliseconds => (int)(Ticks / TickPerMillisecond % 1000);

	// R3 · UniTask 타이머에 넘길 때만 TimeSpan으로 바꾼다. 게임 코드의 길이는 이 타입으로 다닌다.
	public TimeSpan ToTimeSpan() => new TimeSpan(Ticks * (TimeSpan.TicksPerSecond / TickPerSecond));

	public static UnixSpan operator +(UnixSpan left, UnixSpan right)
	{
		return new UnixSpan(left.Ticks + right.Ticks);
	}

	public static UnixSpan operator -(UnixSpan left, UnixSpan right)
	{
		return new UnixSpan(left.Ticks - right.Ticks);
	}

	public static UnixSpan operator -(UnixSpan span)
	{
		return new UnixSpan(-span.Ticks);
	}

	// 길이를 몇 배로. 예: UnixSpan.FromDays(1) * 3
	public static UnixSpan operator *(UnixSpan span, long times)
	{
		return new UnixSpan(span.Ticks * times);
	}

	public static UnixSpan operator *(long times, UnixSpan span)
	{
		return new UnixSpan(span.Ticks * times);
	}

	public static bool operator ==(UnixSpan left, UnixSpan right) => left.Ticks == right.Ticks;
	public static bool operator !=(UnixSpan left, UnixSpan right) => left.Ticks != right.Ticks;
	public static bool operator <(UnixSpan left, UnixSpan right) => left.Ticks < right.Ticks;
	public static bool operator >(UnixSpan left, UnixSpan right) => left.Ticks > right.Ticks;
	public static bool operator <=(UnixSpan left, UnixSpan right) => left.Ticks <= right.Ticks;
	public static bool operator >=(UnixSpan left, UnixSpan right) => left.Ticks >= right.Ticks;

	public bool Equals(UnixSpan other) => Ticks == other.Ticks;
	public override bool Equals(object obj) => obj is UnixSpan other && Equals(other);
	public override int GetHashCode() => Ticks.GetHashCode();
	public int CompareTo(UnixSpan other) => Ticks.CompareTo(other.Ticks);

	// 예: 1일 02:03:04.005, -00:00:05.000
	public override string ToString()
	{
		var sign = Ticks < 0 ? "-" : "";
		var span = Ticks < 0 ? -this : this;
		var clock = span.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
			+ span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
			+ span.Seconds.ToString("00", CultureInfo.InvariantCulture) + "."
			+ span.Milliseconds.ToString("000", CultureInfo.InvariantCulture);
		return span.TotalDays > 0 ? sign + span.TotalDays.ToString(CultureInfo.InvariantCulture) + "일 " + clock : sign + clock;
	}
}
