using System;

namespace QuestSample.Server
{
    public enum QuestError
    {
        AlreadyReceived,
        NotCompleted,
        Expired,
        NothingToReceive,
        Network,
    }

    /// <summary>서버가 요청을 거절했거나 보내지 못했다(Network). 클라이언트 상태는 바꾸지 않는다.</summary>
    public sealed class QuestServerException : Exception
    {
        public QuestServerException(QuestError error)
            : base("Quest request rejected: " + error)
        {
            Error = error;
        }

        // 전송 오류(타임아웃 · 연결 끊김)를 Network로 바꿀 때 원래 예외를 남긴다.
        public QuestServerException(QuestError error, Exception innerException)
            : base("Quest request failed: " + error, innerException)
        {
            Error = error;
        }

        public QuestError Error { get; }
    }
}
