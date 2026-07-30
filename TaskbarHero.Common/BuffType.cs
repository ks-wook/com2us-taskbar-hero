namespace TaskbarHero.Common
{
    // 계정 획득량 버프 종류(소모품 사용으로 부여). consumable_master.buff_type·player_buff.buff_type과 같은 값이다.
    // 숫자 값은 클라이언트와 공유하는 계약이므로 변경 금지(신규 값 추가는 허용).
    public enum BuffType
    {
        ExpGain = 1,   // 경험치 획득량
        GoldGain = 2,  // 골드 획득량
    }
}
