using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스테이지 클리어 연출 오버레이. 서버 클리어 응답을 받으면 화면 전체에 표시된다.
    /// - 클리어 팡파레 이펙트(프레임 시퀀스, 슬로우모션과 무관하게 unscaled 시간으로 재생).
    ///   <b>클리어 연출에서만</b> 재생하며, 보상 획득 연출로 재활용할 때는 뒤편에 나오지 않는다.
    /// - 보상(골드·경험치·전리품 아이템 아이콘+수량) 노출. 세 종류 모두 공용 아이템 슬롯
    ///   프리팹(ItemSlot, <see cref="ItemSlotView"/>)을 사용해 수량/획득량이 슬롯 안쪽에
    ///   동일하게 노출되도록 통일한다(경험치는 아이콘 대신 "EXP" 라벨을 표시하는 SetupLabel 사용).
    ///   보상 칸은 한꺼번에 나타나지 않고 <b>왼쪽 칸부터 하나씩</b> 없던 상태에서 커지며 등장한다
    ///   (<see cref="StartRewardPopIn"/>).
    /// - 화면 클릭 또는 5초 경과 시 자동으로 닫히며, 닫힐 때 게임 속도를 정상으로 복원한다.
    /// 정적 계층(Canvas·팡파레·타이틀·보상 행 컨테이너·안내 문구)은 <see cref="EditorConstruct"/>가
    /// 구성해 <c>Assets/Prefabs/UI/StageClearOverlay.prefab</c>으로 저장되고(StageClearOverlayBuilder),
    /// 런타임에는 이 프리팹을 Instantiate해 데이터만 배선한다(가변 개수의 보상 칸만 동적으로 채움).
    /// 프리팹이 없으면(에셋 미빌드) 폴백으로 런타임에 EditorConstruct를 직접 호출한다.
    /// </summary>
    public class StageClearOverlay : MonoBehaviour
    {
        private const float AutoCloseSeconds = 5f;
        private const float FanfareFps = 24f;

        // 보상 칸 등장 연출: 왼쪽 칸부터 차례로, 없던 상태(스케일 0)에서 원래 크기로 커진다.
        private const float RewardPopDuration = 0.22f;  // 칸 하나가 다 커지는 데 걸리는 시간
        private const float RewardPopInterval = 0.11f;  // 다음 칸이 등장하기까지의 간격

        [Tooltip("전체화면 클릭 시 닫기 처리할 투명 차단막 버튼.")]
        [SerializeField] private Button _dimButton;
        [Tooltip("클리어 팡파레 프레임 시퀀스를 그리는 이미지.")]
        [SerializeField] private Image _fanfareImage;
        [Tooltip("상단 타이틀 텍스트. 재활용 시 호출측이 문구를 바꾼다(클리어/우편 수령 등).")]
        [SerializeField] private Text _titleText;
        [Tooltip("보상 칸(공용 아이템 슬롯)이 채워지는 가로 정렬 컨테이너.")]
        [SerializeField] private RectTransform _rewardsRow;

        private const string StageClearTitle = "STAGE CLEAR!";

        private StageClearAssets _assets;
        private Sprite[] _frames;
        private int _frameIndex;
        private float _frameTimer;
        private bool _dismissed;
        private bool _restoreTimeScale;
        private bool _showFanfare;
        private Action _onClosed;

        /// <summary>클리어 응답 데이터로 오버레이를 생성·표시한다. onClosed는 닫힐 때(클릭/자동) 1회 호출된다.
        /// 전투 종료 슬로우모션 상태에서 열리므로 닫을 때 게임 속도를 정상으로 되돌리고, 팡파레를 재생한다.</summary>
        public static void Show(StageClearData data, Action onClosed = null)
        {
            // 전투 연출 띠(기능 패널 아래) — 인벤토리·출석부 등을 열어 둔 동안 가리지 않는다.
            SoundManager.Jingle(SoundId.JingleStageClear);
            ShowRewards(StageClearTitle, data != null ? data.rewards : null,
                restoreTimeScale: true, showFanfare: true, UiSortingOrder.BattleResult, onClosed);
        }

        /// <summary>
        /// 클리어 연출 UI(보상 칸 순차 등장)를 <b>보상 획득 연출로 재활용</b>한다.
        /// 우편함 첨부 수령처럼 "무엇을 얼마나 받았는지"를 같은 방식으로 보여줄 때 쓴다.
        /// 전투 승리 연출이 아니므로 <b>뒤편 팡파레 이펙트는 재생하지 않고</b> 게임 속도에도 손대지 않는다.
        /// </summary>
        /// <param name="title">상단에 표시할 문구(예: "보상 획득!").</param>
        /// <param name="rewards">표시할 보상(골드·경험치·아이템). 비어 있으면 보상 칸 없이 연출만 나온다.</param>
        public static void ShowRewards(string title, StageRewardsDto rewards, Action onClosed = null)
        {
            // 패널(우편함·거래소)에서 띄우므로 그 위 띠에 올린다 — 전투 연출과 달리 패널에 가려지면 안 된다.
            ShowRewards(title, rewards, restoreTimeScale: false, showFanfare: false,
                UiSortingOrder.RewardOverPanel, onClosed);
        }

        /// <summary>오버레이를 만들어 표시하는 공통 경로(클리어·보상 획득 재활용 양쪽).
        /// <paramref name="sortingOrder"/>로 어느 띠에 뜰지 정한다(클리어는 패널 아래, 보상 재활용은 패널 위).
        /// 프리팹이 없으면 런타임 구성으로 폴백한다.</summary>
        private static void ShowRewards(string title, StageRewardsDto rewards, bool restoreTimeScale,
            bool showFanfare, int sortingOrder, Action onClosed)
        {
            var assets = StageClearAssets.Load();
            StageClearOverlay overlay;
            if (assets != null && assets.overlayPrefab != null)
            {
                var go = Instantiate(assets.overlayPrefab);
                overlay = go.GetComponent<StageClearOverlay>();
            }
            else
            {
                Debug.LogWarning("[StageClear] StageClearAssets.overlayPrefab이 없어 런타임 구성으로 대체합니다. " +
                                 "에디터에서 'TaskbarHero/UI/클리어 연출 프리팹 빌드'를 실행하세요.");
                var go = new GameObject("StageClearOverlay");
                overlay = go.AddComponent<StageClearOverlay>();
                overlay.EditorConstruct();
                overlay.WireEvents();
            }

            overlay._assets = assets;
            overlay._onClosed = onClosed;
            overlay._restoreTimeScale = restoreTimeScale;
            overlay._showFanfare = showFanfare;
            overlay.ApplySortingOrder(sortingOrder); // 프리팹에 구워진 값을 호출 경로에 맞게 덮어쓴다
            overlay.SetTitle(title);
            overlay.Populate(rewards);
        }

        /// <summary>이 오버레이 캔버스의 정렬 순서를 설정한다(같은 프리팹을 두 띠에서 쓰므로 표시 직전에 정한다).</summary>
        private void ApplySortingOrder(int sortingOrder)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.sortingOrder = sortingOrder;
            }
        }

        /// <summary>상단 타이틀 문구를 바꾼다(빈 값이면 프리팹에 구워진 기본 문구를 유지).</summary>
        private void SetTitle(string title)
        {
            if (_titleText != null && !string.IsNullOrEmpty(title))
            {
                _titleText.text = title;
            }
        }

#if UNITY_EDITOR
        /// <summary>[에디터 QA용] 샘플 보상 데이터로 오버레이를 띄운다(네트워크 없이 연출 확인).</summary>
        public static void ShowDebugSample()
        {
            var data = new StageClearData();
            data.rewards.gold = 1200;
            data.rewards.exp = 340;
            // 등급 1~5 각 1개씩 넣어 등급별 슬롯 배경색을 한눈에 확인한다(31111~31151).
            data.rewards.items.Add(new RewardItemDto { itemCode = 31111, quantity = 1 }); // 노말
            data.rewards.items.Add(new RewardItemDto { itemCode = 31121, quantity = 2 }); // 고급
            data.rewards.items.Add(new RewardItemDto { itemCode = 31131, quantity = 1 }); // 희귀
            data.rewards.items.Add(new RewardItemDto { itemCode = 31141, quantity = 1 }); // 영웅
            data.rewards.items.Add(new RewardItemDto { itemCode = 31151, quantity = 1 }); // 전설
            Show(data);
        }
#endif

        private void Awake()
        {
            WireEvents();
        }

        /// <summary>구조 배선 후 이벤트 리스너를 연결한다. 프리팹 Instantiate/런타임 폴백 구성 양쪽에서 호출된다.</summary>
        private void WireEvents()
        {
            if (_dimButton != null)
            {
                _dimButton.onClick.RemoveListener(Dismiss); // 중복 등록 방지
                _dimButton.onClick.AddListener(Dismiss);
            }
        }

        /// <summary>정적 계층(Canvas·Dim·팡파레·타이틀·보상 행 컨테이너·안내 문구)을 구성한다.
        /// StageClearOverlayBuilder가 에디터에서 1회 호출해 프리팹으로 굽거나(정본 경로),
        /// 프리팹이 없을 때 런타임 폴백으로 직접 호출된다.</summary>
        public void EditorConstruct()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 최상단 캔버스(패널 100·HUD 10보다 위).
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 프리팹에 굽는 기본값(전투 연출 띠). 표시 직전 ApplySortingOrder가 호출 경로에 맞게 덮어쓴다.
            canvas.sortingOrder = UiSortingOrder.BattleResult;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // 어두운 배경(클릭 시 닫힘).
            var dim = CreateChild("Dim", transform, Vector2.zero, Vector2.one);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 클릭 닫기용 투명 차단막
            _dimButton = dim.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;

            // 팡파레 이펙트(화면 중앙, 보상 아이템 뒤). Dim 다음·보상 앞에 생성되어 아이템보다 뒤에 그려진다.
            var fanfare = CreateChild("Fanfare", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            fanfare.sizeDelta = new Vector2(900f, 900f);
            fanfare.anchoredPosition = Vector2.zero;
            _fanfareImage = fanfare.gameObject.AddComponent<Image>();
            _fanfareImage.raycastTarget = false;
            _fanfareImage.preserveAspect = true; // 전체 프레임을 잘림 없이 표시

            // 타이틀(상단). 기본은 "STAGE CLEAR!"이며, 재활용 시 호출측이 SetTitle로 문구를 바꾼다.
            var title = CreateText("Title", transform, font, StageClearTitle, 96, TextAnchor.MiddleCenter);
            title.color = new Color(1f, 0.92f, 0.4f);
            title.fontStyle = FontStyle.Bold;
            _titleText = title;
            var trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900f, 160f);
            trt.anchoredPosition = new Vector2(0f, 440f);

            // 보상 행 컨테이너(화면 중앙, 팡파레 위에 그려짐). 실제 보상 칸(가변 개수)은 Populate가 채운다.
            _rewardsRow = CreateChild("Rewards", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _rewardsRow.sizeDelta = new Vector2(960f, 220f);
            _rewardsRow.anchoredPosition = Vector2.zero;
            var layout = _rewardsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // 안내 문구.
            var hint = CreateText("Hint", transform, font, "클릭하거나 잠시 기다리면 닫힙니다", 34, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.7f);
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(900f, 60f);
            hrt.anchoredPosition = new Vector2(0f, 70f);
        }

        /// <summary>보상 데이터로 팡파레·보상 칸을 채우고 자동 닫기 타이머를 시작한다.
        /// 팡파레는 클리어 연출에서만 재생한다(재활용 호출은 <see cref="_showFanfare"/>가 false라 이미지를 꺼 둔다).</summary>
        private void Populate(StageRewardsDto rewards)
        {
            _frames = _showFanfare && _assets != null ? _assets.fanfareFrames : null;
            if (_fanfareImage != null)
            {
                bool hasFrames = _frames != null && _frames.Length > 0;
                _fanfareImage.enabled = hasFrames;
                if (hasFrames)
                {
                    _fanfareImage.sprite = _frames[0];
                }
            }

            BuildRewards(rewards);
            StartRewardPopIn();

            StartCoroutine(AutoCloseAfter(AutoCloseSeconds));
        }

        /// <summary>
        /// 보상 칸 등장 연출을 시작한다. 만들어진 칸을 <b>같은 프레임에</b> 스케일 0으로 감춰
        /// (한 프레임 통째로 보였다 사라지는 깜빡임 방지) 왼쪽부터 하나씩 커지게 한다.
        /// 가로 레이아웃은 칸의 크기(sizeDelta)로 자리를 잡고 스케일은 보지 않으므로,
        /// 등장 중에도 각 칸의 위치는 흔들리지 않는다.
        /// </summary>
        private void StartRewardPopIn()
        {
            if (_rewardsRow == null || _rewardsRow.childCount == 0)
            {
                return;
            }

            var slots = new RectTransform[_rewardsRow.childCount];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = (RectTransform)_rewardsRow.GetChild(i); // 자식 순서 = 화면 왼쪽부터의 순서
                slots[i].localScale = Vector3.zero;
            }
            StartCoroutine(RewardPopInSequence(slots));
        }

        /// <summary>보상 칸을 왼쪽부터 <see cref="RewardPopInterval"/> 간격으로 하나씩 등장시킨다.</summary>
        private IEnumerator RewardPopInSequence(RectTransform[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                StartCoroutine(RewardPopIn(slots[i]));
                yield return new WaitForSecondsRealtime(RewardPopInterval);
            }
        }

        /// <summary>칸 하나를 스케일 0 → 원래 크기로 키운다(슬로우모션과 무관하게 unscaled 시간 사용).</summary>
        private static IEnumerator RewardPopIn(RectTransform slot)
        {
            float elapsed = 0f;
            while (elapsed < RewardPopDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / RewardPopDuration);
                slot.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, k);
                yield return null;
            }
            slot.localScale = Vector3.one;
        }

        /// <summary>골드·경험치·전리품 아이템을 보상 행에 채운다(모두 공용 아이템 슬롯으로 통일).</summary>
        private void BuildRewards(StageRewardsDto rewards)
        {
            if (_rewardsRow == null)
            {
                return;
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 경험치 — 공용 슬롯(ItemSlot 프리팹)에 아이콘 대신 "EXP" 라벨을 표시해 골드·아이템과 동일하게
            // 슬롯 안쪽(우하단)에 획득량이 노출되도록 통일한다.
            if (rewards != null && rewards.exp > 0)
            {
                CreateLabelRewardEntry(_rewardsRow, font, "EXP", new Color(0.4f, 0.8f, 1f), $"+{rewards.exp:N0}");
            }
            // 골드(item_1 아이콘 재사용). 마스터 데이터에 없는 재화라 hover 상세는 끈다.
            if (rewards != null && rewards.gold > 0)
            {
                CreateRewardEntry(_rewardsRow, font, 1, rewards.gold, $"+{rewards.gold:N0}", false);
            }
            // 전리품 아이템 — 공용 슬롯(ItemSlot 프리팹)이 등급 배경·아이콘·수량을 표시하고 hover 시 상세 팝업을 띄운다.
            if (rewards != null && rewards.items != null)
            {
                foreach (var item in rewards.items)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    string qty = item.quantity > 1 ? $"x{item.quantity}" : string.Empty;
                    CreateRewardEntry(_rewardsRow, font, item.itemCode, item.quantity, qty, true);
                }
            }
        }

        /// <summary>아이템 코드의 등급(1~5)을 마스터 데이터에서 조회한다. 없으면 노말(1).</summary>
        private static int GradeOf(int itemCode)
        {
            var db = MasterDataManager.Db;
            return db != null && db.Items.TryGetValue(itemCode, out var im) ? im.grade : 1;
        }

        /// <summary>보상 한 칸을 공용 아이템 슬롯 프리팹(ItemSlot)으로 만든다(등급 배경·아이콘·수량,
        /// hover 시 item_detail_bg 배경의 공용 상세 팝업). 프리팹 미배선(에셋 미빌드) 시 기존 코드 구성 폴백.</summary>
        private void CreateRewardEntry(RectTransform parent, Font font, int itemCode, long quantity, string qtyText, bool showDetail)
        {
            var prefab = _assets != null ? _assets.itemSlotPrefab : null;
            if (prefab != null)
            {
                var slotGo = Instantiate(prefab, parent);
                var srt = (RectTransform)slotGo.transform;
                srt.sizeDelta = new Vector2(150f, 150f);
                var view = slotGo.GetComponent<ItemSlotView>();
                if (view != null)
                {
                    view.Setup(itemCode, quantity, qtyText, showDetail);
                    return;
                }
                Destroy(slotGo); // 프리팹에 뷰가 없으면 폴백으로
            }
            CreateRewardEntryFallback(parent, font, GetIcon(itemCode), qtyText,
                itemCode == 1 ? new Color(1f, 0.85f, 0.3f) : Color.white,
                GradeColors.RewardSlotBackground(GradeOf(itemCode)));
        }

        /// <summary>아이콘이 없는 보상(경험치 등) 한 칸을 공용 아이템 슬롯 프리팹(ItemSlot)의 라벨 모드로
        /// 만든다. 골드·아이템과 동일한 슬롯에 텍스트 라벨 + 슬롯 안쪽 수치를 표시한다.
        /// 프리팹 미배선(에셋 미빌드) 시 기존 코드 구성 폴백.</summary>
        private void CreateLabelRewardEntry(RectTransform parent, Font font, string label, Color labelColor, string valueText)
        {
            var prefab = _assets != null ? _assets.itemSlotPrefab : null;
            if (prefab != null)
            {
                var slotGo = Instantiate(prefab, parent);
                var srt = (RectTransform)slotGo.transform;
                srt.sizeDelta = new Vector2(150f, 150f);
                var view = slotGo.GetComponent<ItemSlotView>();
                if (view != null)
                {
                    view.SetupLabel(label, labelColor, valueText);
                    return;
                }
                Destroy(slotGo);
            }
            CreateLabelRewardEntryFallback(parent, font, label, labelColor, valueText);
        }

        /// <summary>[폴백] 아이콘+수량 보상 항목 한 칸을 코드로 만든다(아이콘 없으면 색 사각형).
        /// slotColor는 등급별 슬롯 배경. 수량은 슬롯 안쪽 우하단에 표시해 실제 ItemSlot 프리팹과
        /// 동일한 배치가 되도록 한다. 폴백에서는 hover 상세를 제공하지 않는다.</summary>
        private void CreateRewardEntryFallback(RectTransform parent, Font font, Sprite icon, string qtyText, Color tint, Color slotColor)
        {
            var entry = new GameObject("Reward", typeof(RectTransform), typeof(Image));
            entry.transform.SetParent(parent, false);
            var ert = (RectTransform)entry.transform;
            ert.sizeDelta = new Vector2(150f, 150f);

            // 슬롯 프레임(item_slot 테두리 장식). 프레임 스프라이트가 없으면(에셋 미빌드) 등급색 사각형 폴백.
            var slotImg = entry.GetComponent<Image>();
            Sprite frame = _assets != null ? _assets.itemSlotFrame : null;
            if (frame != null)
            {
                slotImg.sprite = frame;
                slotImg.color = Color.white;
                // 등급별 배경색(프레임 테두리 안쪽 영역, 아이콘 뒤).
                var gradeRt = CreateChild("GradeBg", entry.transform, Vector2.zero, Vector2.one);
                gradeRt.offsetMin = new Vector2(12f, 12f);
                gradeRt.offsetMax = new Vector2(-12f, -12f);
                var gradeImg = gradeRt.gameObject.AddComponent<Image>();
                gradeImg.color = slotColor;
                gradeImg.raycastTarget = false;
            }
            else
            {
                slotImg.color = slotColor; // 등급별 배경색(프레임 미배선 폴백)
            }
            slotImg.raycastTarget = false;

            var iconRt = CreateChild("Icon", entry.transform, Vector2.zero, Vector2.one);
            iconRt.offsetMin = new Vector2(12f, 12f);
            iconRt.offsetMax = new Vector2(-12f, -12f);
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            if (icon != null)
            {
                iconImg.sprite = icon;
                iconImg.color = tint;
            }
            else
            {
                iconImg.color = tint * new Color(1f, 1f, 1f, 0.6f); // 아이콘 없을 때 색 폴백
            }

            CreateQuantityLabel(entry.transform, font, qtyText);
        }

        /// <summary>[폴백] 아이콘이 없는 보상(경험치 등) 한 칸을 코드로 만든다. 텍스트 라벨을 슬롯 중앙에,
        /// 수치는 슬롯 안쪽 우하단에 표시해 <see cref="CreateRewardEntryFallback"/>과 배치를 통일한다.</summary>
        private void CreateLabelRewardEntryFallback(RectTransform parent, Font font, string label, Color labelColor, string valueText)
        {
            var entry = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            entry.transform.SetParent(parent, false);
            var ert = (RectTransform)entry.transform;
            ert.sizeDelta = new Vector2(150f, 150f);

            var slotImg = entry.GetComponent<Image>();
            Sprite frame = _assets != null ? _assets.itemSlotFrame : null;
            if (frame != null)
            {
                slotImg.sprite = frame;
                slotImg.color = Color.white;
                var gradeRt = CreateChild("GradeBg", entry.transform, Vector2.zero, Vector2.one);
                gradeRt.offsetMin = new Vector2(12f, 12f);
                gradeRt.offsetMax = new Vector2(-12f, -12f);
                var gradeImg = gradeRt.gameObject.AddComponent<Image>();
                gradeImg.color = GradeColors.RewardSlotBackground(1);
                gradeImg.raycastTarget = false;
            }
            else
            {
                slotImg.color = GradeColors.RewardSlotBackground(1);
            }
            slotImg.raycastTarget = false;

            var lbl = CreateText("Label", entry.transform, font, label, 40, TextAnchor.MiddleCenter);
            lbl.color = labelColor;
            lbl.fontStyle = FontStyle.Bold;
            var lrt = (RectTransform)lbl.transform;
            lrt.anchorMin = new Vector2(0.09f, 0.09f);
            lrt.anchorMax = new Vector2(0.91f, 0.91f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            CreateQuantityLabel(entry.transform, font, valueText);
        }

        /// <summary>슬롯 안쪽 우하단에 수량/획득량 텍스트를 배치한다(ItemSlot 프리팹의 Qty 배치와 동일 비율).</summary>
        private void CreateQuantityLabel(Transform parent, Font font, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            var qty = CreateText("Qty", parent, font, text, 30, TextAnchor.LowerRight);
            qty.color = Color.white;
            qty.fontStyle = FontStyle.Bold;
            var qrt = (RectTransform)qty.transform;
            qrt.anchorMin = new Vector2(0.08f, 0.06f);
            qrt.anchorMax = new Vector2(0.92f, 0.36f);
            qrt.offsetMin = Vector2.zero;
            qrt.offsetMax = Vector2.zero;
        }

        private Sprite GetIcon(int code)
        {
            return _assets != null ? _assets.GetIcon(code) : null;
        }

        /// <summary>슬로우모션(timeScale)과 무관하게 unscaled 시간으로 팡파레를 재생한다.</summary>
        private void Update()
        {
            if (_frames == null || _frames.Length == 0 || _fanfareImage == null)
            {
                return;
            }
            _frameTimer += Time.unscaledDeltaTime;
            float dur = 1f / FanfareFps;
            while (_frameTimer >= dur)
            {
                _frameTimer -= dur;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                _fanfareImage.sprite = _frames[_frameIndex];
            }
        }

        private IEnumerator AutoCloseAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Dismiss();
        }

        /// <summary>오버레이를 닫고 게임 속도를 정상(1)으로 복원한다.</summary>
        public void Dismiss()
        {
            if (_dismissed)
            {
                return;
            }
            _dismissed = true;
            if (_restoreTimeScale)
            {
                Time.timeScale = 1f; // 전투 종료 슬로우모션 복원(클리어 연출 전용 — 재활용 호출은 속도에 손대지 않는다)
            }
            var cb = _onClosed;
            _onClosed = null;
            Destroy(gameObject);
            cb?.Invoke();
        }

        // ── UI 생성 헬퍼 ──

        private static RectTransform CreateChild(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Text CreateText(string name, Transform parent, Font font, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
