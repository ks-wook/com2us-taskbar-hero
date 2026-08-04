using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TaskbarHero.Client.Managers;

namespace TaskbarHero.ClientEditor
{
    /// <summary>
    /// 캐릭터 일러스트 DB(<see cref="CharacterIllustrationDatabase"/>) 빌드 도구.
    /// <c>Assets/Art/Character/Image/Cutout/{직업}_{성별}.png</c>을 훑어 (직업, 성별) → 스프라이트로 채우고,
    /// 그림마다 측정한 <b>얼굴 좌표</b>를 함께 적어 넣는다.
    ///
    /// <para><b>컷아웃이 없으면 먼저 만든다</b> — 원본 일러스트는 초록 크로마키 배경이라 그대로 쓸 수 없다.
    /// <c>python tools/character_illust_cutout.py --sheet out.png</c>로 배경을 지운 컷아웃을 만들고,
    /// 같은 스크립트가 출력하는 얼굴 좌표를 아래 <see cref="FaceAnchors"/>에 옮긴다(대지 PNG로 프레이밍을 눈으로 확인).</para>
    ///
    /// 메뉴: TaskbarHero/캐릭터/일러스트 DB 빌드
    /// </summary>
    public static class CharacterIllustrationDatabaseBuilder
    {
        private const string CutoutDir = "Assets/Art/Character/Image/Cutout";
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
        /// <para><b>8종 전부 플레이 모드에서 실제 카드를 보며 맞춘 값이다</b> — 스크립트의 살색 픽셀 추정은
        /// 첫 값을 잡는 용도였고(손·팔의 살색을 얼굴로 잡는 한계가 있다), 최종 값은 화면에서 확인해 정했다.</para>
        ///
        /// <para>다시 맞추려면 편성창을 플레이 모드로 띄우고 카드 안 <c>Illust</c>를 옮긴 뒤, 창 대비 보이는 영역을
        /// 역산해(<c>cropW = 창폭/이미지폭</c>, <c>faceX = cropX + cropW/2</c>,
        /// <c>faceY = 1 − (cropY + cropH×0.66)</c>) 그 값을 여기와 DB 에셋에 옮긴다.
        /// 파티에 없는 캐릭터는 <c>tools/character_illust_cutout.py --sheet</c> 대지로 프레이밍을 확인할 수 있다.</para>
        /// </summary>
        private static readonly Dictionary<string, Vector2> FaceAnchors = new Dictionary<string, Vector2>
        {
            { "Knight_Male", new Vector2(0.521f, 0.205f) },
            { "Knight_Female", new Vector2(0.511f, 0.189f) },
            { "Archer_Male", new Vector2(0.500f, 0.247f) },
            { "Archer_Female", new Vector2(0.477f, 0.227f) },
            // 마법사는 남·여 일러스트의 구도가 같아 한 값을 두 성별에 함께 쓴다.
            { "Mage_Male", new Vector2(0.518f, 0.179f) },
            { "Mage_Female", new Vector2(0.518f, 0.179f) },
            { "Slayer_Male", new Vector2(0.518f, 0.172f) },
            { "Slayer_Female", new Vector2(0.522f, 0.205f) },
        };

        /// <summary>표시할 크롭 높이(이미지 높이 대비). 얼굴 좌표의 ±0.02 오차에도 얼굴이 프레임에 남는 여유값.</summary>
        private const float CropHeight = 0.46f;

        [MenuItem("TaskbarHero/캐릭터/일러스트 DB 빌드")]
        public static void Build()
        {
            FixCutoutImport();
            var db = LoadOrCreate();
            var entries = new List<CharacterIllustrationDatabase.Entry>();
            var missing = new List<string>();

            foreach (var cls in ClassCodes)
            {
                foreach (var gender in Genders)
                {
                    string name = $"{cls.Key}_{gender.Key}";
                    var sprite = LoadSprite($"{CutoutDir}/{name}.png");
                    if (sprite == null)
                    {
                        missing.Add(name);
                        continue;
                    }
                    if (!FaceAnchors.TryGetValue(name, out var face))
                    {
                        // 좌표를 모르는 새 일러스트는 가운데 위쪽으로 가정해 둔다(스크립트로 재측정 권장).
                        face = new Vector2(0.5f, 0.2f);
                        Debug.LogWarning($"[CharacterIllustrationDatabaseBuilder] {name}의 얼굴 좌표가 없어 기본값을 씁니다. " +
                                         "tools/character_illust_cutout.py로 측정해 FaceAnchors에 추가하세요.");
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
                          ? $" · 컷아웃 없음 {missing.Count}종({string.Join(", ", missing)}) — "
                            + "python tools/character_illust_cutout.py 로 생성하세요."
                          : string.Empty));
        }

        /// <summary>
        /// 컷아웃의 임포트 규격을 교정한다 — <b>Sprite · Single · Full Rect</b>.
        ///
        /// <para><b>왜 필요한가</b> — 새 PNG가 프로젝트 기본값(Multiple)으로 들어오면 Unity가 자동 슬라이스해
        /// 스프라이트가 <b>여백을 잘라낸 부분 영역</b>이 된다(예: 텍스처 1024×558인데 스프라이트 rect 691×512).
        /// 얼굴 좌표는 <b>이미지 전체</b> 기준이므로, 부분 영역 스프라이트를 쓰면 크롭이 어긋난다.
        ///
        /// <para><b>왜 이 폴더는 임포트를 고쳐도 되는가</b> — 공용 아트의 임포트를 바꾸지 말라는 규칙은
        /// <c>Multiple</c>로 쪼개 둔 기존 스프라이트의 서브 참조가 끊기는 사고를 막기 위한 것이다.
        /// 이 폴더는 <c>tools/character_illust_cutout.py</c>가 만든 <b>파생 에셋</b>이고 이 DB만 참조하므로
        /// 끊길 서브 참조가 없다(원본 <c>Image/*.png</c>는 건드리지 않는다).</para>
        /// </summary>
        private static void FixCutoutImport()
        {
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { CutoutDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
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
                    ti.alphaIsTransparency = true; // 배경을 지운 컷아웃이라 알파 경계가 깨지지 않게
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
                Debug.Log($"[CharacterIllustrationDatabaseBuilder] 컷아웃 임포트 교정: {fixedCount}개 재임포트(Sprite·Single·Full Rect)");
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
