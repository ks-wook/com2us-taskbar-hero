using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// 화면 우측 상단의 '적용 중인 버프' 아이콘. 활성 버프가 하나라도 있을 때만 노출되고,
    /// 아이콘에 커서를 올리면 적용 중인 버프 종류·증가율·남은 시간·만료 시각을 툴팁으로 보여준다.
    /// 계층은 <see cref="GameSceneHudController"/>가 HUD 캔버스에 코드로 만들며(전용 프리팹 없음),
    /// 상태는 <see cref="BuffManager"/> 캐시를 구독해 갱신한다.
    ///
    /// 남은 시간은 서버 시각 기준으로 1초마다 다시 계산하고, 카운트다운이 끝난 버프는 목록에서 지운 뒤
    /// 서버에 한 번 재동기화해 확인한다(만료 확정은 서버 몫 — 소모품/버프 기획서 5.2).
    /// </summary>
    public class BuffIndicator : MonoBehaviour
    {
        private const float TickInterval = 1f;      // 남은 시간 텍스트 갱신 주기(초)
        private const float TooltipWidth = 420f;
        private const float TooltipLineHeight = 34f;
        private const float TooltipPadding = 18f;

        private Image _icon;
        private GameObject _tooltipRoot;
        private RectTransform _tooltipRect;
        private Text _tooltipText;
        private float _nextTick;
        private bool _hovering;

        /// <summary>아이콘·툴팁 계층을 구성한다(HUD 빌드 시 1회 호출). 아이콘 스프라이트가 없으면 색 사각형으로 폴백한다.</summary>
        public void Build(Font font, Sprite iconSprite, Sprite tooltipBackground, Vector2 anchoredPos, float size)
        {
            var iconGo = new GameObject("BuffIcon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(transform, false);
            _icon = iconGo.GetComponent<Image>();
            _icon.sprite = iconSprite;
            _icon.preserveAspect = true;
            _icon.color = iconSprite != null ? Color.white : new Color(0.95f, 0.78f, 0.25f, 1f);
            _icon.raycastTarget = true; // hover 판정 대상
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(1f, 1f); // 우측 상단
            irt.pivot = new Vector2(1f, 1f);
            irt.anchoredPosition = anchoredPos;
            irt.sizeDelta = new Vector2(size, size);

            iconGo.AddComponent<PointerHoverRelay>().Bind(_ => ShowTooltip(), HideTooltip);

            BuildTooltip(font, tooltipBackground, irt, size);
            SetVisible(false); // 초기에는 숨김(활성 버프가 확인되면 켜진다)
        }

        /// <summary>아이콘 아래에 붙는 상세 툴팁(배경 + 텍스트)을 만든다. 처음엔 숨겨 둔다.</summary>
        private void BuildTooltip(Font font, Sprite background, RectTransform iconRect, float iconSize)
        {
            var go = new GameObject("BuffTooltip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.11f, 0.96f); // 아트 미배선 시 단색 폴백
            if (background != null)
            {
                bg.sprite = background;
                bg.type = Image.Type.Simple;
                bg.color = Color.white;
            }
            bg.raycastTarget = false; // 툴팁이 커서를 가로채면 아이콘의 hover가 끊긴다

            _tooltipRect = (RectTransform)go.transform;
            _tooltipRect.anchorMin = _tooltipRect.anchorMax = new Vector2(1f, 1f);
            _tooltipRect.pivot = new Vector2(1f, 1f);
            _tooltipRect.anchoredPosition = new Vector2(iconRect.anchoredPosition.x,
                                                        iconRect.anchoredPosition.y - iconSize - 8f);
            _tooltipRect.sizeDelta = new Vector2(TooltipWidth, TooltipLineHeight * 2f + TooltipPadding * 2f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            _tooltipText = textGo.GetComponent<Text>();
            _tooltipText.font = font;
            _tooltipText.fontSize = 24;
            _tooltipText.alignment = TextAnchor.UpperLeft;
            _tooltipText.color = Color.white;
            _tooltipText.raycastTarget = false;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(TooltipPadding, TooltipPadding);
            trt.offsetMax = new Vector2(-TooltipPadding, -TooltipPadding);

            _tooltipRoot = go;
            _tooltipRoot.SetActive(false);
        }

        private void OnEnable()
        {
            BuffManager.Changed += OnBuffsChanged;
            OnBuffsChanged();
        }

        private void OnDisable()
        {
            BuffManager.Changed -= OnBuffsChanged;
        }

        /// <summary>버프 캐시가 바뀌면 아이콘 노출 여부와 툴팁 내용을 즉시 맞춘다.</summary>
        private void OnBuffsChanged()
        {
            SetVisible(BuffManager.HasActiveBuff);
            RefreshTooltipText();
        }

        /// <summary>1초마다 남은 시간을 다시 계산한다. 카운트다운이 끝난 버프가 있으면 목록에서 지우고
        /// 서버에 재동기화를 요청한다(연장·추가 사용으로 서버 상태가 다를 수 있으므로 확인이 필요하다).</summary>
        private void Update()
        {
            if (Time.unscaledTime < _nextTick)
            {
                return;
            }
            _nextTick = Time.unscaledTime + TickInterval;

            if (BuffManager.PruneExpired())
            {
                BuffManager.Refresh(); // 만료 확정은 서버 응답으로 확인
                return;                // PruneExpired가 Changed를 발생시켜 표시는 이미 갱신됐다
            }
            if (_hovering)
            {
                RefreshTooltipText(); // 툴팁을 보고 있을 때만 초 단위로 다시 그린다
            }
        }

        /// <summary>아이콘(과 열려 있던 툴팁)의 표시 상태를 바꾼다. 적용 중인 버프가 없으면 아이콘을 노출하지 않는다.</summary>
        private void SetVisible(bool visible)
        {
            if (_icon != null && _icon.gameObject.activeSelf != visible)
            {
                _icon.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                HideTooltip();
            }
        }

        /// <summary>hover 진입: 최신 내용으로 툴팁을 채워 표시한다.</summary>
        private void ShowTooltip()
        {
            _hovering = true;
            if (!BuffManager.HasActiveBuff)
            {
                return;
            }
            RefreshTooltipText();
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(true);
                _tooltipRoot.transform.SetAsLastSibling();
            }
        }

        /// <summary>hover 이탈: 툴팁을 닫는다.</summary>
        private void HideTooltip()
        {
            _hovering = false;
            if (_tooltipRoot != null)
            {
                _tooltipRoot.SetActive(false);
            }
        }

        /// <summary>적용 중인 버프 목록(종류 · 증가율 · 남은 시간 · 만료 시각)으로 툴팁 텍스트와 높이를 갱신한다.</summary>
        private void RefreshTooltipText()
        {
            if (_tooltipText == null)
            {
                return;
            }

            var buffs = BuffManager.ActiveBuffs;
            var sb = new StringBuilder();
            sb.Append("<b>적용 중인 버프</b>");
            int lines = 1;
            foreach (var b in buffs)
            {
                if (b == null)
                {
                    continue;
                }
                sb.AppendLine();
                sb.Append($"{BuffManager.DisplayName(b.buffType)} <color=#FFD34D>{BuffManager.BonusText(b.buffValue)}</color>");
                sb.AppendLine();
                sb.Append($"  남은 시간 {BuffManager.RemainText(BuffManager.RemainingSeconds(b))} (만료 {BuffManager.ExpireTimeText(b.expiresAt)})");
                lines += 2;
            }
            _tooltipText.text = sb.ToString();

            if (_tooltipRect != null)
            {
                _tooltipRect.sizeDelta = new Vector2(TooltipWidth, lines * TooltipLineHeight + TooltipPadding * 2f);
            }
        }
    }
}
