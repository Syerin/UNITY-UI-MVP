using System;
using System.Threading;
using System.Threading.Tasks;
using QuestSample.Presentation;
using QuestSample.Server;
using QuestSample.Store;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace QuestSample.Demo
{
    /// <summary>
    /// 데모 도구. 실제 게임이라면 전투 · 강화 결과가 서버에 기록되고 클라이언트가 새로 받아 온다.
    /// 여기서는 가짜 서버를 직접 움직인 뒤 Store에 새로 받으라고 한다.
    /// </summary>
    public sealed class DemoPanelPresenter : IStartable, IDisposable
    {
        readonly DemoPanelView _view;
        readonly FakeQuestServer _server;
        readonly QuestStore _store;
        readonly CompositeDisposable _disposables = new CompositeDisposable();

        public DemoPanelPresenter(DemoPanelView view, FakeQuestServer server, QuestStore store)
        {
            _view = view;
            _server = server;
            _store = store;
        }

        public void Start()
        {
            _view.OnWinBattle
                .SubscribeAwait((_, ct) => ReportAsync(FakeQuestServer.BattleMission, "전투 승리를 기록했습니다", ct), AwaitOperation.Drop)
                .AddTo(_disposables);
            _view.OnEnhance
                .SubscribeAwait((_, ct) => ReportAsync(FakeQuestServer.EnhanceMission, "장비 강화를 기록했습니다", ct), AwaitOperation.Drop)
                .AddTo(_disposables);
            _view.OnSkipDay.Subscribe(_ =>
            {
                _server.SkipDay();
                _view.SetStatus("다음 날로 넘겼습니다. 화면은 아직 어제 목록입니다. 받기를 눌러 보세요");
            }).AddTo(_disposables);
            _store.RefreshRetryScheduled
                .Subscribe(delay => _view.SetStatus(QuestFormatter.RetryText(delay)))
                .AddTo(_disposables);
            _view.OnFailNext.Subscribe(_ =>
            {
                _server.FailNextRequest(QuestError.Network);
                _view.SetStatus("다음 요청 하나가 통신 오류로 실패합니다");
            }).AddTo(_disposables);
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }

        async ValueTask ReportAsync(string missionKey, string done, CancellationToken cancellationToken)
        {
            _server.AddProgress(missionKey, 1);
            if (_store.IsRequesting.CurrentValue)
            {
                // 진행 중인 요청의 응답에 이 기록이 담겨 온다(가짜 서버는 응답할 때 스냅샷을 만든다). 따로 묻지 않는다.
                _view.SetStatus(done);
                return;
            }

            try
            {
                await _store.RefreshAsync(cancellationToken);
                _view.SetStatus(done);
            }
            catch (QuestServerException e)
            {
                _view.SetStatus(QuestFormatter.ErrorText(e.Error));
            }
        }
    }
}
