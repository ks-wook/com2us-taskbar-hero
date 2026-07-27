using System.Collections;
using System.Collections.Generic;
using TaskbarHero.Client.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 파티 구성(1~3인)에 맞춰 스킬 아이콘/쿨타임 UI와 초상화를 <b>런타임에 동적 생성</b>한다.
    /// 멤버 수만큼 행을 만들고, 각 행에 [초상화][스킬 슬롯…]을 배치한다. 슬롯/초상화는 씬에 둔
    /// 템플릿(비활성)을 복제해 스타일을 유지하며, 초상화는 멤버별 <see cref="PortraitCameraRig"/>로
    /// 렌더한다. 쿨타임은 코루틴으로 각 슬롯의 방사형 오버레이(fillAmount)와 남은 초를 갱신한다.
    /// </summary>
    public class SkillCooldownUI : MonoBehaviour
    {
        [Tooltip("파티/쿨타임을 제공하는 전투 컨트롤러(없으면 씬에서 탐색)")]
        public BattleDevController controller;

        [Header("템플릿(비활성 상태로 씬에 둔다)")]
        [Tooltip("스킬 슬롯 템플릿(자식: Icon(Image)/CooldownFill(Image, Filled)/Remaining(Text))")]
        public GameObject slotTemplate;
        [Tooltip("초상화 RawImage 템플릿")]
        public RectTransform portraitTemplate;
        [Tooltip("초상화 프레임 Image 템플릿")]
        public RectTransform portraitFrameTemplate;

        [Header("레이아웃(우상단 앵커 기준, 음수 = 좌/하)")]
        public float slotStartX = -204f;
        public float slotStepX = 92f;
        public float rowStartY = -20f;
        public float rowStepY = -108f;
        public float portraitX = -302f;
        public Vector2 frameOffset = new Vector2(3f, 3f);

        [Header("초상화 카메라")]
        public int portraitBaseLayer = 31;   // 멤버 i → (base - i) 레이어
        public float portraitOrtho = 0.27f;
        public Vector2 portraitAim = new Vector2(0f, 0.48f);

        [Tooltip("쿨타임 표시 갱신 주기(초)")]
        public float updateInterval = 0.05f;

        [Header("배경 스트립 도킹")]
        [Tooltip("true면 UI 블록 전체를 배경(길 스트립) 상단선 바로 위로 내려 배치한다(창 크기 변화 추종).")]
        public bool dockAboveBackground = true;
        [Tooltip("배경 상단선과 UI 블록 하단 사이 간격(캔버스 단위)")]
        public float dockMargin = 180f;
        [Tooltip("true면 UI 블록을 화면 좌측 상단 기준으로 배치한다(false=기존 우측 상단).")]
        public bool alignLeft = true;
        [Tooltip("좌측 정렬 시 기존 우상단 기준 음수 x 좌표에 더할 평행이동량(내부 배치 순서를 유지한 채 좌측으로 옮김)")]
        public float alignLeftShift = 440f;

        [Header("아군 체력바(세로, 초상화 왼쪽 옆)")]
        [Tooltip("초상화 가장자리에서 체력바까지 간격")]
        public float hpBarGap = 8f;
        public float hpBarWidth = 14f;
        public float hpBarHeight = 84f;
        [Tooltip("체력바 추가 위치 오프셋(초상화 기준 계산값에 더함)")]
        public Vector2 hpBarOffset = new Vector2(-45f, -40f);
        public Color hpBarBgColor = new Color(0f, 0f, 0f, 0.6f);
        public Color hpBarFillColor = new Color(0.25f, 0.9f, 0.35f, 1f);

        private class SlotRT
        {
            public PlayerCombatant member;
            public int skillCode;
            public Image cooldownFill;
            public Text remainingText;
        }

        private class HpBar
        {
            public PlayerCombatant member;
            public Image fill;
        }

        private readonly List<SlotRT> _slots = new List<SlotRT>();
        private readonly List<HpBar> _hpBars = new List<HpBar>();
        private readonly List<GameObject> _spawned = new List<GameObject>(); // 재구성 시 제거할 생성물(슬롯/초상화/리그/체력바)

        private RectTransform _dockRoot;       // 생성물을 묶는 컨테이너(스트립 도킹 시 통째로 이동)
        private float _blockBottomFromTop;     // 캔버스 상단 기준 UI 블록 하단까지의 거리(캔버스 단위)
        private ScrollingBackground _background;
        private Canvas _canvas;
        private bool _dockApplied;             // 현재 창 크기 기준으로 도킹 계산을 이미 적용했는지
        private int _dockScreenW, _dockScreenH; // 도킹 계산 당시의 화면 크기(변하면 재계산)
        private float _dockScale;               // 도킹 계산 당시의 캔버스 스케일

        // hover 툴팁(스킬 정보) — 슬롯 위에 커서를 올리면 표시.
        // EventSystem 이벤트 대신 매 프레임 커서 위치를 슬롯 사각형과 대조(폴링)한다:
        // 오버레이 창이 포커스가 없어도(비활성) 툴팁이 동작해야 하기 때문.
        private class HoverSlot
        {
            public RectTransform rect;
            public PlayerCombatant member;
            public int skillCode;
        }

        private readonly List<HoverSlot> _hoverSlots = new List<HoverSlot>();
        private RectTransform _tooltipRoot;
        private Text _tipName;
        private Text _tipDesc;
        private Text _tipStats;
        private Text _tipCooldown;
        private HoverSlot _activeSlot;         // 현재 툴팁을 띄운 슬롯

        private static Sprite _whiteSprite;
        /// <summary>체력바용 1x1 흰색 스프라이트(최초 1회 생성).</summary>
        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var t = new Texture2D(1, 1);
                    t.SetPixel(0, 0, Color.white);
                    t.Apply();
                    _whiteSprite = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                }
                return _whiteSprite;
            }
        }

        private IEnumerator Start()
        {
            if (controller == null)
            {
                controller = Object.FindAnyObjectByType<BattleDevController>();
            }

            // 파티 스폰 대기(컨트롤러 Start에서 스폰됨)
            while (controller == null || controller.Party == null || controller.Party.Count == 0)
            {
                yield return null;
            }

            BuildUI();

            var wait = new WaitForSeconds(Mathf.Max(0.01f, updateInterval));
            while (true)
            {
                UpdateCooldowns();
                yield return wait;
            }
        }

        /// <summary>파티가 바뀌었을 때(선택 재시작 등) 생성물을 모두 제거하고 현재 파티로 UI를 다시 만든다.</summary>
        public void Rebuild()
        {
            HideTooltip();
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
            _slots.Clear();
            _hpBars.Clear();
            _hoverSlots.Clear();
            if (controller != null && controller.Party != null && controller.Party.Count > 0)
                BuildUI();
        }

        private void BuildUI()
        {
            if (slotTemplate != null) slotTemplate.SetActive(false);
            if (portraitTemplate != null) portraitTemplate.gameObject.SetActive(false);
            if (portraitFrameTemplate != null) portraitFrameTemplate.gameObject.SetActive(false);
            EnsureDockRoot();
            EnsurePointerInfra();

            var party = controller.Party;
            for (int i = 0; i < party.Count; i++)
            {
                var member = party[i];
                if (member == null) continue;
                float baseY = rowStartY + i * rowStepY;

                // 초상화 프레임 + RawImage
                if (portraitFrameTemplate != null)
                {
                    var frame = Instantiate(portraitFrameTemplate, _dockRoot);
                    frame.gameObject.SetActive(true);
                    PlaceTopCorner(frame, portraitX + frameOffset.x, baseY + frameOffset.y);
                    _spawned.Add(frame.gameObject);
                }
                if (portraitTemplate != null)
                {
                    var por = Instantiate(portraitTemplate, _dockRoot);
                    por.gameObject.SetActive(true);
                    PlaceTopCorner(por, portraitX, baseY);
                    _spawned.Add(por.gameObject);
                    var raw = por.GetComponent<RawImage>();

                    var rigGo = new GameObject("PortraitRig_" + i);
                    _spawned.Add(rigGo);
                    var rig = rigGo.AddComponent<PortraitCameraRig>();
                    rig.targetRawImage = raw;
                    rig.explicitTarget = member.transform;
                    rig.portraitLayer = portraitBaseLayer - i;
                    rig.orthoSize = portraitOrtho;
                    rig.aimOffset = portraitAim;
                }

                // 아군 세로 체력바(초상화 왼쪽 옆, 하단을 초상화 좌측 하단에 정렬)
                float portraitHalfW = (portraitTemplate != null && portraitTemplate.sizeDelta.x > 1f) ? portraitTemplate.sizeDelta.x * 0.5f : 40f;
                float portraitHalfH = (portraitTemplate != null && portraitTemplate.sizeDelta.y > 1f) ? portraitTemplate.sizeDelta.y * 0.5f : 48f;
                float barX = portraitX - portraitHalfW - hpBarGap - hpBarWidth * 0.5f;
                float portraitBottomY = baseY - portraitHalfH; // 초상화 하단 y
                CreateHpBar(member, i, new Vector2(barX + hpBarOffset.x, portraitBottomY + hpBarOffset.y));

                // 스킬 슬롯
                if (slotTemplate == null) continue;
                for (int j = 0; j < member.SkillCount; j++)
                {
                    int code = member.SkillCodeAt(j);
                    var slotGo = Instantiate(slotTemplate, _dockRoot);
                    slotGo.SetActive(true);
                    var rt = slotGo.GetComponent<RectTransform>();
                    if (rt != null) PlaceTopCorner(rt, slotStartX + j * slotStepX, baseY);
                    _spawned.Add(slotGo);

                    // hover 툴팁 배선: 슬롯 루트가 레이캐스트를 받도록 보장(창 클릭 통과 판정의 uGUI 대상)하고,
                    // 폴링 대상 목록에 등록한다.
                    var rootGraphic = slotGo.GetComponent<Graphic>();
                    if (rootGraphic != null)
                    {
                        rootGraphic.raycastTarget = true;
                    }
                    else
                    {
                        var hitArea = slotGo.AddComponent<Image>(); // 투명 레이캐스트 영역
                        hitArea.color = new Color(0f, 0f, 0f, 0f);
                        hitArea.raycastTarget = true;
                    }
                    _hoverSlots.Add(new HoverSlot { rect = rt, member = member, skillCode = code });

                    var iconT = FindChild(slotGo.transform, "Icon");
                    if (iconT != null)
                    {
                        var img = iconT.GetComponent<Image>();
                        var icon = member.SkillIconAt(j);
                        if (img != null && icon != null) img.sprite = icon;
                    }

                    var fillT = FindChild(slotGo.transform, "CooldownFill");
                    var textT = FindChild(slotGo.transform, "Remaining");
                    _slots.Add(new SlotRT
                    {
                        member = member,
                        skillCode = code,
                        cooldownFill = fillT != null ? fillT.GetComponent<Image>() : null,
                        remainingText = textT != null ? textT.GetComponent<Text>() : null,
                    });
                }
            }

            MeasureBlockBottom();
        }

        /// <summary>
        /// 요소를 상단 코너 기준으로 배치한다. 우측 정렬이면 우상단 앵커(기존 음수 x 좌표 그대로),
        /// 좌측 정렬이면 좌상단 앵커로 바꾸고 <see cref="alignLeftShift"/>만큼 평행이동해
        /// 내부 배치 순서(체력바-초상화-슬롯)를 유지한 채 블록을 좌측에 붙인다.
        /// </summary>
        private void PlaceTopCorner(RectTransform rt, float x, float y)
        {
            if (alignLeft)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x + alignLeftShift, y);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(x, y);
            }
        }

        /// <summary>생성물을 묶어 통째로 이동시킬 컨테이너(DockRoot)를 1회 생성한다(캔버스 전체 스트레치라 좌표계 동일).</summary>
        private void EnsureDockRoot()
        {
            if (_dockRoot != null) return;
            var go = new GameObject("DockRoot", typeof(RectTransform));
            _dockRoot = (RectTransform)go.transform;
            _dockRoot.SetParent(transform, false);
            _dockRoot.anchorMin = Vector2.zero;
            _dockRoot.anchorMax = Vector2.one;
            _dockRoot.offsetMin = Vector2.zero;
            _dockRoot.offsetMax = Vector2.zero;
        }

        /// <summary>uGUI hover 이벤트 전제 조건을 보장한다: 씬에 EventSystem이 없으면 생성, 캔버스에 GraphicRaycaster 부착.</summary>
        private void EnsurePointerInfra()
        {
            if (EventSystem.current == null && Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                _spawned.Add(es); // 우리가 만든 경우에만 수명 관리(재구성 시 재생성)
            }
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>슬롯 hover 시 스킬 정보 툴팁을 만든다(1회, 이후 재사용). 배경+이름/설명/효과/쿨타임 텍스트.</summary>
        private void EnsureTooltip()
        {
            if (_tooltipRoot != null) return;

            var go = new GameObject("SkillTooltip", typeof(RectTransform), typeof(Image));
            _tooltipRoot = (RectTransform)go.transform;
            _tooltipRoot.SetParent(_dockRoot, false);
            // 슬롯과 같은 상단 코너 앵커 공간(좌측 정렬이면 좌상단). 피벗은 슬롯 반대편 모서리.
            Vector2 corner = alignLeft ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            _tooltipRoot.anchorMin = _tooltipRoot.anchorMax = corner;
            _tooltipRoot.pivot = corner;
            _tooltipRoot.sizeDelta = new Vector2(320f, 168f);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.12f, 0.95f);
            bg.raycastTarget = false; // 정보 전용 — 슬롯 hover를 가로채지 않음

            _tipName = NewTipText("TipName", 24, FontStyle.Bold, new Color(1f, 0.92f, 0.6f), 12f, 10f, 296f, 30f);
            _tipDesc = NewTipText("TipDesc", 18, FontStyle.Normal, new Color(0.9f, 0.92f, 0.98f), 12f, 44f, 296f, 62f);
            _tipDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipStats = NewTipText("TipStats", 18, FontStyle.Normal, new Color(0.7f, 0.9f, 0.8f), 12f, 110f, 296f, 26f);
            _tipCooldown = NewTipText("TipCooldown", 18, FontStyle.Normal, new Color(0.75f, 0.78f, 0.62f), 12f, 138f, 296f, 24f);

            _tooltipRoot.gameObject.SetActive(false);
        }

        /// <summary>툴팁 내부 텍스트 한 줄을 만든다(툴팁 좌상단 기준 배치).</summary>
        private Text NewTipText(string name, int size, FontStyle style, Color color, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_tooltipRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.UpperLeft;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// 매 프레임 커서가 어느 슬롯 위인지 폴링해 툴팁을 표시/숨김한다.
        /// EventSystem 이벤트가 아닌 직접 사각형 검사라 창이 포커스를 잃어도 동작한다.
        /// </summary>
        private void UpdateHover()
        {
            if (_hoverSlots.Count == 0) return;
            Vector2 pos = Input.mousePosition;
            HoverSlot found = null;
            foreach (var h in _hoverSlots)
            {
                if (h.rect != null && h.rect.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(h.rect, pos)) // Overlay 캔버스 → 카메라 불필요
                {
                    found = h;
                    break;
                }
            }
            if (found == null)
            {
                HideTooltip();
            }
            else if (_activeSlot != found)
            {
                ShowTooltip(found);
            }
        }

        /// <summary>hover된 슬롯의 스킬 정보를 채워 툴팁을 슬롯 왼쪽에 표시한다.</summary>
        private void ShowTooltip(HoverSlot hover)
        {
            if (hover == null || hover.member == null) return;
            EnsureTooltip();
            _activeSlot = hover;

            string name = null;
            int coefType = 0;
            float coef = 0f, duration = 0f, cooldown = 0f;
            hover.member.TryGetSkillInfo(hover.skillCode, out name, out coefType, out coef, out duration, out cooldown);

            string desc = null;
            var db = MasterDataManager.Db;
            if (db != null && db.Skills != null && db.Skills.TryGetValue(hover.skillCode, out var sm))
            {
                if (string.IsNullOrEmpty(name)) name = sm.name;
                desc = sm.description;
            }

            string typeText = coefType == 2 ? "버프" : coefType == 3 ? "디버프" : "공격";
            _tipName.text = string.IsNullOrEmpty(name) ? $"스킬 {hover.skillCode}" : name;
            _tipDesc.text = desc ?? "";
            _tipStats.text = $"{typeText} · 계수 ×{coef:0.##}" + (duration > 0f ? $" · 지속 {duration:0.#}초" : "");
            _tipCooldown.text = $"쿨타임 {cooldown:0.#}초";

            // 슬롯 바깥쪽(우측 정렬=왼쪽, 좌측 정렬=오른쪽)에, 슬롯 상단과 맞춰 배치(캔버스 위로 벗어나면 아래로 클램프).
            var slot = hover.rect;
            Vector2 slotPos = slot != null ? slot.anchoredPosition : Vector2.zero;
            float slotHalfW = slot != null ? slot.sizeDelta.x * 0.5f : 42f;
            float slotHalfH = slot != null ? slot.sizeDelta.y * 0.5f : 42f;
            float x = alignLeft ? slotPos.x + slotHalfW + 10f : slotPos.x - slotHalfW - 10f;
            float y = Mathf.Min(slotPos.y + slotHalfH, -8f);
            _tooltipRoot.anchoredPosition = new Vector2(x, y);
            _tooltipRoot.SetAsLastSibling(); // 다른 슬롯/체력바 위에 렌더
            _tooltipRoot.gameObject.SetActive(true);
        }

        /// <summary>툴팁을 숨긴다.</summary>
        private void HideTooltip()
        {
            _activeSlot = null;
            if (_tooltipRoot != null) _tooltipRoot.gameObject.SetActive(false);
        }

        /// <summary>캔버스 상단 기준 UI 블록 하단까지의 거리(캔버스 단위)를 측정한다 — 스트립 도킹 시프트 계산용.</summary>
        private void MeasureBlockBottom()
        {
            float lowest = 0f; // 상단(우상단 앵커) 기준 y — 아래로 음수
            foreach (var go in _spawned)
            {
                var rt = go != null ? go.transform as RectTransform : null;
                if (rt == null || rt.parent != _dockRoot) continue;
                float bottomEdge = rt.anchoredPosition.y - rt.pivot.y * rt.sizeDelta.y;
                lowest = Mathf.Min(lowest, bottomEdge);
            }
            _blockBottomFromTop = -lowest;
            _dockApplied = false; // 블록이 바뀌었으니 도킹 위치 재계산
        }

        /// <summary>매 프레임 스트립 도킹 위치와 슬롯 hover 툴팁을 갱신한다.</summary>
        private void LateUpdate()
        {
            UpdateDock();
            UpdateHover();
        }

        /// <summary>
        /// 배경(길 스트립) 상단선을 화면 좌표로 환산해, UI 블록 하단이 그 선 바로 위(dockMargin 간격)에 오도록
        /// DockRoot를 아래로 내린다. <b>고정 배치</b>: 창 크기(해상도)·캔버스 스케일이 바뀌거나 UI가 재구성될 때만
        /// 재계산하고, 그 외에는 위치를 유지한다(카메라 이동·줌 등에 따라 위아래로 흔들리지 않음).
        /// </summary>
        private void UpdateDock()
        {
            if (!dockAboveBackground || _dockRoot == null || _blockBottomFromTop <= 0f) return;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            var cam = Camera.main;
            if (_canvas == null || cam == null) return;

            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            if (_dockApplied && Screen.width == _dockScreenW && Screen.height == _dockScreenH &&
                Mathf.Approximately(scale, _dockScale))
            {
                return; // 창 크기 변화 없음 → 현재 위치 고정 유지
            }
            if (_background == null)
            {
                _background = Object.FindAnyObjectByType<ScrollingBackground>();
                if (_background == null) return;
            }

            float stripTopScreenY = cam.WorldToScreenPoint(new Vector3(cam.transform.position.x, _background.VisibleTopY, 0f)).y;
            float stripFromTop = (Screen.height - stripTopScreenY) / scale; // 캔버스 단위, 화면 상단 기준
            float shift = stripFromTop - dockMargin - _blockBottomFromTop;
            _dockRoot.anchoredPosition = new Vector2(0f, -Mathf.Max(0f, shift));

            _dockApplied = true;
            _dockScreenW = Screen.width;
            _dockScreenH = Screen.height;
            _dockScale = scale;
        }

        /// <summary>초상화 옆에 세로 체력바(배경+아래→위 채움)를 만들어 멤버에 연결한다. pos는 바 <b>하단 중앙</b> 위치(초상화 하단에 정렬).</summary>
        private void CreateHpBar(PlayerCombatant member, int i, Vector2 pos)
        {
            // 배경(상단 코너 앵커 기준, 피벗을 하단 중앙으로 두어 pos가 바닥 시작점이 되게)
            var bg = new GameObject("HpBarBg_" + i, typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_dockRoot, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.pivot = new Vector2(0.5f, 0f); // 하단 중앙 피벗 → anchoredPosition.y = 바 바닥
            bgRt.sizeDelta = new Vector2(hpBarWidth, hpBarHeight);
            PlaceTopCorner(bgRt, pos.x, pos.y);
            var bgImg = bg.GetComponent<Image>();
            bgImg.sprite = WhiteSprite;
            bgImg.color = hpBarBgColor;
            _spawned.Add(bg);

            // 채움(부모에 꽉 차게, 1px 인셋) — 세로 채움(아래에서 위로)
            var fg = new GameObject("HpBarFill_" + i, typeof(RectTransform), typeof(Image));
            fg.transform.SetParent(bg.transform, false);
            var fgRt = fg.GetComponent<RectTransform>();
            fgRt.anchorMin = new Vector2(0f, 0f);
            fgRt.anchorMax = new Vector2(1f, 1f);
            fgRt.offsetMin = new Vector2(1f, 1f);
            fgRt.offsetMax = new Vector2(-1f, -1f);
            var fgImg = fg.GetComponent<Image>();
            fgImg.sprite = WhiteSprite;
            fgImg.type = Image.Type.Filled;
            fgImg.fillMethod = Image.FillMethod.Vertical;
            fgImg.fillOrigin = (int)Image.OriginVertical.Bottom;
            fgImg.color = hpBarFillColor;
            fgImg.fillAmount = 1f;

            _hpBars.Add(new HpBar { member = member, fill = fgImg });
        }

        private void UpdateCooldowns()
        {
            foreach (var hb in _hpBars)
            {
                if (hb == null || hb.member == null || hb.fill == null) continue;
                hb.fill.fillAmount = hb.member.MaxHp > 0 ? Mathf.Clamp01((float)hb.member.Hp / hb.member.MaxHp) : 0f;
            }

            foreach (var s in _slots)
            {
                if (s == null || s.member == null) continue;
                bool ok = s.member.TryGetSkillCooldown(s.skillCode, out float remaining, out float total);
                float frac = (ok && total > 0f) ? Mathf.Clamp01(remaining / total) : 0f;
                if (s.cooldownFill != null)
                {
                    s.cooldownFill.fillAmount = frac;
                    s.cooldownFill.enabled = frac > 0.001f;
                }
                if (s.remainingText != null)
                {
                    s.remainingText.text = remaining > 0.05f ? Mathf.CeilToInt(remaining).ToString() : "";
                }
            }
        }

        private static Transform FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindChild(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
