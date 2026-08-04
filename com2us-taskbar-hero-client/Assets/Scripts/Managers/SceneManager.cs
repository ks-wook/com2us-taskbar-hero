using System;
using System.Collections;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 씬 전환(동기·비동기 로드, 재로드)을 담당하는 매니저.
    /// UnityEngine.SceneManagement.SceneManager를 감싸 프로젝트의 씬 전환 흐름을 한곳에서 관리한다.
    /// (클래스명이 Unity의 SceneManager와 겹치므로 원본은 USceneManager 별칭으로 참조한다.)
    /// </summary>
    public class SceneManager : MonoBehaviour
    {
        public static SceneManager Instance { get; private set; }

        /// <summary>비동기 씬 로드 진행률(0~1) 변경 콜백.</summary>
        public event Action<float> LoadProgressChanged;

        /// <summary>씬 로드 완료 콜백. 로드된 씬 이름을 전달한다.</summary>
        public event Action<string> SceneLoaded;

        /// <summary>현재 비동기 씬 로드가 진행 중인지 여부.</summary>
        public bool IsLoading { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>지정한 씬을 동기 방식으로 즉시 로드한다.</summary>
        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneManager] 씬 이름이 비어 있습니다.", this);
                return;
            }

            // 씬 전환음(사운드 정의서 §4.2). 로드 직전에 울려야 소리가 끊기지 않는다
            // (SoundManager는 DontDestroyOnLoad라 씬이 바뀌어도 재생이 이어진다).
            SoundManager.Sfx(SoundId.SceneTransition);
            USceneManager.LoadScene(sceneName);
            SceneLoaded?.Invoke(sceneName);
        }

        /// <summary>지정한 씬을 비동기 방식으로 로드한다. 진행률은 <see cref="LoadProgressChanged"/>로 전달된다.</summary>
        public void LoadSceneAsync(string sceneName)
        {
            if (IsLoading)
            {
                Debug.LogWarning("[SceneManager] 이미 씬 로드가 진행 중입니다.", this);
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneManager] 씬 이름이 비어 있습니다.", this);
                return;
            }

            StartCoroutine(LoadSceneRoutine(sceneName));
        }

        /// <summary>현재 활성 씬을 다시 로드한다.</summary>
        public void ReloadCurrentScene()
        {
            LoadScene(USceneManager.GetActiveScene().name);
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            IsLoading = true;
            SoundManager.Sfx(SoundId.SceneTransition); // 비동기 로드도 같은 전환음으로 시작한다(§4.2)
            LoadProgressChanged?.Invoke(0f);

            var operation = USceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                Debug.LogError(
                    $"[SceneManager] '{sceneName}' 씬을 로드할 수 없습니다. Build Settings에 추가됐는지 확인하세요.",
                    this);
                IsLoading = false;
                yield break;
            }

            while (!operation.isDone)
            {
                LoadProgressChanged?.Invoke(operation.progress);
                yield return null;
            }

            LoadProgressChanged?.Invoke(1f);
            IsLoading = false;
            SceneLoaded?.Invoke(sceneName);
        }
    }
}
