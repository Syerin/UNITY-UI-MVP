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

    /// <summary>서버가 요청을 거절했다. 클라이언트 상태는 바꾸지 않는다.</summary>
    public sealed class QuestServerException : Exception
    {
        public QuestServerException(QuestError error)
            : base("Quest request rejected: " + error)
        {
            Error = error;
        }

        public QuestError Error { get; }
    }
}
