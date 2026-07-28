namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 사운드 식별자. 값은 <c>Assets/Sound</c> 아래 파일명(확장자 제외)과 1:1로 대응하며,
    /// 에디터 빌더(SoundDatabaseBuilder)가 이 이름으로 클립을 찾아 <see cref="SoundDatabase"/>에 채운다.
    /// <b>현재 생성된 36종만 정의한다</b> — 미생성분(사운드 리소스 정의서 §5·§6의 20종)은 파일이 만들어질 때 추가한다.
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
    }
}
