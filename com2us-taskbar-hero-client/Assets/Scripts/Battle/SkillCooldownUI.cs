using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
            _slots.Clear();
            _hpBars.Clear();
            if (controller != null && controller.Party != null && controller.Party.Count > 0)
                BuildUI();
        }

        private void BuildUI()
        {
            if (slotTemplate != null) slotTemplate.SetActive(false);
            if (portraitTemplate != null) portraitTemplate.gameObject.SetActive(false);
            if (portraitFrameTemplate != null) portraitFrameTemplate.gameObject.SetActive(false);

            var party = controller.Party;
            for (int i = 0; i < party.Count; i++)
            {
                var member = party[i];
                if (member == null) continue;
                float baseY = rowStartY + i * rowStepY;

                // 초상화 프레임 + RawImage
                if (portraitFrameTemplate != null)
                {
                    var frame = Instantiate(portraitFrameTemplate, transform);
                    frame.gameObject.SetActive(true);
                    frame.anchoredPosition = new Vector2(portraitX + frameOffset.x, baseY + frameOffset.y);
                    _spawned.Add(frame.gameObject);
                }
                if (portraitTemplate != null)
                {
                    var por = Instantiate(portraitTemplate, transform);
                    por.gameObject.SetActive(true);
                    por.anchoredPosition = new Vector2(portraitX, baseY);
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
                    var slotGo = Instantiate(slotTemplate, transform);
                    slotGo.SetActive(true);
                    var rt = slotGo.GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition = new Vector2(slotStartX + j * slotStepX, baseY);
                    _spawned.Add(slotGo);

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
        }

        /// <summary>초상화 옆에 세로 체력바(배경+아래→위 채움)를 만들어 멤버에 연결한다. pos는 바 <b>하단 중앙</b> 위치(초상화 하단에 정렬).</summary>
        private void CreateHpBar(PlayerCombatant member, int i, Vector2 pos)
        {
            // 배경(우상단 앵커 기준, 피벗을 하단 중앙으로 두어 pos가 바닥 시작점이 되게)
            var bg = new GameObject("HpBarBg_" + i, typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(transform, false);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(1f, 1f);
            bgRt.pivot = new Vector2(0.5f, 0f); // 하단 중앙 피벗 → anchoredPosition.y = 바 바닥
            bgRt.sizeDelta = new Vector2(hpBarWidth, hpBarHeight);
            bgRt.anchoredPosition = pos;
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
