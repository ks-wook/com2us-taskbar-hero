using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Battle;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스테이지 클리어 연출용 <see cref="StageClearAssets"/>(Resources)를 생성·갱신하는 에디터 도구.
    /// - 팡파레 프레임: Assets/Art/Effect/UI/StageClearFanfare/StageClearFanfare_*.png (번호순)
    /// - 아이템 아이콘: Assets/Art/Icon/Item/item_{code}.png (파일명에서 코드 파싱)
    /// - 아이템 칸 테두리 프레임: Assets/Art/UI/item_slot.png
    /// 스프라이트는 Multiple 모드이므로 각 텍스처의 첫 Sprite 서브에셋을 참조로 담는다.
    /// 메뉴: TaskbarHero/UI/클리어 연출 에셋 빌드
    /// </summary>
    public static class StageClearAssetsBuilder
    {
        private const string FanfareDir = "Assets/Art/Effect/UI/StageClearFanfare";
        private const string ItemIconDir = "Assets/Art/Icon/Item";
        private const string ItemSlotFramePath = "Assets/Art/UI/item_slot.png";
        private const string BuffMarkPath = "Assets/Art/Icon/Etc/버프마크.png";
        private const string ExpIconPath = "Assets/Art/Icon/Etc/경험치.png";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string OverlayPrefabPath = "Assets/Prefabs/UI/StageClearOverlay.prefab";
        private const string AssetPath = "Assets/Resources/StageClearAssets.asset";

        [MenuItem("TaskbarHero/UI/클리어 연출 에셋 빌드")]
        public static void Build()
        {
            // 팡파레 프레임(번호 오름차순).
            var frameFiles = Directory.GetFiles(FanfareDir, "StageClearFanfare_*.png")
                .Select(f => f.Replace('\\', '/'))
                .OrderBy(f => f)
                .ToArray();
            var frames = new List<Sprite>();
            foreach (var f in frameFiles)
            {
                var sp = LoadSprite(f);
                if (sp != null)
                {
                    frames.Add(sp);
                }
            }

            // 아이템 아이콘(item_{code}.png).
            var iconFiles = Directory.GetFiles(ItemIconDir, "item_*.png")
                .Select(f => f.Replace('\\', '/'));
            var icons = new List<StageClearAssets.IconEntry>();
            foreach (var f in iconFiles)
            {
                string name = Path.GetFileNameWithoutExtension(f); // item_31121
                string codeStr = name.Substring("item_".Length);
                if (!int.TryParse(codeStr, out int code))
                {
                    continue;
                }
                var sp = LoadSprite(f);
                if (sp != null)
                {
                    icons.Add(new StageClearAssets.IconEntry { code = code, sprite = sp });
                }
            }

            // 에셋 로드 or 신규 생성.
            EnsureResourcesFolder();
            var asset = AssetDatabase.LoadAssetAtPath<StageClearAssets>(AssetPath);
            bool isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<StageClearAssets>();
            }
            asset.fanfareFrames = frames.ToArray();
            asset.itemIcons = icons.ToArray();
            asset.itemSlotFrame = LoadSprite(ItemSlotFramePath); // 보상 아이템 칸 테두리 프레임
            asset.buffMarkIcon = LoadSprite(BuffMarkPath);       // 버프로 늘어난 보상 칸에 붙는 표시 마크
            asset.expIcon = LoadSprite(ExpIconPath);             // 경험치 보상이 HUD로 날아갈 때 쓰는 아이콘
            // 공용 아이템 슬롯 프리팹(재빌드 시 배선 유지). 아직 없으면 ItemSlotBuilder 실행 후 다시 배선된다.
            asset.itemSlotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            // 클리어 연출 오버레이 프리팹(재빌드 시 배선 유지). 아직 없으면 StageClearOverlayBuilder 실행 후 다시 배선된다.
            asset.overlayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPrefabPath);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StageClearAssetsBuilder] 완료: 팡파레 {frames.Count}프레임, 아이템 아이콘 {icons.Count}종, " +
                      $"슬롯 프레임 {(asset.itemSlotFrame != null ? "배선" : "없음")}, " +
                      $"버프마크 {(asset.buffMarkIcon != null ? "배선" : "없음")}, " +
                      $"경험치 아이콘 {(asset.expIcon != null ? "배선" : "없음")} → {AssetPath}");
        }

        /// <summary>Multiple 스프라이트 모드 텍스처에서 첫 Sprite 서브에셋을 로드한다.</summary>
        private static Sprite LoadSprite(string path)
        {
            var direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null)
            {
                return direct;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite sp)
                {
                    return sp;
                }
            }
            Debug.LogWarning($"[StageClearAssetsBuilder] Sprite 로드 실패: {path}");
            return null;
        }

        private static void EnsureResourcesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
        }
    }
}
