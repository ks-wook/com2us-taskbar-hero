using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 캐릭터 일러스트 DB(<see cref="CharacterIllustrationDatabase"/>) 빌드 도구.
    /// <c>Assets/Art/Character/Image/{직업}_{성별}.png</c>을 훑어 (직업, 성별) → 스프라이트로 채우고,
    /// 그림마다 측정한 <b>얼굴 좌표</b>를 함께 적어 넣는다.
    ///
    /// <para><b>원본이 곧 컷아웃이다(2026-08-05~)</b> — 예전에는 원본이 초록 크로마키 배경이라
    /// <c>tools/character_illust_cutout.py</c>로 만든 <c>Image/Cutout/</c> 파생본을 참조했다. 지금은
    /// <b>원본 8종이 배경 제거 + 캐릭터에 맞춘 크롭 상태로 직접 편집</b>되어 있어 파생본이 필요 없다.
    /// <c>Cutout/</c> 폴더는 더 이상 참조하지 않는다(이 빌더도 훑지 않는다).</para>
    ///
    /// <para><b>그림을 교체하면 얼굴 좌표를 다시 맞춰야 한다</b> — 아래 <see cref="FaceAnchors"/> 참조.</para>
    ///
    /// 메뉴: TaskbarHero/캐릭터/일러스트 DB 빌드
    /// </summary>
    public static class CharacterIllustrationDatabaseBuilder
    {
        private const string SourceDir = "Assets/Art/Character/Image";
        private const string LegacyCutoutDir = "Assets/Art/Character/Image/Cutout"; // 미참조(임포트 교정 제외용)
        private const string DatabasePath = "Assets/Resources/CharacterIllustrationDatabase.asset";

        /// <summary>파일명의 직업 이름 → class_master 코드.</summary>
        private static readonly Dictionary<string, int> ClassCodes = new Dictionary<string, int>
        {
            { "Knight", 1 },
            { "Archer", 2 },
            { "Mage", 3 },
            { "Slayer", 4 },
        };

        /// <summary>파일명의 성별 → CharacterGender 값(1:남 2:여).</summary>
        private static readonly Dictionary<string, int> Genders = new Dictionary<string, int>
        {
            { "Male", 1 },
            { "Female", 2 },
        };

        /// <summary>
        /// 일러스트별 얼굴 중심(normalized, <b>y는 위에서부터</b>).
        ///
        /// <para><b>2026-08-05 편집본(배경 제거 + 좌우 크롭) 기준으로 다시 측정한 값이다.</b> 그림마다 좌우로
        /// 잘라낸 폭이 달라 <c>x</c>가 전부 바뀌었고, 기사(여)·슬레이어(남)는 아예 다른 고해상도 원화로 교체됐다.
        /// <c>y</c>는 캐릭터가 그림 위쪽에 붙어 있어 크롭이 상단으로 clamp되므로 프레이밍에 거의 영향이 없다.</para>
        ///
        /// <para><b>다시 맞추는 법</b> — 편성창을 띄우지 않고도 검증할 수 있다.
        /// <c>TeamListController.BuildFaceIllustration</c>의 크롭 계산(<c>cropW = cropH × (이미지높이/이미지폭) ×
        /// SlotWindowAspect</c>, <c>cropX = faceX − cropW/2</c>, <c>cropY = 1 − faceY − cropH×(1−FaceInCrop)</c>,
        /// 둘 다 0~1로 clamp)을 그대로 적용해 잘라낸 대지를 만들어 눈으로 확인하고 <c>x</c>를 조정한다.
        /// 얼굴이 프레임 오른쪽으로 치우쳐 잘리면 <c>x</c>를 키운다.</para>
        /// </summary>
        private static readonly Dictionary<string, Vector2> FaceAnchors = new Dictionary<string, Vector2>
        {
            { "Knight_Male", new Vector2(0.460f, 0.120f) },
            { "Knight_Female", new Vector2(0.575f, 0.110f) },
            { "Archer_Male", new Vector2(0.470f, 0.120f) },
            { "Archer_Female", new Vector2(0.555f, 0.110f) },
            // 마법사는 남·여 일러스트의 구도가 같아 한 값을 두 성별에 함께 쓴다.
            { "Mage_Male", new Vector2(0.470f, 0.140f) },
            { "Mage_Female", new Vector2(0.470f, 0.140f) },
            { "Slayer_Male", new Vector2(0.605f, 0.150f) },
            { "Slayer_Female", new Vector2(0.500f, 0.140f) },
        };

        /// <summary>표시할 크롭 높이(이미지 높이 대비). 얼굴 좌표의 ±0.02 오차에도 얼굴이 프레임에 남는 여유값.</summary>
        private const float CropHeight = 0.46f;

        [MenuItem("TaskbarHero/캐릭터/일러스트 DB 빌드")]
        public static void Build()
        {
            FixIllustrationImport();
            var db = LoadOrCreate();
            var entries = new List<CharacterIllustrationDatabase.Entry>();
            var missing = new List<string>();

            foreach (var cls in ClassCodes)
            {
                foreach (var gender in Genders)
                {
                    string name = $"{cls.Key}_{gender.Key}";
                    var sprite = LoadSprite($"{SourceDir}/{name}.png");
                    if (sprite == null)
                    {
                        missing.Add(name);
                        continue;
                    }
                    if (!FaceAnchors.TryGetValue(name, out var face))
                    {
                        // 좌표를 모르는 새 일러스트는 가운데 위쪽으로 가정해 둔다(크롭 대지로 재측정 권장).
                        face = new Vector2(0.5f, 0.2f);
                        Debug.LogWarning($"[CharacterIllustrationDatabaseBuilder] {name}의 얼굴 좌표가 없어 기본값을 씁니다. " +
                                         "FaceAnchors 주석의 '다시 맞추는 법'대로 측정해 추가하세요.");
                    }
                    entries.Add(new CharacterIllustrationDatabase.Entry
                    {
                        classCode = cls.Value,
                        gender = gender.Value,
                        sprite = sprite,
                        faceCenter = face,
                        cropHeight = CropHeight,
                    });
                }
            }

            db.entries = entries.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CharacterIllustrationDatabaseBuilder] 완료: {entries.Count}종 배선"
                      + (missing.Count > 0
                          ? $" · 일러스트 없음 {missing.Count}종({string.Join(", ", missing)}) — "
                            + $"{SourceDir}/(직업)_(성별).png 로 넣어 주세요."
                          : string.Empty));
        }

        /// <summary>
        /// 일러스트의 임포트 규격을 교정한다 — <b>Sprite · Single · Full Rect</b>.
        ///
        /// <para><b>왜 필요한가</b> — PNG가 프로젝트 기본값(Multiple)으로 들어오면 Unity가 자동 슬라이스해
        /// 스프라이트가 <b>여백을 잘라낸 부분 영역</b>이 된다(예: 텍스처 1024×558인데 스프라이트 rect 691×512).
        /// 얼굴 좌표는 <b>이미지 전체</b> 기준이므로, 부분 영역 스프라이트를 쓰면 크롭이 어긋난다.
        /// (2026-08-05 편집본이 실제로 Multiple로 들어와 있었다.)</para>
        ///
        /// <para><b>왜 이 폴더는 임포트를 고쳐도 되는가</b> — 공용 아트의 임포트를 바꾸지 말라는 규칙은
        /// <c>Multiple</c>로 쪼개 둔 기존 스프라이트의 <b>서브 참조가 끊기는 사고</b>를 막기 위한 것이다.
        /// 이 8장은 서브 스프라이트를 참조하는 프리팹·씬·에셋이 하나도 없고(GUID 검색으로 확인) 이 DB만 쓰므로
        /// 끊길 참조가 없다. 하위 <c>Cutout/</c>(구 파생본)은 이제 미참조라 대상에서 제외한다.</para>
        /// </summary>
        private static void FixIllustrationImport()
        {
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SourceDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(LegacyCutoutDir))
                {
                    continue; // 미참조 구 파생본은 건드리지 않는다
                }
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                {
                    continue;
                }

                bool changed = false;
                if (ti.textureType != TextureImporterType.Sprite)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (ti.spriteImportMode != SpriteImportMode.Single)
                {
                    ti.spriteImportMode = SpriteImportMode.Single; // 잘린 부분 영역이 아니라 이미지 전체를 쓴다
                    changed = true;
                }
                if (!ti.alphaIsTransparency)
                {
                    ti.alphaIsTransparency = true; // 배경을 지운 그림이라 알파 경계가 깨지지 않게
                    changed = true;
                }
                if (ti.mipmapEnabled)
                {
                    ti.mipmapEnabled = false; // UI 전용
                    changed = true;
                }
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                if (settings.spriteMeshType != SpriteMeshType.FullRect)
                {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    ti.SetTextureSettings(settings);
                    changed = true;
                }

                if (changed)
                {
                    ti.SaveAndReimport();
                    fixedCount++;
                }
            }
            if (fixedCount > 0)
            {
                Debug.Log($"[CharacterIllustrationDatabaseBuilder] 일러스트 임포트 교정: {fixedCount}개 재임포트(Sprite·Single·Full Rect)");
            }
        }

        /// <summary>스프라이트를 로드한다(Single/Multiple 임포트 모두 대응).</summary>
        private static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (obj is Sprite s)
                {
                    return s;
                }
            }
            return null;
        }

        /// <summary>DB 에셋을 불러오고, 없으면 Resources 폴더에 새로 만든다.</summary>
        private static CharacterIllustrationDatabase LoadOrCreate()
        {
            var db = AssetDatabase.LoadAssetAtPath<CharacterIllustrationDatabase>(DatabasePath);
            if (db != null)
            {
                return db;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            db = ScriptableObject.CreateInstance<CharacterIllustrationDatabase>();
            AssetDatabase.CreateAsset(db, DatabasePath);
            Debug.Log($"[CharacterIllustrationDatabaseBuilder] DB 에셋 생성: {DatabasePath}");
            return db;
        }
    }
}
