namespace QuestSample.Presentation
{
    /// <summary>
    /// 셀 하나가 그릴 값. 문구와 버튼 상태까지 정해진 채로 온다 — 셀은 넣기만 한다.
    /// record라서 같은 값이 다시 오면 셀이 건너뛸 수 있다.
    /// </summary>
    public sealed record QuestRowData(
        string Id,
        string Title,
        string ProgressText,
        float ProgressRatio,
        string RewardText,
        string ButtonText,
        bool CanReceive,
        bool IsReceived);
}
