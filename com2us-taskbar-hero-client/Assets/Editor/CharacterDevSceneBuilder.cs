using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 몬스터 유닛 자동생성 개발 씬 <c>Assets/Scenes/CharacterDevScene.unity</c>를 만들거나 갱신하는 도구.
    /// 기획서: <c>docs/캐릭터-개발씬-기획서.md</c> §5
    ///
    /// <para>씬 구성은 최소한이다 — 카메라 · EventSystem · <see cref="CharacterDevController"/> · PreviewAnchor.
    /// <b>SPUM_Scene은 쓰지 않는다</b>: 조합·저장 기능만 <see cref="SpumUnitComposer"/>로 뽑아 재구현했고,
    /// 플레이 시작 시 프리뷰 캐릭터 <b>한 명</b>만 앵커에 세운다(기획서 §2.1·§2.2·§5).</para>
    ///
    /// BattleDevScene·AnimDevScene과 같은 개발용 하네스라 Build Settings에는 등록하지 않는다(N1).
    /// 메뉴: TaskbarHero/UI/캐릭터 개발 씬(CharacterDevScene) 생성
    /// </summary>
    public static class CharacterDevSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/CharacterDevScene.unity";
        private const string RecipePath = "Assets/Dev/monster-appearance-recipe.json";

        // 프리뷰 캐릭터 한 명만 보는 씬이라 가깝게 당겨 잡는다(AnimDevScene과 같은 계열의 프레이밍).
        private static readonly Vector3 CameraPosition = new Vector3(0f, 0.4f, -10f);
        private const float CameraOrthographicSize = 1.2f;

        [MenuItem("TaskbarHero/UI/캐릭터 개발 씬(CharacterDevScene) 생성")]
        public static void Build()
        {
            var recipe = AssetDatabase.LoadAssetAtPath<TextAsset>(RecipePath);
            if (recipe == null)
            {
                Debug.LogWarning($"[CharacterDevSceneBuilder] 레시피 JSON을 찾지 못했습니다: {RecipePath} "
                                 + "— 씬은 만들되 레시피 슬롯은 비워 둡니다(코드 시드 랜덤으로만 동작).");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 카메라 — 단색 배경(AnimDevScene과 동일 계열). AudioListener는 여기 하나뿐이다.
            var camGo = new GameObject("CharacterDevCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.17f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = CameraOrthographicSize;
            camGo.transform.position = CameraPosition;

            // 하네스 UI 클릭 처리에 필요(이 프로젝트는 새 Input System).
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // 프리뷰 캐릭터가 놓이는 자리. SPUM 유닛 루트가 RectTransform이라
            // 컨트롤러가 앵커를 중립화해 배치한다(§2.6).
            var anchorGo = new GameObject("PreviewAnchor");
            anchorGo.transform.position = Vector3.zero;

            var ctrlGo = new GameObject("CharacterDevController");
            var ctrl = ctrlGo.AddComponent<CharacterDevController>();
            WireController(ctrl, recipe, cam, anchorGo.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[CharacterDevSceneBuilder] 완료: {ScenePath} 생성 "
                      + "(CharacterDevCamera / EventSystem / PreviewAnchor / CharacterDevController). "
                      + "프리뷰 캐릭터는 플레이 시작 시 PreviewAnchor에 1명 생성됩니다. "
                      + "Build Settings에는 등록하지 않음(개발용 하네스 씬).");
        }

        /// <summary>컨트롤러의 레시피·카메라·미리보기 앵커 참조를 배선한다.</summary>
        private static void WireController(CharacterDevController ctrl, TextAsset recipe, Camera cam,
                                           Transform previewAnchor)
        {
            var so = new SerializedObject(ctrl);
            SetRef(so, "recipeJson", recipe);
            SetRef(so, "cam", cam);
            SetRef(so, "previewAnchor", previewAnchor);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(SerializedObject so, string propertyName, Object value)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[CharacterDevSceneBuilder] {propertyName} 프로퍼티를 찾지 못했습니다.");
                return;
            }
            prop.objectReferenceValue = value;
        }
    }

    /// <summary>
    /// CharacterDevScene이 예약한 <b>던전 전투 배선</b>을 플레이 종료 직후에 실행하는 훅(기획서 F8).
    /// <para><see cref="DungeonBattleBuilder"/>는 씬을 열고 저장하므로 플레이 중에는 실행할 수 없다.
    /// 그래서 하네스는 EditorPrefs에 플래그만 남기고 플레이를 멈추고, 여기서 그 플래그를 보고 실행한다.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class CharacterDevWiringHook
    {
        static CharacterDevWiringHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // 도메인 리로드가 플레이 종료와 함께 일어나면 위 이벤트를 놓칠 수 있어, 로드 시점에도 한 번 확인한다.
            EditorApplication.delayCall += RunIfRequested;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += RunIfRequested;
            }
        }

        private static void RunIfRequested()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || !EditorPrefs.GetBool(CharacterDevController.RunWiringPrefKey, false))
            {
                return;
            }

            EditorPrefs.DeleteKey(CharacterDevController.RunWiringPrefKey);
            Debug.Log("[CharacterDevScene] 예약된 '던전 전투 배선'을 실행합니다.");
            DungeonBattleBuilder.Build();
        }
    }
}
