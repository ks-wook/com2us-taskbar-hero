using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.Client.EditorTools
{
    /// <summary>
    /// <c>Assets/Resources/MasterData/*.json</c>이 바뀌면 <see cref="MasterDataManager"/>의 캐시를 즉시 무효화한다.
    ///
    /// <para><b>필요한 이유</b>: 마스터 데이터는 정적 캐시(<c>MasterDataManager._db</c>)에 1회만 로드된다.
    /// 이 프로젝트는 Enter Play Mode Options의 <c>DisableDomainReload</c>가 켜져 있어 플레이를 껐다 켜도
    /// 정적 필드가 살아남으므로, JSON을 최신화해도 <b>에디터를 재시작하기 전까지 인게임 수치가 그대로였다</b>
    /// (2026-07-31 몬스터 HP 조정이 반영되지 않은 원인). 플레이 시작 시점은
    /// <c>MasterDataManager.ResetCacheOnPlay</c>가 막고, <b>편집 중·플레이 중의 JSON 변경</b>은 이 후처리기가 막는다.</para>
    ///
    /// <para><b>한계</b>: 캐시만 버리므로 다음 <c>Db</c> 접근에서 최신 값으로 다시 읽힌다. 다만 진행 중인 전투처럼
    /// 시작 시점에 마스터 값을 이미 복사해 둔 곳(<c>BattleDevController._monsterMaxHp</c> 등)은 그대로다 —
    /// 바뀐 수치를 전투에 보려면 스테이지를 다시 입장하거나 플레이를 재시작해야 한다.</para>
    /// </summary>
    public sealed class MasterDataChangeWatcher : AssetPostprocessor
    {
        private const string WatchFolder = "Assets/Resources/MasterData/";

        /// <summary>임포트/삭제/이동된 에셋 중 마스터 데이터 JSON이 있으면 캐시를 버린다.</summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!Touches(importedAssets) && !Touches(deletedAssets) && !Touches(movedAssets))
            {
                return;
            }

            MasterDataManager.Unload();
            Debug.Log("[MasterData] JSON 변경 감지 → 캐시 무효화(다음 조회에서 최신 값으로 재로드). " +
                      "진행 중인 전투에 반영하려면 스테이지를 다시 입장하거나 플레이를 재시작할 것.");
        }

        /// <summary>경로 목록에 마스터 데이터 폴더의 JSON이 포함돼 있는지.</summary>
        private static bool Touches(string[] paths)
        {
            if (paths == null)
            {
                return false;
            }
            foreach (var path in paths)
            {
                if (path != null
                    && path.StartsWith(WatchFolder, System.StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
