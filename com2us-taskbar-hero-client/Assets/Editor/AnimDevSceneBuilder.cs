using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 캐릭터 프리팹 애니메이션 확인용 개발 씬 <c>Assets/Scenes/AnimDevScene.unity</c>를 만들거나 갱신하는 도구.
    /// <para>
    /// 씬 구성은 최소한으로 둔다 — 카메라 · EventSystem · <b>Knight_Male 프리팹 인스턴스 하나</b> ·
    /// 애니메이션 목록 UI를 만드는 <see cref="AnimDevController"/>. 배경·조명·이펙트는 두지 않는다
    /// (SPUM 스프라이트는 Sprites-Default(무광) 머티리얼이라 URP 2D에서 조명 없이도 정상 렌더링된다).
    /// </para>
    /// <para>
    /// <b>새 애니메이션(.anim)을 테스트에 넣으려면</b> 파일을 <c>Assets/Animations</c>에 두고 이 도구를 다시 실행한다 —
    /// 그 폴더의 클립을 컨트롤러의 '추가 클립' 슬롯에 배선해 목록에 얹는다(프리팹의 SPUM 클립 리스트는 건드리지 않는다).
    /// 재생 상태는 이름으로 추정하며(기본 ATTACK), 틀리면 씬 인스펙터에서 항목의 <c>playAs</c>만 바꾸면 된다.
    /// </para>
    /// BattleDevScene과 같은 개발용 하네스 씬이므로 Build Settings에는 등록하지 않는다.
    /// 메뉴: TaskbarHero/UI/애니메이션 개발 씬(AnimDevScene) 생성
    /// </summary>
    public static class AnimDevSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/AnimDevScene.unity";
        private const string CharacterPrefabPath = "Assets/Prefabs/Character/Knight_Male.prefab";

        // 이 폴더의 .anim은 '추가 클립'으로 자동 배선된다 — 새 클립을 여기 넣고 이 도구를 다시 실행하면 목록에 올라간다.
        private const string ExtraClipFolder = "Assets/Animations";

        // 캐릭터 한 명만 보는 씬이라 가깝게 당겨 잡는다(캐릭터 높이 약 0.8유닛).
        // 가로 위치는 AnimDevController가 실행 시 Game View 종횡비를 보고 다시 맞춘다(목록 패널에 가리지 않게).
        private static readonly Vector3 CharacterPosition = new Vector3(0.6f, 0f, 0f);
        private static readonly Vector3 CameraPosition = new Vector3(0.6f, 0.4f, -10f);
        private const float CameraOrthographicSize = 1f;

        [MenuItem("TaskbarHero/UI/애니메이션 개발 씬(AnimDevScene) 생성")]
        public static void Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[AnimDevSceneBuilder] 캐릭터 프리팹을 찾지 못했습니다: {CharacterPrefabPath}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 카메라 — 캐릭터만 보이므로 단색 배경. AudioListener를 함께 붙인다(없으면 매 프레임 경고).
            var camGo = new GameObject("AnimDevCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.17f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = CameraOrthographicSize;
            camGo.transform.position = CameraPosition;

            // 목록 버튼 클릭 처리에 필요. 이 프로젝트는 새 Input System을 쓰므로 다른 씬과 같은 입력 모듈을 붙인다.
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // 테스트 대상 — 프리팹 인스턴스(연결 유지: 프리팹을 고치면 씬에도 반영된다).
            var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            character.transform.position = CharacterPosition;

            // 애니메이션 목록 UI + 캐릭터 클릭 재생을 담당하는 하네스 컨트롤러.
            var ctrlGo = new GameObject("AnimDevController");
            var ctrl = ctrlGo.AddComponent<AnimDevController>();
            WireController(ctrl, character, cam);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[AnimDevSceneBuilder] 완료: {ScenePath} 생성 "
                      + $"(AnimDevCamera / EventSystem / {prefab.name} / AnimDevController). "
                      + "Build Settings에는 등록하지 않음(개발용 하네스 씬).");
        }

        /// <summary>컨트롤러의 대상 캐릭터(SPUM_Prefabs)와 카메라 참조를 배선한다.</summary>
        private static void WireController(AnimDevController ctrl, GameObject character, Camera cam)
        {
            var spum = character.GetComponent<SPUM_Prefabs>();
            if (spum == null)
            {
                Debug.LogWarning($"[AnimDevSceneBuilder] {character.name}에 SPUM_Prefabs가 없습니다 — "
                                 + "컨트롤러가 런타임에 씬에서 직접 찾습니다.");
            }

            var so = new SerializedObject(ctrl);
            var targetProp = so.FindProperty("target");
            if (targetProp != null)
            {
                targetProp.objectReferenceValue = spum;
            }
            var camProp = so.FindProperty("cam");
            if (camProp != null)
            {
                camProp.objectReferenceValue = cam;
            }
            WireExtraClips(so, character);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// <see cref="ExtraClipFolder"/>의 애니메이션 클립을 컨트롤러의 '추가 클립' 슬롯에 배선한다.
        /// 프리팹의 SPUM 클립 리스트를 건드리지 않고 목록에 얹기 위한 경로이며,
        /// 이미 특수 모션으로 노출되는 클립(SpumCharacterAnimator의 돌진·분노)은 중복이라 제외한다.
        /// </summary>
        private static void WireExtraClips(SerializedObject so, GameObject character)
        {
            var arrayProp = so.FindProperty("extraClips");
            if (arrayProp == null)
            {
                Debug.LogWarning("[AnimDevSceneBuilder] extraClips 프로퍼티를 찾지 못했습니다.");
                return;
            }

            var used = new HashSet<AnimationClip>();
            var helper = character.GetComponent<SpumCharacterAnimator>();
            if (helper != null)
            {
                used.Add(helper.chargeDashClip);
                used.Add(helper.rageClip);
            }

            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { ExtraClipFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
                .Where(clip => clip != null && !used.Contains(clip))
                .ToList();

            arrayProp.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
            {
                var element = arrayProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("clip").objectReferenceValue = clips[i];
                element.FindPropertyRelative("playAs").enumValueIndex = (int)GuessState(clips[i].name);
            }

            Debug.Log($"[AnimDevSceneBuilder] 추가 클립 {clips.Count}개 배선: "
                      + (clips.Count > 0 ? string.Join(", ", clips.Select(c => c.name)) : "없음"));
        }

        /// <summary>
        /// 클립 이름으로 재생 상태를 추정한다(1회성 모션이 대부분이라 기본은 ATTACK).
        /// 추정이 틀리면 씬의 AnimDevController 인스펙터에서 해당 항목의 <c>playAs</c>만 바꾸면 된다.
        /// </summary>
        private static PlayerState GuessState(string clipName)
        {
            string name = clipName.ToLower();
            if (name.Contains("idle")) return PlayerState.IDLE;
            if (name.Contains("move") || name.Contains("walk") || name.Contains("run")) return PlayerState.MOVE;
            if (name.Contains("damage") || name.Contains("hit")) return PlayerState.DAMAGED;
            if (name.Contains("debuff") || name.Contains("stun")) return PlayerState.DEBUFF;
            if (name.Contains("die") || name.Contains("death")) return PlayerState.DEATH;
            return PlayerState.ATTACK;
        }
    }
}
