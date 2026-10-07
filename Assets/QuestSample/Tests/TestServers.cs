using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using QuestSample.Domain;
using QuestSample.Server;

namespace QuestSample.Tests
{
    /// <summary>
    /// 가짜 서버를 감싸 요청을 세거나, 응답을 붙잡거나, 실패시키는 테스트용 서버.
    /// 아무 설정도 하지 않으면 안쪽 서버에 그대로 넘긴다.
    /// </summary>
    sealed class TestServer : IQuestServer
    {
        readonly IQuestServer _inner;
        readonly Queue<QuestBoard> _replayBoards = new Queue<QuestBoard>();
        UniTaskCompletionSource _hold;
        UniTaskCompletionSource _holdResponses;
        bool _loseNextResponse;
        int _failBoards;
        QuestError _failBoardsWith;
        Exception _throwNext;

        public TestServer(IQuestServer inner)
        {
            _inner = inner;
        }

        public int BoardRequests { get; private set; }
        public int ReceiveRequests { get; private set; }
        public IReadOnlyList<string> SentIds { get; private set; } = Array.Empty<string>();

        /// <summary>다음 목록 요청 count번을 error로 거절한다.</summary>
        public void FailBoards(int count, QuestError error)
        {
            _failBoards = count;
            _failBoardsWith = error;
        }

        /// <summary>다음 목록 요청에 이 스냅샷을 돌려준다(늦게 도착한 옛 응답 흉내).</summary>
        public void ReplayBoard(QuestBoard board)
        {
            _replayBoards.Enqueue(board);
        }

        /// <summary>다음 요청에서 거절이 아닌 예외(타임아웃 등)를 던진다.</summary>
        public void ThrowNext(Exception exception)
        {
            _throwNext = exception;
        }

        /// <summary>받기 응답을 Release할 때까지 붙잡는다(요청이 진행 중인 상태를 만든다).</summary>
        public void HoldReceives()
        {
            _hold = new UniTaskCompletionSource();
        }

        /// <summary>서버는 바로 처리하고, 받기 응답만 Release할 때까지 붙잡는다(응답이 늦게 도착하는 상황).</summary>
        public void HoldResponses()
        {
            _holdResponses = new UniTaskCompletionSource();
        }

        /// <summary>다음 받기는 서버가 처리한 뒤 응답이 유실된다(타임아웃).</summary>
        public void LoseNextResponse()
        {
            _loseNextResponse = true;
        }

        public void Release()
        {
            var hold = _hold;
            var holdResponses = _holdResponses;
            _hold = null;
            _holdResponses = null;
            hold?.TrySetResult();
            holdResponses?.TrySetResult();
        }

        public async UniTask<QuestBoard> GetBoardAsync(CancellationToken cancellationToken)
        {
            BoardRequests++;
            ThrowIfAsked();
            if (_failBoards > 0)
            {
                _failBoards--;
                throw new QuestServerException(_failBoardsWith);
            }

            if (_replayBoards.Count > 0)
            {
                return _replayBoards.Dequeue();
            }

            return await _inner.GetBoardAsync(cancellationToken);
        }

        public async UniTask<ReceiveResponse> ReceiveAsync(QuestGroup group, string questId, CancellationToken cancellationToken)
        {
            ReceiveRequests++;
            ThrowIfAsked();
            if (_hold != null)
            {
                await _hold.Task;
            }

            return await AfterProcess(await _inner.ReceiveAsync(group, questId, cancellationToken));
        }

        public async UniTask<ReceiveResponse> ReceiveAllAsync(QuestGroup group, IReadOnlyList<string> questIds, CancellationToken cancellationToken)
        {
            ReceiveRequests++;
            SentIds = questIds.ToArray();
            ThrowIfAsked();
            if (_hold != null)
            {
                await _hold.Task;
            }

            return await AfterProcess(await _inner.ReceiveAllAsync(group, questIds, cancellationToken));
        }

        // 서버가 처리한 뒤: 응답을 잃거나(타임아웃) 늦게 돌려준다.
        async UniTask<ReceiveResponse> AfterProcess(ReceiveResponse response)
        {
            if (_loseNextResponse)
            {
                _loseNextResponse = false;
                throw new TimeoutException("응답 유실");
            }

            if (_holdResponses != null)
            {
                await _holdResponses.Task;
            }

            return response;
        }

        void ThrowIfAsked()
        {
            var exception = _throwNext;
            _throwNext = null;
            if (exception != null)
            {
                throw exception;
            }
        }
    }
}
