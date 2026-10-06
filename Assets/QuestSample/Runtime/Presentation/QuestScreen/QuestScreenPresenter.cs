using System;
using System.Threading;
using System.Threading.Tasks;
using QuestSample.Domain;
using QuestSample.Server;
using QuestSample.Store;
using QuestSample.Timing;
using R3;
using VContainer.Unity;

namespace QuestSample.Presentation
{
    /// <summary>
    /// 임무 화면이 열려 있는 동안만 산다(화면 스코프). 화면을 닫으면 구독이 함께 사라진다.
    /// 판단은 하지 않는다 — Store의 값과 QuestFormatter의 문구를 View에 넘기고, 입력을 Store로 넘긴다.
    /// </summary>
    public sealed class QuestScreenPresenter : IInitializable, IDisposable
    {
        readonly QuestScreenView _view;
        readonly QuestStore _store;
        readonly QuestScreenNavigator _navigator;
        readonly Clock100ms _clock;
        readonly ReactiveProperty<QuestGroup> _tab = new ReactiveProperty<QuestGroup>(QuestGroup.DailyMission);
        readonly CompositeDisposable _disposables = new CompositeDisposable();

        public QuestScreenPresenter(QuestScreenView view, QuestStore store, QuestScreenNavigator navigator, Clock100ms clock)
        {
            _view = view;
            _store = store;
            _navigator = navigator;
            _clock = clock;
        }

        // 화면은 프레임 도중(버튼 입력)에 만들어진다. IStartable.Start는 다음 프레임에 불려 첫 프레임에 프리팹 기본값이 비치므로,
        // 컨테이너를 만드는 즉시 불리는 Initialize에서 값을 채운다.
        public void Initialize()
        {
            _view.OnTabClicked.Subscribe(tab => _tab.Value = tab).AddTo(_disposables);
            _tab.Subscribe(tab =>
            {
                _view.SetTabSelected(tab == QuestGroup.DailyMission);
                _view.SetSeasonPointsVisible(tab == QuestGroup.SeasonPass);
            }).AddTo(_disposables);

            // 목록: 고른 탭의 퀘스트를 행으로 바꿔 넘긴다. 값이 그대로인 행은 셀이 건너뛴다.
            _tab.Select(tab => _store.Quests(tab)).Switch()
                .Subscribe(quests => _view.List.Render(QuestFormatter.ToRows(quests)))
                .AddTo(_disposables);

            // 레드닷과 모두 받기는 Store가 정한 값을 그대로 쓴다.
            _store.HasReceivableMission.Subscribe(_view.SetMissionBadge).AddTo(_disposables);
            _store.HasReceivablePass.Subscribe(_view.SetPassBadge).AddTo(_disposables);
            _tab.Select(tab => _store.HasReceivable(tab)).Switch()
                .Subscribe(_view.SetReceiveAllInteractable)
                .AddTo(_disposables);
            _store.SeasonPoints
                .Subscribe(points => _view.SetSeasonPoints(QuestFormatter.SeasonPointsText(points)))
                .AddTo(_disposables);

            // 남은 시간: 100ms 시계를 구독하되(여는 즉시 한 번 받는다),
            // "다음 초기화 − 지금"의 초가 바뀔 때만 문구를 만든다. 글자는 1초에 한 번만 바뀐다.
            _clock.Now
                .CombineLatest(_tab, (now, tab) => (Tab: tab, Now: now))
                .DistinctUntilChangedBy(x => (x.Tab, ShownSeconds(x.Tab, x.Now)))
                .Subscribe(x => _view.SetCountdown(QuestFormatter.CountdownText(
                    x.Tab, _store.NextReset(x.Tab, x.Now), x.Now)))
                .AddTo(_disposables);

            // 받기와 모두 받기를 한 줄로 합쳐, 응답이 오기 전에 들어온 입력은 버린다(이 화면에서의 연타).
            // 화면을 닫았다 다시 연 경우처럼 앞서 보낸 요청이 아직 진행 중이면 보내지 않는다 — 요청은 Store에서 한 번에 하나다.
            Observable.Merge(
                    _view.List.OnReceiveClicked.Select(id => new ReceiveRequest(_tab.Value, id)),
                    _view.OnReceiveAllClicked.Select(_ => new ReceiveRequest(_tab.Value, null)))
                .Where(_ => !_store.IsRequesting.CurrentValue)
                .SubscribeAwait(HandleReceiveAsync, AwaitOperation.Drop)
                .AddTo(_disposables);

            _view.OnCloseClicked.Subscribe(_ => _navigator.Close()).AddTo(_disposables);
        }

        public void Dispose()
        {
            _disposables.Dispose();
            _tab.Dispose();
        }

        async ValueTask HandleReceiveAsync(ReceiveRequest request, CancellationToken screenClosed)
        {
            string message;
            try
            {
                var response = request.QuestId == null
                    ? await _store.ReceiveAllAsync(request.Group)
                    : await _store.ReceiveAsync(request.Group, request.QuestId);
                message = QuestFormatter.GrantedText(response.Granted, response.Rejected.Count);
            }
            catch (QuestServerException e)
            {
                // 서버 거절 · 통신 오류: Store는 성공 응답만 반영하므로 상태는 그대로다. 이유만 알린다.
                // 전송 오류(타임아웃 등)도 Store가 경계에서 QuestError.Network로 바꿔 이리 온다.
                message = QuestFormatter.ErrorText(e.Error);
            }
            catch (OperationCanceledException)
            {
                return; // 앱이 끝나는 중이다.
            }

            // 요청 중에 화면이 닫혔다면 결과는 이미 Store에 반영됐고, 알릴 화면만 없다.
            if (!screenClosed.IsCancellationRequested)
            {
                _view.Toast.Show(message);
            }
        }

        // 화면에 보이는 남은 초. 이 값이 바뀔 때만 문구를 다시 만든다.
        long ShownSeconds(QuestGroup tab, UnixTime now)
        {
            return Math.Max(0L, (_store.NextReset(tab, now) - now).TotalSeconds);
        }

        sealed record ReceiveRequest(QuestGroup Group, string QuestId);
    }
}
