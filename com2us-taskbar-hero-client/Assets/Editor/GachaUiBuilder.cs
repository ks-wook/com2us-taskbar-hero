using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;
using TaskbarHero.Client.Managers;
using TaskbarHero.Client.UI;
using TaskbarHero.Client.UI.Gacha;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 뽑기(가챠) UI 빌드 도구. 세 가지를 한 번에 처리한다.
    /// <list type="number">
    /// <item>뽑기 패널 정적 계층을 <see cref="GachaPanelController.EditorConstruct"/>로 구성해 프리팹으로 굽는다
    ///   (결과 연출 오버레이도 같은 프리팹 안에 포함된다).</item>
    /// <item><c>Assets/Art/UI/Gacha</c>를 훑어 <b>파일명의 가챠 코드</b>별로 배너/탭 스프라이트를 배선한다 —
    ///   배너를 추가할 때 코드를 고칠 필요 없이 png 3장을 넣고 이 메뉴를 다시 실행하면 된다.</item>
    /// <item>Title/GameScene의 UIManager에 프리팹을, GameScene HUD에 뽑기 아이콘을 배선한다.</item>
    /// </list>
    /// <b>아트 임포트 설정은 건드리지 않는다</b>(공용 아트 규칙 — <c>Multiple</c>로 임포트된 텍스처를 <c>Single</c>로
    /// 바꾸면 서브 스프라이트 참조가 끊긴다). 이미 임포트된 스프라이트를 읽어 배선만 하며, 스프라이트 에셋을
    /// 바로 찾지 못하면 서브 스프라이트를 집는 폴백을 쓴다.
    /// 메뉴: TaskbarHero/UI/뽑기 패널·씬 배선
    /// </summary>
    public static class GachaUiBuilder
    {
        private const string PrefabPath = "Assets/Prefabs/UI/GachaPanel.prefab";
        private const string ItemSlotPrefabPath = "Assets/Prefabs/UI/ItemSlot.prefab";
        private const string ArtDir = "Assets/Art/UI/Gacha";
        private const string GlowGrade4Dir = "Assets/Art/Effect/UI/FourStarSlotGlow";
        private const string GlowGrade5Dir = "Assets/Art/Effect/UI/FiveStarSlotGlow";
        private const string GachaIconPath = "Assets/Art/Icon/뽑기.png";
        private const string PanelBgPath = "Assets/Art/UI/modal_bg.png";
        private const string BoxBgPath = "Assets/Art/UI/ui_bg.png";
        private const string DetailBgPath = "Assets/Art/UI/item_detail_bg.png";
        private const string ButtonPath = "Assets/Art/UI/pixel_rpg_button.png";
        private const string ArrowPath = "Assets/Art/UI/화살표버튼.png";
        // 결과 창 테두리(액자) — 스테이지 지역 창과 같은 아트를 써서 화면 톤을 맞춘다.
        private const string WindowFramePath = "Assets/Art/UI/Trade/01_Frames_Panels/window_frame_hollow.png";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

        // 배너 아트 파일명 규칙(가챠 코드가 접미사로 붙는다).
        private const string BannerPrefix = "gacha_banner_";
        private const string BannerFramesPrefix = "BannerFrames_"; // 움직이는 배너 프레임 폴더(코드별)
        private const float DefaultBannerFps = 12f;                // 원본 GIF와 같은 재생 속도
        private const string TabNormalPrefix = "gacha_btn_normal_";
        private const string TabSelectedPrefix = "gacha_btn_selected_";

        [MenuItem("TaskbarHero/UI/뽑기 패널·씬 배선")]
        public static void Build()
        {
            var prefab = BuildPrefab();
            AssignToScene(TitleScenePath, prefab, false);
            AssignToScene(GameScenePath, prefab, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[GachaUiBuilder] 완료: 뽑기 패널 프리팹 생성 + 배너 아트 배선 + Title/GameScene 배선.");
        }

        private static GameObject BuildPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");

            var root = new GameObject("GachaPanel");
            var ctrl = root.AddComponent<GachaPanelController>();

            // 결과 연출 오버레이는 패널 안의 자식으로 함께 굽는다(자기 캔버스로 패널 위에 그린다).
            var overlayGo = new GameObject("ResultOverlay", typeof(RectTransform));
            overlayGo.transform.SetParent(root.transform, false);
            var overlay = overlayGo.AddComponent<GachaResultOverlay>();

            var oso = new SerializedObject(overlay);
            oso.FindProperty("_resultBackground").objectReferenceValue = LoadSprite($"{ArtDir}/gacha_result_bg.png");
            oso.FindProperty("_slotFrame").objectReferenceValue = LoadSprite($"{ArtDir}/gacha_result_slot.png");
            // 제목 명판(텍스트 제목 대신 노출된다 — 없으면 텍스트로 폴백).
            oso.FindProperty("_titleImageSprite").objectReferenceValue = LoadSprite($"{ArtDir}/gacha_result_ui.png");
            oso.FindProperty("_windowFrame").objectReferenceValue = LoadSprite(WindowFramePath);
            oso.FindProperty("_buttonSprite").objectReferenceValue = LoadSprite(ButtonPath);
            oso.FindProperty("_itemSlotPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            oso.FindProperty("_clipGrade3").objectReferenceValue = LoadClip($"{ArtDir}/3성연출.mp4");
            oso.FindProperty("_clipGrade4").objectReferenceValue = LoadClip($"{ArtDir}/4성연출.mp4");
            oso.FindProperty("_clipGrade5").objectReferenceValue = LoadClip($"{ArtDir}/5성연출.mp4");
            FillFrames(oso.FindProperty("_glowGrade4"), GlowGrade4Dir);
            FillFrames(oso.FindProperty("_glowGrade5"), GlowGrade5Dir);
            oso.ApplyModifiedPropertiesWithoutUndo();

            // EditorConstruct 전에 배선해 정적 계층이 아트를 바로 반영하게 한다.
            var so = new SerializedObject(ctrl);
            so.FindProperty("_panelBackground").objectReferenceValue = LoadSprite(PanelBgPath);
            so.FindProperty("_boxBackground").objectReferenceValue = LoadSprite(BoxBgPath);
            so.FindProperty("_detailBackground").objectReferenceValue = LoadSprite(DetailBgPath);
            so.FindProperty("_buttonSprite").objectReferenceValue = LoadSprite(ButtonPath);
            // 기록 페이저 화살표(오른쪽 방향 한 장 — '<'는 컨트롤러가 좌우 반전해 쓴다).
            so.FindProperty("_arrowSprite").objectReferenceValue = LoadSprite(ArrowPath);
            // 천장 픽업 아이템 칸도 공용 슬롯 프리팹을 쓴다(결과 오버레이와 같은 아트).
            so.FindProperty("_itemSlotPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ItemSlotPrefabPath);
            so.FindProperty("_resultOverlay").objectReferenceValue = overlay;
            FillBannerArt(so.FindProperty("_bannerArt"));
            so.ApplyModifiedPropertiesWithoutUndo();

            ctrl.EditorConstruct(); // 패널 + 결과 오버레이 정적 계층을 프리팹에 굽는다

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[GachaUiBuilder] 프리팹 저장: {PrefabPath}");
            return prefab;
        }

        /// <summary>
        /// 아트 폴더의 <c>gacha_banner_&lt;code&gt;</c>·<c>gacha_btn_normal_&lt;code&gt;</c>·
        /// <c>gacha_btn_selected_&lt;code&gt;</c>를 코드별로 묶어 <c>_bannerArt</c> 배열에 채운다(코드 오름차순).
        /// </summary>
        private static void FillBannerArt(SerializedProperty array)
        {
            var byCode = new SortedDictionary<int, GachaBannerArt>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);

                // GIF은 정적 배너 후보로 쓰지 않는다 — Unity가 <b>첫 프레임짜리 정지 텍스처</b>로 임포트하므로
                // 같은 코드의 png와 둘 다 잡혀 어느 쪽이 배선될지 순서에 좌우된다(움직임은 BannerFrames_<코드>가 담당).
                if (path.EndsWith(".gif", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string prefix = file.StartsWith(TabSelectedPrefix) ? TabSelectedPrefix
                    : file.StartsWith(TabNormalPrefix) ? TabNormalPrefix
                    : file.StartsWith(BannerPrefix) ? BannerPrefix
                    : null;
                if (prefix == null)
                {
                    continue; // 결과 배경·슬롯 등 배너별 아트가 아닌 파일
                }
                string suffix = file.Substring(prefix.Length);
                if (!Regex.IsMatch(suffix, @"^\d+$") ||
                    !int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                {
                    continue; // 코드가 붙지 않은 파일(예: gacha_banner_normal)은 배선 대상이 아니다
                }

                if (!byCode.TryGetValue(code, out var art))
                {
                    art = new GachaBannerArt { gachaCode = code };
                    byCode[code] = art;
                }
                var sprite = LoadSprite(path);
                if (prefix == BannerPrefix) art.banner = sprite;
                else if (prefix == TabNormalPrefix) art.tabNormal = sprite;
                else art.tabSelected = sprite;
            }

            array.arraySize = byCode.Count;
            int i = 0;
            foreach (var pair in byCode)
            {
                var element = array.GetArrayElementAtIndex(i++);
                element.FindPropertyRelative("gachaCode").intValue = pair.Key;
                element.FindPropertyRelative("banner").objectReferenceValue = pair.Value.banner;
                element.FindPropertyRelative("tabNormal").objectReferenceValue = pair.Value.tabNormal;
                element.FindPropertyRelative("tabSelected").objectReferenceValue = pair.Value.tabSelected;
                WireBannerFrames(element, pair.Key);
            }
            Debug.Log($"[GachaUiBuilder] 배너 아트 배선: {byCode.Count}개 코드 " +
                      $"({string.Join(", ", new List<int>(byCode.Keys).ConvertAll(c => c.ToString()))})");
        }

        /// <summary>
        /// 배너의 <b>움직이는 배경</b> 프레임을 배선한다 — <c>Assets/Art/UI/Gacha/BannerFrames_&lt;code&gt;/</c> 폴더가
        /// 있으면 그 안의 프레임을 파일명 순서로 담고, 없으면 배열을 비워 정적 배너(<c>gacha_banner_&lt;code&gt;.png</c>)로
        /// 그려지게 한다.
        /// <para><b>왜 폴더인가</b>: 원본은 GIF(<c>gacha_banner_60002.gif</c>)인데 <b>Unity는 GIF를 첫 프레임짜리
        /// 정지 텍스처로만 임포트</b>하므로 애니메이션이 살지 않는다. 그래서 GIF에서 프레임을 뽑아 이 폴더에 두고
        /// 스프라이트 시퀀스로 돌린다(프레임 추출은 아트 작업 단계에서 수행).</para>
        /// </summary>
        private static void WireBannerFrames(SerializedProperty element, int code)
        {
            var frames = element.FindPropertyRelative("bannerFrames");
            var fps = element.FindPropertyRelative("bannerFps");
            if (frames == null)
            {
                return;
            }
            string dir = $"{ArtDir}/{BannerFramesPrefix}{code}";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                frames.arraySize = 0; // 정적 배너 사용
                return;
            }
            FillFrames(frames, dir);
            if (fps != null && fps.floatValue <= 0f)
            {
                fps.floatValue = DefaultBannerFps;
            }
            Debug.Log($"[GachaUiBuilder] 배너 {code} 애니메이션 프레임 {frames.arraySize}장 배선 ({dir}).");
        }

        /// <summary>스프라이트 시퀀스 폴더의 프레임을 <b>파일명 순서</b>(…_01, _02 …)로 배열에 채운다.</summary>
        private static void FillFrames(SerializedProperty array, string dir)
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(string.CompareOrdinal); // 파일명에 0 패딩이 있어 사전순 = 프레임순

            array.arraySize = paths.Count;
            for (int i = 0; i < paths.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = LoadSprite(paths[i]);
            }
            if (paths.Count == 0)
            {
                Debug.LogWarning($"[GachaUiBuilder] 글로우 프레임을 찾지 못했습니다: {dir}");
            }
        }

        /// <summary>스프라이트를 로드한다(Single/Multiple 임포트 모두 대응 — 임포트 설정은 바꾸지 않는다).</summary>
        private static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s)
                {
                    return s;
                }
            }
            Debug.LogWarning($"[GachaUiBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        /// <summary>등급 연출 영상(VideoClip)을 로드한다. 없으면 경고만 남기고 연출을 생략한다.</summary>
        private static VideoClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[GachaUiBuilder] 연출 영상을 찾지 못했습니다: {path}");
            }
            return clip;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        /// <summary>씬의 UIManager에 뽑기 패널을, GameScene이면 HUD에 뽑기 아이콘도 함께 배선한다.</summary>
        private static void AssignToScene(string scenePath, GameObject prefab, bool wireHudIcon)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning($"[GachaUiBuilder] {scene.name}에 UIManager가 없어 건너뜁니다.");
            }
            else
            {
                var so = new SerializedObject(uiManager);
                so.FindProperty("gachaPanelPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (wireHudIcon)
            {
                var hud = Object.FindAnyObjectByType<GameSceneHudController>(FindObjectsInactive.Include);
                if (hud == null)
                {
                    Debug.LogWarning($"[GachaUiBuilder] {scene.name}에 GameSceneHudController가 없어 아이콘 배선을 건너뜁니다.");
                }
                else
                {
                    var hso = new SerializedObject(hud);
                    hso.FindProperty("gachaIcon").objectReferenceValue = LoadSprite(GachaIconPath);
                    hso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[GachaUiBuilder] {scene.name} 배선 완료.");
        }
    }
}
