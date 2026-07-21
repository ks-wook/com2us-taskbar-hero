using System.Collections.Generic;
using UnityEngine;
using TaskbarHero.Client.MasterData;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 빌드에 번들된 마스터 데이터(Assets/Resources/MasterData/*.json)를 읽어
    /// <see cref="MasterDatabase"/> 로 파싱·캐싱해 앱 전역에 제공하는 정적 매니저.
    ///
    /// 마스터 데이터는 정적·읽기 전용·전 유저 공통이므로(기획서 §2) 씬 오브젝트 없이
    /// 정적 클래스로 1회 로드해 유지한다(<see cref="Session"/> 과 동일 패턴, 도메인 리로드 시 초기화).
    /// 런타임 다운로드·버전 협상은 없다(기획서 §8).
    /// </summary>
    public static class MasterDataManager
    {
        /// <summary>Resources 하위 마스터 데이터 폴더 경로(확장자·리소스 접두어 제외).</summary>
        private const string ResourceFolder = "MasterData";

        private static MasterDatabase _db;

        /// <summary>마스터 데이터가 로드되어 캐싱된 상태인지.</summary>
        public static bool IsLoaded => _db != null;

        /// <summary>
        /// 캐싱된 마스터 데이터베이스. 최초 접근 시 자동 로드한다.
        /// (명시적으로 먼저 로드하려면 <see cref="Load"/> 사용)
        /// </summary>
        public static MasterDatabase Db
        {
            get
            {
                if (_db == null)
                {
                    Load();
                }

                return _db;
            }
        }

        /// <summary>
        /// Resources/MasterData/*.json 을 읽어 캐시를 (재)구성한다.
        /// 이미 로드돼 있어도 강제로 다시 읽는다. 로드된 DB 를 반환한다.
        /// </summary>
        public static MasterDatabase Load()
        {
            var texts = new Dictionary<string, string>();
            var missing = new List<string>();

            foreach (string table in MasterDatabase.AllTables)
            {
                // Resources.Load 는 확장자를 뺀 경로를 쓴다: "MasterData/class_master"
                var asset = Resources.Load<TextAsset>(ResourceFolder + "/" + table);
                if (asset == null)
                {
                    missing.Add(table);
                    texts[table] = null;
                }
                else
                {
                    texts[table] = asset.text;
                }
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning(
                    "[MasterData] 번들 JSON 을 찾지 못한 테이블: " + string.Join(", ", missing) +
                    "\n(Unity 에디터의 TaskbarHero/마스터 데이터/최신화 로 재생성하세요.)");
            }

            var db = new MasterDatabase();
            db.Load(table => texts.TryGetValue(table, out var json) ? json : null);
            _db = db;

            Debug.Log($"[MasterData] 로드 완료: {MasterDatabase.AllTables.Length}개 테이블 / 총 {db.TotalRows}행 캐싱");
            return db;
        }

        /// <summary>캐시가 없으면 로드한다(이미 있으면 아무 것도 하지 않음).</summary>
        public static void EnsureLoaded()
        {
            if (_db == null)
            {
                Load();
            }
        }

        /// <summary>캐시를 비운다.</summary>
        public static void Unload()
        {
            _db?.Clear();
            _db = null;
        }
    }
}
