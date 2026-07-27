using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Battle;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스테이지 클리어 연출 오버레이 프리팹(<c>Assets/Prefabs/UI/StageClearOverlay.prefab</c>) 생성 도구.
    /// 정적 계층(Canvas·Dim·팡파레·타이틀·보상 행 컨테이너·안내 문구)은 <see cref="StageClearOverlay.EditorConstruct"/>가
    /// 구성하며, 이 빌더는 그 결과를 프리팹으로 굽고 <c>StageClearAssets.overlayPrefab</c>에 배선한다.
    /// 런타임(StageClearOverlay.Show)은 이 프리팹을 Instantiate해 가변 개수의 보상 칸(공용 ItemSlot)만
    /// 동적으로 채운다. 메뉴: TaskbarHero/UI/클리어 연출 프리팹 빌드
    /// </summary>
    public static class StageClearOverlayBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/StageClearOverlay.prefab";
        private const string StageClearAssetPath = "Assets/Resources/StageClearAssets.asset";

        [MenuItem("TaskbarHero/UI/클리어 연출 프리팹 빌드")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            WireStageClearAssets(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[StageClearOverlayBuilder] 완료: 클리어 연출 오버레이 프리팹 생성 + StageClearAssets 배선.");
        }

        private static GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            }

            var root = new GameObject("StageClearOverlay");
            var overlay = root.AddComponent<StageClearOverlay>();
            overlay.EditorConstruct(); // 정적 계층을 프리팹에 굽는다

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[StageClearOverlayBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>StageClearAssets(Resources)에 오버레이 프리팹을 배선한다.</summary>
        private static void WireStageClearAssets(GameObject prefab)
        {
            var asset = AssetDatabase.LoadAssetAtPath<StageClearAssets>(StageClearAssetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[StageClearOverlayBuilder] {StageClearAssetPath}가 없어 건너뜁니다. " +
                                 "'TaskbarHero/UI/클리어 연출 에셋 빌드'를 먼저 실행하세요.");
                return;
            }
            asset.overlayPrefab = prefab;
            EditorUtility.SetDirty(asset);
            Debug.Log("[StageClearOverlayBuilder] StageClearAssets.overlayPrefab 배선 완료.");
        }
    }
}
