using GameServer.Data;
using SqlKata.Execution;
using TaskbarHero.Common.MasterData;

namespace GameServer.MasterData;

/// <summary>
/// 마스터(정적) 데이터 인메모리 캐시. 서버 기동 시 마스터 DB에서 코드→정의 딕셔너리로 적재한다.
/// 세이브 API가 참조하는 마스터는 현재 class_master(직업 코드 검증)뿐이므로 그것만 적재한다.
/// 스테이지/전투·인벤토리 등 후속 기능 착수 시 필요한 테이블을 여기에 추가한다.
/// 로드 실패 시 IsLoaded=false로 두고, 관련 요청은 MasterDataNotLoaded(10001)로 처리한다.
/// </summary>
public sealed class MasterDataProvider
{
    private readonly MasterDbFactory _masterDbFactory;
    private readonly ILogger<MasterDataProvider> _logger;

    private IReadOnlyDictionary<int, ClassMaster> _classes = new Dictionary<int, ClassMaster>();

    public MasterDataProvider(MasterDbFactory masterDbFactory, ILogger<MasterDataProvider> logger)
    {
        _masterDbFactory = masterDbFactory;
        _logger = logger;
    }

    /// <summary>기동 시 1회 적재 성공 여부. false면 관련 요청에 MasterDataNotLoaded를 반환한다.</summary>
    public bool IsLoaded { get; private set; }

    public bool IsValidClass(int classCode) => _classes.ContainsKey(classCode);

    public ClassMaster? GetClass(int classCode)
        => _classes.TryGetValue(classCode, out var c) ? c : null;

    /// <summary>마스터 DB에서 class_master를 읽어 인메모리로 적재한다. 기동 시 1회 호출.</summary>
    public async Task LoadAsync()
    {
        try
        {
            using var db = _masterDbFactory.Create();
            var rows = await db.Query("class_master")
                .Select("class_code", "name", "unlock_type",
                        "hp", "atk", "def", "move_speed", "crit_chance", "crit_damage", "cooldown")
                .GetAsync();

            var classes = new Dictionary<int, ClassMaster>();
            foreach (var row in rows)
            {
                var master = new ClassMaster
                {
                    classCode = Convert.ToInt32(row.class_code),
                    name = (string)row.name,
                    unlockType = Convert.ToInt32(row.unlock_type),
                    baseStats = new Stats
                    {
                        hp = Convert.ToInt64(row.hp),
                        atk = Convert.ToInt64(row.atk),
                        def = Convert.ToInt64(row.def),
                        moveSpeed = Convert.ToSingle(row.move_speed),
                        critChance = Convert.ToSingle(row.crit_chance),
                        critDamage = Convert.ToSingle(row.crit_damage),
                        cooldown = Convert.ToSingle(row.cooldown),
                    },
                };
                classes[master.classCode] = master;
            }

            if (classes.Count == 0)
            {
                throw new InvalidOperationException("class_master가 비어 있습니다.");
            }

            _classes = classes;
            IsLoaded = true;
            _logger.LogInformation("마스터 데이터 적재 완료: class_master {Count}건", classes.Count);
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            _logger.LogError(ex, "마스터 데이터 적재 실패. 관련 요청은 MasterDataNotLoaded(10001)로 처리됩니다.");
        }
    }
}
