using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TaskbarHero.Client.UI;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 파티 편성 전용 씬 <c>Assets/Scenes/TeamListScene.unity</c>를 만들거나 갱신하는 도구.
    /// 편성은 팝업 UI가 아니라 독립 씬으로 다루므로, 씬에는 카메라 · EventSystem ·
    /// <see cref="TeamListController"/> 캔버스만 둔다(계층은 컨트롤러가 코드로 구성).
    /// UI 아트(패널 프레임 ui_bg · 자리 슬롯 TeamList/party_slot · 버튼 pixel_rpg_button · 제목 아이콘 편성)를
    /// 컨트롤러에 배선한 뒤 계층을 굽는다.
    /// 씬을 Build Settings 목록에도 등록한다(SceneManager.LoadScene 으로 전환하기 위해 필요).
    /// 메뉴: TaskbarHero/UI/편성 씬(TeamListScene) 생성
    /// </summary>
    public static class TeamListSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TeamListScene.unity";

        // 패널 배경은 9-slice 나무 프레임(ui_bg) — 창 비율(정사각 확장 창)에 따라 패널이 늘어나도 모양이 유지된다.
        private const string PanelSpritePath = "Assets/Art/UI/ui_bg.png";
        private const string ButtonSpritePath = "Assets/Art/UI/pixel_rpg_button.png";
        // 파티 자리 슬롯 아트(석재 액자 + 빈 자리 물음표 실루엣).
        private const string SlotSpritePath = "Assets/Art/UI/TeamList/party_slot.png";
        private const string TitleIconPath = "Assets/Art/Icon/편성.png";

        [MenuItem("TaskbarHero/UI/편성 씬(TeamListScene) 생성")]
        public static void Build()
        {
            // 주의: 공용 아트(ui_bg·pixel_rpg_button 등)의 임포트 설정(spriteImportMode·spriteBorder)은
            // 여기서 절대 바꾸지 않는다. Multiple로 임포트된 텍스처를 Single로 바꾸면 서브 스프라이트가 사라져
            // 그 스프라이트를 쓰던 기존 UI(모달·로그인·메일·출석부 등)의 배경·버튼 참조가 모두 끊긴다.
            // 이 도구는 이미 임포트된 스프라이트를 '읽어서 배선'만 한다.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 카메라 — UI만 있는 씬이라 배경색만 채운다.
            // AudioListener를 함께 붙인다(없으면 Unity가 "no audio listener" 경고를 매 프레임 낸다).
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            // 버튼 클릭 처리에 필요. 이 프로젝트는 새 Input System을 쓰므로 다른 씬과 같은 입력 모듈을 붙인다.
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

            // 편성 캔버스 — 아트를 먼저 배선하고(Construct가 배경·프레임·버튼에 적용) 계층을 굽는다.
            var canvasGo = new GameObject("TeamListCanvas", typeof(RectTransform));
            var ctrl = canvasGo.AddComponent<TeamListController>();
            WireArt(ctrl);
            ctrl.EditorConstruct();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();

            Debug.Log($"[TeamListSceneBuilder] 완료: {ScenePath} 생성 + UI 아트 배선 + Build Settings 등록 "
                      + $"(카메라 / EventSystem / TeamListCanvas)");
        }

        /// <summary>컨트롤러의 아트 참조(패널·프레임·버튼·제목 아이콘)를 배선한다.</summary>
        private static void WireArt(TeamListController ctrl)
        {
            var so = new SerializedObject(ctrl);
            SetSprite(so, "_panelSprite", PanelSpritePath);
            SetSprite(so, "_slotSprite", SlotSpritePath);
            SetSprite(so, "_buttonSprite", ButtonSpritePath);
            SetSprite(so, "_titleIcon", TitleIconPath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>직렬화 프로퍼티에 스프라이트를 배선한다(없으면 경고만 남기고 비워 둔다 — 컨트롤러가 단색으로 폴백).</summary>
        private static void SetSprite(SerializedObject so, string propertyName, string path)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[TeamListSceneBuilder] 프로퍼티를 찾지 못했습니다: {propertyName}");
                return;
            }
            prop.objectReferenceValue = LoadSpriteAt(path);
        }

        /// <summary>경로의 스프라이트를 로드한다(Single/Multiple 모두 대응 — 임포트 설정은 건드리지 않는다).</summary>
        private static Sprite LoadSpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s) return s;
            }
            Debug.LogWarning($"[TeamListSceneBuilder] 스프라이트를 찾지 못했습니다: {path}");
            return null;
        }

        /// <summary>씬을 Build Settings 목록 끝에 추가한다(이미 있으면 활성화만 보장).</summary>
        private static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var found = scenes.FirstOrDefault(s => s.path == ScenePath);
            if (found != null)
            {
                found.enabled = true;
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
