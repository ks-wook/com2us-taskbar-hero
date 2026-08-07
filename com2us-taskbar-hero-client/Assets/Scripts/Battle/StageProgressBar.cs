using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// <b>던전 배경 띠의 우측 하단</b>에 현재 스테이지 진행도(처치 몬스터 ÷ 전체 몬스터)를 표시하는 진행 바.
    /// 스테이지 입장 시 0%에서 시작해 몬스터를 처치할수록 상승하고, 전멸(클리어) 시 100%가 된다.
    /// 현재 진행 위치는 바 아래의 위쪽 화살표(straight_up)가 따라가며 가리키고, 바 우측 끝에는
    /// 보스(boss) 아이콘을 두어 "어디까지 왔는지 → 목표(보스)"가 한눈에 읽히게 한다(스프라이트 미배선 시 생략).
    /// <see cref="DungeonBattleFlow"/>가 스테이지 입장 시 <see cref="Attach"/>로 배선하며, 매 프레임
    /// <see cref="BattleDevController.ServerBattleProgress"/>를 폴링해 채움/퍼센트를 갱신한다.
    /// 서버 전투가 없으면(개발용 무한 웨이브 등) 표시되지 않는다. 모든 그래픽은 raycastTarget=false라
    /// 클릭 통과·창 드래그를 방해하지 않는다.
    /// </summary>
    public class StageProgressBar : MonoBehaviour
    {
        // 던전 배경 띠(캔버스 y 216~576) 안쪽 우측 하단에 놓는다. 화면 최하단은 하단 UI(아이콘 줄 +
        // ui_bg_3 배경, y 30~216 — 던전 띠 아래에 여백 없이 맞붙는다)가 쓰므로 그 위로 올려 겹치지 않게
        // 하고, 눈에 들어오도록 크게 잡는다.
        // 하단 UI 토글 버튼은 우측 상단(버프 아이콘 아래)에 있어 이 자리와 겹치지 않는다.
        private const float BarWidth = 420f;
        private const float BarHeight = 34f;
        private const float BarRightMargin = -32f; // 화면 우측에서 띄우는 거리
        private const float BarBottomY = 270f;     // 바 하단 y(아래 화살표까지 던전 띠 안에 들어가는 높이)
        private const float FillInset = 3f;        // 배경 테두리 안쪽 여백
        private const float ArrowSize = 46f;       // 진행 위치 화살표 크기
        private const float BossSize = 64f;        // 우측 끝 보스(목표) 아이콘 크기
        // 화살표 y 오프셋(바 하단 기준, pivot=위 꼭짓점): 양수면 위 꼭짓점이 트랙 안쪽으로 파고들어
        // 현재 위치를 가리키고, 몸통은 바 아래로 내려온다(아래 끝 = 바 하단 − ArrowSize + 이 값).
        private const float ArrowYOffset = 8f;

        private static StageProgressBar s_instance;

        private BattleDevController _battle;
        private GameObject _root;       // 표시 토글 대상(바 전체)
        private RectTransform _fillRect;
        private RectTransform _arrowRect; // 현재 진행 위치 화살표(바 아래, 채움 끝을 따라감)
        private Text _label;
        private int _lastPercent = -1;

        /// <summary>진행 바를 생성(최초 1회)하고 감시할 전투 컨트롤러를 배선한다. 재호출 시 참조만 갱신한다.
        /// arrowSprite = 현재 진행 위치 화살표(straight_up), bossSprite = 우측 끝 목표 아이콘(boss).</summary>
        public static void Attach(BattleDevController battle, Sprite arrowSprite = null, Sprite bossSprite = null)
        {
            if (s_instance == null)
            {
                var go = new GameObject("StageProgressBar");
                s_instance = go.AddComponent<StageProgressBar>();
                s_instance.Build(arrowSprite, bossSprite);
            }
            s_instance._battle = battle;
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null; // 씬 전환으로 파괴되면 다음 Attach에서 재생성
            }
        }

        /// <summary>매 프레임 진행도를 폴링해 표시 여부·채움 폭·퍼센트 텍스트를 갱신한다.</summary>
        private void LateUpdate()
        {
            bool active = _battle != null && _battle.ServerBattleActive;
            if (_root != null && _root.activeSelf != active)
            {
                _root.SetActive(active);
            }
            if (!active)
            {
                return;
            }

            float p = _battle.ServerBattleProgress;
            float innerWidth = BarWidth - FillInset * 2f;
            if (_fillRect != null)
            {
                _fillRect.sizeDelta = new Vector2(innerWidth * p, _fillRect.sizeDelta.y);
            }
            if (_arrowRect != null)
            {
                // 화살표가 채움 끝(현재 진행 위치)을 따라가며 바를 아래에서 위로 가리킨다.
                _arrowRect.anchoredPosition = new Vector2(FillInset + innerWidth * p, ArrowYOffset);
            }
            int percent = Mathf.FloorToInt(p * 100f + 0.0001f);
            if (_label != null && percent != _lastPercent)
            {
                _lastPercent = percent;
                _label.text = $"{percent}%";
            }
        }

        // ── 계층 생성 ──

        /// <summary>우측 하단 도킹 캔버스와 바(배경 + 채움 + 퍼센트 텍스트 + 진행 화살표 + 보스 아이콘) 계층을 생성한다.</summary>
        private void Build(Sprite arrowSprite, Sprite bossSprite)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.StageProgress; // HUD 위, 전투 연출·패널 아래
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            // 현재 씬 규격(GameScene은 높이 1440 기준)으로 즉시 맞춘다 — 주기 스윕을 기다리면 첫 표시 때 잠깐 크게 그려진다.
            GameViewLayout.ApplyCurrentScaler(scaler);
            // GraphicRaycaster는 붙이지 않는다 — 표시 전용 UI(클릭 대상 아님).

            // 전투 화면 밴드 컨테이너 — GameScene 창은 좌우에 패널 여백이 붙어 캔버스가 전투 화면보다
            // 넓으므로, 우측 하단 도킹인 바를 캔버스 직속으로 두면 빈 여백으로 밀려난다.
            var gameArea = new GameObject("GameArea", typeof(RectTransform));
            gameArea.transform.SetParent(transform, false);
            GameAreaRect.Attach((RectTransform)gameArea.transform);

            // 바 루트(우측 하단 도킹, 표시 토글 대상)
            _root = new GameObject("Bar", typeof(RectTransform));
            _root.transform.SetParent(gameArea.transform, false);
            var rootRt = (RectTransform)_root.transform;
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(1f, 0f);
            rootRt.pivot = new Vector2(1f, 0f);
            rootRt.anchoredPosition = new Vector2(BarRightMargin, BarBottomY); // 던전 배경 띠 안쪽 우측 하단
            rootRt.sizeDelta = new Vector2(BarWidth, BarHeight);

            // 배경(어두운 트랙)
            var bg = NewImage("Track", rootRt, new Color(0f, 0f, 0f, 0.6f));
            StretchFull(bg.rectTransform);

            // 채움(좌측 정렬, 폭 = 진행도 비율)
            var fill = NewImage("Fill", rootRt, new Color(1f, 0.82f, 0.25f, 0.95f)); // 골드 톤
            _fillRect = fill.rectTransform;
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = new Vector2(FillInset, 0f);
            _fillRect.sizeDelta = new Vector2(0f, -FillInset * 2f);

            // 퍼센트 텍스트(중앙)
            var txtGo = new GameObject("Percent", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(rootRt, false);
            _label = txtGo.GetComponent<Text>();
            _label.font = font;
            _label.text = "0%";
            _label.fontSize = 24;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = Color.black;
            _label.raycastTarget = false;
            StretchFull((RectTransform)txtGo.transform);

            // 우측 끝 보스(목표) 아이콘 — 채움이 여기 닿으면 클리어라는 시각적 목표점.
            if (bossSprite != null)
            {
                var boss = NewImage("BossGoal", rootRt, Color.white);
                boss.sprite = bossSprite;
                boss.preserveAspect = true;
                var brt = boss.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f); // 바 우측 끝 중앙
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = Vector2.zero;
                brt.sizeDelta = new Vector2(BossSize, BossSize);
            }

            // 현재 진행 위치 화살표 — 바 아래에서 채움 끝을 위로 가리킨다(LateUpdate가 x를 갱신).
            if (arrowSprite != null)
            {
                var arrow = NewImage("ProgressArrow", rootRt, Color.white);
                arrow.sprite = arrowSprite;
                arrow.preserveAspect = true;
                _arrowRect = arrow.rectTransform;
                _arrowRect.anchorMin = _arrowRect.anchorMax = new Vector2(0f, 0f); // 바 좌하단 기준
                _arrowRect.pivot = new Vector2(0.5f, 1f);                          // 위 꼭짓점이 바 하단에 닿게
                _arrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize);
                _arrowRect.anchoredPosition = new Vector2(FillInset, ArrowYOffset); // 시작(0%) 위치
            }

            _root.SetActive(false); // 서버 전투 시작 전에는 숨김
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
