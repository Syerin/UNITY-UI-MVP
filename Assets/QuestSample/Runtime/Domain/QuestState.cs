namespace QuestSample.Domain
{
    /// <summary>
    /// 퀘스트형 컨텐츠(일일 임무, 시즌 패스, 업적…)가 공유하는 세 단계.
    /// </summary>
    public enum QuestState
    {
        Progress,
        Receivable,
        Complete,
    }
}
