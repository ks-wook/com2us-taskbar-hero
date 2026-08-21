namespace GameServer.Logging;

/// <summary>
/// 이벤트 로그 태그 상수([로그 이벤트 정의](../../docs/공통/로그-이벤트-정의.md) 4.3).
/// 태그가 곧 적재 테이블(<c>도메인.동작</c> → <c>도메인_동작_logs</c>)이므로,
/// <b>메서드 이름에서 자동으로 만들지 않고 여기서 상수로 명시</b>한다 — 리팩터링이 적재 대상을 바꾸는
/// 결합을 만들지 않기 위해서다(4.2).
/// </summary>
public static class EventLogTags
{
    // ── 5.2 세션 / 캐릭터 ──

    /// <summary>접속 시 세이브 로드 → <c>save_load_logs</c>. 이 체계의 세션 개시 이벤트(DAU·리텐션의 근거).</summary>
    public const string SaveLoad = "save.load";

    /// <summary>최초 캐릭터 생성(계정 세이브 초기화) → <c>player_create_logs</c>.</summary>
    public const string PlayerCreate = "player.create";
}
