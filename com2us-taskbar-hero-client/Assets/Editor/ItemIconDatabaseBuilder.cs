using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// <see cref="ItemIconDatabase"/>(Resources)를 생성·갱신하는 에디터 도구.
    /// Assets/Art/Icon/Item/item_{code}.png 를 스캔해 코드→아이콘 매핑을 채운다(골드 item_1 포함).
    /// 메뉴: TaskbarHero/UI/아이템 아이콘 DB 빌드
    /// </summary>
    public static class ItemIconDatabaseBuilder
    {
        private const string IconDir = "Assets/Art/Icon/Item";
        private const string AssetPath = "Assets/Resources/ItemIconDatabase.asset";

        [MenuItem("TaskbarHero/UI/아이템 아이콘 DB 빌드")]
        public static void Build()
        {
            var icons = new List<ItemIconDatabase.IconEntry>();
            foreach (var file in Directory.GetFiles(IconDir, "item_*.png"))
            {
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path); // item_31121
                string codeStr = name.Substring("item_".Length);
                if (!int.TryParse(codeStr, out int code))
                {
                    continue;
                }
                var sp = LoadSprite(path);
                if (sp != null)
                {
                    icons.Add(new ItemIconDatabase.IconEntry { code = code, sprite = sp });
                }
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var asset = AssetDatabase.LoadAssetAtPath<ItemIconDatabase>(AssetPath);
            bool isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<ItemIconDatabase>();
            }
            asset.entries = icons.ToArray();

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
            Debug.Log($"[ItemIconDatabaseBuilder] 완료: 아이콘 {icons.Count}종 → {AssetPath}");
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
            return null;
        }
    }
}
