using System;
using System.Globalization;

namespace QuestSample.Timing
{
    /// <summary>
    /// UnixTime을 한 시간대의 달력으로 읽는 확장. offset을 주지 않으면 한국 표준시(UTC+9)로 읽는다.
    /// 요일은 월요일부터 센다(Weekday). 요일 · 시각 · 다음 일간/주간 초기화를 틱 정수 계산만으로 구한다(DateTime 변환 없음).
    /// </summary>
    public static class UnixTimeExtensions
    {
        // 한국 표준시 UTC+9. 서머타임이 없어서 고정 오프셋으로 충분하다.
        public static readonly UnixSpan Kst = UnixSpan.FromHours(9);

        // 1970-01-01(유닉스 시간 0)은 목요일이다.
        const int EpochWeekday = (int)Weekday.Thursday; // 3

        // 그 시간대 기준 날짜 번호. 1970-01-01 = 0이고 하루에 1씩 는다.
        public static long DayNumber(this UnixTime time, UnixSpan? offset = null)
        {
            return FloorDiv(LocalTick(time, offset), UnixSpan.TickPerDay);
        }

        // 그 시간대 기준, 그날 0시부터 지난 길이 (0 ~ 1일 미만)
        public static UnixSpan TimeOfDay(this UnixTime time, UnixSpan? offset = null)
        {
            return new UnixSpan(FloorMod(LocalTick(time, offset), UnixSpan.TickPerDay));
        }

        // 오늘이 무슨 요일인지 (월=0 … 일=6)
        public static Weekday GetWeekday(this UnixTime time, UnixSpan? offset = null)
        {
            return (Weekday)FloorMod(time.DayNumber(offset) + EpochWeekday, UnixSpan.DaysPerWeek);
        }

        // 지금이 몇 시인지 (0 ~ 23)
        public static int GetHour(this UnixTime time, UnixSpan? offset = null)
        {
            return (int)time.TimeOfDay(offset).TotalHours;
        }

        // 그날 0시
        public static UnixTime StartOfDay(this UnixTime time, UnixSpan? offset = null)
        {
            return time - time.TimeOfDay(offset);
        }

        // 이번 주 월요일 0시. 월요일이 0이라 요일 번호만큼 날을 빼면 된다.
        public static UnixTime StartOfWeek(this UnixTime time, UnixSpan? offset = null)
        {
            return time.StartOfDay(offset) - UnixSpan.FromDays((int)time.GetWeekday(offset));
        }

        // 오늘부터 target 요일까지의 요일 차이 (0 ~ 6). 오늘이 그 요일이면 0.
        public static int DaysUntil(this UnixTime time, Weekday target, UnixSpan? offset = null)
        {
            return (int)FloorMod((int)target - (int)time.GetWeekday(offset), UnixSpan.DaysPerWeek);
        }

        // 다음 일간 초기화: 매일 hour시 minute분. 지금이 딱 그 시각이면 이미 초기화된 것으로 보고 다음 날.
        public static UnixTime NextDaily(this UnixTime time, int hour, int minute = 0, UnixSpan? offset = null)
        {
            var today = time.StartOfDay(offset) + ClockTime(hour, minute);
            return today > time ? today : today + UnixSpan.FromDays(1);
        }

        // 다음 주간 초기화: 매주 day요일 hour시 minute분. 이번 주 그 시각이 지났으면(정각 포함) 다음 주.
        public static UnixTime NextWeekly(this UnixTime time, Weekday day, int hour, int minute = 0, UnixSpan? offset = null)
        {
            var thisWeek = time.StartOfWeek(offset) + UnixSpan.FromDays((int)day) + ClockTime(hour, minute);
            return thisWeek > time ? thisWeek : thisWeek + UnixSpan.FromWeeks(1);
        }

        // 지금 기간이 시작된 시각(가장 최근 초기화). 받은 시각이 이것보다 이르면 초기화 전에 받은 것이다.
        public static UnixTime LastDaily(this UnixTime time, int hour, int minute = 0, UnixSpan? offset = null)
        {
            return time.NextDaily(hour, minute, offset) - UnixSpan.FromDays(1);
        }

        public static UnixTime LastWeekly(this UnixTime time, Weekday day, int hour, int minute = 0, UnixSpan? offset = null)
        {
            return time.NextWeekly(day, hour, minute, offset) - UnixSpan.FromWeeks(1);
        }

        // 지금 달의 월간 초기화가 지났으면 그 시각, 아니면 지난달의 것. day는 모든 달에 있는 1 ~ 28만 받는다.
        public static UnixTime LastMonthly(this UnixTime time, int day, int hour, int minute = 0, UnixSpan? offset = null)
        {
            var thisMonth = MonthlyReset(time, 0, day, hour, minute, offset);
            return thisMonth <= time ? thisMonth : MonthlyReset(time, -1, day, hour, minute, offset);
        }

        // 다음 월간 초기화: 매달 day일 hour시 minute분. 지금이 딱 그 시각이면 다음 달.
        public static UnixTime NextMonthly(this UnixTime time, int day, int hour, int minute = 0, UnixSpan? offset = null)
        {
            var thisMonth = MonthlyReset(time, 0, day, hour, minute, offset);
            return thisMonth > time ? thisMonth : MonthlyReset(time, 1, day, hour, minute, offset);
        }

        // 남은 시간 문구. 하루가 넘으면 일수를 붙이고, 지났으면 00:00:00. 예: 6일 01:05:09, 23:59:59
        public static string FormatRemaining(UnixSpan remaining)
        {
            if (remaining < UnixSpan.Zero)
            {
                remaining = UnixSpan.Zero;
            }

            var clock = remaining.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
                + remaining.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                + remaining.Seconds.ToString("00", CultureInfo.InvariantCulture);
            return remaining.TotalDays > 0 ? remaining.TotalDays.ToString(CultureInfo.InvariantCulture) + "일 " + clock : clock;
        }

        // time이 속한 달(그 시간대 기준)에서 monthsToAdd만큼 옮긴 달의 day일 hour시 minute분.
        // 달의 길이는 28 ~ 31일로 달라서 틱 계산 대신 DateTime의 달력을 빌린다.
        static UnixTime MonthlyReset(UnixTime time, int monthsToAdd, int day, int hour, int minute, UnixSpan? offset)
        {
            if (day < 1 || day > 28)
            {
                throw new ArgumentOutOfRangeException(nameof(day), day, "1 ~ 28 (모든 달에 있는 날)");
            }

            var zone = offset ?? Kst;
            var local = (time + zone).DateTime; // 그 시간대의 벽시계를 UTC인 것처럼 읽는다
            var resetDay = new DateTime(local.Year, local.Month, day, 0, 0, 0, DateTimeKind.Utc).AddMonths(monthsToAdd);
            return new UnixTime(resetDay) - zone + ClockTime(hour, minute);
        }

        static long LocalTick(UnixTime time, UnixSpan? offset)
        {
            return time.Tick + (offset ?? Kst).Ticks;
        }

        static UnixSpan ClockTime(int hour, int minute)
        {
            if (hour < 0 || hour > 23)
            {
                throw new ArgumentOutOfRangeException(nameof(hour), hour, "0 ~ 23");
            }

            if (minute < 0 || minute > 59)
            {
                throw new ArgumentOutOfRangeException(nameof(minute), minute, "0 ~ 59");
            }

            return UnixSpan.FromHours(hour) + UnixSpan.FromMinutes(minute);
        }

        // 음수(1970년 이전, 음수 오프셋)에서도 나머지는 0 이상, 몫은 내림이 되게 한다.
        static long FloorMod(long value, long divisor)
        {
            var remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        static long FloorDiv(long value, long divisor)
        {
            return (value - FloorMod(value, divisor)) / divisor;
        }
    }
}
