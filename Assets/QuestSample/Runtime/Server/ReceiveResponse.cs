using System.Collections.Generic;
using QuestSample.Domain;

namespace QuestSample.Server
{
    /// <summary>받기 응답. 받은 뒤의 전체 상태, 이번에 지급된 보상, 모두 받기에서 검증에 걸려 주지 못한 Id.</summary>
    public sealed record ReceiveResponse(QuestBoard Board, IReadOnlyList<Reward> Granted, IReadOnlyList<string> Rejected);
}
