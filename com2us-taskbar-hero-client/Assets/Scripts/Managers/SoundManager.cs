using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 전역 사운드 관리자(사운드 리소스 정의서 §8.2). 씬 배선 없이 부팅 시 스스로 1회 생성되고
    /// <c>DontDestroyOnLoad</c>로 씬 전환에도 살아남아 BGM을 끊김 없이 이어 재생한다.
    /// <list type="bullet">
    /// <item><b>BGM 2채널 크로스페이드</b> — 전투 → 보스 전환처럼 곡이 바뀔 때 부드럽게 넘긴다.</item>
    /// <item><b>앰비언트 1채널</b> — 모닥불 같은 상시 루프(BGM과 별개로 겹쳐 재생).</item>
    /// <item><b>SFX 풀</b> — 동시 발음 <see cref="SfxVoiceCount"/>개. 같은 클립이 한 프레임에 몰려도
    ///   <see cref="SameClipCooldown"/> 안에는 한 번만 나가 소리가 찢어지지 않는다(§8.3).</item>
    /// <item><b>볼륨 3채널</b> — Master/BGM/SFX. 상주형 창이라 마스터 기본값을 낮게 잡고(§2.4)
    ///   PlayerPrefs에 저장한다.</item>
    /// </list>
    /// 클립은 <see cref="SoundDatabase"/>(Resources)에서 ID로 찾는다. 아직 만들지 않은 사운드는
    /// 클립이 없어 호출해도 조용히 무시되므로, 호출측에 존재 여부 분기를 두지 않아도 된다.
    /// 모든 페이드는 <see cref="Time.unscaledDeltaTime"/>을 쓴다(클리어·패배 연출의 슬로우모션과 무관).
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        private const int SfxVoiceCount = 10;
        private const float SameClipCooldown = 0.05f;
        private const float DefaultBgmFade = 1.0f;

        private const string PrefMaster = "th_vol_master";
        private const string PrefBgm = "th_vol_bgm";
        private const string PrefSfx = "th_vol_sfx";
        private const string PrefMuted = "th_vol_muted";

        // 볼륨 기본값. 생성된 BGM 트랙이 효과음보다 낮게 마스터링돼 있어, BGM 채널은 최대(1.0)로 두고
        // 효과음 채널을 낮춰 균형을 맞춘다(BGM 출력 0.70 : SFX 0.35). 상주형 창이라 마스터는
        // 여전히 절반 근처로 유지한다(정의서 §2.4의 취지 — 장시간 켜 둬도 부담 없게).
        private const float DefaultMaster = 0.70f;
        private const float DefaultBgm = 1.00f;
        private const float DefaultSfx = 0.50f;
        // 기본값을 바꿨을 때 이전에 저장된 값을 1회 초기화하기 위한 버전(올리면 저장값을 버리고 새 기본값 적용).
        private const int VolumeDefaultsVersion = 2;
        private const string PrefDefaultsVersion = "th_vol_defaults_ver";

        public static SoundManager Instance { get; private set; }

        private SoundDatabase _db;
        private AudioSource[] _bgmSources;   // 0/1 번갈아 쓰며 크로스페이드
        private int _activeBgm;              // 현재 소리를 내는 쪽 인덱스
        private AudioSource _ambientSource;
        private AudioSource[] _sfxSources;
        private int _nextSfxVoice;
        private readonly Dictionary<SoundId, float> _lastPlayedAt = new Dictionary<SoundId, float>();
        private Coroutine _bgmFadeRoutine;

        private float _masterVolume = DefaultMaster;
        private float _bgmVolume = DefaultBgm;
        private float _sfxVolume = DefaultSfx;
        private bool _muted;

        /// <summary>현재 재생 중인 BGM(없으면 None). 같은 곡을 다시 요청하면 무시하는 판정에 쓴다.</summary>
        public SoundId CurrentBgm { get; private set; }

        public float MasterVolume => _masterVolume;
        public float BgmVolume => _bgmVolume;
        public float SfxVolume => _sfxVolume;
        public bool Muted => _muted;

        /// <summary>
        /// 부팅 시 1회 생성한다(씬 배선 불필요). <b>BeforeSceneLoad</b>여야 한다 —
        /// AfterSceneLoad로 두면 첫 씬(TitleScene)의 <c>Awake()</c>가 먼저 돌아
        /// 그 시점의 <see cref="Instance"/>가 null이라 타이틀 BGM 요청이 통째로 무시된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => EnsureInstance();

        /// <summary>인스턴스가 없으면 만들어 반환한다(호출 순서에 상관없이 안전하게 쓰도록).</summary>
        private static SoundManager EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("SoundManager");
                go.AddComponent<SoundManager>(); // Awake에서 Instance를 채운다
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _db = SoundDatabase.Load();
            if (_db == null)
            {
                Debug.LogWarning("[Sound] SoundDatabase를 찾지 못했습니다. " +
                                 "에디터에서 'TaskbarHero/Sound/사운드 DB 빌드'를 실행하세요.");
            }

            LoadVolumePrefs();
            BuildSources();

            // 앰비언트(모닥불 등)는 씬 전용이라 씬이 바뀌면 자동으로 끊는다.
            // BGM은 씬을 넘겨도 이어져야 하므로 여기서 건드리지 않는다.
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        /// <summary>씬이 바뀌면 이전 씬의 앰비언트 루프를 정리한다.</summary>
        private void OnActiveSceneChanged(
            UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            StopAmbient();
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>BGM 2채널·앰비언트·SFX 풀 AudioSource를 만든다(전부 이 오브젝트에 붙인다).</summary>
        private void BuildSources()
        {
            _bgmSources = new AudioSource[2];
            for (int i = 0; i < _bgmSources.Length; i++)
            {
                _bgmSources[i] = gameObject.AddComponent<AudioSource>();
                _bgmSources[i].playOnAwake = false;
                _bgmSources[i].loop = true;
                _bgmSources[i].volume = 0f;
            }

            _ambientSource = gameObject.AddComponent<AudioSource>();
            _ambientSource.playOnAwake = false;
            _ambientSource.loop = true;
            _ambientSource.volume = 0f;

            _sfxSources = new AudioSource[SfxVoiceCount];
            for (int i = 0; i < _sfxSources.Length; i++)
            {
                _sfxSources[i] = gameObject.AddComponent<AudioSource>();
                _sfxSources[i].playOnAwake = false;
                _sfxSources[i].loop = false;
            }
        }

        // ── BGM ──

        /// <summary>BGM을 바꾼다. 이미 같은 곡이 재생 중이면 아무것도 하지 않아 씬 재진입에도 끊기지 않는다.
        /// 이전 곡은 페이드 아웃, 새 곡은 페이드 인으로 겹쳐 넘긴다(fadeSeconds 0이면 즉시 전환).</summary>
        public void PlayBgm(SoundId id, float fadeSeconds = DefaultBgmFade)
        {
            if (id == CurrentBgm)
            {
                return;
            }
            var clip = Clip(id);
            if (clip == null)
            {
                return; // 미생성 사운드 — 조용히 무시
            }

            CurrentBgm = id;
            int next = 1 - _activeBgm;
            _bgmSources[next].clip = clip;
            _bgmSources[next].loop = true;
            _bgmSources[next].volume = 0f;
            _bgmSources[next].Play();

            StartBgmFade(next, fadeSeconds);
        }

        /// <summary>BGM을 끈다(페이드 아웃 후 정지).</summary>
        public void StopBgm(float fadeSeconds = DefaultBgmFade)
        {
            CurrentBgm = SoundId.None;
            StartBgmFade(-1, fadeSeconds); // 목표 채널 없음 = 전부 페이드 아웃
        }

        /// <summary>페이드 코루틴을 새로 시작한다(진행 중이던 페이드는 취소해 볼륨이 어긋나지 않게 한다).</summary>
        private void StartBgmFade(int target, float fadeSeconds)
        {
            if (_bgmFadeRoutine != null)
            {
                StopCoroutine(_bgmFadeRoutine);
            }
            _bgmFadeRoutine = StartCoroutine(BgmFadeRoutine(target, Mathf.Max(0f, fadeSeconds)));
        }

        /// <summary>target 채널은 목표 볼륨까지 올리고 나머지는 0까지 내린 뒤 정지시킨다.</summary>
        private IEnumerator BgmFadeRoutine(int target, float duration)
        {
            float goal = BgmOutputVolume();
            var from = new float[_bgmSources.Length];
            for (int i = 0; i < _bgmSources.Length; i++)
            {
                from[i] = _bgmSources[i].volume;
            }

            float elapsed = 0f;
            while (duration > 0f && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < _bgmSources.Length; i++)
                {
                    _bgmSources[i].volume = Mathf.Lerp(from[i], i == target ? goal : 0f, k);
                }
                yield return null;
            }

            for (int i = 0; i < _bgmSources.Length; i++)
            {
                _bgmSources[i].volume = i == target ? goal : 0f;
                if (i != target)
                {
                    _bgmSources[i].Stop();
                    _bgmSources[i].clip = null;
                }
            }
            if (target >= 0)
            {
                _activeBgm = target;
            }
            _bgmFadeRoutine = null;
        }

        // ── 앰비언트(모닥불 등 상시 루프) ──

        /// <summary>앰비언트 루프를 재생한다(BGM과 별개 채널이라 겹쳐 들린다). 같은 클립이면 무시.</summary>
        public void PlayAmbient(SoundId id)
        {
            var clip = Clip(id);
            if (clip == null || _ambientSource.clip == clip)
            {
                return;
            }
            _ambientSource.clip = clip;
            _ambientSource.volume = SfxOutputVolume();
            _ambientSource.Play();
        }

        /// <summary>앰비언트 루프를 멈춘다.</summary>
        public void StopAmbient()
        {
            _ambientSource.Stop();
            _ambientSource.clip = null;
        }

        // ── SFX ──

        /// <summary>효과음을 한 번 재생한다. 같은 ID가 <see cref="SameClipCooldown"/> 안에 몰리면
        /// (광역 스킬 피격음·다수 몬스터 사망 등) 한 번만 나가도록 눌러 준다.</summary>
        public void PlaySfx(SoundId id, float volumeScale = 1f)
        {
            var clip = Clip(id);
            if (clip == null)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (_lastPlayedAt.TryGetValue(id, out float last) && now - last < SameClipCooldown)
            {
                return;
            }
            _lastPlayedAt[id] = now;

            var src = _sfxSources[_nextSfxVoice];
            _nextSfxVoice = (_nextSfxVoice + 1) % _sfxSources.Length;
            src.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * SfxOutputVolume());
        }

        /// <summary>연출 징글(클리어·패배)을 재생한다. 루프하지 않으며 BGM 채널을 건드리지 않는다.</summary>
        public void PlayJingle(SoundId id)
        {
            PlaySfx(id);
        }

        // ── 볼륨 ──

        /// <summary>마스터 볼륨(0~1)을 설정하고 저장한다.</summary>
        public void SetMasterVolume(float value) => SetVolume(ref _masterVolume, PrefMaster, value);

        /// <summary>BGM 볼륨(0~1)을 설정하고 저장한다.</summary>
        public void SetBgmVolume(float value) => SetVolume(ref _bgmVolume, PrefBgm, value);

        /// <summary>효과음 볼륨(0~1)을 설정하고 저장한다.</summary>
        public void SetSfxVolume(float value) => SetVolume(ref _sfxVolume, PrefSfx, value);

        /// <summary>전체 음소거를 켜고 끈다(볼륨 값은 보존).</summary>
        public void SetMuted(bool muted)
        {
            _muted = muted;
            PlayerPrefs.SetInt(PrefMuted, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyVolumes();
        }

        /// <summary>볼륨 값을 갱신·저장하고 재생 중인 채널에 즉시 반영한다.</summary>
        private void SetVolume(ref float field, string prefKey, float value)
        {
            field = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(prefKey, field);
            PlayerPrefs.Save();
            ApplyVolumes();
        }

        /// <summary>저장된 볼륨 설정을 불러온다(없으면 기본값).</summary>
        private void LoadVolumePrefs()
        {
            // 기본값을 조정했으면(버전 상승) 예전에 저장된 값을 한 번 버리고 새 기본값으로 시작한다.
            if (PlayerPrefs.GetInt(PrefDefaultsVersion, 0) != VolumeDefaultsVersion)
            {
                PlayerPrefs.DeleteKey(PrefMaster);
                PlayerPrefs.DeleteKey(PrefBgm);
                PlayerPrefs.DeleteKey(PrefSfx);
                PlayerPrefs.SetInt(PrefDefaultsVersion, VolumeDefaultsVersion);
                PlayerPrefs.Save();
            }
            _masterVolume = PlayerPrefs.GetFloat(PrefMaster, DefaultMaster);
            _bgmVolume = PlayerPrefs.GetFloat(PrefBgm, DefaultBgm);
            _sfxVolume = PlayerPrefs.GetFloat(PrefSfx, DefaultSfx);
            _muted = PlayerPrefs.GetInt(PrefMuted, 0) == 1;
        }

        /// <summary>현재 재생 중인 BGM·앰비언트 채널 볼륨을 설정값으로 다시 맞춘다
        /// (SFX는 재생 시점에 볼륨을 곱하므로 소급 적용하지 않는다).</summary>
        private void ApplyVolumes()
        {
            if (_bgmSources != null && _bgmFadeRoutine == null)
            {
                for (int i = 0; i < _bgmSources.Length; i++)
                {
                    _bgmSources[i].volume = i == _activeBgm && _bgmSources[i].isPlaying ? BgmOutputVolume() : 0f;
                }
            }
            if (_ambientSource != null && _ambientSource.isPlaying)
            {
                _ambientSource.volume = SfxOutputVolume();
            }
        }

        private float BgmOutputVolume() => _muted ? 0f : _masterVolume * _bgmVolume;

        private float SfxOutputVolume() => _muted ? 0f : _masterVolume * _sfxVolume;

        /// <summary>ID에 해당하는 클립(DB 미배선·미생성이면 null).</summary>
        private AudioClip Clip(SoundId id)
        {
            if (_db == null)
            {
                _db = SoundDatabase.Load();
            }
            return _db != null ? _db.Get(id) : null;
        }

        // ── 정적 편의 래퍼(인스턴스 null 체크를 호출측에서 반복하지 않도록) ──

        // 인스턴스가 아직 없으면 만들어서라도 재생한다 — 호출측(씬 Awake 등)이 부팅 순서를 신경 쓰지 않게.

        /// <summary>BGM 재생.</summary>
        public static void Bgm(SoundId id, float fadeSeconds = DefaultBgmFade)
            => EnsureInstance()?.PlayBgm(id, fadeSeconds);

        /// <summary>효과음 재생.</summary>
        public static void Sfx(SoundId id, float volumeScale = 1f)
            => EnsureInstance()?.PlaySfx(id, volumeScale);

        /// <summary>징글 재생.</summary>
        public static void Jingle(SoundId id) => EnsureInstance()?.PlayJingle(id);

        /// <summary>앰비언트 루프 재생.</summary>
        public static void Ambient(SoundId id) => EnsureInstance()?.PlayAmbient(id);
    }
}
