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
            // 골드(item_1 아이콘 재사용). 마스터 데이터에 없는 재화라 hover 상세는 끈다.
            if (rewards != null && rewards.gold > 0)
            {
                CreateRewardEntry(row, font, 1, rewards.gold, $"+{rewards.gold:N0}", false);
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
                    CreateRewardEntry(row, font, item.itemCode, item.quantity, qty, true);
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

        /// <summary>[폴백] 아이콘+수량 보상 항목 한 칸을 코드로 만든다(아이콘 없으면 색 사각형).
        /// slotColor는 등급별 슬롯 배경. 폴백에서는 hover 상세를 제공하지 않는다.</summary>
        private void CreateRewardEntryFallback(RectTransform parent, Font font, Sprite icon, string qtyText, Color tint, Color slotColor)
        {
            var entry = new GameObject("Reward", typeof(RectTransform));
            entry.transform.SetParent(parent, false);
            var ert = (RectTransform)entry.transform;
            ert.sizeDelta = new Vector2(150f, 190f);

            // 아이콘 배경(슬롯): item_slot 프레임(테두리 장식)을 바탕으로 깔고, 등급색은 테두리 안쪽에 겹친다.
            // 프레임 스프라이트가 없으면(에셋 미빌드) 기존처럼 등급색 사각형만 표시하는 폴백.
            var slot = CreateChild("Slot", entry.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            slot.sizeDelta = new Vector2(140f, 140f);
            slot.anchoredPosition = Vector2.zero;
            var slotImg = slot.gameObject.AddComponent<Image>();
            Sprite frame = _assets != null ? _assets.itemSlotFrame : null;
            if (frame != null)
            {
                slotImg.sprite = frame;
                slotImg.color = Color.white;
                // 등급별 배경색(프레임 테두리 안쪽 영역, 아이콘 뒤).
                var gradeRt = CreateChild("GradeBg", slot, Vector2.zero, Vector2.one);
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
            // 아이템 칸과 통일감 있게 item_slot 프레임을 사용(중앙이 어두운 판이라 별도 배경 불필요).
            Sprite frame = _assets != null ? _assets.itemSlotFrame : null;
            if (frame != null)
            {
                slotImg.sprite = frame;
                slotImg.color = Color.white;
            }
            else
            {
                slotImg.color = new Color(0.12f, 0.14f, 0.22f, 0.95f);
            }
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
