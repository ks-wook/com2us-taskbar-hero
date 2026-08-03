using UnityEditor;
using UnityEngine;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// Game 뷰의 <b>Low Resolution Aspect Ratios</b>(저해상도 렌더링)를 꺼 두는 도구.
    ///
    /// <para><b>왜 필요한가</b> — 이 토글이 켜져 있으면 Unity는 게임을 <b>논리 해상도(포인트)로 렌더한 뒤
    /// Game 뷰 창 크기(물리 픽셀)로 확대</b>한다. 고DPI 디스플레이(예: 배율 150% → <c>pixelsPerPoint 1.5</c>)에서는
    /// 화면 전체가 1.5배 업스케일돼 <b>텍스트·픽셀아트가 통째로 흐려진다</b>.
    /// 2026-08-03 실측: 창은 물리 1708×944인데 게임은 1081×608로 렌더 → 캔버스 배율 1.689,
    /// 텍스트 래스터 17~25px. 토글을 끄자 1622×913 렌더 / 배율 2.535 / 래스터 25~38px로 올라갔다.</para>
    ///
    /// <para>글리프에 들어가는 픽셀 수 자체가 달라지는 문제이므로 <c>UiTextStyle</c>의 선명화 셰이더로는
    /// 보정할 수 없다. 텍스트가 흐리다는 보고를 받으면 <b>이 설정을 가장 먼저</b> 확인한다.</para>
    ///
    /// <para><b>왜 자동 보정인가</b> — 이 값은 프로젝트 설정이 아니라 <b>Game 뷰 창(에디터 레이아웃) 상태</b>라
    /// 레이아웃 초기화·에디터 버전 업그레이드로 조용히 다시 켜진다(2026-08-03에 실제로 켜져 있었다).
    /// 그래서 에디터 로드 시마다 확인해 꺼 준다(같은 이유로 자동 보정하는 <see cref="UiTextShaderRegistrar"/>와 동일한 패턴).
    /// 세션 중에 직접 켜는 것은 막지 않으며(저DPI 기기 에뮬레이션 등), 다음 에디터 로드에서 다시 꺼진다.</para>
    ///
    /// 수동 실행: 메뉴 <b>TaskbarHero/UI/Game 뷰 저해상도 렌더링 끄기</b>
    /// </summary>
    public static class GameViewResolutionGuard
    {
        [InitializeOnLoadMethod]
        private static void DisableOnLoad()
        {
            // 에디터 로드 직후에는 Game 뷰가 아직 없을 수 있어 한 프레임 뒤에 처리한다.
            EditorApplication.delayCall += () => Disable(false);
        }

        [MenuItem("TaskbarHero/UI/Game 뷰 저해상도 렌더링 끄기")]
        private static void DisableFromMenu() => Disable(true);

        /// <summary>열려 있는 모든 Game 뷰의 저해상도 렌더링을 끈다. <paramref name="verbose"/>면 이미 꺼져 있을 때도 로그를 남긴다.</summary>
        private static void Disable(bool verbose)
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                return; // 에디터 내부 타입이 바뀐 경우 — 조용히 넘어간다(수동 토글로 대응)
            }

            var property = gameViewType.GetProperty("lowResolutionForAspectRatios",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            int turnedOff = 0, alreadyOff = 0;
            foreach (var window in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                var view = window as EditorWindow;
                if (view == null)
                {
                    continue;
                }

                if (property.GetValue(view) is bool low && low)
                {
                    property.SetValue(view, false);
                    view.Repaint();
                    turnedOff++;
                }
                else
                {
                    alreadyOff++;
                }
            }

            if (turnedOff > 0)
            {
                Debug.Log($"[GameViewResolutionGuard] Game 뷰 저해상도 렌더링을 껐습니다({turnedOff}개) — "
                          + "고DPI 화면에서 텍스트가 업스케일로 흐려지는 것을 막습니다.");
            }
            else if (verbose)
            {
                Debug.Log($"[GameViewResolutionGuard] 이미 꺼져 있습니다(확인한 Game 뷰 {alreadyOff}개). "
                          + $"현재 렌더 해상도 {Screen.width}×{Screen.height}");
            }
        }
    }
}
