using UnityEngine;
using VContainer.Unity;

namespace QuestSample.Presentation
{
    /// <summary>임무 화면을 열고 닫는다. 열 때 화면 스코프를 앱 스코프의 자식으로 만들고, 닫을 때 스코프째 없앤다.</summary>
    public sealed class QuestScreenNavigator
    {
        readonly LifetimeScope _parent;
        readonly QuestScreenLifetimeScope _prefab;
        QuestScreenLifetimeScope _opened;

        public QuestScreenNavigator(LifetimeScope parent, QuestScreenLifetimeScope prefab)
        {
            _parent = parent;
            _prefab = prefab;
        }

        public void Open()
        {
            if (_opened != null)
            {
                return;
            }

            _opened = _parent.CreateChildFromPrefab(_prefab);
        }

        public void Close()
        {
            if (_opened == null)
            {
                return;
            }

            Object.Destroy(_opened.gameObject);
            _opened = null;
        }
    }
}
