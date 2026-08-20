namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 사운드 식별자. 값은 <c>Assets/Sound</c> 아래 파일명(확장자 제외)과 1:1로 대응하며,
    /// 에디터 빌더(SoundDatabaseBuilder)가 이 이름으로 클립을 찾아 <see cref="SoundDatabase"/>에 채운다.
    /// <b>클라이언트가 배선한 사운드만 정의한다</b> — 아직 쓰지 않는 파일은 배선 시점에 추가한다
    /// (사운드 리소스 정의서 §5·§6의 전투·성장 전용음은 미배선).
    /// </summary>
    public enum SoundId
    {
        None = 0,

        // ── BGM (Assets/Sound/BGM) ──
        BgmTitle,
        BgmCharacterCreate,
        BgmBattleAct1,
        BgmBattleAct2,
        BgmBattleAct3,
        BgmBattleAct4,
        BgmBattleAct5,
        BgmBoss,

        // 연출 징글(루프하지 않음)
        JingleStageClear,
        JingleDefeat,

        // ── SFX/UI ──
        UiClick,
        UiClickBack,
        UiPanelOpen,
        UiPanelClose,
        UiTab,
        UiModalOpen,
        UiModalOk,
        UiModalCancel,
        UiError,
        UiSlotSelect,
        UiTooltip,
        UiNotify,

        // ── SFX/System ──
        TitleLogo,
        TitleStart,
        LoginSuccess,
        LoginFail,
        SignupSuccess,
        SceneTransition,
        CharFocus,
        CharCreate,
        AmbCampfireLoop,

        // ── SFX/Battle/Common ──
        StageEnter,
        BossWarning,
        HitFlesh,
        HitArmor,
        AllyDeath,
        MonsterDeath,
        LevelUp,
        RewardGet,

        // ── SFX/Battle/Knight (사운드 리소스 정의서 §5.2) ──
        KnightBasic,
        KnightShieldChargeStart,
        KnightShieldChargeImpact,
        KnightPowerStrike,
        KnightRage,

        // ── SFX/Battle/Archer (§5.3) ──
        ArcherBowShot,
        ArcherArrowImpact,
        ArcherMultiShot,

        // ── SFX/Battle/Mage (§5.4) ──
        MageFireball,
        MageFrostNova,
        MageLightningBolt,

        // ── SFX/Battle/Slayer (§5.5) ──
        SlayerBasic,
        SlayerLeap,
        SlayerGroundSlam,
        SlayerBerserk,
        SlayerCleave,

        // ── SFX/Battle/Monster (§5.6) ──
        MonAttack,
        BossRoar,

        // ── SFX/Growth (§6) ──
        ItemEquip,
        GoldSpend,
        UpgradeSuccess,
        CubeCombine,
        RewardClaim,
        // 장비 강화 2단 연출(사운드 리소스 정의서 §6) — 망치음은 요청 시작에, 성공음은 망치질이 끝나는 시점에.
        EnhanceHammer,
        EnhanceSuccess,

        // ── SFX/BossRush (사운드 리소스 정의서 §7.5) ──
        // 포탈은 보스러시 라운드 전환에서만 쓰고, 시작·신기록·시간 경고는 이 콘텐츠에만 있는 사건이다.
        PortalOpen,
        PortalTravel,
        BossRushStart,
        NewRecord,
        // 제한 시간이 없어져(도전은 완주·전멸로만 끝난다) 재생 지점이 사라진 소리다. 파일과 매핑은
        // 남겨 두되 호출부는 없다 — 제한 시간이 다시 도입되면 그때 배선한다(사운드 리소스 정의서 §7.5).
        TimeWarning,

        // ── SFX/Gacha (사운드 리소스 정의서 §7) ──
        // 등급 연출음은 3·4·5등급만 둔다 — 노말·고급은 연출 영상이 없어 슬롯 공개음으로 끝난다.
        GachaPullSingle,
        GachaPullMulti,
        GachaSlotReveal,
        GachaGrade3,
        GachaGrade4,
        GachaGrade5,
    }
}
