using System;
using System.Globalization;

namespace QuestSample.Timing
{
    /// <summary>
    /// 유닉스 시간(1970-01-01 00:00:00 UTC)부터 지난 시간을 틱으로 들고 있는 값. 시점이다(눈금).
    /// 길이는 UnixSpan이고, 틱 단위 약속(TickPer*)도 그쪽에 있다. 시점 − 시점 = 길이, 시점 ± 길이 = 시점.
    /// DateTime.Ticks(1초 = 10,000,000)와는 단위가 달라서, 바꿀 때는 이 타입의 생성자와 DateTime 속성을 거친다.
    /// </summary>
    public readonly struct UnixTime : IEquatable<UnixTime>, IComparable<UnixTime>
    {
        // DateTime 틱(100ns) 몇 개가 이 타입의 1 틱인지
        const long DateTimeTicksPerTick = TimeSpan.TicksPerSecond / UnixSpan.TickPerSecond; // 10,000L

        // 0001-01-01부터 1970-01-01까지 = 62,135,596,800초
        const long EpochOffset = 62_135_596_800L * UnixSpan.TickPerSecond;

        public static readonly UnixTime Epoch = new UnixTime(0L);

        // 지금을 읽는 곳은 이 TimeProvider 하나다. 기본은 기기 시각이고, 테스트는 가짜 시계로, 서버 시각을 맞출 때는 그 보정을 담은 TimeProvider로 바꾼다.
        public static TimeProvider TimeProvider { get; set; } = System.TimeProvider.System;

        // 지금. DateTime.UtcNow를 직접 읽지 않고 TimeProvider를 거친다 — 바꾸면 게임 전체의 지금이 같이 바뀐다.
        public static UnixTime Now => FromUnixMilliseconds(TimeProvider.GetUtcNow().ToUnixTimeMilliseconds());

        // 1970-01-01 00:00:00 UTC부터의 틱. 그 이전은 음수.
        public long Tick { get; }

        // 틱으로 만든다. 서버가 주는 시각(ms)은 FromUnixMilliseconds로 받는다.
        public UnixTime(long tickTime)
        {
            Tick = tickTime;
        }

        // Local은 UTC로 바꾸고, Unspecified는 UTC로 본다. 1 틱보다 작은 부분은 버린다.
        public UnixTime(DateTime dateTime)
        {
            var utc = dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime;

            // DateTime.Ticks는 0 이상이라 이 나눗셈은 항상 내림이다 — 1970년 이전도 1 틱 단위로 맞게 떨어진다.
            Tick = utc.Ticks / DateTimeTicksPerTick - EpochOffset;
        }

        // 서버는 1970-01-01 UTC부터의 밀리초를 준다. 틱으로 바꿔 받는다.
        public static UnixTime FromUnixMilliseconds(long milliseconds)
        {
            return new UnixTime(milliseconds * UnixSpan.TickPerMillisecond);
        }

        // 서버에 보낼 때. 1ms보다 작은 부분은 버린다(1970년 이전도 내림).
        public long ToUnixMilliseconds()
        {
            var remainder = Tick % UnixSpan.TickPerMillisecond;
            return (Tick - (remainder < 0 ? remainder + UnixSpan.TickPerMillisecond : remainder)) / UnixSpan.TickPerMillisecond;
        }

        // UTC 기준 DateTime
        public DateTime DateTime => new DateTime((Tick + EpochOffset) * DateTimeTicksPerTick, DateTimeKind.Utc);

        // 기기 시간대 기준 DateTime. 화면에 보여 줄 때 쓴다.
        public DateTime ToLocalDateTime()
        {
            return DateTime.ToLocalTime();
        }

        // 아래 문자열은 모두 UTC 기준이다. 기기 시간으로 보여 주려면 ToLocalDateTime()으로 포맷한다.
        //2014년
        public string YYYY => DateTime.Year.ToString("0000", CultureInfo.InvariantCulture);
        //01월
        public string MM => DateTime.Month.ToString("00", CultureInfo.InvariantCulture);
        //01일
        public string DD => DateTime.Day.ToString("00", CultureInfo.InvariantCulture);
        //00시~23시
        public string HH => DateTime.Hour.ToString("00", CultureInfo.InvariantCulture);
        //00분~59분
        public string mm => DateTime.Minute.ToString("00", CultureInfo.InvariantCulture);
        //00초~59초
        public string ss => DateTime.Second.ToString("00", CultureInfo.InvariantCulture);
        //000~999 밀리초
        public string SSS => DateTime.Millisecond.ToString("000", CultureInfo.InvariantCulture);

        // 시점 + 길이 = 시점. 예: UnixTime.Now + UnixSpan.FromMinutes(30)
        public static UnixTime operator +(UnixTime time, UnixSpan span)
        {
            return new UnixTime(time.Tick + span.Ticks);
        }

        public static UnixTime operator -(UnixTime time, UnixSpan span)
        {
            return new UnixTime(time.Tick - span.Ticks);
        }

        // 시점 − 시점 = 길이. 예: 남은 시간 = endTime - UnixTime.Now
        public static UnixSpan operator -(UnixTime left, UnixTime right)
        {
            return new UnixSpan(left.Tick - right.Tick);
        }

        public static bool operator ==(UnixTime left, UnixTime right) => left.Tick == right.Tick;
        public static bool operator !=(UnixTime left, UnixTime right) => left.Tick != right.Tick;
        public static bool operator <(UnixTime left, UnixTime right) => left.Tick < right.Tick;
        public static bool operator >(UnixTime left, UnixTime right) => left.Tick > right.Tick;
        public static bool operator <=(UnixTime left, UnixTime right) => left.Tick <= right.Tick;
        public static bool operator >=(UnixTime left, UnixTime right) => left.Tick >= right.Tick;

        public bool Equals(UnixTime other) => Tick == other.Tick;
        public override bool Equals(object obj) => obj is UnixTime other && Equals(other);
        public override int GetHashCode() => Tick.GetHashCode();
        public int CompareTo(UnixTime other) => Tick.CompareTo(other.Tick);

        // 예: 2014-01-01T00:00:00.000Z
        public override string ToString()
        {
            return DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }
}
