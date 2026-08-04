using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
// 이 네임스페이스에도 같은 이름의 씬 전환 매니저가 있어 유니티 쪽을 별칭으로 구분한다(TaskbarWindow와 동일 규약).
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// GameScene 창을 <b>[좌 패널 여백] · [전투 화면] · [우 패널 여백]</b> 3분할로 잡는 레이아웃 정의와,
    /// 그 규격을 런타임에 카메라·캔버스에 적용하는 구동기.
    ///
    /// <para><b>왜 필요한가</b> — 기능 패널(거래소·출석부·메일 / 스테이지·가방)이 전투 화면 위에 겹쳐
    /// 게임을 가렸다. 창을 좌우로 넓히고 <b>전투 화면은 가운데 밴드에 고정</b>하면, 패널은 늘어난
    /// 좌우 여백에 들어가 전투를 전혀 가리지 않는다. GameScene 창은 DWM 알파 합성 투명 창이라
    /// (<see cref="TaskbarWindow"/>) 늘어난 여백은 패널이 없을 때 <b>바탕화면이 그대로 비치는</b>
    /// 투명 영역이고, 클릭도 뒤 창으로 통과한다.</para>
    ///
    /// <para><b>전투 화면이 그대로 유지되는 이유</b> — 배경 길 타일 폭·몬스터 스폰 지점 등 전투 구성이
    /// 전부 카메라의 <c>aspect</c>·가장자리에서 파생된다(<c>ScrollingBackground</c>·<c>BattleDevController</c>).
    /// 카메라 <see cref="Camera.rect"/>를 가운데 정사각형 밴드로 제한하면 <c>aspect</c>가 1로 유지되므로,
    /// 창을 넓혀도 전투 화면은 넓어지기 전과 픽셀 단위로 동일하다.</para>
    /// </summary>
    public class GameViewLayout : MonoBehaviour
    {
        // ── 설계 규격(논리 캔버스 단위) ──

        /// <summary>설계 기준 세로 길이. 캔버스는 항상 이 높이가 되도록 스케일한다.</summary>
        public const float DesignHeight = 1440f;

        /// <summary>
        /// 전투 화면과 패널 사이 간격. <b>모든 패널이 이 간격만큼 전투 화면에서 떨어져 열린다</b> —
        /// 패널은 화면 끝이 아니라 전투 화면 밴드 가장자리에 붙으므로(<c>SidePanel.Dock</c>),
        /// 폭이 제각각이어도 안쪽 간격은 항상 같다.
        /// </summary>
        public const float PanelGap = 40f;

        /// <summary>가장 넓은 좌측 패널(거래소) 폭.</summary>
        private const float WidestLeftPanel = 1120f;

        /// <summary>가장 넓은 우측 패널(스테이지 지도) 폭.</summary>
        private const float WidestRightPanel = 1040f;

        /// <summary>가장 넓은 패널 바깥쪽 끝과 창 가장자리 사이에 남기는 여유.</summary>
        private const float GutterOuterMargin = 12f;

        /// <summary>
        /// 왼쪽 패널 여백 — [전투 화면 간격 40] + [가장 넓은 좌측 패널 1120] + [창 가장자리 여유 12] = 1172.
        /// 이렇게 잡아야 가장 넓은 패널도 여백 안에 정확히 들어가 전투 화면을 1px도 침범하지 않는다.
        /// </summary>
        public const float GutterLeft = PanelGap + WidestLeftPanel + GutterOuterMargin;

        /// <summary>가운데 전투 화면 폭(정사각형이라 <see cref="DesignHeight"/>와 같다).</summary>
        public const float GameWidth = 1440f;

        /// <summary>오른쪽 패널 여백 — 같은 계산(40 + 1040 + 12 = 1092).</summary>
        public const float GutterRight = PanelGap + WidestRightPanel + GutterOuterMargin;

        /// <summary>설계 기준 가로 길이(= 좌 여백 + 전투 화면 + 우 여백 = 3704).</summary>
        public const float DesignWidth = GutterLeft + GameWidth + GutterRight;

        /// <summary>GameScene 창 가로세로비(= 3704 / 1440 ≒ 2.572).</summary>
        public const float WindowAspect = DesignWidth / DesignHeight;

        /// <summary>전투 화면 밴드의 좌측 경계(창 폭 대비 비율).</summary>
        public const float GameAreaMinX = GutterLeft / DesignWidth;

        /// <summary>전투 화면 밴드의 우측 경계(창 폭 대비 비율).</summary>
        public const float GameAreaMaxX = (GutterLeft + GameWidth) / DesignWidth;

        private const string GameSceneName = "GameScene";

        // 그 외 씬에서 쓰는 기본 캔버스 규격(각 컨트롤러의 BuildCanvas가 굽는 값과 같다).
        private static readonly Vector2 DefaultReference = new Vector2(1080f, 1920f);
        private const float DefaultMatch = 0.5f;

        // 캔버스를 다시 훑는 주기. 패널은 UIManager가 필요할 때 인스턴스화하므로
        // 씬 로드 시점 1회 적용만으로는 나중에 생긴 캔버스를 놓친다.
        private const float SweepInterval = 0.4f;

        // ── 공용 적용 헬퍼 ──

        /// <summary>
        /// 캔버스 스케일러를 GameScene 규격(높이 기준 1440)으로 맞춘다.
        /// <para><b>왜 높이 기준(match=1)인가</b> — 기본값인 match=0.5는 배율이 창 <i>폭</i>에도 걸려서,
        /// 창을 넓히면 UI가 통째로 커지고 논리 세로 길이가 1440 → 911로 줄어 큰 패널이 잘린다.
        /// 높이 기준으로 두면 배율이 <c>화면높이/1440</c>로 고정돼, 창을 아무리 넓혀도
        /// <b>패널 크기는 그대로이고 늘어난 폭이 전부 좌우 여백</b>이 된다.</para>
        /// <para>정사각형 창이던 종전 GameScene의 배율도 <c>화면높이/1440</c>였으므로
        /// 이 변경으로 기존 UI 크기는 달라지지 않는다.</para>
        /// </summary>
        public static void ApplyGameScaler(CanvasScaler scaler)
        {
            if (scaler == null)
            {
                return;
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(DesignWidth, DesignHeight);
            scaler.matchWidthOrHeight = 1f; // 1 = 높이 기준
            ApplyScaleFactorNow(scaler);
        }

        /// <summary>캔버스 스케일러를 기본 규격(1080×1920, match 0.5)으로 되돌린다(GameScene 밖).</summary>
        public static void ApplyDefaultScaler(CanvasScaler scaler)
        {
            if (scaler == null)
            {
                return;
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = DefaultReference;
            scaler.matchWidthOrHeight = DefaultMatch;
            ApplyScaleFactorNow(scaler);
        }

        /// <summary>
        /// 스케일러가 계산할 배율을 <b>지금 즉시</b> 캔버스에 반영한다(공식은 <c>CanvasScaler</c>의
        /// <c>ScaleWithScreenSize</c> + <c>MatchWidthOrHeight</c>와 동일).
        /// <para><b>왜 필요한가</b> — <c>CanvasScaler</c>는 <b>자기 Update에서만</b> <c>Canvas.scaleFactor</c>를
        /// 고쳐 쓴다. 프리팹에는 <c>scaleFactor = 1</c>이 구워져 있어, 새로 만든 패널은 스케일러가 한 번 돌기
        /// 전까지 1배로 그려진다 — GameScene의 올바른 배율(화면높이/1440)보다 훨씬 커서
        /// <b>처음 열 때만 UI가 크게 나왔다가 줄어드는</b> 원인이 됐다. 참조 해상도만 맞춰서는 이 프레임을
        /// 막을 수 없어 배율까지 직접 계산해 넣는다(스케일러가 다음 Update에 같은 값을 다시 넣는다).</para>
        /// </summary>
        private static void ApplyScaleFactorNow(CanvasScaler scaler)
        {
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            Vector2 size = canvas.renderingDisplaySize;
            if (size.x <= 0f || size.y <= 0f)
            {
                size = new Vector2(Screen.width, Screen.height);
            }
            Vector2 reference = scaler.referenceResolution;
            if (size.x <= 0f || size.y <= 0f || reference.x <= 0f || reference.y <= 0f)
            {
                return;
            }

            float logWidth = Mathf.Log(size.x / reference.x, 2f);
            float logHeight = Mathf.Log(size.y / reference.y, 2f);
            canvas.scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
        }

        /// <summary>
        /// 캔버스 스케일러를 <b>현재 씬 규격</b>으로 즉시 맞춘다(GameScene = 높이 1440 기준, 그 외 = 기본 규격).
        /// <para><b>왜 필요한가</b> — 런타임에 새로 만들어지는 캔버스(UIManager가 처음 여는 패널 프리팹 등)는
        /// 프리팹에 구워진 기본 규격(1080×1920, match 0.5)으로 시작한다. 주기 스윕(<c>SweepInterval</c>)만
        /// 믿으면 최대 0.4초 동안 그 규격으로 렌더되는데, 폭이 넓은 GameScene 창에서는 배율이 약 1.6배라
        /// <b>패널이 잠깐 크게 나왔다가 줄어든다</b>. 캔버스를 만든 직후 이 메서드를 부르면 첫 프레임부터
        /// 올바른 배율로 그려진다.</para>
        /// </summary>
        public static void ApplyCurrentScaler(CanvasScaler scaler)
        {
            if (scaler == null || !IsManagedScaler(scaler))
            {
                return;
            }
            if (LayoutActive)
            {
                ApplyGameScaler(scaler);
            }
            else
            {
                ApplyDefaultScaler(scaler);
            }
        }

        /// <summary>새로 만든 UI 계층(비활성 포함) 안의 모든 캔버스 스케일러에 <see cref="ApplyCurrentScaler"/>를 적용한다.</summary>
        public static void ApplyCurrentScalers(GameObject root)
        {
            if (root == null)
            {
                return;
            }
            var scalers = root.GetComponentsInChildren<CanvasScaler>(true);
            foreach (var scaler in scalers)
            {
                ApplyCurrentScaler(scaler);
            }
        }

        /// <summary>
        /// RectTransform을 <b>전투 화면 밴드</b>(가운데)에만 걸치도록 스트레치한다.
        /// 직접 부르기보다 <see cref="GameAreaRect"/>를 붙여 쓴다 — 그래야 레이아웃이 꺼진 씬에서
        /// 자동으로 전체 스트레치로 되돌아간다.
        /// </summary>
        public static void ApplyGameArea(RectTransform rt)
        {
            Stretch(rt, GameAreaMinX, GameAreaMaxX);
        }

        /// <summary>RectTransform을 캔버스 전체로 스트레치한다(레이아웃이 꺼진 씬의 기본 상태).</summary>
        public static void ApplyFullArea(RectTransform rt)
        {
            Stretch(rt, 0f, 1f);
        }

        /// <summary>가로 구간 [minX, maxX]·세로 전체로 스트레치한다.</summary>
        private static void Stretch(RectTransform rt, float minX, float maxX)
        {
            if (rt == null)
            {
                return;
            }
            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>현재 씬이 3분할 레이아웃(GameScene) 적용 대상인지.</summary>
        public static bool LayoutActive => s_instance != null && s_instance._active;

        /// <summary>전투 화면 밴드에 해당하는 카메라 뷰포트 사각형.</summary>
        public static Rect GameAreaViewport =>
            new Rect(GameAreaMinX, 0f, GameAreaMaxX - GameAreaMinX, 1f);

        // ── 런타임 구동기 ──

        private static GameViewLayout s_instance;

        private bool _active;              // 현재 씬이 GameScene인지(= 3분할 레이아웃 적용 대상)
        private float _nextSweep;
        private Camera _gutterClear;       // 좌우 여백을 매 프레임 투명으로 지우는 카메라

        /// <summary>부팅 시 구동기를 1회 생성한다(씬 배선 불필요).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_instance != null)
            {
                return;
            }
            var go = new GameObject("GameViewLayout");
            go.AddComponent<GameViewLayout>();
        }

        /// <summary>싱글턴으로 자리잡고 씬 전환을 구독한 뒤, 현재 씬에 규격을 즉시 적용한다.</summary>
        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;
            DontDestroyOnLoad(gameObject);
            USceneManager.activeSceneChanged += OnSceneChanged;
            ApplyScene(USceneManager.GetActiveScene().name);
        }

        /// <summary>씬 전환 구독을 해제한다.</summary>
        private void OnDestroy()
        {
            if (s_instance == this)
            {
                USceneManager.activeSceneChanged -= OnSceneChanged;
                s_instance = null;
            }
        }

        /// <summary>씬이 바뀌면 그 씬에 맞는 규격(GameScene = 3분할, 그 외 = 기본)으로 다시 적용한다.</summary>
        private void OnSceneChanged(Scene from, Scene to)
        {
            ApplyScene(to.name);
        }

        /// <summary>씬 이름으로 적용 여부를 판정하고 카메라·캔버스에 규격을 적용한다.</summary>
        private void ApplyScene(string sceneName)
        {
            _active = sceneName == GameSceneName;
            ApplyCamera();
            SweepCanvases();
            SweepGameAreas();
            _nextSweep = Time.unscaledTime + SweepInterval;
        }

        /// <summary>
        /// 나중에 생긴 캔버스(UIManager가 인스턴스화하는 패널)까지 잡기 위해 주기적으로 다시 훑고,
        /// 씬 로드 순서 때문에 늦게 준비되는 카메라도 함께 재적용한다.
        /// </summary>
        private void Update()
        {
            if (Time.unscaledTime < _nextSweep)
            {
                return;
            }
            _nextSweep = Time.unscaledTime + SweepInterval;
            ApplyCamera();
            SweepCanvases();
            SweepGameAreas();
        }

        /// <summary>현재 로드된 모든 <see cref="GameAreaRect"/>를 현재 레이아웃 상태로 다시 맞춘다.</summary>
        private void SweepGameAreas()
        {
            var areas = FindObjectsByType<GameAreaRect>(FindObjectsInactive.Include);
            foreach (var area in areas)
            {
                area.Apply();
            }
        }

        /// <summary>
        /// 메인 카메라를 전투 화면 밴드로 제한하고, 좌우 여백을 매 프레임 투명으로 지우는
        /// 보조 카메라를 유지한다. GameScene이 아니면 둘 다 원상 복구한다.
        /// </summary>
        private void ApplyCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            if (!_active)
            {
                if (cam.rect != FullViewport)
                {
                    cam.rect = FullViewport;
                }
                if (_gutterClear != null && _gutterClear.enabled)
                {
                    _gutterClear.enabled = false;
                }
                return;
            }

            var target = GameAreaViewport;
            if (cam.rect != target)
            {
                cam.rect = target;
            }
            EnsureGutterClearCamera(cam);
        }

        private static Rect FullViewport => new Rect(0f, 0f, 1f, 1f);

        /// <summary>
        /// 좌우 여백 전용 클리어 카메라를 만들어 둔다.
        /// <para><b>왜 필요한가</b> — 메인 카메라에 <see cref="Camera.rect"/>를 주면 그 밖의 픽셀은
        /// <b>아무도 쓰지 않아</b> 백버퍼의 이전 프레임 잔상이 남을 수 있다. 알파 합성 창에서는 그 잔상이
        /// 그대로 보이므로, 아무것도 렌더하지 않고(<c>cullingMask = 0</c>) 창 전체를 투명으로 지우는
        /// 카메라를 메인보다 먼저(depth가 낮게) 돌려 매 프레임 여백을 비운다.</para>
        /// </summary>
        private void EnsureGutterClearCamera(Camera main)
        {
            if (_gutterClear == null)
            {
                var go = new GameObject("GutterClearCamera");
                go.transform.SetParent(transform, false);
                _gutterClear = go.AddComponent<Camera>();
                _gutterClear.orthographic = true;
                _gutterClear.cullingMask = 0;             // 아무 레이어도 그리지 않는다(클리어 전용)
                _gutterClear.useOcclusionCulling = false;
                _gutterClear.allowHDR = false;
                _gutterClear.allowMSAA = false;
            }

            _gutterClear.enabled = true;
            _gutterClear.rect = FullViewport;
            _gutterClear.clearFlags = CameraClearFlags.SolidColor;
            _gutterClear.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _gutterClear.depth = main.depth - 1f;         // 메인보다 먼저 그려 창 전체를 비운다
        }

        /// <summary>
        /// 현재 로드된 모든 캔버스 스케일러를 현재 씬 규격으로 맞춘다.
        /// <para>기본 규격(1080×1920)이나 GameScene 규격(3600×1440)을 쓰는 캔버스만 건드린다 —
        /// 자체 규격을 쓰는 개발용 오버레이(<c>DevLogConsole</c>: 360×640)는 그대로 둔다.</para>
        /// <para>패널 캔버스는 <c>DontDestroyOnLoad</c>에 있어 씬을 넘나들므로, GameScene을 떠날 때
        /// 기본 규격으로 되돌려야 타이틀 씬에서 쓰는 공용 UI(모달·로딩·환경설정)가 어긋나지 않는다.</para>
        /// </summary>
        private void SweepCanvases()
        {
            var found = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include);
            foreach (var scaler in found)
            {
                if (!IsManagedScaler(scaler))
                {
                    continue;
                }
                if (_active)
                {
                    ApplyGameScaler(scaler);
                }
                else
                {
                    ApplyDefaultScaler(scaler);
                }
            }
        }

        /// <summary>이 구동기가 규격을 관리해도 되는 스케일러인지(기본 규격 또는 GameScene 규격) 판정한다.</summary>
        private static bool IsManagedScaler(CanvasScaler scaler)
        {
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                return false;
            }
            Vector2 r = scaler.referenceResolution;
            return (Mathf.Approximately(r.x, DefaultReference.x) && Mathf.Approximately(r.y, DefaultReference.y))
                || (Mathf.Approximately(r.x, DesignWidth) && Mathf.Approximately(r.y, DesignHeight));
        }
    }
}
