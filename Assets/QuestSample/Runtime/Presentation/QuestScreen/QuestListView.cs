using System.Collections.Generic;
using R3;
using UnityEngine;

namespace QuestSample.Presentation
{
    /// <summary>
    /// 셀을 재사용하는 목록. 행이 늘면 셀을 더 만들고, 줄면 남는 셀을 끈다.
    /// 값이 그대로인 셀은 셀 쪽에서 건너뛴다. 셀 프리팹은 Resources/UI에서 불러온다.
    /// </summary>
    public sealed class QuestListView : MonoBehaviour
    {
        const string CellPath = "UI/QuestCell";

        [SerializeField] RectTransform content;

        readonly List<QuestCellView> _cells = new List<QuestCellView>();
        readonly Subject<string> _receiveClicked = new Subject<string>();
        QuestCellView _cellPrefab;

        public Observable<string> OnReceiveClicked => _receiveClicked;

        public void Render(IReadOnlyList<QuestRowData> rows)
        {
            while (_cells.Count < rows.Count)
            {
                var cell = Instantiate(CellPrefab(), content);
                cell.OnReceiveClicked.Subscribe(_receiveClicked.OnNext).AddTo(cell);
                _cells.Add(cell);
            }

            for (var i = 0; i < _cells.Count; i++)
            {
                var used = i < rows.Count;
                _cells[i].gameObject.SetActive(used);
                if (used)
                {
                    _cells[i].Render(rows[i]);
                }
            }
        }

        // 처음 셀이 필요할 때 한 번 불러 둔다. Awake에서 부르지 않는 이유: 화면 스코프(LifetimeScope, 실행 순서 -5000)가
        // 먼저 깨어나 Presenter를 초기화하므로, 이 View의 Awake보다 Render가 먼저 불릴 수 있다.
        QuestCellView CellPrefab()
        {
            if (_cellPrefab == null)
            {
                _cellPrefab = Resources.Load<QuestCellView>(CellPath);
            }

            return _cellPrefab;
        }

        void OnDestroy()
        {
            _receiveClicked.Dispose();
        }
    }
}
