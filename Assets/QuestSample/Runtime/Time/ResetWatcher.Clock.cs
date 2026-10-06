using System;
using R3;

namespace QuestSample.Timing
{
    public sealed partial class ResetWatcher
    {
        // 시계를 구독해 틱마다 Check한다. 지금을 읽는 것은 시계뿐이고, 여기서는 받은 시각으로 초기화 시각을 넘었는지만 본다.
        // 받는 쪽에서 예외가 나도 구독은 멈추지 않는다(R3는 예외를 로그로 넘기고 계속 돈다).
        // 멈출 일이 있으면 돌려준 IDisposable을 Dispose한다.
        public IDisposable Watch(Observable<UnixTime> now)
        {
            return now.Subscribe(time => Check(time));
        }
    }
}
