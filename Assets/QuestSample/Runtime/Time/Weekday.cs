using System;

/// <summary>
/// 요일. 월요일부터 센다(월=0 … 일=6).
/// .NET DayOfWeek는 일요일이 0이라 헷갈리므로 우리 코드는 이것만 쓰고, .NET과 주고받을 때만 바꾼다.
/// </summary>
public enum Weekday
{
	Monday = 0,    // 월
	Tuesday = 1,   // 화
	Wednesday = 2, // 수
	Thursday = 3,  // 목
	Friday = 4,    // 금
	Saturday = 5,  // 토
	Sunday = 6,    // 일
}

public static class WeekdayExtensions
{
	private static readonly string[] KoreanNames = { "월", "화", "수", "목", "금", "토", "일" };

	// "월" … "일"
	public static string ToKorean(this Weekday weekday)
	{
		return KoreanNames[(int)weekday];
	}

	// .NET DayOfWeek(일=0 … 토=6)에서 바꾼다. DateTime.DayOfWeek를 받을 때만 쓴다.
	public static Weekday ToWeekday(this DayOfWeek dayOfWeek)
	{
		return (Weekday)(((int)dayOfWeek + 6) % UnixSpan.DaysPerWeek);
	}

	// .NET DayOfWeek로 바꾼다. .NET API에 넘길 때만 쓴다.
	public static DayOfWeek ToDayOfWeek(this Weekday weekday)
	{
		return (DayOfWeek)(((int)weekday + 1) % UnixSpan.DaysPerWeek);
	}
}
