using System;

/// <summary>
/// 간격 회복(스태미나 · 티켓 등). 서버가 준 값에서 지금 몇 개인지 계산만 한다 — 상태가 없어서 감시자(Watcher)를 따로 두지 않는다.
/// 간격(1분 · 3분 · 7분 …)과 최대치는 서버와 같은 데이터에서 받고, 개수와 기준 시각은 서버가 처음에 한 번 준다.
/// 그 뒤로는 클라가 지금 시각으로 계산하고, 요청을 주고받다 서버가 최신 값을 주면(안 맞을 때) 그 값으로 새로 만든다.
/// 기기 시각은 보정하지 않는다 — 쓸 수 있는지는 서버가 판단한다. 아직 이 샘플에서 쓰는 화면은 없다.
///
/// 예: 스태미나 표시(화면 스코프). Store가 Recovery를 ReactiveProperty로 들고, 100ms 시계와 묶어 개수가 바뀔 때만 그린다.
///     store.Stamina.CombineLatest(clock.Now, (stamina, now) => stamina.ValueAt(now)).DistinctUntilChanged()
///         .Subscribe(view.SetStamina).AddTo(disposables);
///
/// 예: 다음 1개까지 남은 시간. 가득이면 null이다.
///     var next = stamina.NextAt(now);
///     view.SetRecoveryTime(next.HasValue ? UnixTimeExtensions.FormatRemaining(next.Value - now) : "가득 참");
///
/// 예: 서버 응답으로 바로잡기(스태미나를 쓰거나 받는 응답마다). 간격 · 최대치는 서버와 같은 데이터에서.
///     _stamina.Value = new Recovery(response.Stamina, data.StaminaMax, data.StaminaInterval, response.StaminaRecoveredAt);
/// </summary>
public readonly struct Recovery
{
	public Recovery(long value, long max, UnixSpan interval, UnixTime anchor)
	{
		Value = value;
		Max = max;
		Interval = interval;
		Anchor = anchor;
	}

	// 기준 시각의 개수(서버가 준 값)
	public long Value { get; }

	// 최대치. 시간으로는 여기까지만 찬다(보상 등으로는 넘칠 수 있다).
	public long Max { get; }

	// 1개가 차는 간격
	public UnixSpan Interval { get; }

	// 기준 시각: 이 시각부터 간격마다 1개씩 찬다(서버의 "마지막으로 찬 시각").
	public UnixTime Anchor { get; }

	// 지금 몇 개인지. 이미 최대치 이상이면 그대로, 아니면 지난 간격 수만큼 더하되 최대치에서 멈춘다.
	public long ValueAt(UnixTime now)
	{
		if (Value >= Max || Interval.Ticks <= 0)
		{
			return Value;
		}

		return Math.Min(Max, Value + Steps(now));
	}

	// 다음 1개가 차는 시각. 지금 가득이면 null.
	public UnixTime? NextAt(UnixTime now)
	{
		if (ValueAt(now) >= Max || Interval.Ticks <= 0)
		{
			return null;
		}

		return Anchor + Interval * (Steps(now) + 1);
	}

	// 가득 차는 시각. 지금 가득이면 null.
	public UnixTime? FullAt(UnixTime now)
	{
		if (ValueAt(now) >= Max || Interval.Ticks <= 0)
		{
			return null;
		}

		return Anchor + Interval * (Max - Value);
	}

	// 기준 시각부터 지난 간격 수. 기준보다 이르면(기기 시계가 늦음) 0이다.
	private long Steps(UnixTime now)
	{
		var elapsed = (now - Anchor).Ticks;
		return elapsed <= 0 ? 0 : elapsed / Interval.Ticks;
	}
}
