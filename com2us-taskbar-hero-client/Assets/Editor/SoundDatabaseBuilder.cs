using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 사운드 에셋 임포트 교정 + <see cref="SoundDatabase"/> 빌드 도구(사운드 리소스 정의서 §8.1·§8.2).
    /// <list type="number">
    /// <item><c>Assets/Sound</c>의 클립에 종류별 로드 설정을 적용한다(BGM 스트리밍·징글 압축 상주·SFX 즉시 해제).</item>
    /// <item>파일명 ↔ <see cref="SoundId"/>를 대조해 매핑 에셋을 채운다. 아직 만들지 않은 사운드는 비워 둔다.</item>
    /// </list>
    /// 메뉴: TaskbarHero/Sound/사운드 DB 빌드
    /// </summary>
    public static class SoundDatabaseBuilder
    {
        private const string SoundRoot = "Assets/Sound";
        private const string DatabasePath = "Assets/Resources/SoundDatabase.asset";

        /// <summary>ID ↔ 파일명(확장자 제외). 파일명이 곧 식별자라는 정의서 §2.1 규칙을 그대로 따른다.</summary>
        private static readonly Dictionary<SoundId, string> FileNames = new Dictionary<SoundId, string>
        {
            { SoundId.BgmTitle, "bgm_title" },
            { SoundId.BgmCharacterCreate, "bgm_character_create" },
            { SoundId.BgmBattleAct1, "bgm_battle_act1" },
            { SoundId.BgmBattleAct2, "bgm_battle_act2" },
            { SoundId.BgmBattleAct3, "bgm_battle_act3" },
            { SoundId.BgmBattleAct4, "bgm_battle_act4" },
            { SoundId.BgmBattleAct5, "bgm_battle_act5" },
            { SoundId.BgmBoss, "bgm_boss" },
            { SoundId.JingleStageClear, "jingle_stage_clear" },
            { SoundId.JingleDefeat, "jingle_defeat" },

            { SoundId.UiClick, "sfx_ui_click" },
            { SoundId.UiClickBack, "sfx_ui_click_back" },
            { SoundId.UiPanelOpen, "sfx_ui_panel_open" },
            { SoundId.UiPanelClose, "sfx_ui_panel_close" },
            { SoundId.UiTab, "sfx_ui_tab" },
            { SoundId.UiModalOpen, "sfx_ui_modal_open" },
            { SoundId.UiModalOk, "sfx_ui_modal_ok" },
            { SoundId.UiModalCancel, "sfx_ui_modal_cancel" },
            { SoundId.UiError, "sfx_ui_error" },
            { SoundId.UiSlotSelect, "sfx_ui_slot_select" },
            { SoundId.UiTooltip, "sfx_ui_tooltip" },
            { SoundId.UiNotify, "sfx_ui_notify" },

            { SoundId.TitleLogo, "sfx_title_logo" },
            { SoundId.TitleStart, "sfx_title_start" },
            { SoundId.LoginSuccess, "sfx_login_success" },
            { SoundId.LoginFail, "sfx_login_fail" },
            { SoundId.SignupSuccess, "sfx_signup_success" },
            { SoundId.SceneTransition, "sfx_scene_transition" },
            { SoundId.CharFocus, "sfx_char_focus" },
            { SoundId.CharCreate, "sfx_char_create" },
            { SoundId.AmbCampfireLoop, "amb_campfire_loop" },

            { SoundId.StageEnter, "sfx_stage_enter" },
            { SoundId.BossWarning, "sfx_boss_warning" },
            { SoundId.HitFlesh, "sfx_hit_flesh" },
            { SoundId.HitArmor, "sfx_hit_armor" },
            { SoundId.AllyDeath, "sfx_ally_death" },
            { SoundId.MonsterDeath, "sfx_monster_death" },
            { SoundId.LevelUp, "sfx_levelup" },
            { SoundId.RewardGet, "sfx_reward_get" },

            { SoundId.KnightBasic, "sfx_knight_basic" },
            { SoundId.KnightShieldChargeStart, "sfx_knight_shield_charge_start" },
            { SoundId.KnightShieldChargeImpact, "sfx_knight_shield_charge_impact" },
            { SoundId.KnightPowerStrike, "sfx_knight_power_strike" },
            { SoundId.KnightRage, "sfx_knight_rage" },

            { SoundId.ArcherBowShot, "sfx_archer_bow_shot" },
            { SoundId.ArcherArrowImpact, "sfx_archer_arrow_impact" },
            { SoundId.ArcherMultiShot, "sfx_archer_multi_shot" },

            { SoundId.MageFireball, "sfx_mage_fireball" },
            { SoundId.MageFrostNova, "sfx_mage_frost_nova" },
            { SoundId.MageLightningBolt, "sfx_mage_lightning_bolt" },

            { SoundId.SlayerBasic, "sfx_slayer_basic" },
            { SoundId.SlayerLeap, "sfx_slayer_leap" },
            { SoundId.SlayerGroundSlam, "sfx_slayer_ground_slam" },
            { SoundId.SlayerBerserk, "sfx_slayer_berserk" },
            { SoundId.SlayerCleave, "sfx_slayer_cleave" },

            { SoundId.MonAttack, "sfx_mon_attack" },
            { SoundId.BossRoar, "sfx_boss_roar" },

            { SoundId.ItemEquip, "sfx_item_equip" },
            { SoundId.GoldSpend, "sfx_gold_spend" },
            { SoundId.UpgradeSuccess, "sfx_upgrade_success" },
            { SoundId.CubeCombine, "sfx_cube_combine" },
            { SoundId.RewardClaim, "sfx_reward_claim" },
            { SoundId.EnhanceHammer, "sfx_enhance_hammer" },
            { SoundId.EnhanceSuccess, "sfx_enhance_success" },

            { SoundId.PortalOpen, "sfx_portal_open" },
            { SoundId.PortalTravel, "sfx_portal_travel" },
            { SoundId.BossRushStart, "sfx_boss_rush_start" },
            { SoundId.NewRecord, "sfx_new_record" },
            { SoundId.TimeWarning, "sfx_time_warning" },

            { SoundId.GachaPullSingle, "sfx_gacha_pull_single" },
            { SoundId.GachaPullMulti, "sfx_gacha_pull_multi" },
            { SoundId.GachaSlotReveal, "sfx_gacha_slot_reveal" },
            { SoundId.GachaGrade3, "sfx_gacha_grade3" },
            { SoundId.GachaGrade4, "sfx_gacha_grade4" },
            { SoundId.GachaGrade5, "sfx_gacha_grade5" },
        };

        [MenuItem("TaskbarHero/Sound/사운드 DB 빌드")]
        public static void Build()
        {
            var byName = CollectClips();
            ApplyImportSettings(byName);

            var db = LoadOrCreateDatabase();
            var entries = new List<SoundDatabase.Entry>();
            var missing = new List<string>();
            foreach (var pair in FileNames)
            {
                byName.TryGetValue(pair.Value, out var clip);
                if (clip == null)
                {
                    missing.Add(pair.Value);
                    continue;
                }
                entries.Add(new SoundDatabase.Entry { id = pair.Key, clip = clip });
            }

            db.EditorSetEntries(entries);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SoundDatabaseBuilder] 완료: {entries.Count}종 배선"
                      + (missing.Count > 0 ? $" · 파일 없음 {missing.Count}종({string.Join(", ", missing)})" : string.Empty));
        }

        /// <summary>Assets/Sound 아래 오디오 클립을 파일명(확장자 제외) → 클립으로 모은다.</summary>
        private static Dictionary<string, AudioClip> CollectClips()
        {
            var map = new Dictionary<string, AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SoundRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    map[Path.GetFileNameWithoutExtension(path)] = clip;
                }
            }
            return map;
        }

        /// <summary>정의서 §8.1의 로드 설정을 종류별로 적용한다
        /// (BGM 90초 루프는 스트리밍, 짧고 잦은 효과음은 즉시 해제, 징글은 압축 상주).</summary>
        private static void ApplyImportSettings(Dictionary<string, AudioClip> byName)
        {
            int changed = 0;
            foreach (var clip in byName.Values)
            {
                string path = AssetDatabase.GetAssetPath(clip);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    continue;
                }

                string file = Path.GetFileNameWithoutExtension(path);
                AudioClipLoadType loadType;
                bool background;
                if (file.StartsWith("bgm_") || file.StartsWith("amb_"))
                {
                    loadType = AudioClipLoadType.Streaming;   // 90초 루프·30초 앰비언트
                    background = true;
                }
                else if (file.StartsWith("jingle_"))
                {
                    loadType = AudioClipLoadType.CompressedInMemory; // 3~4초 연출음
                    background = false;
                }
                else
                {
                    loadType = AudioClipLoadType.DecompressOnLoad;   // 1~2초, 자주 재생
                    background = false;
                }

                var settings = importer.defaultSampleSettings;
                if (settings.loadType == loadType && importer.loadInBackground == background)
                {
                    continue;
                }
                settings.loadType = loadType;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = background;
                importer.SaveAndReimport();
                changed++;
            }
            Debug.Log($"[SoundDatabaseBuilder] 임포트 설정 적용: {changed}종 재임포트");
        }

        /// <summary>매핑 에셋을 불러오고, 없으면 Resources 폴더에 새로 만든다.</summary>
        private static SoundDatabase LoadOrCreateDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<SoundDatabase>(DatabasePath);
            if (db != null)
            {
                return db;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            db = ScriptableObject.CreateInstance<SoundDatabase>();
            AssetDatabase.CreateAsset(db, DatabasePath);
            Debug.Log($"[SoundDatabaseBuilder] 매핑 에셋 생성: {DatabasePath}");
            return db;
        }
    }
}
