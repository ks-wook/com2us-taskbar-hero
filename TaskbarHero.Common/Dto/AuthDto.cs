using System;

namespace TaskbarHero.Common.Dto
{
    // 계정 서버(/api/auth) 요청/응답 DTO. 서버-클라이언트 공유 계약.
    // Unity JsonUtility 호환을 위해 [Serializable] + public camelCase 필드로 정의한다
    // (서버는 System.Text.Json의 IncludeFields=true로 직렬화). 필드명이 곧 JSON 키다.

    /// <summary>회원가입 요청 body(무인증). { email, password, nickname }</summary>
    [Serializable]
    public class SignupRequest
    {
        public string email;
        public string password;
        public string nickname;
    }

    /// <summary>회원가입 응답. { success, errorCode, userId, message }</summary>
    [Serializable]
    public class SignupResponse
    {
        public bool success;
        public int errorCode;
        public long userId;
        public string message;
    }

    /// <summary>로그인 요청 body(무인증). { email, password }</summary>
    [Serializable]
    public class LoginRequest
    {
        public string email;
        public string password;
    }

    /// <summary>로그인 응답. { success, errorCode, userId, token, message }</summary>
    [Serializable]
    public class LoginResponse
    {
        public bool success;
        public int errorCode;
        public long userId;
        public string token;
        public string message;
    }

    /// <summary>로그아웃 요청 body(인증). { userId, token }</summary>
    [Serializable]
    public class LogoutRequest
    {
        public long userId;
        public string token;
    }

    /// <summary>로그아웃 응답. { success, errorCode, message }</summary>
    [Serializable]
    public class LogoutResponse
    {
        public bool success;
        public int errorCode;
        public string message;
    }

    /// <summary>
    /// 자동 로그인 검증 요청 body(인증). { userId, token }
    /// 클라이언트가 저장해 둔 직전 세션을 타이틀 화면에서 그대로 쓸 수 있는지 확인한다.
    /// </summary>
    [Serializable]
    public class ValidateTokenRequest
    {
        public long userId;
        public string token;
    }

    /// <summary>
    /// 자동 로그인 검증 응답. { success, errorCode, userId, message }
    /// 토큰을 재발급하지 않으므로 token 필드가 없다 — 유효하면 클라이언트가 가진 토큰을 계속 쓴다.
    /// </summary>
    [Serializable]
    public class ValidateTokenResponse
    {
        public bool success;
        public int errorCode;
        public long userId;
        public string message;
    }
}
