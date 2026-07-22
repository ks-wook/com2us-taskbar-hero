using UnityEngine;
using TaskbarHero.Client.Managers;
using TaskbarHero.Common.Dto;

namespace TaskbarHero.Client.Game
{
    /// <summary>
    /// GameScene 진입 시 마지막으로 도전했던 스테이지에 입장 요청을 보낸다.
    /// - 세이브에 마지막 스테이지 기록(player.act/difficulty/stage)이 있으면 그 스테이지로,
    ///   없으면(신규) 1-1로 <c>POST /api/game/stage/enter</c>를 호출한다.
    /// - 현재는 입장 "요청"만 수행하고 결과를 로그로 남긴다. 실제 던전(전투) 구성은 추후 구현.
    /// </summary>
    public class StageEntryController : MonoBehaviour
    {
        private bool _requested;

        private void Start()
        {
            RequestEnterLastStage();
        }

        /// <summary>세이브의 마지막 스테이지(없으면 1-1)로 입장 요청을 보낸다.</summary>
        private void RequestEnterLastStage()
        {
            if (_requested)
            {
                return;
            }

            if (NetworkManager.Instance == null)
            {
                Debug.LogWarning("[StageEntry] NetworkManager를 찾을 수 없어 입장 요청을 보내지 못했습니다.");
                return;
            }

            if (!Session.IsLoggedIn)
            {
                Debug.LogWarning("[StageEntry] 로그인 세션이 없어 입장 요청을 건너뜁니다.");
                return;
            }

            // 기본값 1-1. 세이브에 마지막 스테이지 기록이 있으면 그 값을 사용.
            int act = 1;
            int difficulty = 1;
            int stage = 1;

            var player = Session.GameData != null ? Session.GameData.player : null;
            if (player != null && player.act >= 1 && player.stage >= 1)
            {
                act = player.act;
                difficulty = player.difficulty >= 1 ? player.difficulty : 1;
                stage = player.stage;
                Debug.Log($"[StageEntry] 마지막 스테이지 기록 사용: {act}-{difficulty}-{stage}");
            }
            else
            {
                Debug.Log("[StageEntry] 마지막 스테이지 기록 없음 → 1-1로 입장");
            }

            _requested = true;

            var request = new StageActionRequest
            {
                userId = Session.UserId,
                token = Session.Token,
                data = new StageActionData { act = act, difficulty = difficulty, stage = stage },
            };

            Debug.Log($"[StageEntry] 입장 요청 전송: /api/game/stage/enter ({act}-{difficulty}-{stage})");
            NetworkManager.Instance.PostToGame<StageEnterResponse>(
                "/api/game/stage/enter", request, OnEnterSuccess, OnEnterError);
        }

        /// <summary>입장 성공: 진입 데이터를 로그로 확인한다(실제 던전 구성은 추후).</summary>
        private void OnEnterSuccess(StageEnterResponse response)
        {
            var d = response != null ? response.data : null;
            if (d == null)
            {
                Debug.Log("[StageEntry] 입장 완료(응답 데이터 없음).");
                return;
            }

            int monsterKinds = d.monsters != null ? d.monsters.Count : 0;
            int boss = d.boss != null ? d.boss.monsterCode : 0;
            Debug.Log($"[StageEntry] 입장 완료: {d.act}-{d.difficulty}-{d.stage} " +
                      $"(stageId={d.stageId}, 배경={d.backgroundType}, 몬스터종류={monsterKinds}, 보스={boss}). " +
                      "실제 던전 구성은 추후 구현.");
        }

        /// <summary>입장 실패: 오류를 로그로 남긴다.</summary>
        private void OnEnterError(NetworkError error)
        {
            Debug.LogWarning($"[StageEntry] 입장 요청 실패: {error}");
        }
    }
}
