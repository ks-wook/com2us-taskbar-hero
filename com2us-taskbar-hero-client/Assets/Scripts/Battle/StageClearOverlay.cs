using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;
using TaskbarHero.Common.MasterData;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 스테이지 클리어 연출 오버레이. 서버 클리어 응답을 받으면 화면 전체에 표시된다.
    /// - 클리어 팡파레 이펙트(프레임 시퀀스, 슬로우모션과 무관하게 unscaled 시간으로 재생)
    /// - 보상(골드·경험치·전리품 아이템 아이콘+수량) 노출
    /// - 화면 클릭 또는 5초 경과 시 자동으로 닫히며, 닫힐 때 게임 속도를 정상으로 복원한다.
    /// 런타임에 자체 Canvas를 코드로 구성한다(HUD 방식과 동일).
    /// </summary>
    public class StageClearOverlay : MonoBehaviour
    {
        private const float AutoCloseSeconds = 5f;
        private const float FanfareFps = 24f;

        private StageClearAssets _assets;
        private Image _fanfareImage;
        private Sprite[] _frames;
        private int _frameIndex;
        private float _frameTimer;
        private bool _dismissed;
        private Action _onClosed;

        // 아이템 hover 상세 툴팁(인벤토리와 동일한 정보 표시). 코드로 구성한다.
        private RectTransform _tooltipRoot;
        private Text _tipName;
        private Text _tipSub;
        private Text _tipReq;
        private Text _tipDesc;

        /// <summary>클리어 응답 데이터로 오버레이를 생성·표시한다. onClosed는 닫힐 때(클릭/자동) 1회 호출된다.</summary>
        public static void Show(StageClearData data, Action onClosed = null)
        {
            var go = new GameObject("StageClearOverlay");
            var overlay = go.AddComponent<StageClearOverlay>();
            overlay._onClosed = onClosed;
            overlay.Build(data);
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

        /// <summary>전체 화면 캔버스와 팡파레·보상 UI를 구성한다.</summary>
        private void Build(StageClearData data)
        {
            _assets = StageClearAssets.Load();
            if (_assets == null)
            {
                Debug.LogWarning("[StageClear] StageClearAssets(Resources)가 없어 아이콘/팡파레가 비어 있습니다. 에디터에서 'TaskbarHero/UI/클리어 연출 에셋 빌드'를 실행하세요.");
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 최상단 캔버스(패널 100·HUD 10보다 위).
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // 어두운 배경(클릭 시 닫힘).
            var dim = CreateChild("Dim", transform, Vector2.zero, Vector2.one);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0f); // 배경을 어둡게 하지 않는다 — 클릭 닫기용 투명 차단막
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Dismiss);

            // 팡파레 이펙트(화면 중앙, 보상 아이템 뒤). Dim 다음·보상 앞에 생성되어 아이템보다 뒤에 그려진다.
            var fanfare = CreateChild("Fanfare", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            fanfare.sizeDelta = new Vector2(900f, 900f);
            fanfare.anchoredPosition = Vector2.zero;
            _fanfareImage = fanfare.gameObject.AddComponent<Image>();
            _fanfareImage.raycastTarget = false;
            _fanfareImage.preserveAspect = true; // 전체 프레임을 잘림 없이 표시
            _frames = _assets != null ? _assets.fanfareFrames : null;
            if (_frames != null && _frames.Length > 0)
            {
                _fanfareImage.sprite = _frames[0];
            }
            else
            {
                _fanfareImage.enabled = false;
            }

            // "STAGE CLEAR" 타이틀(상단).
            var title = CreateText("Title", transform, font, "STAGE CLEAR!", 96, TextAnchor.MiddleCenter);
            title.color = new Color(1f, 0.92f, 0.4f);
            title.fontStyle = FontStyle.Bold;
            var trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900f, 160f);
            trt.anchoredPosition = new Vector2(0f, 440f);

            // 보상 항목 구성(화면 중앙, 팡파레 위에 그려짐).
            BuildRewards(data, font);

            // 안내 문구.
            var hint = CreateText("Hint", transform, font, "클릭하거나 잠시 기다리면 닫힙니다", 34, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.7f);
            var hrt = (RectTransform)hint.transform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(900f, 60f);
            hrt.anchoredPosition = new Vector2(0f, 70f);

            BuildTooltip(font); // 아이템 hover 상세 툴팁(최상단, 처음엔 숨김)

            StartCoroutine(AutoCloseAfter(AutoCloseSeconds));
        }

        /// <summary>골드·경험치·전리품 아이템을 가로로 배치한다(아이콘+수량).</summary>
        private void BuildRewards(StageClearData data, Font font)
        {
            var rewards = data != null ? data.rewards : null;

            // 보상 행 컨테이너(화면 중앙). 팡파레보다 뒤 순번(자식 인덱스 상 뒤)이라 이펙트 위에 그려진다.
            var row = CreateChild("Rewards", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            row.sizeDelta = new Vector2(960f, 220f);
            row.anchoredPosition = Vector2.zero;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // 경험치(아이콘 없이 텍스트 배지).
            if (rewards != null && rewards.exp > 0)
            {
                CreateTextBadge(row, font, "EXP", $"+{rewards.exp}", new Color(0.4f, 0.8f, 1f));
            }
            // 골드(item_1 아이콘 재사용). 재화이므로 등급 색이 아닌 기본(노말) 슬롯 배경.
            if (rewards != null && rewards.gold > 0)
            {
                CreateRewardEntry(row, font, GetIcon(1), $"+{rewards.gold}", new Color(1f, 0.85f, 0.3f),
                    GradeColors.RewardSlotBackground(1));
            }
            // 전리품 아이템 — 등급별로 슬롯 배경색을 달리한다(마스터 데이터의 item_master.grade 기준).
            if (rewards != null && rewards.items != null)
            {
                foreach (var item in rewards.items)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    string qty = item.quantity > 1 ? $"x{item.quantity}" : string.Empty;
                    CreateRewardEntry(row, font, GetIcon(item.itemCode), qty, Color.white,
                        GradeColors.RewardSlotBackground(GradeOf(item.itemCode)),
                        item.itemCode, item.quantity); // 아이템 슬롯 hover 시 상세 툴팁
                }
            }
        }

        /// <summary>아이템 코드의 등급(1~5)을 마스터 데이터에서 조회한다. 없으면 노말(1).</summary>
        private static int GradeOf(int itemCode)
        {
            var db = MasterDataManager.Db;
            return db != null && db.Items.TryGetValue(itemCode, out var im) ? im.grade : 1;
        }

        /// <summary>아이콘+수량 보상 항목 한 칸을 만든다(아이콘 없으면 색 사각형 폴백). slotColor는 등급별 슬롯 배경.
        /// itemCode > 0이면 슬롯 hover 시 인벤토리처럼 아이템 상세 툴팁을 띄운다.</summary>
        private void CreateRewardEntry(RectTransform parent, Font font, Sprite icon, string qtyText, Color tint, Color slotColor,
            int itemCode = 0, long quantity = 0)
        {
            var entry = new GameObject("Reward", typeof(RectTransform));
            entry.transform.SetParent(parent, false);
            var ert = (RectTransform)entry.transform;
            ert.sizeDelta = new Vector2(150f, 190f);

            // 아이콘 배경(슬롯).
            var slot = CreateChild("Slot", entry.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            slot.sizeDelta = new Vector2(140f, 140f);
            slot.anchoredPosition = Vector2.zero;
            var slotImg = slot.gameObject.AddComponent<Image>();
            slotImg.color = slotColor; // 등급별 배경색
            // 아이템 칸은 hover 감지를 위해 레이캐스트 대상으로 두고 hover 핸들러를 붙인다(재화/경험치는 미부착).
            bool hoverable = itemCode > 0;
            slotImg.raycastTarget = hoverable;
            if (hoverable)
            {
                var hover = slot.gameObject.AddComponent<RewardItemHover>();
                hover.Init(this, itemCode, quantity);
            }

            var iconRt = CreateChild("Icon", slot, Vector2.zero, Vector2.one);
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

            if (!string.IsNullOrEmpty(qtyText))
            {
                var qty = CreateText("Qty", entry.transform, font, qtyText, 38, TextAnchor.MiddleCenter);
                qty.color = Color.white;
                qty.fontStyle = FontStyle.Bold;
                var qrt = (RectTransform)qty.transform;
                qrt.anchorMin = new Vector2(0.5f, 0f);
                qrt.anchorMax = new Vector2(0.5f, 0f);
                qrt.pivot = new Vector2(0.5f, 0f);
                qrt.sizeDelta = new Vector2(150f, 46f);
                qrt.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>라벨+값 텍스트 배지(경험치 등 아이콘 없는 보상).</summary>
        private void CreateTextBadge(RectTransform parent, Font font, string label, string value, Color color)
        {
            var entry = new GameObject("Badge", typeof(RectTransform));
            entry.transform.SetParent(parent, false);
            var ert = (RectTransform)entry.transform;
            ert.sizeDelta = new Vector2(150f, 190f);

            var slot = CreateChild("Slot", entry.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            slot.sizeDelta = new Vector2(140f, 140f);
            var slotImg = slot.gameObject.AddComponent<Image>();
            slotImg.color = new Color(0.12f, 0.14f, 0.22f, 0.95f);
            slotImg.raycastTarget = false;

            var lbl = CreateText("Label", slot, font, label, 40, TextAnchor.MiddleCenter);
            lbl.color = color;
            lbl.fontStyle = FontStyle.Bold;
            var lrt = (RectTransform)lbl.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            var val = CreateText("Value", entry.transform, font, value, 38, TextAnchor.MiddleCenter);
            val.color = Color.white;
            val.fontStyle = FontStyle.Bold;
            var vrt = (RectTransform)val.transform;
            vrt.anchorMin = new Vector2(0.5f, 0f);
            vrt.anchorMax = new Vector2(0.5f, 0f);
            vrt.pivot = new Vector2(0.5f, 0f);
            vrt.sizeDelta = new Vector2(150f, 46f);
            vrt.anchoredPosition = Vector2.zero;
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
            Time.timeScale = 1f;
            var cb = _onClosed;
            _onClosed = null;
            Destroy(gameObject);
            cb?.Invoke();
        }

        // ── 아이템 상세 툴팁(인벤토리와 동일한 정보 표시) ──

        /// <summary>hover 상세 툴팁(배경 + 이름/등급·종류/요구/설명)을 최상단에 구성한다(처음엔 숨김).</summary>
        private void BuildTooltip(Font font)
        {
            var go = new GameObject("ItemTooltip", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.08f, 0.09f, 0.14f, 0.98f);
            img.raycastTarget = false; // 툴팁은 입력 통과(닫기/hover 방해 안 함)
            _tooltipRoot = (RectTransform)go.transform;
            _tooltipRoot.anchorMin = _tooltipRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltipRoot.pivot = new Vector2(0f, 1f); // 좌상단 기준(커서 우하단에 표시)
            _tooltipRoot.sizeDelta = new Vector2(360f, 220f);

            _tipName = CreateTipText(font, "Name", 28, 14f, 328f, 36f);
            _tipSub = CreateTipText(font, "Sub", 20, 54f, 328f, 28f);
            _tipReq = CreateTipText(font, "Req", 18, 86f, 328f, 28f);
            _tipDesc = CreateTipText(font, "Desc", 18, 118f, 328f, 92f);
            _tipDesc.horizontalOverflow = HorizontalWrapMode.Wrap;

            go.SetActive(false);
        }

        /// <summary>툴팁 내부 텍스트 한 줄(좌상단 기준, y는 위에서 아래로).</summary>
        private Text CreateTipText(Font font, string name, int size, float y, float w, float h)
        {
            var t = CreateText(name, _tooltipRoot, font, string.Empty, size, TextAnchor.UpperLeft);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -y);
            rt.sizeDelta = new Vector2(w, h);
            return t;
        }

        /// <summary>아이템 코드의 상세 정보를 채워 커서 근처에 툴팁을 표시한다(hover 진입 시).</summary>
        public void ShowItemTooltip(int itemCode, long quantity, Vector2 screenPos)
        {
            if (_tooltipRoot == null)
            {
                return;
            }
            var info = BuildItemInfo(itemCode, quantity);
            _tipName.text = info.name;
            _tipName.color = GradeColors.Name(info.gradeValue);        // 이름을 등급 색으로
            _tipSub.text = string.IsNullOrEmpty(info.category) ? info.grade : $"{info.grade} · {info.category}";
            _tipReq.text = info.requirement;
            _tipDesc.text = info.description;

            _tooltipRoot.gameObject.SetActive(true);
            _tooltipRoot.SetAsLastSibling();
            Reposition(screenPos);
        }

        /// <summary>툴팁을 숨긴다(hover 이탈 시).</summary>
        public void HideItemTooltip()
        {
            if (_tooltipRoot != null)
            {
                _tooltipRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>커서 스크린 좌표를 캔버스 로컬 좌표로 변환해 배치하고 화면 안으로 클램프.</summary>
        private void Reposition(Vector2 screenPos)
        {
            var canvasRect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out var local);
            local += new Vector2(18f, -18f);

            var size = _tooltipRoot.sizeDelta;
            float halfW = canvasRect.rect.width * 0.5f;
            float halfH = canvasRect.rect.height * 0.5f;
            local.x = Mathf.Clamp(local.x, -halfW, halfW - size.x);
            local.y = Mathf.Clamp(local.y, -halfH + size.y, halfH);
            _tooltipRoot.anchoredPosition = local;
        }

        // ── 아이템 정보 구성(마스터 데이터 — 인벤토리 BuildDisplay와 동일 규칙) ──

        private struct ItemInfo
        {
            public string name;
            public int gradeValue;
            public string grade;
            public string category;
            public string requirement;
            public string description;
        }

        private static ItemInfo BuildItemInfo(int itemCode, long quantity)
        {
            var db = MasterDataManager.Db;
            ItemMaster im = null;
            if (db != null)
            {
                db.Items.TryGetValue(itemCode, out im);
            }

            var info = new ItemInfo
            {
                name = im != null ? im.name : $"아이템 {itemCode}",
                gradeValue = im != null ? im.grade : 1,
                grade = im != null && db.Grades.TryGetValue(im.grade, out var g) ? g.name : "노말",
                category = Category(im),
                requirement = string.Empty,
                description = BuildDescription(im, quantity),
            };
            if (im != null && im.itemType == 1)
            {
                string cls = im.classReq == 0
                    ? "공용"
                    : (db.Classes.TryGetValue(im.classReq, out var cm) ? cm.name : $"직업 {im.classReq}");
                info.requirement = im.levelReq > 0 ? $"요구 Lv.{im.levelReq} / {cls}" : cls;
            }
            return info;
        }

        /// <summary>아이템 종류(무기/보조무기/방어구/재료/재화).</summary>
        private static string Category(ItemMaster im)
        {
            if (im == null)
            {
                return string.Empty;
            }
            switch (im.itemType)
            {
                case 1:
                    if (im.equipSlot == 1) return "무기";
                    if (im.equipSlot == 2) return "보조무기";
                    return "방어구";
                case 2: return "재료";
                case 3: return "재화";
                default: return "기타";
            }
        }

        /// <summary>아이템 설명문(종류별). 장비는 옵션 효과, 재료/재화는 용도 설명.</summary>
        private static string BuildDescription(ItemMaster im, long quantity)
        {
            if (im == null)
            {
                return string.Empty;
            }
            if (im.itemType == 1)
            {
                string effect = BuildStatsText(im);
                return string.IsNullOrEmpty(effect) || effect == "옵션 없음"
                    ? "착용 시 캐릭터에 장착되는 장비입니다."
                    : $"착용 시 다음 효과를 부여합니다.\n{effect}";
            }
            if (im.itemType == 2)
            {
                string q = quantity > 1 ? $" (획득 {quantity})" : string.Empty;
                return $"강화·합성 등에 사용하는 재료입니다.{q}";
            }
            if (im.itemType == 3)
            {
                return "게임 내에서 사용하는 재화입니다.";
            }
            return string.Empty;
        }

        /// <summary>장비 옵션 스탯 요약 문자열.</summary>
        private static string BuildStatsText(ItemMaster im)
        {
            if (im == null || im.itemType != 1)
            {
                return string.Empty;
            }
            var s = im.baseStats;
            var parts = new List<string>();
            if (s.atk != 0) parts.Add($"ATK +{s.atk}");
            if (s.def != 0) parts.Add($"DEF +{s.def}");
            if (s.hp != 0) parts.Add($"HP +{s.hp}");
            if (s.critChance != 0) parts.Add($"치명확률 +{s.critChance * 100f:0.#}%");
            if (s.critDamage != 0) parts.Add($"치명피해 +{s.critDamage * 100f:0.#}%");
            if (s.moveSpeed != 0) parts.Add($"이동속도 +{s.moveSpeed:0.##}");
            if (s.cooldown != 0) parts.Add($"쿨타임 {s.cooldown:0.##}");
            return parts.Count > 0 ? string.Join("\n", parts) : "옵션 없음";
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

    /// <summary>클리어 보상 아이템 칸의 hover 감지기. 진입 시 오버레이에 상세 툴팁을 요청하고, 이탈 시 숨긴다.</summary>
    public class RewardItemHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private StageClearOverlay _overlay;
        private int _itemCode;
        private long _quantity;

        public void Init(StageClearOverlay overlay, int itemCode, long quantity)
        {
            _overlay = overlay;
            _itemCode = itemCode;
            _quantity = quantity;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_overlay != null)
            {
                _overlay.ShowItemTooltip(_itemCode, _quantity, eventData.position);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_overlay != null)
            {
                _overlay.HideItemTooltip();
            }
        }
    }
}
