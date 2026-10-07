using System;
using QuestSample.Store;
using R3;
using VContainer.Unity;

namespace QuestSample.Presentation
{
    /// <summary>
    /// 로비는 앱과 수명이 같다. 임무 화면이 닫혀 있어도 로비 레드닷은 Store의 값을 따라 바뀐다.
    /// </summary>
    public sealed class LobbyPresenter : IInitializable, IDisposable
    {
        readonly LobbyView _view;
        readonly QuestStore _store;
        readonly QuestScreenNavigator _navigator;
        readonly CompositeDisposable _disposables = new CompositeDisposable();

        public LobbyPresenter(LobbyView view, QuestStore store, QuestScreenNavigator navigator)
        {
            _view = view;
            _store = store;
            _navigator = navigator;
        }

        // 화면 값은 IInitializable에서 채운다 — 임무 화면과 같은 규칙(README '설계 결정').
        // 스코프가 언제 만들어지든(씬 시작이든 프레임 도중이든) 첫 프레임부터 배지가 Store 값과 맞는다.
        public void Initialize()
        {
            _store.HasAnyReceivable.Subscribe(_view.SetQuestBadge).AddTo(_disposables);
            _view.OnQuestClicked.Subscribe(_ => _navigator.Open()).AddTo(_disposables);
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}
