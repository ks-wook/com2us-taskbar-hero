using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.MasterData;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 오프라인(방치) 보상 정산 결과 팝업. Login→GameScene 전환 시 정산한 결과(<see cref="Session.PendingOfflineReward"/>)를
    /// 표시한다. 상단에 경과 시간·획득 골드/경험치를 요약하고, 하단에 파티원 3명의 초상화 프리팹 + 정산 후 레벨 + 경험치 막대를 보여준다.
    /// 계층은 에디터 빌드 시 프리팹에 정적 저장되고(코드 구성), 표시될 때마다 세션 실데이터로 갱신한다.
    /// (파티 편성 패널 <see cref="PartyPanelController"/>와 동일한 초상화 렌더 방식을 따른다.)
    /// </summary>
    public class OfflineRewardPanelController : MonoBehaviour
    {
        private const int MaxSlots = 3;
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        [SerializeField] private Button _confirmButton;
        [SerializeField] private Text _subtitleText;   // 경과 시간 안내
        [SerializeField] private Text _goldText;        // 획득 골드
        [SerializeField] private Text _expText;         // 획득 경험치(파티 공통)
        // 슬롯별 구성(에디터 빌드로 baked): 캐릭터 프리팹 렌더(RawImage)·직업/레벨 캡션·경험치 막대(fill)·경험치 텍스트.
        [SerializeField] private List<RectTransform> _slots = new List<RectTransform>();
        [SerializeField] private List<RawImage> _slotRenders = new List<RawImage>();
        [SerializeField] private List<Text> _slotCaptions = new List<Text>();
        [SerializeField] private List<RectTransform> _slotExpFills = new List<RectTransform>();
        [SerializeField] private List<Text> _slotExpTexts = new List<Text>();
        [Header("직업별 캐릭터 프리팹(classCode → 프리팹, 에디터 빌더가 배선: 기사1·레인저2·마법사3)")]
        [SerializeField] private List<ClassCharacter> _classCharacters = new List<ClassCharacter>();
        [Header("배경")]
        [Tooltip("Assets/Art/UI/modal_bg를 배선한다(에디터 빌더). 없으면 단색 배경.")]
        [SerializeField] private Sprite _backgroundSprite;

        /// <summary>초상화에 렌더할 직업별 캐릭터 프리팹 매핑(classCode → 프리팹).</summary>
        [System.Serializable]
        private struct ClassCharacter
        {
            public int classCode;
            public GameObject prefab;
        }

        // 초상화 렌더 설정(파티 패널과 동일 방식). 슬롯마다 화면 밖 격리 위치에 캐릭터를 두고 전용 카메라로 렌더한다.
        private const int PortraitLayer = 28; // 파티/인벤토리 초상과 공유(패널 상호 배타), 전투 초상(29~31)과 비겹침
        private const float PortraitOrtho = 0.7f;
        private static readonly Vector2 PortraitAim = new Vector2(0f, 0.45f);
        private static readonly Color PortraitBg = new Color(0.10f, 0.11f, 0.16f, 1f);

        private CharacterPortrait[] _portraits;
        private GameObject[] _portraitStages;
        private int[] _portraitClassCode;

        private Font _font;
        private bool AlreadyBuilt => _slots != null && _slots.Count == MaxSlots && _slots[0] != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct();
            }
            WireRuntime();
        }

        private void OnEnable()
        {
            if (!AlreadyBuilt)
            {
                return;
            }
            EnsurePortraits();

            // Login에서 정산해 대기 중인 결과를 1회 소비해 표시한다. 없으면(직접 진입 등) 팝업을 닫는다.
            var result = Session.ConsumePendingOfflineReward();
            if (result == null)
            {
                Close();
                return;
            }
            Populate(result);
        }

        /// <summary>패널이 숨겨지면 초상화 렌더러(카메라)를 꺼 불필요한 렌더를 막는다.</summary>
        private void OnDisable()
        {
            if (_portraitStages != null)
            {
                foreach (var s in _portraitStages)
                {
                    if (s != null) s.SetActive(false);
                }
            }
        }

        /// <summary>파괴 시 화면 밖 초상화 스테이지(카메라·RT·캐릭터 인스턴스)를 함께 정리한다.</summary>
        private void OnDestroy()
        {
            if (_portraitStages != null)
            {
                foreach (var s in _portraitStages)
                {
                    if (s != null) Destroy(s);
                }
                _portraitStages = null;
            }
        }

        /// <summary>에디터 빌드 전용: 전체 계층 생성.</summary>
        public void EditorConstruct() => Construct();

        // ── 구성 ──

        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);
            BuildSummary(panel);
            BuildSlots(panel);
            BuildConfirm(panel);
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110; // 다른 패널(100)보다 위(진입 시 최상단 팝업)
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0.72f));
            Stretch(img.rectTransform);
            // 보상 팝업은 반드시 '받기'로만 닫도록 Dim 클릭으로는 닫지 않는다(입력만 가로막음).
            img.raycastTarget = true;
        }

        private RectTransform BuildPanel()
        {
            var img = NewImage("OfflineRewardRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            ApplyBackground(img);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(880f, 1180f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private void BuildHeader(RectTransform panel)
        {
            var title = NewText("Title", panel, "오프라인 보상", 52, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -32f);
            trt.sizeDelta = new Vector2(700f, 70f);

            _subtitleText = NewText("Subtitle", panel, "", 30, TextAnchor.MiddleCenter);
            _subtitleText.color = new Color(0.8f, 0.85f, 0.95f);
            var srt = _subtitleText.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -108f);
            srt.sizeDelta = new Vector2(760f, 48f);
        }

        /// <summary>획득 골드·경험치 요약 박스(파티 공통 지급분)를 구성한다.</summary>
        private void BuildSummary(RectTransform panel)
        {
            var box = NewImage("SummaryBox", panel, new Color(0.15f, 0.17f, 0.26f, 1f));
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -176f);
            brt.sizeDelta = new Vector2(760f, 130f);

            _goldText = NewText("GoldGained", box.rectTransform, "", 34, TextAnchor.MiddleCenter);
            _goldText.fontStyle = FontStyle.Bold;
            PlaceCenter(_goldText.rectTransform, 0.5f, 0.72f, 720f, 48f);

            _expText = NewText("ExpGained", box.rectTransform, "", 34, TextAnchor.MiddleCenter);
            _expText.fontStyle = FontStyle.Bold;
            _expText.color = new Color(0.6f, 0.9f, 1f);
            PlaceCenter(_expText.rectTransform, 0.5f, 0.28f, 720f, 48f);
        }

        private void BuildSlots(RectTransform panel)
        {
            _slots.Clear();
            _slotRenders.Clear();
            _slotCaptions.Clear();
            _slotExpFills.Clear();
            _slotExpTexts.Clear();

            const float slotW = 250f;
            const float slotH = 620f;
            const float gap = 16f;
            float totalW = MaxSlots * slotW + (MaxSlots - 1) * gap;
            float startX = -totalW * 0.5f + slotW * 0.5f;

            for (int i = 0; i < MaxSlots; i++)
            {
                var slot = NewImage($"OfflineSlot{i}", panel, new Color(0.13f, 0.15f, 0.23f, 1f));
                var rt = slot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(slotW, slotH);
                rt.anchoredPosition = new Vector2(startX + i * (slotW + gap), -60f);
                _slots.Add(rt);

                // 캐릭터 프리팹 렌더(RawImage). 상단 대부분을 채우고 하단은 캡션·경험치 공간.
                var render = NewRawImage("Render", rt);
                render.rectTransform.anchorMin = new Vector2(0f, 0f);
                render.rectTransform.anchorMax = new Vector2(1f, 1f);
                render.rectTransform.offsetMin = new Vector2(10f, 150f);
                render.rectTransform.offsetMax = new Vector2(-10f, -10f);
                render.color = new Color(1f, 1f, 1f, 0f); // 캐릭터 배정 전 투명
                render.raycastTarget = false;
                render.gameObject.SetActive(false);
                _slotRenders.Add(render);

                // 직업 · 레벨 캡션
                var cap = NewText("Caption", rt, "", 28, TextAnchor.MiddleCenter);
                cap.fontStyle = FontStyle.Bold;
                PlaceCenter(cap.rectTransform, 0.5f, 0f, slotW - 20f, 40f);
                cap.rectTransform.anchoredPosition = new Vector2(0f, 112f);
                _slotCaptions.Add(cap);

                // 경험치 막대(배경 + 좌측 앵커 fill)
                var barBg = NewImage("ExpBarBg", rt, new Color(0.05f, 0.06f, 0.09f, 1f));
                var barRt = barBg.rectTransform;
                barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0f);
                barRt.pivot = new Vector2(0.5f, 0f);
                barRt.anchoredPosition = new Vector2(0f, 64f);
                barRt.sizeDelta = new Vector2(slotW - 30f, 28f);

                var fill = NewImage("ExpBarFill", barBg.rectTransform, new Color(0.35f, 0.7f, 1f, 1f));
                var fillRt = fill.rectTransform;
                fillRt.anchorMin = new Vector2(0f, 0f);
                fillRt.anchorMax = new Vector2(0f, 1f); // 채움 비율은 갱신 시 anchorMax.x로 조정
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillRt.offsetMin = Vector2.zero;
                fillRt.offsetMax = Vector2.zero;
                _slotExpFills.Add(fillRt);

                // 경험치 텍스트(현재/요구)
                var expT = NewText("ExpText", rt, "", 22, TextAnchor.MiddleCenter);
                expT.color = new Color(0.8f, 0.88f, 1f);
                PlaceCenter(expT.rectTransform, 0.5f, 0f, slotW - 20f, 32f);
                expT.rectTransform.anchoredPosition = new Vector2(0f, 30f);
                _slotExpTexts.Add(expT);
            }
        }

        private void BuildConfirm(RectTransform panel)
        {
            var btn = NewImage("ConfirmButton", panel, new Color(0.25f, 0.55f, 0.35f, 1f));
            var rt = btn.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 30f);
            rt.sizeDelta = new Vector2(400f, 96f);
            var label = NewText("Label", btn.rectTransform, "받기", 40, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            Stretch(label.rectTransform);
            _confirmButton = btn.gameObject.AddComponent<Button>();
        }

        private void WireRuntime()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(Close);
                _confirmButton.onClick.AddListener(Close);
            }
        }

        /// <summary>슬롯별 초상화 렌더러(전용 카메라+RT를 얹은 화면 밖 오브젝트)를 1회 생성한다(런타임 전용).</summary>
        private void EnsurePortraits()
        {
            if (!Application.isPlaying || _slotRenders == null || _slotRenders.Count < MaxSlots)
            {
                return;
            }
            if (_portraits != null)
            {
                foreach (var s in _portraitStages)
                {
                    if (s != null) s.SetActive(true);
                }
                return;
            }
            _portraits = new CharacterPortrait[MaxSlots];
            _portraitStages = new GameObject[MaxSlots];
            _portraitClassCode = new int[MaxSlots];
            for (int i = 0; i < MaxSlots; i++)
            {
                _portraitClassCode[i] = -2; // 첫 갱신에서 반드시 배정되도록
                // 파티 패널(700대)·인벤토리와 겹치지 않는 화면 밖 위치.
                var origin = new Vector3(1500f + i * 40f, 1500f, 0f);
                var stage = new GameObject($"OfflinePortraitStage{i}");
                stage.transform.position = origin;
                var p = stage.AddComponent<CharacterPortrait>();
                p.Initialize(_slotRenders[i], PortraitLayer, 300, 460, PortraitOrtho, PortraitAim, PortraitBg, origin);
                _portraits[i] = p;
                _portraitStages[i] = stage;
            }
        }

        // ── 갱신(정산 결과) ──

        /// <summary>정산 결과로 요약(경과·골드·경험치)과 3슬롯(초상화·레벨·경험치 막대)을 채운다.</summary>
        private void Populate(OfflineRewardResult result)
        {
            long gainedGold = result.rewards != null ? result.rewards.gold : 0L;
            long gainedExp = result.rewards != null ? result.rewards.exp : 0L;

            if (_subtitleText != null)
            {
                string span = FormatDuration(result.effectiveSec);
                _subtitleText.text = result.capped
                    ? $"{span} 동안 자리를 비웠어요 (최대 12시간)"
                    : $"{span} 동안 자리를 비웠어요";
            }
            if (_goldText != null)
            {
                _goldText.text = $"골드  +{GoldFormat.Highlight(gainedGold)}";
            }
            if (_expText != null)
            {
                _expText.text = $"경험치  +{gainedExp:N0}";
            }

            var db = MasterDataManager.Db;
            var states = result.characters;

            for (int i = 0; i < MaxSlots; i++)
            {
                OfflineCharacterState state = states != null && i < states.Count ? states[i] : null;
                var render = i < _slotRenders.Count ? _slotRenders[i] : null;
                var cap = i < _slotCaptions.Count ? _slotCaptions[i] : null;
                var fill = i < _slotExpFills.Count ? _slotExpFills[i] : null;
                var expT = i < _slotExpTexts.Count ? _slotExpTexts[i] : null;
                var slot = i < _slots.Count ? _slots[i] : null;

                if (state == null)
                {
                    // 파티원이 3명 미만이면 남는 슬롯은 숨긴다.
                    if (slot != null) slot.gameObject.SetActive(false);
                    if (_portraits != null && i < _portraits.Length && _portraits[i] != null && _portraitClassCode[i] != -1)
                    {
                        _portraits[i].SetCharacter(null);
                        _portraitClassCode[i] = -1;
                    }
                    continue;
                }

                if (slot != null) slot.gameObject.SetActive(true);

                int classCode = ClassCodeOf(state.characterId);
                var prefab = PrefabForClass(classCode);
                if (_portraits != null && i < _portraits.Length && _portraits[i] != null && _portraitClassCode[i] != classCode)
                {
                    _portraits[i].SetCharacter(prefab);
                    _portraitClassCode[i] = classCode;
                }
                if (render != null)
                {
                    render.gameObject.SetActive(true);
                    render.color = prefab != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                }
                if (cap != null)
                {
                    string cls = db != null && db.Classes.TryGetValue(classCode, out var cm) ? cm.name : $"직업 {classCode}";
                    cap.text = $"{cls} Lv.{state.level}";
                }

                // 경험치 막대: exp는 현재 레벨 내 누적치, requiredExp는 레벨업 필요량(level_master). 최대 레벨은 MAX.
                long required = db != null && db.Levels.TryGetValue(state.level, out var lm) ? lm.requiredExp : 0L;
                if (required <= 0)
                {
                    if (fill != null) fill.anchorMax = new Vector2(1f, 1f);
                    if (expT != null) expT.text = "EXP  MAX";
                }
                else
                {
                    long cur = state.exp > 0 ? state.exp : 0;
                    float frac = Mathf.Clamp01((float)cur / required);
                    if (fill != null) fill.anchorMax = new Vector2(frac, 1f);
                    if (expT != null) expT.text = $"EXP  {cur:N0} / {required:N0}";
                }
            }
        }

        /// <summary>정산 대상 캐릭터(characterId)의 직업 코드를 세션 세이브에서 조회한다(없으면 -1).</summary>
        private static int ClassCodeOf(int characterId)
        {
            var chars = Session.GameData != null ? Session.GameData.characters : null;
            if (chars != null)
            {
                foreach (var c in chars)
                {
                    if (c != null && c.characterId == characterId)
                    {
                        return c.classCode;
                    }
                }
            }
            return -1;
        }

        /// <summary>classCode에 해당하는 초상화 캐릭터 프리팹을 반환한다(없으면 null).</summary>
        private GameObject PrefabForClass(int classCode)
        {
            if (_classCharacters != null)
            {
                foreach (var e in _classCharacters)
                {
                    if (e.prefab != null && e.classCode == classCode)
                    {
                        return e.prefab;
                    }
                }
            }
            return null;
        }

        /// <summary>경과 초를 "N시간 M분"(또는 "M분") 형태로 표기한다.</summary>
        private static string FormatDuration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            long hours = seconds / 3600;
            long minutes = (seconds % 3600) / 60;
            if (hours > 0)
            {
                return minutes > 0 ? $"{hours}시간 {minutes}분" : $"{hours}시간";
            }
            return $"{minutes}분";
        }

        /// <summary>팝업을 닫는다.</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.OfflineReward);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 헬퍼 ──

        /// <summary>패널 루트 배경에 배경 스프라이트(modal_bg)를 적용한다(지정 시). 없으면 기존 단색 배경 유지.</summary>
        private void ApplyBackground(Image img)
        {
            if (img == null || _backgroundSprite == null) return;
            img.sprite = _backgroundSprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static RawImage NewRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RawImage>();
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void PlaceCenter(RectTransform rt, float fx, float fy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(fx, fy);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
