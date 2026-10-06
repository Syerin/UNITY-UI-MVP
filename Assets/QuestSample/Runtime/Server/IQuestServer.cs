using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QuestSample.Domain;

namespace QuestSample.Server
{
    /// <summary>
    /// 서버 경계. 진행도 · 받음 여부는 모두 서버가 정하고, 클라이언트는 응답을 반영만 한다.
    /// 거절은 QuestServerException으로 온다. 구현은 전송 오류(타임아웃 · 연결 끊김)도 QuestError.Network로 바꿔 던진다
    /// (Store도 경계에서 한 번 더 감싼다).
    /// </summary>
    public interface IQuestServer
    {
        UniTask<QuestBoard> GetBoardAsync(CancellationToken cancellationToken);

        UniTask<ReceiveResponse> ReceiveAsync(QuestGroup group, string questId, CancellationToken cancellationToken);

        /// <summary>
        /// 모두 받기. 클라가 받을 수 있다고 본 Id를 보낸다 — 서버는 목록을 탐색하지 않고, 어차피 하는 검증을 보낸 Id에만 한다.
        /// 통과한 것만 주고 걸린 Id는 응답의 Rejected에 담는다. 하나도 주지 못하면 그 이유로 거절한다.
        /// </summary>
        UniTask<ReceiveResponse> ReceiveAllAsync(QuestGroup group, IReadOnlyList<string> questIds, CancellationToken cancellationToken);
    }
}
