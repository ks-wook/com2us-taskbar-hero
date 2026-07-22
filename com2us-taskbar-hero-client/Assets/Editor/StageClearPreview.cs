using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Battle;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 스테이지 클리어 연출 오버레이를 에디터에서 바로 미리보는 도구.
    /// 서버 없이 샘플 보상 데이터로 오버레이를 띄운다(팡파레·보상·클릭/5초 닫힘 확인용).
    /// 메뉴: TaskbarHero/UI/클리어 연출 미리보기
    /// - Play 중이면 즉시 표시.
    /// - 정지 상태면 Play 모드 진입 후(도메인 리로드 이후) 자동으로 1회 표시한다.
    /// </summary>
    [InitializeOnLoad]
    public static class StageClearPreview
    {
        private const string PendingKey = "TBH_StageClearPreviewPending";

        static StageClearPreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("TaskbarHero/UI/클리어 연출 미리보기")]
        public static void Preview()
        {
            if (Application.isPlaying)
            {
                StageClearOverlay.ShowDebugSample();
            }
            else
            {
                // Play 진입은 도메인 리로드를 유발하므로 EditorPrefs로 의도를 넘긴 뒤 진입.
                EditorPrefs.SetBool(PendingKey, true);
                EditorApplication.EnterPlaymode();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(PendingKey, false))
            {
                EditorPrefs.SetBool(PendingKey, false);
                // 씬 초기화(Start 등)가 한 프레임 돈 뒤 표시.
                EditorApplication.delayCall += () =>
                {
                    if (Application.isPlaying)
                    {
                        StageClearOverlay.ShowDebugSample();
                    }
                };
            }
        }
    }
}
