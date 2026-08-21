using TaskbarHero.Common;

namespace AccountServer.Models;

// AuthService가 컨트롤러에 돌려주는 처리 결과 모델.
// 클라이언트로 나가는 응답 DTO는 TaskbarHero.Common.Dto(서버-클라 공유)에 있고,
// 여기 있는 것은 서비스 → 컨트롤러 사이에서만 쓰는 서버 내부 모델이다.

/// <summary>회원가입 처리 결과(에러 코드 + 발급된 user_id).</summary>
public readonly record struct SignupResult(ErrorCode ErrorCode, long UserId);

/// <summary>로그인 처리 결과(에러 코드 + user_id + 발급 토큰).</summary>
public readonly record struct LoginResult(ErrorCode ErrorCode, long UserId, string Token);
