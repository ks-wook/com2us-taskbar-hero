using System;
using TaskbarHero.Common.Dto;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 인증이 끝난 뒤(로그인 또는 자동 로그인) <b>게임 진입까지의 연쇄</b>를 담당한다.
    /// 두 경로가 같은 순서를 타야 하므로 한곳에 모아 두고, 화면 상태(로딩 문구·입력 잠금)는
    /// 호출자가 콜백으로 처리한다.
    ///
    /// <code>
    /// /api/game/load  →  캐릭터 없음? → CreateCharacterScene
    ///                 └  있음 → /api/game/offline/claim → GameScene
    /// </code>
    ///
    /// <para>오프라인 보상은 <b>GameScene 진입 직전</b>에 정산해야 한다 — heartbeat가 시작되기 전에
    /// 정산하지 않으면 경과 시간이 소실된다(오프라인 보상 기획서 §6.2). 정산할 오프라인이 없거나(3001)
    /// 실패해도 게임 진입은 계속한다.</para>
    /// </summary>
    public static class GameEntryFlow
    {
        /// <summary>
        /// 세션(<see cref="Session.UserId"/>·<see cref="Session.Token"/>)이 채워진 상태에서 게임 진입을 시작한다.
        /// </summary>
        /// <param name="onProgress">진행 문구 표시용(로딩 안내). 빈 문자열은 문구 지움을 뜻한다.</param>
        /// <param name="onFailed">진입 실패(세이브 로드 실패·씬 매니저 없음). 호출자가 자기 UI를 복구한다.</param>
        /// <param name="onEnteringScene">씬 전환 직전 호출(입력 잠금 해제 등 정리용).</param>
        public static void Begin(Action<string> onProgress = null,
                                 Action<NetworkError> onFailed = null,
                                 Action onEnteringScene = null)
        {
            if (NetworkManager.Instance == null)
            {
                Debug.LogError("[GameEntry] NetworkManager가 없어 진입을 중단합니다.");
                onFailed?.Invoke(null);
                return;
            }

            onProgress?.Invoke("데이터 로드 중...");
            var request = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<LoadResponse>("/api/game/load", request,
                resp => OnLoadSuccess(resp, onProgress, onFailed, onEnteringScene),
                error =>
                {
                    Debug.LogWarning($"[GameEntry] 게임 데이터 로드 실패: {error}");
                    onFailed?.Invoke(error);
                });
        }

        /// <summary>세이브 스냅샷을 세션에 담고, 캐릭터 유무에 따라 캐릭터 생성 씬 또는 오프라인 정산으로 갈린다.</summary>
        private static void OnLoadSuccess(LoadResponse response, Action<string> onProgress,
                                          Action<NetworkError> onFailed, Action onEnteringScene)
        {
            bool hasCharacter = !response.data.isNew
                                && response.data.characters != null && response.data.characters.Count > 0;
            Debug.Log($"[GameEntry] 게임 데이터 로드 완료. isNew={response.data.isNew}, " +
                      $"캐릭터수={(response.data.characters != null ? response.data.characters.Count : 0)}");

            Session.SetGameData(response.data);

            if (SceneManager.Instance == null)
            {
                Debug.LogError("[GameEntry] SceneManager가 없어 씬 전환을 할 수 없습니다.");
                onFailed?.Invoke(null);
                return;
            }

            // 회원가입 직후 최초 캐릭터 생성 진입은 '뒤로가기' 미노출(게임 안 진입 아님).
            Session.CreateCharacterFromGame = false;
            Session.CreateCharacterReturnScene = "GameScene"; // 생성 후 복귀 지점(편성 씬 진입 흔적 정리)

            // 캐릭터가 없으면(신규 계정) 오프라인 정산 대상이 아니므로 캐릭터 생성 씬으로 전환.
            if (!hasCharacter)
            {
                onEnteringScene?.Invoke();
                onProgress?.Invoke(string.Empty);
                SceneManager.Instance.LoadScene("CreateCharacterScene");
                return;
            }

            onProgress?.Invoke("오프라인 보상 정산 중...");
            var claimRequest = new AuthRequest { userId = Session.UserId, token = Session.Token };
            NetworkManager.Instance.PostToGame<OfflineClaimResponse>("/api/game/offline/claim", claimRequest,
                resp =>
                {
                    if (resp != null && resp.data != null)
                    {
                        Session.ApplyOfflineReward(resp.data);
                        Debug.Log($"[GameEntry] 오프라인 보상 정산 완료: gold=+{resp.data.rewards.gold}, " +
                                  $"exp=+{resp.data.rewards.exp}, effectiveSec={resp.data.effectiveSec}, capped={resp.data.capped}");
                    }
                    EnterGameScene(onProgress, onEnteringScene);
                },
                error =>
                {
                    // 정산할 오프라인 없음(3001)·이미 정산(3002) 등은 정상 흐름이다 — 팝업 없이 진입한다.
                    Debug.Log($"[GameEntry] 오프라인 보상 없음/생략(code={error.ErrorCode}) → 팝업 없이 GameScene 진입");
                    EnterGameScene(onProgress, onEnteringScene);
                });
        }

        /// <summary>GameScene으로 전환한다(정산 성공/실패 공통).</summary>
        private static void EnterGameScene(Action<string> onProgress, Action onEnteringScene)
        {
            onEnteringScene?.Invoke();
            onProgress?.Invoke(string.Empty);
            SceneManager.Instance.LoadScene("GameScene");
        }
    }
}
