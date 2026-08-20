using System.Collections.Generic;
using System.Linq;
using TaskbarHero.Client.Battle;   // MonsterSwingFxPalette — 스윙 이펙트 색 프리셋
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// <see cref="CharacterDevController"/>의 하네스 UI(기획서 §5 "하네스 UI 구성").
/// 화면을 세 갈래로 나눈다 — 좌: 몬스터 목록 / 중앙: 미리보기·애니메이션 검수 / 우: 능력치·외형 패널.
/// <para>로직부와 같은 클래스의 partial이며, UI를 만드는 코드가 길어 파일만 분리했다.</para>
/// </summary>
public partial class CharacterDevController
{
    private static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.11f, 0.92f);
    private static readonly Color RowNormal = new Color(0.18f, 0.20f, 0.26f, 0.95f);
    private static readonly Color RowSelected = new Color(0.24f, 0.45f, 0.72f, 1f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.34f, 1f);
    private static readonly Color ButtonPrimary = new Color(0.20f, 0.42f, 0.30f, 1f);
    private static readonly Color TitleColor = new Color(1f, 0.87f, 0.45f);
    private static readonly Color HintColor = new Color(0.75f, 0.78f, 0.85f);
    private static readonly Color WarnColor = new Color(0.98f, 0.80f, 0.30f);

    /// <summary>계열(race)별 이름 후보 — 자동 생성이 아니라 고르면 입력란을 채우는 목록이다(§4.3).</summary>
    private static readonly Dictionary<string, string[]> NameCandidates = new Dictionary<string, string[]>
    {
        { "undead", new[] { "스켈레톤 병사", "스켈레톤 궁수", "구울", "리치", "본나이트" } },
        { "devil", new[] { "임프", "데몬", "마계 기사", "서큐버스", "발록" } },
        { "human", new[] { "타락한 병사", "광신도", "도적", "흑마법사", "배교자" } },
        { "orc", new[] { "오크 전사", "오크 주술사", "오우거" } },
        { "elf", new[] { "숲의 파수꾼", "다크엘프 궁수" } },
        { "highelf", new[] { "타락한 대천사", "빛의 배신자" } },
    };

    /// <summary>좌측 패널이 지금 무엇을 보여 주는지 — 몬스터 목록 / 파츠 고르기(커스텀 외형).</summary>
    private enum LeftTab
    {
        Monsters,
        Parts,
    }

    /// <summary>커스텀 외형에서 고를 수 있는 파트(조합 순서와 같다).</summary>
    private static readonly string[] SelectableParts =
    {
        "Body", "Eye", "Hair", "FaceHair", "Cloth", "Pant", "Armor", "Helmet", "Weapons", "Back",
    };

    private LeftTab _leftTab = LeftTab.Monsters;
    private string _selectedPart = "Body";
    private readonly List<(LeftTab Tab, Image Bg)> _tabRows = new List<(LeftTab, Image)>();

    private GameObject _canvasGo;
    private Text _statusText;
    private Text _logText;
    private RectTransform _listContent;
    private InputField _nameInput;
    private InputField _hpInput;
    private InputField _atkInput;
    private Text _levelText;
    private Text _baseNoteText;
    private Text _codeText;
    private Text _recipeText;
    private Text _appearanceText;
    private Text _batchTargetText;
    private RectTransform _nameCandidateRow;

    private readonly List<Button> _gatedButtons = new List<Button>();   // SPUM 준비 전에는 잠그는 버튼들
    private readonly List<(int Code, Image Bg)> _listRows = new List<(int, Image)>();
    private readonly List<(int Value, Image Bg)> _actRows = new List<(int, Image)>();
    private readonly List<(int Value, Image Bg)> _stageRows = new List<(int, Image)>();
    // 약몹/강몹 토글(Act1·2만 의미가 있다 — 강몹은 +1 레벨). Value: false = 약몹, true = 강몹
    private readonly List<(bool Value, Image Bg)> _strongRows = new List<(bool, Image)>();
    // 무기 스윙 이펙트 색 프리셋 버튼(Value: -1 = 기본색, 1~5 = 지역 색)
    private readonly List<(int Value, Image Bg)> _effectColorRows = new List<(int, Image)>();
    private Image _effectColorSwatch;   // 현재 색 견본 막대
    private Text _effectColorText;

    // ══════════════════════════════════════════════════════════════════
    //  구성
    // ══════════════════════════════════════════════════════════════════

    /// <summary>하네스 UI를 런타임에 만든다(개발 하네스라 픽셀 고정 스케일로 둔다).</summary>
    private void BuildUi()
    {
        if (_canvasGo != null)
        {
            Destroy(_canvasGo);
        }
        _gatedButtons.Clear();
        _listRows.Clear();
        _actRows.Clear();
        _stageRows.Clear();
        _effectColorRows.Clear();
        _tabRows.Clear();

        _canvasGo = new GameObject("CharacterDevCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // SPUM 캔버스보다 위(§5)
        _canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        BuildLeftPanel(_canvasGo.transform);
        BuildRightPanel(_canvasGo.transform);
        BuildCenterBar(_canvasGo.transform);

        RefreshList();
        RefreshRightPanel();
        RefreshPartPanel();
        RefreshUi();
    }

    /// <summary>좌: 몬스터 목록 + 일괄 생성·배선 버튼(§5).</summary>
    private void BuildLeftPanel(Transform parent)
    {
        var panel = NewUi("LeftPanel", parent, out Image bg);
        bg.color = PanelBg;
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.offsetMin = new Vector2(8f, 8f);
        panel.offsetMax = new Vector2(408f, -8f);

        MakeTopLabel(panel, "Title", "CharacterDev", 17, FontStyle.Bold, TitleColor, -6f, 24f);
        _statusText = MakeTopLabel(panel, "Status", "초기화 중…", 12, FontStyle.Normal, HintColor, -30f, 34f);

        // 탭 — 몬스터 목록 / 파츠 고르기(커스텀 외형).
        var tabRow = NewUi("Tabs", panel, out Image tabBg);
        tabBg.color = new Color(0f, 0f, 0f, 0f);
        tabRow.anchorMin = new Vector2(0f, 1f);
        tabRow.anchorMax = new Vector2(1f, 1f);
        tabRow.pivot = new Vector2(0.5f, 1f);
        tabRow.offsetMin = new Vector2(6f, -26f);
        tabRow.offsetMax = new Vector2(-6f, 0f);
        tabRow.anchoredPosition = new Vector2(0f, -64f);
        var tabLayout = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 4f;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = true;

        _tabRows.Add((LeftTab.Monsters, MakeChoice(tabRow, "몬스터 목록", () => SelectTab(LeftTab.Monsters))));
        _tabRows.Add((LeftTab.Parts, MakeChoice(tabRow, "파츠 고르기", () => SelectTab(LeftTab.Parts))));

        // 목록(스크롤) — 탭에 따라 몬스터 목록 또는 파츠 목록을 그린다.
        var scrollRt = NewUi("Scroll", panel, out Image scrollBg);
        scrollBg.color = new Color(0f, 0f, 0f, 0.25f);
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(6f, 108f);
        scrollRt.offsetMax = new Vector2(-6f, -94f);
        _listContent = MakeScrollContent(scrollRt);

        // 하단 버튼 묶음.
        var bottom = NewUi("Bottom", panel, out Image bottomBg);
        bottomBg.color = new Color(0f, 0f, 0f, 0f);
        bottom.anchorMin = new Vector2(0f, 0f);
        bottom.anchorMax = new Vector2(1f, 0f);
        bottom.pivot = new Vector2(0.5f, 0f);
        bottom.offsetMin = new Vector2(6f, 6f);
        bottom.offsetMax = new Vector2(-6f, 100f);
        var layout = bottom.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        _batchTargetText = MakeText(bottom, "대상: 프리팹 없는 코드만", 12, FontStyle.Normal, HintColor, 18f);
        MakeButton(bottom, "대상 전환 (없는 코드만 ↔ 전체)", () =>
        {
            _batchOnlyMissing = !_batchOnlyMissing;
            RefreshUi();
        }, gated: false);
        MakeButton(bottom, "일괄 생성 (외형만)", () =>
        {
            if (!_busy)
            {
                StartCoroutine(BatchGenerate());
            }
        });
        MakeButton(bottom, "던전 전투 배선 실행 (플레이 종료 후)", RequestDungeonWiring, gated: false);
    }

    /// <summary>우: 능력치(위) · 외형(아래) 패널과 산출 버튼(§5).</summary>
    private void BuildRightPanel(Transform parent)
    {
        var panel = NewUi("RightPanel", parent, out Image bg);
        bg.color = PanelBg;
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.offsetMin = new Vector2(-420f, 8f);
        panel.offsetMax = new Vector2(-8f, -8f);

        var scrollRt = NewUi("Scroll", panel, out Image scrollBg);
        scrollBg.color = new Color(0f, 0f, 0f, 0f);
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(6f, 6f);
        scrollRt.offsetMax = new Vector2(-6f, -6f);
        var content = MakeScrollContent(scrollRt);

        // ── 능력치 ──
        MakeText(content, "▍능력치", 15, FontStyle.Bold, TitleColor, 24f);
        MakeText(content, "지역(Act)", 12, FontStyle.Normal, HintColor, 18f);
        var actRow = MakeRow(content, 26f);
        for (int act = 1; act <= MonsterStatCurve.ActCount; act++)
        {
            int captured = act;
            var image = MakeChoice(actRow, "Act " + act, () =>
            {
                _act = captured;
                RefreshRightPanel();
            });
            _actRows.Add((act, image));
        }

        MakeText(content, "스테이지 (10 = 보스)", 12, FontStyle.Normal, HintColor, 18f);
        var stageRowA = MakeRow(content, 26f);
        var stageRowB = MakeRow(content, 26f);
        for (int stage = 1; stage <= MonsterStatCurve.StagePerAct; stage++)
        {
            int captured = stage;
            var target = stage <= 5 ? stageRowA : stageRowB;
            var image = MakeChoice(target, stage.ToString(), () =>
            {
                _stage = captured;
                RefreshRightPanel();
            });
            _stageRows.Add((stage, image));
        }

        MakeText(content, "약몹 / 강몹 (Act1·2만 — 강몹은 +1 레벨)", 12, FontStyle.Normal, HintColor, 18f);
        var strongRow = MakeRow(content, 26f);
        foreach (bool strong in new[] { false, true })
        {
            bool captured = strong;
            var image = MakeChoice(strongRow, strong ? "강몹 (+1)" : "약몹", () =>
            {
                _strong = captured;
                RefreshRightPanel();
            });
            _strongRows.Add((strong, image));
        }

        _levelText = MakeText(content, "등장 레벨 추천: -", 12, FontStyle.Bold, WarnColor, 32f);

        MakeButton(content, "통일 기준값 채우기 (hp 50 / atk 5·1)", FillBaseStats);

        _nameInput = MakeInput(content, "이름", string.Empty, InputField.ContentType.Standard);
        MakeText(content, "이름 후보(계열 기준 — 고르면 채워집니다)", 11, FontStyle.Normal, HintColor, 16f);
        _nameCandidateRow = MakeRow(content, 24f);

        _hpInput = MakeInput(content, "hp", string.Empty, InputField.ContentType.IntegerNumber);
        _atkInput = MakeInput(content, "attack", string.Empty, InputField.ContentType.IntegerNumber);
        _hpInput.onValueChanged.AddListener(_ => RefreshRecommendation());
        _atkInput.onValueChanged.AddListener(_ => RefreshRecommendation());
        _baseNoteText = MakeText(content, "기준값 대비: -", 12, FontStyle.Normal, HintColor, 18f);

        _codeText = MakeText(content, "제안 코드: -", 12, FontStyle.Bold, Color.white, 18f);
        MakeButton(content, "제안 코드로 신규 작업", () =>
        {
            int suggested = SuggestedCode();
            if (suggested == 0)
            {
                Log("이 대역에 비어 있는 코드가 없습니다.");
                return;
            }
            if (UsedCodes().Contains(suggested))
            {
                // 보스 대역처럼 이미 쓰는 코드면 채번을 건너뛰고 경고한다(§4.4 · §6.4).
                Log($"코드 {suggested}는 이미 사용 중입니다 — 덮어쓰려면 목록에서 그 몬스터를 선택하세요.");
                return;
            }
            _selectedCode = suggested;
            _nameInput.text = string.Empty;
            FillBaseStats();
            RefreshList();
            RefreshRightPanel();
            Log($"신규 코드 {suggested}로 작업을 시작합니다.");
        }, gated: false);

        // ── 외형 ──
        MakeText(content, "▍외형", 15, FontStyle.Bold, TitleColor, 26f);
        _recipeText = MakeText(content, "레시피: -", 12, FontStyle.Normal, HintColor, 40f);
        MakeButton(content, "외형 재생성 (레시피로 조합)", () =>
        {
            if (_selectedCode == 0)
            {
                Log("먼저 몬스터를 고르세요.");
                return;
            }
            if (GeneratePreview(_selectedCode, out string error))
            {
                Log($"미리보기 생성 — monster_{_selectedCode}");
            }
            else
            {
                Log($"생성 실패 — {error}");
            }
        });

        MakeButton(content, "저장된 프리팹 불러오기", () =>
        {
            if (_selectedCode == 0)
            {
                Log("먼저 몬스터를 고르세요.");
                return;
            }
            if (LoadFromPrefab(_selectedCode, out string error))
            {
                Log($"불러오기 — monster_{_selectedCode}.prefab의 외형을 프리뷰에 올렸습니다.");
            }
            else
            {
                Log($"불러오기 실패 — {error}");
            }
        });

        _appearanceText = MakeText(content, "현재 외형: -", 11, FontStyle.Normal, HintColor, 52f);
        MakeText(content, "파트별로 바꾸려면 왼쪽 [파츠 고르기] 탭을 쓰세요.", 11, FontStyle.Normal, HintColor, 18f);

        // ── 무기 스윙 이펙트 색 ──
        // 몬스터가 무기를 휘두를 때 나오는 궤적·불티의 기준색이다(MonsterSwingFxPalette).
        // 지역 색 프리셋으로 고르며, 보스는 그 지역 컬러링을 쓰는 것이 기본이다.
        MakeText(content, "▍무기 스윙 이펙트 색", 15, FontStyle.Bold, TitleColor, 26f);
        MakeText(content, "휘두를 때 나오는 궤적·불티의 색입니다(무기가 있는 몬스터만).",
            11, FontStyle.Normal, HintColor, 16f);
        var fxRowA = MakeRow(content, 26f);
        var fxRowB = MakeRow(content, 26f);
        _effectColorRows.Add((-1, MakeChoice(fxRowA, "기본(불티)", () => SetEffectColor(null))));
        for (int act = 1; act <= 5; act++)
        {
            int captured = act;
            var row = act <= 2 ? fxRowA : fxRowB;
            var image = MakeChoice(row, $"{act}지역 {MonsterSwingFxPalette.ActColorName(act)}",
                () => SetEffectColor(MonsterSwingFxPalette.ActColor(captured)));
            _effectColorRows.Add((captured, image));
        }
        _effectColorSwatch = MakeSwatch(content, 16f);
        _effectColorText = MakeText(content, "현재 색: -", 11, FontStyle.Normal, HintColor, 18f);
        MakeText(content, "저장하면 프리팹에 기록됩니다. 레시피 JSON의 effectColor가 초기값입니다.",
            11, FontStyle.Normal, HintColor, 18f);

        // ── 산출 ──
        MakeText(content, "▍산출", 15, FontStyle.Bold, TitleColor, 26f);
        MakeButton(content, "프리팹 저장", () =>
        {
            if (_selectedCode == 0)
            {
                Log("먼저 몬스터를 고르세요.");
                return;
            }
            if (SavePrefab(_selectedCode, out string error))
            {
                Log($"저장 완료 — monster_{_selectedCode}.prefab (※ '던전 전투 배선' 재실행 필요)");
                RefreshRows();
                RefreshList();
            }
            else
            {
                Log($"저장 실패 — {error}");
            }
        }, primary: true);

        MakeButton(content, "monster_master 반영", () =>
        {
            if (!ReadStatInputs(out long hp, out long attack))
            {
                Log("hp·attack은 1 이상의 정수여야 합니다.");
                return;
            }
            if (ApplyStatsToMaster(_selectedCode, _nameInput.text, hp, attack, out string error))
            {
                Log($"monster_master 반영 — {_selectedCode} {_nameInput.text} hp {hp} / atk {attack}");
                if (!MonsterStatCurve.MatchesBase(hp, attack, IsBossSelection()))
                {
                    Log($"※ 통일 기준값({MonsterStatCurve.BaseHp}/{MonsterStatCurve.BaseAttackOf(IsBossSelection())})과 다릅니다 — "
                        + "세기는 monster_master가 아니라 stage_spawn.monster_level이 만든다(값 문서 §9.4).");
                }
                Log("※ 서버 정본 반영은 별도 작업이다(§7.4) — [스니펫 복사]로 넘길 것.");
                RefreshList();
                RefreshRecommendation();
            }
            else
            {
                Log($"반영 실패 — {error}");
            }
        }, primary: true, gated: false);

        MakeButton(content, "스니펫 복사 (서버 정본용)", () =>
        {
            if (!ReadStatInputs(out long hp, out long attack))
            {
                Log("hp·attack을 확인하세요.");
                return;
            }
            string snippet = BuildSnippet(_selectedCode, _nameInput.text, hp, attack,
                                          _act, _stage, RecommendedLevel());
            GUIUtility.systemCopyBuffer = snippet;
            Log("스니펫을 클립보드에 복사했습니다:");
            foreach (var line in snippet.Split('\n'))
            {
                Log("  " + line);
            }
        }, gated: false);

        MakeButton(content, "전투로 검수 (BattleDevScene)", () =>
        {
            if (_selectedCode == 0)
            {
                Log("먼저 몬스터를 고르세요.");
                return;
            }
            OpenBattleForReview(_selectedCode);
        }, gated: false);
    }

    /// <summary>중앙 하단: 애니메이션 검수 버튼(§7.2)과 하네스 로그.</summary>
    private void BuildCenterBar(Transform parent)
    {
        var bar = NewUi("CenterBar", parent, out Image bg);
        bg.color = new Color(0f, 0f, 0f, 0f);
        bar.anchorMin = new Vector2(0.5f, 0f);
        bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(520f, 190f);
        bar.anchoredPosition = new Vector2(0f, 8f);

        var layout = bar.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var logBg = NewUi("Log", bar, out Image logImage);
        logImage.color = new Color(0f, 0f, 0f, 0.55f);
        logBg.gameObject.AddComponent<LayoutElement>().minHeight = 150f;
        _logText = MakeStretchedText(logBg, "", 12, FontStyle.Normal, new Color(0.85f, 0.88f, 0.95f));

        var animRow = MakeRow(bar, 26f);
        MakeChoice(animRow, "IDLE", () => PlayPreviewAnimation(PlayerState.IDLE));
        MakeChoice(animRow, "MOVE", () => PlayPreviewAnimation(PlayerState.MOVE));
        MakeChoice(animRow, "ATTACK", () => PlayPreviewAnimation(PlayerState.ATTACK));
        MakeChoice(animRow, "DAMAGED", () => PlayPreviewAnimation(PlayerState.DAMAGED));
        MakeChoice(animRow, "DEATH", () => PlayPreviewAnimation(PlayerState.DEATH));
    }

    // ══════════════════════════════════════════════════════════════════
    //  갱신
    // ══════════════════════════════════════════════════════════════════

    /// <summary>좌측 탭을 바꾼다.</summary>
    private void SelectTab(LeftTab tab)
    {
        _leftTab = tab;
        foreach (var (value, bg) in _tabRows)
        {
            bg.color = value == tab ? RowSelected : ButtonColor;
        }
        RefreshList();
    }

    /// <summary>좌측 목록을 현재 탭에 맞게 다시 그린다.</summary>
    private void RefreshList()
    {
        if (_listContent == null)
        {
            return;
        }

        _listRows.Clear();
        for (int i = _listContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_listContent.GetChild(i).gameObject);
        }

        foreach (var (value, bg) in _tabRows)
        {
            bg.color = value == _leftTab ? RowSelected : ButtonColor;
        }

        if (_leftTab == LeftTab.Parts)
        {
            BuildPartList();
        }
        else
        {
            BuildMonsterList();
        }
    }

    /// <summary>외형이 바뀌면(조합·불러오기·파트 교체) 파츠 목록과 요약 라벨을 다시 칠한다.</summary>
    private void RefreshPartPanel()
    {
        if (_appearanceText != null)
        {
            _appearanceText.text = _partSelection.Count == 0
                ? "현재 외형: (비어 있음 — 조합하거나 프리팹을 불러오세요)"
                : "현재 외형: " + string.Join(" / ",
                    SelectableParts.Where(_partSelection.ContainsKey)
                                   .Select(p => $"{p} {_partSelection[p]}"));
        }

        if (_leftTab == LeftTab.Parts)
        {
            RefreshList();
        }
    }

    /// <summary>
    /// 파츠 고르기 목록 — 위에 파트 버튼, 아래에 그 파트에서 고를 수 있는 파츠(설치된 것만).
    /// 지금 입고 있는 항목은 파란색으로 표시하고, 클릭하면 그 파트만 바뀐다.
    /// </summary>
    private void BuildPartList()
    {
        var partRowA = MakeRow(_listContent, 24f);
        var partRowB = MakeRow(_listContent, 24f);
        for (int i = 0; i < SelectableParts.Length; i++)
        {
            string part = SelectableParts[i];
            var target = i < 5 ? partRowA : partRowB;
            var bg = MakeChoice(target, part, () =>
            {
                _selectedPart = part;
                RefreshList();
            });
            bg.color = string.Equals(part, _selectedPart, System.StringComparison.OrdinalIgnoreCase)
                ? RowSelected
                : ButtonColor;
        }

        string current = _partSelection.TryGetValue(_selectedPart, out string value) ? value : "(없음)";
        MakeText(_listContent, $"▍{_selectedPart} — 현재: {current}", 12, FontStyle.Bold, TitleColor, 20f);

        var actionRow = MakeRow(_listContent, 24f);
        MakeChoice(actionRow, "이 파트 비우기", () => ClearPart(_selectedPart));
        MakeChoice(actionRow, "목록 새로고침", RefreshList);

        if (_composer == null)
        {
            MakeText(_listContent, "조합 엔진이 준비되지 않았습니다.", 12, FontStyle.Normal, WarnColor, 20f);
            return;
        }

        var items = _composer.PartsOf(_selectedPart);
        MakeText(_listContent, $"고를 수 있는 파츠 {items.Count}개", 11, FontStyle.Normal, HintColor, 18f);

        foreach (var item in items)
        {
            string fileName = item.FileName;
            bool worn = current.Split(',').Contains(fileName);
            string tags = string.Join(" ", new[]
            {
                string.IsNullOrEmpty(item.Race) ? null : item.Race,
                string.IsNullOrEmpty(item.Gender) ? null : item.Gender,
                item.Class != null && item.Class.Length > 0 ? string.Join("+", item.Class) : null,
            }.Where(t => !string.IsNullOrEmpty(t)));

            var rt = NewUi("PartRow", _listContent, out Image bg);
            bg.color = worn ? RowSelected : RowNormal;
            rt.gameObject.AddComponent<LayoutElement>().minHeight = 24f;

            var text = MakeStretchedText(rt, worn ? $"● {fileName}   {tags}" : $"{fileName}   {tags}",
                                         12, FontStyle.Normal, Color.white);
            text.alignment = TextAnchor.MiddleLeft;

            var button = rt.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SetPart(_selectedPart, fileName));
        }
    }

    /// <summary>몬스터 목록(코드·이름·능력치·추천 대비 차이·프리팹/레시피 유무).</summary>
    private void BuildMonsterList()
    {
        foreach (var row in _monsterRows)
        {
            int code = row.Code;
            string marks = (row.HasPrefab ? "P" : "·") + (row.HasRecipe ? "R" : "·");
            string label = row.Orphan
                ? $"{code}  (고아 프리팹 — 마스터 없음)   [{marks}]"
                : $"{code}  {row.Name}\n      hp {row.Hp} / atk {row.Attack} — {row.BaseNote} · 등장 {row.LevelNote}   [{marks}]";

            var rt = NewUi("Row", _listContent, out Image bg);
            bg.color = code == _selectedCode ? RowSelected : RowNormal;
            rt.gameObject.AddComponent<LayoutElement>().minHeight = row.Orphan ? 26f : 40f;

            var text = MakeStretchedText(rt, label, 12, FontStyle.Normal, Color.white);
            text.alignment = TextAnchor.MiddleLeft;

            var button = rt.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectMonster(code));
            _listRows.Add((code, bg));
        }
    }

    /// <summary>목록에서 한 종을 고른다 — 우측 패널이 그 몬스터 값으로 채워진다.</summary>
    private void SelectMonster(int code)
    {
        _selectedCode = code;

        var row = _monsterRows.FirstOrDefault(r => r.Code == code);
        if (row != null && !row.Orphan)
        {
            _nameInput.text = row.Name;
            _hpInput.text = row.Hp.ToString();
            _atkInput.text = row.Attack.ToString();
        }

        int act = MonsterStatCurve.ActOfCode(code);
        if (act > 0)
        {
            _act = act;
            _stage = MonsterStatCurve.IsBossCode(code) ? MonsterStatCurve.BossStage : 1;
            // 마스터에 약/강 컬럼은 없다 — 코드 관례로 초기값만 잡고, 씬에서 토글로 바꾼다.
            _strong = MonsterStatCurve.GuessStrongFromCode(code);
        }

        LoadEffectColorFor(code);

        foreach (var (rowCode, bg) in _listRows)
        {
            bg.color = rowCode == _selectedCode ? RowSelected : RowNormal;
        }
        RefreshRightPanel();
    }

    // ══════════════════════════════════════════════════════════════════
    //  무기 스윙 이펙트 색
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 고른 몬스터의 스윙 이펙트 색을 편집 상태로 올린다.
    /// <para>우선순위는 <b>저장된 프리팹 → 레시피(effectColor, 없으면 보스는 지역 색)</b>이다 —
    /// 씬에서 색만 바꿔 저장한 몬스터를 다시 골랐을 때 그 값이 그대로 보여야 하기 때문이다.</para>
    /// </summary>
    private void LoadEffectColorFor(int code)
    {
        _effectColor = null;
#if UNITY_EDITOR
        var saved = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            MonsterUnitFactory.PrefabPath(code, monsterPrefabFolder));
        var palette = saved != null ? saved.GetComponent<MonsterSwingFxPalette>() : null;
        if (palette != null)
        {
            _effectColor = palette.BaseColor;
            return;
        }
        if (saved != null)
        {
            return; // 프리팹은 있는데 팔레트가 없다 = 기본색으로 저장된 상태
        }
#endif
        _effectColor = MonsterUnitFactory.SwingFxColorFor(RecipeFor(code), code);
    }

    /// <summary>이펙트 색을 고른다(null = 기본 불티색). 저장할 때 프리팹에 반영된다.</summary>
    private void SetEffectColor(Color? color)
    {
        _effectColor = color;
        RefreshEffectColorUi();
        Log(color.HasValue
            ? $"스윙 이펙트 색 선택 — #{ColorUtility.ToHtmlStringRGB(color.Value)} (저장해야 프리팹에 반영됩니다)"
            : "스윙 이펙트 색 선택 — 기본(불티) (저장해야 프리팹에 반영됩니다)");
    }

    /// <summary>이펙트 색 프리셋 버튼 선택 표시·견본·설명을 현재 값으로 갱신한다.</summary>
    private void RefreshEffectColorUi()
    {
        foreach (var (value, bg) in _effectColorRows)
        {
            bool selected = value < 0
                ? !_effectColor.HasValue
                : _effectColor.HasValue && SameColor(_effectColor.Value, MonsterSwingFxPalette.ActColor(value));
            bg.color = selected ? RowSelected : ButtonColor;
        }

        if (_effectColorSwatch != null)
        {
            _effectColorSwatch.color = _effectColor ?? MonsterSwingFxPalette.DefaultColor;
        }
        if (_effectColorText != null)
        {
            _effectColorText.text = _effectColor.HasValue
                ? $"현재 색: #{ColorUtility.ToHtmlStringRGB(_effectColor.Value)}"
                : $"현재 색: 기본(불티) #{ColorUtility.ToHtmlStringRGB(MonsterSwingFxPalette.DefaultColor)}";
        }
    }

    /// <summary>색 비교(프리셋 선택 표시용) — 부동소수 오차와 8비트 저장 왕복을 견디도록 여유를 둔다.</summary>
    private static bool SameColor(Color a, Color b)
    {
        const float e = 0.01f;
        return Mathf.Abs(a.r - b.r) < e && Mathf.Abs(a.g - b.g) < e && Mathf.Abs(a.b - b.b) < e;
    }

    /// <summary>우측 패널(선택 상태·추천·레시피 요약·이름 후보)을 현재 값으로 다시 칠한다.</summary>
    private void RefreshRightPanel()
    {
        foreach (var (value, bg) in _actRows)
        {
            bg.color = value == _act ? RowSelected : ButtonColor;
        }
        foreach (var (value, bg) in _stageRows)
        {
            bg.color = value == _stage ? RowSelected : ButtonColor;
        }
        // 보스 스테이지와 Act3~5에는 약/강 구분이 없다 — 그 자리에서는 선택 자체가 레벨을 바꾸지 않는다(§4.4).
        bool strongUsable = !IsBossSelection() && MonsterStatCurve.SupportsStrong(_act);
        foreach (var (value, bg) in _strongRows)
        {
            bg.color = value == _strong && strongUsable ? RowSelected : ButtonColor;
        }
        RefreshEffectColorUi();

        if (_codeText != null)
        {
            int suggested = SuggestedCode();
            string used = UsedCodes().Contains(suggested) ? " (이미 사용 중)" : string.Empty;
            _codeText.text = $"작업 코드: {(_selectedCode == 0 ? "-" : _selectedCode.ToString())}   /   제안 코드: {(suggested == 0 ? "-" : suggested.ToString())}{used}";
        }

        if (_recipeText != null)
        {
            if (_selectedCode == 0)
            {
                _recipeText.text = "레시피: -";
            }
            else
            {
                var recipe = RecipeFor(_selectedCode);
                bool hasRecipe = _recipes.ContainsKey(_selectedCode);
                _recipeText.text = (hasRecipe ? "레시피: " : "레시피 없음(코드 시드 랜덤): ") + recipe.Summary()
                                   + (string.IsNullOrEmpty(recipe.note) ? "" : $"\n{recipe.note}");
            }
        }

        RefreshNameCandidates();
        RefreshRecommendation();
    }

    /// <summary>선택된 레시피의 계열에 맞는 이름 후보 버튼을 다시 만든다(§4.3).</summary>
    private void RefreshNameCandidates()
    {
        if (_nameCandidateRow == null)
        {
            return;
        }

        for (int i = _nameCandidateRow.childCount - 1; i >= 0; i--)
        {
            Destroy(_nameCandidateRow.GetChild(i).gameObject);
        }

        string race = _selectedCode != 0 ? RecipeFor(_selectedCode).race : string.Empty;
        if (string.IsNullOrEmpty(race) || !NameCandidates.TryGetValue(race, out string[] candidates))
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            string captured = candidate;
            MakeChoice(_nameCandidateRow, candidate, () => _nameInput.text = captured);
        }
    }

    /// <summary>지금 고른 자리가 보스 자리인지(스테이지 10 또는 보스 코드를 고른 상태).</summary>
    private bool IsBossSelection()
    {
        return _stage == MonsterStatCurve.BossStage
               || (_selectedCode != 0 && MonsterStatCurve.IsBossCode(_selectedCode));
    }

    /// <summary>지금 고른 자리의 추천 등장 레벨(§4.4).</summary>
    private int RecommendedLevel()
    {
        return MonsterStatCurve.SpawnLevel(_act, _stage, IsBossSelection(), _strong);
    }

    /// <summary>
    /// 그 자리의 <b>추천 등장 레벨</b>과 그 레벨에서의 실제 전투 스탯, 그리고 입력한 기준값이
    /// 통일 기준값과 같은지를 보여 준다(F10·F11 · §4.3).
    /// <para>추천되는 것은 hp·attack이 아니다 — 마스터에 넣는 값은 언제나 통일 기준값이고,
    /// 세기는 <c>stage_spawn.monster_level</c>이 만든다(값 문서 §9.4).</para>
    /// </summary>
    private void RefreshRecommendation()
    {
        bool boss = IsBossSelection();
        MonsterStatCurve.Recommend(_act, _stage, boss, _strong,
                                   out int level, out long baseHp, out long baseAttack,
                                   out long hp, out long attack);

        if (_levelText != null)
        {
            string role = boss ? "보스" : (MonsterStatCurve.SupportsStrong(_act) ? (_strong ? "강몹" : "약몹") : "일반");
            _levelText.text = $"등장 레벨 추천 — Act{_act} s{_stage} {role}: Lv{level}"
                              + $"\n  그 레벨의 실제 전투 스탯: hp {hp} / atk {attack}";
        }

        if (_baseNoteText == null)
        {
            return;
        }

        if (!ReadStatInputs(out long inputHp, out long inputAttack))
        {
            _baseNoteText.text = $"통일 기준값: hp {baseHp} / atk {baseAttack} ({(boss ? "보스" : "일반")})";
            _baseNoteText.color = HintColor;
            return;
        }

        bool matches = MonsterStatCurve.MatchesBase(inputHp, inputAttack, boss);
        _baseNoteText.text = $"hp {inputHp} / atk {inputAttack} — {MonsterStatCurve.BaseMatchText(inputHp, inputAttack, boss)}";
        _baseNoteText.color = matches ? HintColor : WarnColor;
    }

    /// <summary>
    /// 역할별 <b>통일 기준값</b>을 입력란에 채운다(일반 50/5 · 보스 50/1 — §4.3 · 값 문서 §9).
    /// 채운 뒤에도 편집은 되지만, 기준값을 벗어나면 레벨이 세기를 만들지 못하므로 경고가 뜬다.
    /// </summary>
    private void FillBaseStats()
    {
        bool boss = IsBossSelection();
        long hp = MonsterStatCurve.BaseHp;
        long attack = MonsterStatCurve.BaseAttackOf(boss);
        _hpInput.text = hp.ToString();
        _atkInput.text = attack.ToString();
        RefreshRecommendation();
        Log($"통일 기준값 채움 — {(boss ? "보스" : "일반")}: hp {hp} / atk {attack} "
            + $"(Act{_act} s{_stage} 추천 등장 레벨 Lv{RecommendedLevel()})");
    }

    /// <summary>SPUM 준비 상태·진행 중 여부에 따라 버튼 잠금과 안내 문구를 갱신한다.</summary>
    private void RefreshUi()
    {
        bool usable = IsReady && !_busy;
        foreach (var button in _gatedButtons)
        {
            if (button != null)
            {
                button.interactable = usable;
            }
        }

        if (_batchTargetText != null)
        {
            _batchTargetText.text = "대상: " + (_batchOnlyMissing ? "프리팹 없는 코드만" : "전체(기존 프리팹은 백업 후 교체)");
        }
    }

    /// <summary>상단 상태 줄(준비 상태·바인딩 오류·진행 상황).</summary>
    private void Status(string message)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = string.IsNullOrEmpty(_initError) ? HintColor : WarnColor;
        }
    }

    private void UpdateLogText()
    {
        if (_logText != null)
        {
            _logText.text = string.Join("\n", _log);
        }
    }

    /// <summary>hp·attack 입력을 읽는다. 0 이하·비정수면 false(반영 버튼이 막힌다 — §6.4).</summary>
    private bool ReadStatInputs(out long hp, out long attack)
    {
        hp = 0;
        attack = 0;
        if (_hpInput == null || _atkInput == null)
        {
            return false;
        }
        if (!long.TryParse(_hpInput.text, out hp) || !long.TryParse(_atkInput.text, out attack))
        {
            return false;
        }
        return hp > 0 && attack > 0;
    }

    private int SuggestedCode()
    {
        return MonsterStatCurve.SuggestCode(_act, _stage == MonsterStatCurve.BossStage, UsedCodes());
    }

    private HashSet<int> UsedCodes()
    {
        var used = new HashSet<int>();
        foreach (var row in _monsterRows)
        {
            used.Add(row.Code);
        }
        return used;
    }

    // ══════════════════════════════════════════════════════════════════
    //  UI 조각 만들기
    // ══════════════════════════════════════════════════════════════════

    private static RectTransform NewUi(string name, Transform parent, out Image image)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        image = go.GetComponent<Image>();
        return rt;
    }

    /// <summary>세로 스크롤 영역을 만들고 항목을 담을 Content를 돌려준다.</summary>
    private static RectTransform MakeScrollContent(RectTransform scrollRt)
    {
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        var viewport = (RectTransform)viewportGo.transform;
        viewport.SetParent(scrollRt, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        scroll.viewport = viewport;

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        var content = (RectTransform)contentGo.transform;
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        var layout = contentGo.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        return content;
    }

    /// <summary>패널 상단에 고정되는 한 줄 라벨.</summary>
    private static Text MakeTopLabel(RectTransform parent, string name, string content, int fontSize,
                                     FontStyle style, Color color, float topOffset, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(8f, -height);
        rt.offsetMax = new Vector2(-8f, 0f);
        rt.anchoredPosition = new Vector2(0f, topOffset);

        var text = go.GetComponent<Text>();
        ApplyFont(text, content, fontSize, style, color);
        return text;
    }

    /// <summary>세로 레이아웃 안에 놓는 라벨(높이 고정).</summary>
    private static Text MakeText(RectTransform parent, string content, int fontSize, FontStyle style,
                                 Color color, float height)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        ApplyFont(text, content, fontSize, style, color);
        go.GetComponent<LayoutElement>().minHeight = height;
        return text;
    }

    /// <summary>부모를 꽉 채우는 라벨(행 안쪽 텍스트).</summary>
    private static Text MakeStretchedText(RectTransform parent, string content, int fontSize, FontStyle style,
                                          Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8f, 2f);
        rt.offsetMax = new Vector2(-6f, -2f);

        var text = go.GetComponent<Text>();
        ApplyFont(text, content, fontSize, style, color);
        return text;
    }

    private static void ApplyFont(Text text, string content, int fontSize, FontStyle style, Color color)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        // 텍스트 rect를 글자 크기에 빡빡하게 잡지 않는다는 규칙과 같은 이유로 세로는 넘치게 둔다.
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
    }

    /// <summary>가로로 항목을 늘어놓는 행.</summary>
    private static RectTransform MakeRow(RectTransform parent, float height)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        go.GetComponent<LayoutElement>().minHeight = height;
        return rt;
    }

    /// <summary>가로 폭 전체를 채우는 단색 견본 막대(고른 색을 눈으로 확인하는 용도).</summary>
    private static Image MakeSwatch(RectTransform parent, float height)
    {
        var rt = NewUi("Swatch", parent, out Image bg);
        bg.color = Color.white;
        bg.raycastTarget = false;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = height;
        return bg;
    }

    /// <summary>세로 레이아웃 안의 가로 폭 전체를 쓰는 버튼.</summary>
    private Button MakeButton(RectTransform parent, string label, UnityAction onClick,
                              bool primary = false, bool gated = true)
    {
        var rt = NewUi("Button", parent, out Image bg);
        bg.color = primary ? ButtonPrimary : ButtonColor;
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 28f;

        var text = MakeStretchedText(rt, label, 13, FontStyle.Bold, Color.white);
        text.alignment = TextAnchor.MiddleCenter;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(onClick);
        if (gated)
        {
            _gatedButtons.Add(button);
        }
        return button;
    }

    /// <summary>가로 행 안의 선택 버튼(Act·스테이지·애니메이션·이름 후보). 선택 표시는 배경색으로 한다.</summary>
    private Image MakeChoice(RectTransform parent, string label, UnityAction onClick)
    {
        var rt = NewUi("Choice", parent, out Image bg);
        bg.color = ButtonColor;

        var text = MakeStretchedText(rt, label, 12, FontStyle.Normal, Color.white);
        text.alignment = TextAnchor.MiddleCenter;

        var button = rt.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        return bg;
    }

    /// <summary>라벨 + 입력란 한 줄.</summary>
    private static InputField MakeInput(RectTransform parent, string label, string value,
                                        InputField.ContentType contentType)
    {
        var row = MakeRow(parent, 26f);

        var labelText = MakeStretchedText(row, label, 12, FontStyle.Normal, HintColor);
        labelText.rectTransform.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

        var fieldRt = NewUi("Input", row, out Image fieldBg);
        fieldBg.color = new Color(0.12f, 0.13f, 0.17f, 1f);

        // rect를 글자 높이에 빡빡하게 잡으면 줄이 통째로 사라지므로 여유를 둔다(CLAUDE.md 규칙).
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var textRt = (RectTransform)textGo.transform;
        textRt.SetParent(fieldRt, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(6f, 2f);
        textRt.offsetMax = new Vector2(-6f, -2f);
        var text = textGo.GetComponent<Text>();
        ApplyFont(text, value, 13, FontStyle.Normal, Color.white);
        text.supportRichText = false;

        var input = fieldRt.gameObject.AddComponent<InputField>();
        input.textComponent = text;
        input.text = value;
        input.contentType = contentType;
        input.targetGraphic = fieldBg;
        return input;
    }
}
