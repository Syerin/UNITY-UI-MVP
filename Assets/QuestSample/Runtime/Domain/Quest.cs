namespace QuestSample.Domain
{
    /// <summary>
    /// 퀘스트형 컨텐츠의 공통 모양. 일일 임무도 시즌 패스 단계도 이 모양으로 바뀐 뒤에
    /// 규칙(QuestRules)과 화면(목록 셀)에 들어간다.
    /// </summary>
    public sealed record Quest(string Id, string Title, long Progress, long Target, bool Received, Reward Reward)
    {
        public QuestState State => QuestRules.StateOf(Progress, Target, Received);
    }
}
