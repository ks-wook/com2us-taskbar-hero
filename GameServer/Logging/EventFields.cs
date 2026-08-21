namespace GameServer.Logging;

/// <summary>
/// 이벤트 고유 필드를 담는 타입의 표식([로그 이벤트 정의](../../docs/공통/로그-이벤트-정의.md) 4.2).
/// <b>이벤트마다 전용 record</b>를 두어 컬럼과 1:1로 맞춘다 — 익명 객체를 쓰면 컬럼이 조용히 어긋난다.
/// <para>프로퍼티 이름은 <c>PascalCase</c>로 쓰고 직렬화 시 <c>snake_case</c>로 바뀐다. 그 이름이 곧
/// 적재 테이블의 컬럼 이름이므로, 필드를 바꾸면 정의 문서(5~7장)·테이블 DDL·fluentd 매핑을 함께 고친다(9.4).</para>
/// </summary>
public interface IEventFields
{
}

// ── 5.2 세션 / 캐릭터 ──

/// <summary>
/// <c>save.load</c> — 접속 시 세이브 로드. <b>이 체계에서 세션 개시를 뜻하는 유일한 이벤트</b>라
/// DAU·재방문율·시간대별 접속 분포가 전부 여기서 나온다(계정 로그를 남기지 않으므로, 5.1).
/// </summary>
/// <param name="IsNew">세이브가 없는 신규 계정인지(가입 → 실제 플레이 전환율의 분모).</param>
/// <param name="OfflineElapsedSec">직전 활동 이후 경과 초. 이탈 후 복귀 간격 분포의 입력이다. 신규는 0.</param>
public sealed record SaveLoadEvent(bool IsNew, long OfflineElapsedSec) : IEventFields;

/// <summary>
/// <c>player.create</c> — <b>최초</b> 캐릭터 생성(계정 세이브 초기화). 두 번째 이후의 캐릭터 추가는
/// 남기지 않는다 — 이 이벤트가 답하는 질문이 <b>첫 직업 선호</b>와 <b>가입 → 플레이 전환</b>이기 때문이다
/// (지금 보유한 직업은 스냅샷이 답하므로 첫 선택만 액션 로그로 남긴다).
/// </summary>
/// <param name="ClassCode">선택한 직업(class_master).</param>
/// <param name="Gender">선택한 성별(1:남 2:여). 외형 전용이라 스탯과 무관하다.</param>
public sealed record PlayerCreateEvent(int ClassCode, int Gender) : IEventFields;
