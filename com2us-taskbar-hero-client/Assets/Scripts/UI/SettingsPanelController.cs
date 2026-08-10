using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 환경설정 패널. <see cref="SoundManager"/>의 볼륨 3채널(전체·BGM·효과음)과 음소거를 슬라이더·토글로 조절한다.
    /// 값은 SoundManager가 PlayerPrefs에 저장하므로 다음 실행에도 유지되고, 조절 즉시 재생 중인 BGM에 반영된다.
    /// 효과음 슬라이더를 놓을 때는 <c>sfx_ui_slot_select</c>를 한 번 울려 바뀐 크기를 귀로 확인할 수 있게 한다.
    /// 창 배경은 가방·스킬·룬 창과 같은 공용 프레임(<c>Assets/Art/UI/ui_bg_2.png</c>)이고, 슬라이더 트랙과
    /// 하단 버튼은 ESC 메뉴와 같은 <c>Assets/Art/UI/System/system_slot.png</c>(9-slice)를 쓴다.
    /// 내용물은 모두 프레임 테두리 안쪽 빈 칸(<see cref="PanelFrame"/>)에만 놓는다.
    /// <para>닫기(X) 버튼은 두지 않고 <b>창 밖 클릭</b>으로 닫으며, 하단에 <b>게임종료</b> 버튼을 둔다
    /// (ESC 메뉴의 같은 항목과 동작이 같다).</para>
    /// 정적 계층은 에디터 빌더(SettingsUiBuilder)가 프리팹에 굽는다.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        // 창 크기. 배경은 공용 프레임(ui_bg_2)이며 Simple로 늘려 그리므로 장식이 찌그러지지 않게
        // 아트 비율(<see cref="PanelFrame.Aspect"/> ≒ 0.715)에 가깝게 잡는다(760/1040 ≒ 0.731).
        // 세로 1040은 <b>가장 낮은 논리 캔버스 높이</b>에 맞춘 값이다 — 16:9 고정 창인 타이틀 계열 씬에서는
        // 기본 규격(1080×1920 · match 0.5)의 논리 높이가 창 크기와 무관하게 1080이 된다(GameScene은 1440).
        private const float PanelWidth = 760f;
        private const float PanelHeight = 1040f;
        // 내용 영역(프레임 테두리 안쪽 빈 칸) 크기 — 창 크기에서 파생되는 상수식이다(538.4 × 755.5).
        private const float ContentWidth = PanelWidth * (1f - PanelFrame.InsetLeft - PanelFrame.InsetRight)
            - PanelFrame.Pad * 2f;
        private const float ContentHeight = PanelHeight * (1f - PanelFrame.InsetTop - PanelFrame.InsetBottom)
            - PanelFrame.Pad * 2f;

        // 내용 영역 <b>좌상단 기준</b> y(아래로 +). 세로 합이 ContentHeight를 넘지 않아 테두리에 닿지 않는다.
        private const float TitleY = 10f;
        private const float TitleHeight = 60f;
        private const float FirstRowY = 110f;   // 첫 볼륨 줄
        private const float RowHeight = 96f;
        private const float RowGap = 10f;
        private const float MuteRowY = 438f;    // 볼륨 3줄 아래
        private const float MuteRowHeight = 80f;
        private const float QuitButtonY = 600f;
        private const float QuitButtonWidth = 360f;
        private const float QuitButtonHeight = 100f;

        private const float SliderWidth = 300f;
        private const float SliderHeight = 28f;
        private const float RowLabelWidth = 120f;
        private const float RowValueWidth = 90f;
        // system_slot(2048×731) 테두리 상하 128px → 트랙 높이(28)에 맞추려면 크게 줄여야 한다.
        private const float TrackPixelsPerUnitMultiplier = 14f;
        // 같은 아트를 버튼(높이 100)에 쓸 때의 배율 — 128px 테두리가 32씩 되어 높이 안에 들어간다(ESC 메뉴와 동일).
        private const float ButtonPixelsPerUnitMultiplier = 4f;

        [Header("UI 리소스 (에디터 빌더가 배선)")]
        [Tooltip("창 배경 프레임(Assets/Art/UI/ui_bg_2.png — 가방·스킬·룬 창과 같은 공용 프레임).")]
        [SerializeField] private Sprite _panelSprite;   // ui_bg_2
        [Tooltip("Assets/Art/UI/System/system_slot.png(9-slice) — 슬라이더 트랙과 하단 버튼에 함께 쓴다.")]
        [SerializeField] private Sprite _trackSprite;   // system_slot

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _dimButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private Slider _masterSlider;
        [SerializeField] private Slider _bgmSlider;
        [SerializeField] private Slider _sfxSlider;
        [SerializeField] private Text _masterValueText;
        [SerializeField] private Text _bgmValueText;
        [SerializeField] private Text _sfxValueText;
        [SerializeField] private Toggle _muteToggle;
        [SerializeField] private Text _muteLabel;

        private Font _font;
        private bool _applying; // 슬라이더 초기화 중에는 콜백이 되먹임되지 않게 막는다

        private bool AlreadyBuilt => _bgmSlider != null;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!AlreadyBuilt)
            {
                Construct(); // 폴백(프리팹 미배선 시)
            }
            WireRuntime();
        }

        /// <summary>패널이 표시될 때마다 현재 볼륨 설정을 슬라이더에 반영한다.</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && AlreadyBuilt)
            {
                PullFromSoundManager();
            }
        }

        /// <summary>에디터 빌드 전용: 전체 정적 계층을 생성해 프리팹에 굽는다.</summary>
        public void EditorConstruct() => Construct();

        // ── 정적 계층 구성 ──

        /// <summary>캔버스·딤·패널·제목·볼륨 슬라이더 3개·음소거 토글·게임종료 버튼을 생성한다.
        /// 내용물은 모두 배경 프레임 테두리 안쪽 빈 칸(<see cref="PanelFrame"/>)에만 놓는다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            var content = PanelFrame.CreateContentArea(panel);
            BuildHeader(content);

            _masterSlider = BuildVolumeRow(content, "Master", "전체", FirstRowY, out _masterValueText);
            _bgmSlider = BuildVolumeRow(content, "Bgm", "배경음", FirstRowY + (RowHeight + RowGap), out _bgmValueText);
            _sfxSlider = BuildVolumeRow(content, "Sfx", "효과음", FirstRowY + (RowHeight + RowGap) * 2f, out _sfxValueText);

            BuildMuteToggle(content, MuteRowY);
            BuildQuitButton(content);
        }

        /// <summary>패널 전용 오버레이 캔버스(다른 패널과 동일 규격, sortingOrder 100).</summary>
        private void BuildCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Panel;
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

        /// <summary>패널 밖 클릭 시 닫히는 투명 차단막.</summary>
        private void BuildDim()
        {
            var img = NewImage("Dim", (RectTransform)transform, new Color(0f, 0f, 0f, 0f));
            Stretch(img.rectTransform);
            _dimButton = img.gameObject.AddComponent<Button>();
            _dimButton.transition = Selectable.Transition.None;
        }

        /// <summary>패널 본체(공용 프레임 ui_bg_2, 아트 없으면 단색).
        /// 9-slice 테두리가 없는 아트라 Simple로 늘려 그린다(가방·스킬·룬 창과 동일).</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            if (_panelSprite != null)
            {
                img.sprite = _panelSprite;
                img.type = Image.Type.Simple;
                img.color = Color.white;
            }
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>제목. 닫기(X) 버튼은 두지 않는다 — 다른 패널과 같이 <b>창 밖을 클릭</b>해 닫는다
        /// (미관상 X를 없앤 프로젝트 규칙: 스테이지·가방·뽑기와 동일).</summary>
        private void BuildHeader(RectTransform content)
        {
            var title = NewText("Title", content, "환경설정", 44, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f);
            TopLeft(title.rectTransform, 0f, TitleY, ContentWidth, TitleHeight);
        }

        /// <summary>볼륨 한 줄(라벨 + 슬라이더 + 퍼센트 값)을 만든다. y는 내용 영역 좌상단 기준(아래로 +).</summary>
        private Slider BuildVolumeRow(RectTransform content, string name, string label, float y, out Text valueText)
        {
            var row = NewChild(name + "Row", content);
            TopLeft(row, 0f, y, ContentWidth, RowHeight);

            var lbl = NewText("Label", row, label, 30, TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            lbl.color = new Color(1f, 0.94f, 0.80f);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(RowLabelWidth, 40f);

            valueText = NewText("Value", row, "100%", 26, TextAnchor.MiddleRight);
            valueText.color = new Color(0.92f, 0.86f, 0.70f);
            var vrt = valueText.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(1f, 0.5f);
            vrt.pivot = new Vector2(1f, 0.5f);
            vrt.anchoredPosition = Vector2.zero;
            vrt.sizeDelta = new Vector2(RowValueWidth, 40f);

            return BuildSlider(row);
        }

        /// <summary>슬라이더(트랙 + 채움 + 핸들)를 만든다. 라벨과 값 텍스트 사이에 가로로 채운다.</summary>
        private Slider BuildSlider(RectTransform row)
        {
            var trackImg = NewImage("Track", row, new Color(0.08f, 0.09f, 0.13f, 1f));
            if (_trackSprite != null)
            {
                trackImg.sprite = _trackSprite;
                trackImg.type = Image.Type.Sliced;
                trackImg.pixelsPerUnitMultiplier = TrackPixelsPerUnitMultiplier;
                trackImg.color = Color.white;
            }
            var trt = trackImg.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            // 라벨(왼쪽)과 퍼센트 값(오른쪽) 사이에 놓는다 — 두 폭 차이만큼 오른쪽으로 밀어 간격을 맞춘다.
            trt.anchoredPosition = new Vector2((RowLabelWidth - RowValueWidth) * 0.5f, 0f);
            trt.sizeDelta = new Vector2(SliderWidth, SliderHeight);

            // 채움 영역(좌 → 우). Slider가 fillRect의 anchorMax.x를 값에 맞춰 조정한다.
            var fillArea = NewChild("FillArea", trt);
            fillArea.anchorMin = new Vector2(0f, 0f);
            fillArea.anchorMax = new Vector2(1f, 1f);
            fillArea.offsetMin = new Vector2(6f, 6f);
            fillArea.offsetMax = new Vector2(-6f, -6f);

            var fill = NewImage("Fill", fillArea, new Color(1f, 0.82f, 0.35f, 1f));
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(1f, 1f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            // 핸들
            var handleArea = NewChild("HandleArea", trt);
            handleArea.anchorMin = new Vector2(0f, 0f);
            handleArea.anchorMax = new Vector2(1f, 1f);
            handleArea.offsetMin = new Vector2(12f, 0f);
            handleArea.offsetMax = new Vector2(-12f, 0f);

            var handle = NewImage("Handle", handleArea, new Color(1f, 0.95f, 0.80f, 1f));
            var hrt = handle.rectTransform;
            hrt.sizeDelta = new Vector2(26f, 38f);

            var slider = trackImg.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handle;
            return slider;
        }

        /// <summary>음소거 토글(간단한 체크 박스 + 라벨). y는 내용 영역 좌상단 기준(아래로 +).</summary>
        private void BuildMuteToggle(RectTransform content, float y)
        {
            var row = NewChild("MuteRow", content);
            TopLeft(row, 0f, y, ContentWidth, MuteRowHeight);

            var box = NewImage("Box", row, new Color(0.08f, 0.09f, 0.13f, 1f));
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(40f, 40f);

            var check = NewImage("Check", brt, new Color(1f, 0.82f, 0.35f, 1f));
            var chrt = check.rectTransform;
            chrt.anchorMin = new Vector2(0.18f, 0.18f);
            chrt.anchorMax = new Vector2(0.82f, 0.82f);
            chrt.offsetMin = Vector2.zero;
            chrt.offsetMax = Vector2.zero;

            _muteLabel = NewText("Label", row, "음소거", 28, TextAnchor.MiddleLeft);
            _muteLabel.color = new Color(1f, 0.94f, 0.80f);
            var lrt = _muteLabel.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(52f, 0f);
            lrt.sizeDelta = new Vector2(260f, 40f);

            _muteToggle = box.gameObject.AddComponent<Toggle>();
            _muteToggle.transition = Selectable.Transition.None;
            _muteToggle.targetGraphic = box;
            _muteToggle.graphic = check;
            _muteToggle.isOn = false;
        }

        /// <summary>하단 '게임종료' 버튼. ESC 메뉴의 같은 항목과 동작·외형(system_slot 9-slice)을 맞춘다.</summary>
        private void BuildQuitButton(RectTransform content)
        {
            var img = NewImage("QuitButton", content, new Color(0.35f, 0.20f, 0.22f, 0.98f)); // 아트 미배선 시 폴백
            if (_trackSprite != null)
            {
                img.sprite = _trackSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ButtonPixelsPerUnitMultiplier;
                img.color = Color.white;
            }
            TopLeft(img.rectTransform, (ContentWidth - QuitButtonWidth) * 0.5f, QuitButtonY,
                QuitButtonWidth, QuitButtonHeight);

            var label = NewText("Label", img.rectTransform, "게임종료", 34, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(1f, 0.94f, 0.80f);
            Stretch(label.rectTransform);

            _quitButton = img.gameObject.AddComponent<Button>();
        }

        private void WireRuntime()
        {
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
            if (_quitButton != null) _quitButton.onClick.AddListener(OnQuitGame);
            if (_masterSlider != null) _masterSlider.onValueChanged.AddListener(OnMasterChanged);
            if (_bgmSlider != null) _bgmSlider.onValueChanged.AddListener(OnBgmChanged);
            if (_sfxSlider != null) _sfxSlider.onValueChanged.AddListener(OnSfxChanged);
            if (_muteToggle != null) _muteToggle.onValueChanged.AddListener(OnMuteChanged);
        }

        // ── 값 반영 ──

        /// <summary>SoundManager의 현재 설정을 슬라이더·토글에 그대로 옮긴다(콜백 되먹임 차단).</summary>
        private void PullFromSoundManager()
        {
            var sm = SoundManager.Instance;
            if (sm == null)
            {
                return;
            }
            _applying = true;
            if (_masterSlider != null) _masterSlider.value = sm.MasterVolume;
            if (_bgmSlider != null) _bgmSlider.value = sm.BgmVolume;
            if (_sfxSlider != null) _sfxSlider.value = sm.SfxVolume;
            if (_muteToggle != null) _muteToggle.isOn = sm.Muted;
            _applying = false;
            RefreshValueTexts();
        }

        /// <summary>세 슬라이더의 퍼센트 표기를 현재 값으로 갱신한다.</summary>
        private void RefreshValueTexts()
        {
            if (_masterValueText != null && _masterSlider != null)
            {
                _masterValueText.text = Percent(_masterSlider.value);
            }
            if (_bgmValueText != null && _bgmSlider != null)
            {
                _bgmValueText.text = Percent(_bgmSlider.value);
            }
            if (_sfxValueText != null && _sfxSlider != null)
            {
                _sfxValueText.text = Percent(_sfxSlider.value);
            }
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

        /// <summary>전체 볼륨 변경 — BGM·효과음 모두에 곱해진다.</summary>
        private void OnMasterChanged(float value)
        {
            if (_applying) return;
            SoundManager.Instance?.SetMasterVolume(value);
            RefreshValueTexts();
        }

        /// <summary>배경음 볼륨 변경 — 재생 중인 BGM에 즉시 반영된다.</summary>
        private void OnBgmChanged(float value)
        {
            if (_applying) return;
            SoundManager.Instance?.SetBgmVolume(value);
            RefreshValueTexts();
        }

        /// <summary>효과음 볼륨 변경 — 바뀐 크기를 바로 들려주려 짧은 확인음을 한 번 재생한다.</summary>
        private void OnSfxChanged(float value)
        {
            if (_applying) return;
            SoundManager.Instance?.SetSfxVolume(value);
            RefreshValueTexts();
            SoundManager.Sfx(SoundId.UiSlotSelect);
        }

        /// <summary>음소거 토글 — 볼륨 값은 보존한 채 출력만 끈다.</summary>
        private void OnMuteChanged(bool muted)
        {
            if (_applying) return;
            SoundManager.Instance?.SetMuted(muted);
        }

        /// <summary>게임을 종료한다(에디터에서는 플레이 정지). ESC 메뉴의 '게임종료'와 같은 동작이다.</summary>
        private static void OnQuitGame()
        {
            Debug.Log("[Settings] 게임종료");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>패널을 닫는다(UIManager 우선).</summary>
        public void Close()
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.Hide(UIManager.PanelType.Settings);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── UI 헬퍼 ──

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private Text NewText(string name, Transform parent, string content, int size, TextAnchor anchor)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>부모(내용 영역)의 좌상단 기준으로 배치한다(y는 아래로 +).</summary>
        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
