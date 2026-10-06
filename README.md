# 임무 · 시즌 패스 UI (Unity · MVP)

일일 임무와 시즌 패스 화면을 MVP로 만든 UI 샘플입니다. 통신 없이 실행되고, 응답 지연(기본 0.4초)을 넣어 연타 · 요청 중 화면 닫기 같은 상황을 시험할 수 있습니다.

Unity 6000.6 · uGUI · R3 · VContainer · UniTask · EditMode 테스트 32개

| 로비 | 임무 | 받기 |
| --- | --- | --- |
| ![로비](Docs/screenshots/01-lobby.png) | ![임무](Docs/screenshots/02-missions.png) | ![받기](Docs/screenshots/03-receive.png) |
| **시즌 패스 모두 받기** | **초기화가 지난 목록으로 받기** | **통신 오류** |
| ![시즌 패스](Docs/screenshots/05-pass-receive-all.png) | ![초기화](Docs/screenshots/06-expired.png) | ![통신 오류](Docs/screenshots/07-network-fail.png) |

## 핵심 설계

- **퀘스트 하나로 정형화** — 일일 임무와 시즌 패스 단계는 같은 흐름(진행 중 → 받기 가능 → 받음)이라, 받기 규칙 · 레드닷 · 목록 셀을 한 벌만 둡니다.
- **판단은 한 곳** — 받을 수 있는지는 `QuestRules`만 정합니다. 레드닷 세 곳과 받기 버튼이 서로 어긋날 수 없습니다.
- **상태는 응답이 정함** — 클라이언트가 먼저 하는 일은 초기화 하나이고, 다음 응답이 덮어씁니다. 앱을 켤 때 한 번 받은 뒤로는 버튼 없이 다시 요청하지 않습니다.
- **Presenter는 연결만, View는 넣기만** — 계산은 Store와 순수 함수(`QuestRules` · `QuestFormatter`)에 있어 씬 없이 테스트합니다.
- **바뀔 때만 그림** — 같은 값은 알리지 않고(`ReadOnlyReactiveProperty`), 같은 행은 셀이 건너뜁니다(`record`).
- **받기는 화면과 따로** — 요청이 이미 처리됐을 수 있어 화면을 닫아도 응답은 반영합니다. 받기와 모두 받기는 한 스트림으로 합쳐 응답 전 입력을 버립니다.
- **모두 받기는 Id 배열** — 받을 수 있는 Id만 골라 보내고, 그중 검증을 통과한 것만 지급됩니다.

## 시간

- 시간 값은 `UnixTime`(시점) · `UnixSpan`(길이) 두 타입만 씁니다. `DateTime`은 `Time` 폴더 안(변환 · 월 계산)에만 있고, `TimeSpan`으로는 R3 · UniTask 타이머에 넘길 때만 바꿉니다.
- 지금은 [`UnixTime.Now`](Assets/QuestSample/Runtime/Time/UnixTime.cs#L19-L23) 한 곳에서 읽고, 뒤에 `TimeProvider`가 있어 테스트에서는 시각을 직접 움직입니다. 기기 시각은 보정하지 않고, 받을 수 있는지는 요청 결과로 확인합니다.
- 틱은 [`Clock100ms`](Assets/QuestSample/Runtime/Time/Clock100ms.cs) 하나이고, 초기화(`ResetWatcher`)와 남은 시간 표시가 구독합니다. 구독자 하나가 예외를 던져도 나머지는 받습니다.
- 회복(스태미나 · 티켓)은 [`Recovery`](Assets/QuestSample/Runtime/Time/Recovery.cs)가 응답으로 받은 개수 · 기준 시각에서 계산만 합니다. 상태가 없어 감시자를 두지 않았고, 아직 쓰는 화면은 없습니다.

## 5분 안에 볼 곳

화면에서 안쪽으로 들어가는 순서입니다. 링크를 누르면 해당 줄이 열립니다.

| | 볼 곳 | 볼 점 |
| --- | --- | --- |
| 1 | [`QuestScreenPresenter.Initialize`](Assets/QuestSample/Runtime/Presentation/QuestScreen/QuestScreenPresenter.cs#L35-L76) | 화면 연결 전부. 받기 연타는 [응답 전 입력을 버리고](Assets/QuestSample/Runtime/Presentation/QuestScreen/QuestScreenPresenter.cs#L68-L73), 남은 시간은 [보이는 초가 바뀔 때만](Assets/QuestSample/Runtime/Presentation/QuestScreen/QuestScreenPresenter.cs#L59-L66) 그림 |
| 2 | [`QuestStore`](Assets/QuestSample/Runtime/Store/QuestStore.cs#L27-L53) | 레드닷은 `QuestRules`로 만든 파생 값. 응답은 [`Accept`](Assets/QuestSample/Runtime/Store/QuestStore.cs#L143-L159) 한 곳으로만, 클라이언트가 먼저 하는 건 [초기화](Assets/QuestSample/Runtime/Store/QuestStore.cs#L161-L182)(일일 임무는 매일, 시즌 패스는 매월)뿐 |
| 3 | [`QuestBoard.QuestsOf`](Assets/QuestSample/Runtime/Domain/QuestBoard.cs#L65-L92) · [`QuestRules`](Assets/QuestSample/Runtime/Domain/QuestRules.cs) | 두 콘텐츠가 같은 `Quest`가 되는 곳. 시즌 패스 단계는 [시즌 포인트가 진행도](Assets/QuestSample/Runtime/Domain/PassTier.cs#L9-L12) |
| 4 | [`ResetWatcher.Check`](Assets/QuestSample/Runtime/Time/ResetWatcher.cs#L107-L129) | "10시인가"가 아니라 "초기화 시각을 넘었는가"를 봄. 일간 · 주간 · 월간이 겹쳐도 한 신호, 받는 쪽 하나가 [예외를 던져도 나머지는 받음](Assets/QuestSample/Runtime/Time/ResetWatcher.cs#L147-L175) |
| 5 | [`QuestStoreTests`](Assets/QuestSample/Tests/QuestStoreTests.cs) | 테스트 이름이 명세. 예: [`초기화가_지난_목록으로_받으면_거절되고_목록을_새로_받는다`](Assets/QuestSample/Tests/QuestStoreTests.cs#L183-L194) |

**다룬 경우**: 받기 연타, 요청 거절, 초기화가 지난 목록으로 받기, 모두 받기에 받을 수 없는 것이 섞임, 화면을 닫아 둔 채 초기화, 기기 시계가 빠름, 첫 목록 받기 실패, 요청 중 화면 닫기, 화면을 연 첫 프레임, 시즌 종료, 일시정지

## 실행

Unity 6000.6(6000.6.0f1에서 확인)에서 `Assets/QuestSample/Scenes/QuestSample.unity`를 열고 Play. 테스트는 `Window > General > Test Runner > EditMode`.
응답 지연은 씬의 `AppLifetimeScope` 인스펙터 **테스트: 응답 지연**(기본 0.4초)에서 바꿉니다. 왼쪽 아래 테스트 도구로 아래 상황을 시험합니다.

1. 접속 임무 **받기** → 시즌 패스 탭에 레드닷
2. **다음 날로 넘기기** → 어제 목록으로 **받기** → 거절되고 새 목록
3. **다음 요청 실패시키기** → **받기** → 통신 오류, 목록은 그대로

## AI 사용

이 샘플은 AI(Claude)와 설계를 논의하며 만들었습니다. 무엇을 보여 줄지(퀘스트 정형화)와 패턴(MVP), 라이브러리 구성(R3 · VContainer · UniTask)은 제가 정했고, 코드 작성은 Claude Code(데스크톱)로 진행했습니다. 에이전트 스킬은 따로 붙이지 않았고, Unity 에디터는 [MCP for Unity](https://github.com/CoplayDev/unity-mcp)로 연결했습니다(샘플 코드는 이 패키지를 참조하지 않습니다).

코드는 AI가 썼고, 저는 설계를 정하고 코드를 검토했습니다. 네이밍 · 포맷 같은 스타일은 팀 규칙에 맞추면 되는 부분입니다.
