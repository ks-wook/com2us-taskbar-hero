using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 아이템 상세 정보 공용 팝업. 아이템 슬롯(<see cref="ItemSlotView"/>) hover 시
    /// item_detail_bg 배경 위에 이름(등급 색)·등급/종류·요구조건·설명을 표시한다.
    /// 최초 표시 시 전용 오버레이 캔버스를 코드로 구성하는 온디맨드 싱글턴이며,
    /// 표시 전용이라 입력을 가로채지 않는다(GraphicRaycaster 없음, 전 위젯 raycast off).
    /// 인벤토리·스테이지 클리어·우편함·(추후) 거래소가 동일 팝업을 사용한다.
    /// </summary>
    public class ItemDetailPopup : MonoBehaviour
    {
        private const float Width = 480f;
        private const float Height = 320f;
        private const float CanvasRefWidth = 1080f;
        private const float CanvasRefHeight = 1920f;

        private static ItemDetailPopup _instance;

        private RectTransform _canvasRect;
        private RectTransform _root;
        private Image _background;
        private Text _nameText;
        private Text _subText;
        private Text _reqText;
        private Text _descText;

        /// <summary>아이템 코드·수량의 상세 문구를 채워 커서 근처에 팝업을 표시한다(강화 단계 없음 = 미강화).
        /// background가 있으면 배경 이미지(item_detail_bg)로, 없으면 단색으로 폴백한다.</summary>
        public static void Show(Sprite background, int itemCode, long quantity, Vector2 screenPos)
        {
            Show(background, itemCode, quantity, 0, screenPos);
        }

        /// <summary>강화 단계를 포함해 상세 문구를 표시한다(이름에 "+N", 옵션 스탯에 강화 배율 반영).</summary>
        public static void Show(Sprite background, int itemCode, long quantity, int enhanceLevel, Vector2 screenPos)
        {
            Ensure().ShowInternal(background, itemCode, quantity, enhanceLevel, screenPos);
        }

        /// <summary>팝업을 숨긴다(hover 이탈 시).</summary>
        public static void Hide()
        {
            if (_instance != null && _instance._root != null)
            {
                _instance._root.gameObject.SetActive(false);
            }
        }

        /// <summary>싱글턴 인스턴스를 반환한다(없으면 오버레이 캔버스와 함께 생성).</summary>
        private static ItemDetailPopup Ensure()
        {
            if (_instance == null)
            {
                var go = new GameObject("ItemDetailPopup");
                _instance = go.AddComponent<ItemDetailPopup>();
                _instance.Build();
            }
            return _instance;
        }

        /// <summary>전용 오버레이 캔버스(최상단)와 팝업 위젯(배경·텍스트 4종)을 구성한다.</summary>
        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 패널(100)·클리어 연출(300)보다 위
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(CanvasRefWidth, CanvasRefHeight);
            scaler.matchWidthOrHeight = 0.5f;
            // 현재 씬 규격(GameScene은 높이 1440 기준)으로 즉시 맞춘다 — 주기 스윕을 기다리면 첫 표시 때 잠깐 크게 그려진다.
            GameViewLayout.ApplyCurrentScaler(scaler);
            _canvasRect = (RectTransform)transform;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rootGo = new GameObject("Popup", typeof(RectTransform), typeof(Image));
            rootGo.transform.SetParent(transform, false);
            _root = (RectTransform)rootGo.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0f, 1f); // 좌상단 기준(커서 우하단에 표시)
            _root.sizeDelta = new Vector2(Width, Height);
            _background = rootGo.GetComponent<Image>();
            _background.raycastTarget = false;

            _nameText = MakeText(font, "Name", 30, 26f, 40f);
            _nameText.fontStyle = FontStyle.Bold;
            _subText = MakeText(font, "Sub", 22, 74f, 30f);
            _subText.color = new Color(1f, 1f, 1f, 0.8f);
            _reqText = MakeText(font, "Req", 20, 108f, 28f);
            _reqText.color = new Color(1f, 1f, 1f, 0.7f);
            _descText = MakeText(font, "Desc", 20, 142f, Height - 142f - 24f);
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _root.gameObject.SetActive(false);
        }

        /// <summary>상세 문구를 채우고 배경을 적용해 커서 근처에 표시한다.
        /// <b>표시음은 재생하지 않는다</b> — hover만으로 뜨는 팝업이라 아이템 칸 위를 지나갈 때마다 울려 거슬린다.</summary>
        private void ShowInternal(Sprite background, int itemCode, long quantity, int enhanceLevel, Vector2 screenPos)
        {
            if (background != null)
            {
                _background.sprite = background;
                _background.type = Image.Type.Simple;
                _background.color = Color.white;
            }
            else
            {
                _background.sprite = null;
                _background.color = new Color(0.08f, 0.09f, 0.14f, 0.98f); // 배경 이미지 미배선 폴백
            }

            var info = ItemInfoText.Build(itemCode, quantity, enhanceLevel);
            _nameText.text = info.name;
            _nameText.color = GradeColors.Name(info.gradeValue); // 이름을 등급 색으로
            _subText.text = string.IsNullOrEmpty(info.category) ? info.grade : $"{info.grade} · {info.category}";
            _reqText.text = info.requirement;
            _descText.text = info.description;

            _root.gameObject.SetActive(true);
            Reposition(screenPos);
        }

        /// <summary>커서 스크린 좌표를 캔버스 로컬 좌표로 변환해 배치하고 화면 안으로 클램프한다.</summary>
        private void Reposition(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out var local);
            local += new Vector2(18f, -18f);

            var size = _root.sizeDelta;
            float halfW = _canvasRect.rect.width * 0.5f;
            float halfH = _canvasRect.rect.height * 0.5f;
            local.x = Mathf.Clamp(local.x, -halfW, halfW - size.x);
            local.y = Mathf.Clamp(local.y, -halfH + size.y, halfH);
            _root.anchoredPosition = local;
        }

        /// <summary>팝업 내부 텍스트 한 줄(좌상단 기준, y는 위에서 아래로, 좌우 여백 32).</summary>
        private Text MakeText(Font font, string name, int size, float y, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_root, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = TextAnchor.UpperLeft;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(32f, -y);
            rt.sizeDelta = new Vector2(Width - 64f, h);
            return t;
        }
    }
}
