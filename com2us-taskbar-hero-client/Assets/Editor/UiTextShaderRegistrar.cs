using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 텍스트 선명화 폴백 셰이더(<c>TaskbarHero/UI Text Sharpen</c>)를
    /// <b>Project Settings > Graphics > Always Included Shaders</b>에 등록해 두는 도구.
    /// <para>
    /// 이 셰이더는 씬·프리팹·머티리얼 에셋 어디에서도 참조되지 않고 <c>Shader.Find</c>로만 쓰이므로,
    /// Always Included에 없으면 빌드에서 통째로 빠져 폴백이 동작하지 않는다.
    /// 그런데 이 목록은 GraphicsSettings에 저장되고 에디터가 설정을 다시 저장할 때 손실될 수 있어
    /// (2026-08-03에 실제로 사라졌다) 에디터 로드 시마다 존재를 확인해 없으면 다시 넣는다.
    /// </para>
    /// 수동 실행: 메뉴 <b>TaskbarHero/UI/텍스트 셰이더 Always Included 등록 확인</b>
    /// </summary>
    public static class UiTextShaderRegistrar
    {
        private const string ShaderAssetPath = "Assets/Shaders/UITextSharpen.shader";

        [InitializeOnLoadMethod]
        private static void EnsureOnLoad() => Ensure(false);

        [MenuItem("TaskbarHero/UI/텍스트 셰이더 Always Included 등록 확인")]
        private static void EnsureFromMenu() => Ensure(true);

        /// <summary>셰이더가 Always Included 목록에 없으면 추가한다. <paramref name="verbose"/>면 이미 있을 때도 로그를 남긴다.</summary>
        private static void Ensure(bool verbose)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
            if (shader == null)
            {
                return; // 셰이더 에셋이 없으면(삭제됐다면) 할 일 없음
            }

            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
                .FirstOrDefault(o => o != null && o.GetType().Name == "GraphicsSettings");
            if (settings == null)
            {
                return;
            }

            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null)
            {
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    if (verbose)
                    {
                        Debug.Log($"[UiTextShaderRegistrar] 이미 등록되어 있습니다: {shader.name}");
                    }
                    return;
                }
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[UiTextShaderRegistrar] Always Included Shaders에 등록했습니다: {shader.name}");
        }
    }
}
