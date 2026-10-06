using System;
using R3;

/// <summary>
/// 앱의 시계 하나. 100ms마다 UnixTime.Now를 한 번 읽어 Now로 알린다.
/// 초 단위로 세는 것은 모두 이 시계를 구독해 같은 시각을 본다(초기화 감시, 남은 시간 표시).
/// 게임에서는 Run이 틱마다 읽어 넣고, 테스트에서는 시각을 직접 넣는다(Set).
/// 기기 시각은 보정하지 않는다 — 받을 수 있는지는 서버가 판단한다.
/// 스킬 쿨타임처럼 게임 안에서 도는 시간은 이 시계가 아니라 게임 시간으로 따로 돈다.
/// </summary>
public sealed class Clock100ms : IDisposable
{
	// 틱 간격
	public static readonly UnixSpan TickInterval = UnixSpan.FromMilliseconds(100);

	private readonly ReactiveProperty<UnixTime> _now;

	public Clock100ms(UnixTime now)
	{
		_now = new ReactiveProperty<UnixTime>(now);
	}

	// 구독하자마자 지금 값을 한 번 주고, 그 뒤로 틱마다 준다. 한 틱 안에서는 모두 같은 시각을 본다.
	public ReadOnlyReactiveProperty<UnixTime> Now => _now;

	public void Set(UnixTime now)
	{
		_now.Value = now;
	}

	// 게임에서 한 번 부른다. 틱마다 UnixTime.Now를 읽어 넣는다. 돌려준 것을 Dispose하면 멈춘다.
	// 틱은 플레이어 루프에서 돌고 timeScale과 무관하다. UpdateRealtime은 쓰지 않는다:
	// R3 1.3.1에서는 첫 틱의 경과 시간을 잘못 재서 한 번짜리 타이머가 바로 울린다(Cysharp/R3#294).
	public IDisposable Run()
	{
		return Observable.Interval(TickInterval.ToTimeSpan(), UnityTimeProvider.UpdateIgnoreTimeScale)
			.Subscribe(_ => Set(UnixTime.Now));
	}

	public void Dispose()
	{
		_now.Dispose();
	}
}
