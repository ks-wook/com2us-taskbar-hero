using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 환경설정 패널. <see cref="SoundManager"/>의 볼륨 3채널(전체·BGM·효과음)과 음소거를 슬라이더·토글로 조절한다.
    /// 값은 SoundManager가 PlayerPrefs에 저장하므로 다음 실행에도 유지되고, 조절 즉시 재생 중인 BGM에 반영된다.
    /// 효과음 슬라이더를 놓을 때는 <c>sfx_ui_slot_select</c>를 한 번 울려 바뀐 크기를 귀로 확인할 수 있게 한다.
    /// 외형은 ESC 메뉴와 같은 <c>Assets/Art/UI/System</c> 아트를 쓴다(패널 system_bg · 슬라이더 트랙 system_slot).
    /// 정적 계층은 에디터 빌더(SettingsUiBuilder)가 프리팹에 굽는다.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;
        private const float PanelWidth = 720f;
        private const float PanelHeight = 620f;
        private const float RowHeight = 96f;
        private const float SliderWidth = 420f;
        private const float SliderHeight = 28f;
        // system_slot(2048×731) 테두리 상하 128px → 트랙 높이(28)에 맞추려면 크게 줄여야 한다.
        private const float TrackPixelsPerUnitMultiplier = 14f;

        [Header("UI 리소스 (Assets/Art/UI/System — 에디터 빌더가 배선)")]
        [SerializeField] private Sprite _panelSprite;   // system_bg
        [SerializeField] private Sprite _trackSprite;   // system_slot(슬라이더 트랙)

        [Header("구성 참조 (에디터 빌더가 배선)")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _dimButton;
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

        /// <summary>캔버스·딤·패널·제목·볼륨 슬라이더 3개·음소거 토글·닫기 버튼을 생성한다.</summary>
        private void Construct()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildDim();
            var panel = BuildPanel();
            BuildHeader(panel);

            _masterSlider = BuildVolumeRow(panel, "Master", "전체", -40f, out _masterValueText);
            _bgmSlider = BuildVolumeRow(panel, "Bgm", "배경음", -40f - RowHeight, out _bgmValueText);
            _sfxSlider = BuildVolumeRow(panel, "Sfx", "효과음", -40f - RowHeight * 2f, out _sfxValueText);

            BuildMuteToggle(panel, -40f - RowHeight * 3f - 12f);
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

        /// <summary>패널 본체(system_bg 9-slice, 아트 없으면 단색).</summary>
        private RectTransform BuildPanel()
        {
            var img = NewImage("PanelRoot", (RectTransform)transform, new Color(0.10f, 0.12f, 0.18f, 0.98f));
            if (_panelSprite != null)
            {
                img.sprite = _panelSprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>제목과 닫기 버튼.</summary>
        private void BuildHeader(RectTransform panel)
        {
            var title = NewText("Title", panel, "환경설정", 44, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.92f, 0.72f);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -28f);
            trt.sizeDelta = new Vector2(400f, 60f);

            var close = NewImage("CloseButton", panel, new Color(0.32f, 0.20f, 0.13f, 0.95f));
            var crt = close.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-30f, -30f);
            crt.sizeDelta = new Vector2(52f, 52f);
            var xt = NewText("X", crt, "X", 30, TextAnchor.MiddleCenter);
            xt.color = new Color(1f, 0.92f, 0.72f);
            Stretch(xt.rectTransform);
            _closeButton = close.gameObject.AddComponent<Button>();
        }

        /// <summary>볼륨 한 줄(라벨 + 슬라이더 + 퍼센트 값)을 만든다. y는 패널 상단 기준 오프셋(음수).</summary>
        private Slider BuildVolumeRow(RectTransform panel, string name, string label, float y, out Text valueText)
        {
            var row = NewChild(name + "Row", panel);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, y - 76f); // 제목 아래부터 시작
            row.sizeDelta = new Vector2(PanelWidth - 80f, RowHeight);

            var lbl = NewText("Label", row, label, 30, TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            lbl.color = new Color(1f, 0.94f, 0.80f);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(10f, 0f);
            lrt.sizeDelta = new Vector2(140f, 40f);

            valueText = NewText("Value", row, "100%", 26, TextAnchor.MiddleRight);
            valueText.color = new Color(0.92f, 0.86f, 0.70f);
            var vrt = valueText.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(1f, 0.5f);
            vrt.pivot = new Vector2(1f, 0.5f);
            vrt.anchoredPosition = new Vector2(-10f, 0f);
            vrt.sizeDelta = new Vector2(90f, 40f);

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
            trt.anchoredPosition = new Vector2(10f, 0f);
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

        /// <summary>음소거 토글(간단한 체크 박스 + 라벨).</summary>
        private void BuildMuteToggle(RectTransform panel, float y)
        {
            var row = NewChild("MuteRow", panel);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, y - 76f);
            row.sizeDelta = new Vector2(PanelWidth - 80f, RowHeight);

            var box = NewImage("Box", row, new Color(0.08f, 0.09f, 0.13f, 1f));
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(10f, 0f);
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
            lrt.anchoredPosition = new Vector2(62f, 0f);
            lrt.sizeDelta = new Vector2(260f, 40f);

            _muteToggle = box.gameObject.AddComponent<Toggle>();
            _muteToggle.transition = Selectable.Transition.None;
            _muteToggle.targetGraphic = box;
            _muteToggle.graphic = check;
            _muteToggle.isOn = false;
        }

        private void WireRuntime()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
            if (_dimButton != null) _dimButton.onClick.AddListener(Close);
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
