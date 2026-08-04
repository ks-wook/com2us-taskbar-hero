using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 버튼 클릭음 전역 정책(사운드 리소스 정의서 §4.1 — <c>sfx_ui_click</c> / <c>sfx_ui_click_back</c>).
    ///
    /// <para><b>왜 자동 스캔인가</b> — 버튼이 40여 곳의 컨트롤러에서 런타임에 생성되므로 생성 지점마다
    /// 클릭음을 배선하면 새 화면에서 빠지기 쉽다. <see cref="UiTextStyle"/>가 텍스트 선명화를 주기 스캔으로
    /// 적용하는 것과 같은 방식으로, 여기서 씬의 모든 <see cref="Button"/>을 훑어 <c>onClick</c>에 클릭음을 한 번만 붙인다.</para>
    ///
    /// <para><b>어떤 소리인가</b> — 이름에 닫기·뒤로·딤이 들어간 버튼은 <see cref="SoundId.UiClickBack"/>,
    /// 그 밖은 <see cref="SoundId.UiClick"/>이다. 자기 화면에 더 맞는 소리를 <b>직접 재생하는 버튼</b>은
    /// <see cref="Suppress"/>로 제외해 두 소리가 겹치지 않게 한다(모달 확인/취소, 가챠·큐브 탭, 룬 노드 등).</para>
    ///
    /// <para>배선 여부는 버튼에 붙는 <see cref="UiClickSoundTag"/>로 기억하므로, 목록에 파괴된 버튼이 쌓이지 않는다.</para>
    /// </summary>
    public static class UiClickSound
    {
        /// <summary>새로 생성된 버튼을 잡기 위한 스캔 주기(초). <see cref="UiTextStyle"/>와 같은 간격.</summary>
        private const float ScanInterval = 0.4f;

        /// <summary>이름으로 '뒤로·닫기' 계열을 판정할 때 찾는 조각(대소문자 무시).</summary>
        private static readonly string[] BackNameHints = { "close", "back", "dim", "닫기", "뒤로" };

        private static float _nextScanTime;

        /// <summary>플레이 시작 시 스캔 훅을 건다(씬 로드 직후 1회 + 이후 주기 스캔).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // 도메인 리로드가 꺼져 있으면 정적 상태가 남으므로 중복 등록을 먼저 해제한다.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Canvas.willRenderCanvases -= Tick;
            Canvas.willRenderCanvases += Tick;

            _nextScanTime = 0f;
            BindAll();
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                         UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            BindAll();
        }

        /// <summary>캔버스 렌더 직전마다 호출되며, 스캔 주기가 지났을 때만 실제로 훑는다.</summary>
        private static void Tick()
        {
            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }
            _nextScanTime = Time.unscaledTime + ScanInterval;
            BindAll();
        }

        /// <summary>씬의 모든 버튼(비활성 포함)에 클릭음을 붙인다(이미 붙었거나 제외된 버튼은 건너뛴다).</summary>
        public static void BindAll()
        {
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                Bind(button);
            }
        }

        /// <summary>버튼 하나에 이름 규칙에 맞는 클릭음을 붙인다(제외 등록된 버튼은 아무것도 하지 않는다).</summary>
        public static void Bind(Button button)
        {
            if (button == null)
            {
                return;
            }

            var tag = button.GetComponent<UiClickSoundTag>();
            if (tag == null)
            {
                tag = button.gameObject.AddComponent<UiClickSoundTag>();
                tag.sound = IsBackButton(button.gameObject.name) ? SoundId.UiClickBack : SoundId.UiClick;
            }
            if (tag.bound)
            {
                return;
            }
            tag.bound = true;

            SoundId sound = tag.sound;
            if (sound == SoundId.None)
            {
                return; // 제외 등록(그 화면이 자기 소리를 직접 낸다)
            }
            button.onClick.AddListener(() => SoundManager.Sfx(sound));
        }

        /// <summary>
        /// 이 버튼을 전역 클릭음에서 제외한다(그 화면이 더 구체적인 소리를 직접 재생하는 경우).
        /// 버튼을 만드는 컨트롤러의 <c>WireRuntime</c>·생성 지점에서 호출하면 스캔이 그 버튼을 건드리지 않는다.
        /// </summary>
        public static void Suppress(Button button)
        {
            if (button == null)
            {
                return;
            }
            var tag = button.GetComponent<UiClickSoundTag>();
            if (tag == null)
            {
                tag = button.gameObject.AddComponent<UiClickSoundTag>();
            }
            tag.sound = SoundId.None;
            tag.bound = true;
        }

        /// <summary>이름에 닫기·뒤로·딤 힌트가 있으면 '뒤로' 계열로 본다.</summary>
        private static bool IsBackButton(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            string lower = name.ToLowerInvariant();
            foreach (var hint in BackNameHints)
            {
                if (lower.Contains(hint))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
