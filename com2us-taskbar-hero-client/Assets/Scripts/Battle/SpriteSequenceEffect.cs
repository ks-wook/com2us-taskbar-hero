using UnityEngine;

namespace TaskbarHero.Client.Battle
{
    /// <summary>
    /// 월드 스페이스 SpriteRenderer에 프레임 배열을 1회(또는 반복) 재생하는 스킬 이펙트.
    /// 스킬 시전 시 이펙트 프리팹을 Instantiate하면 재생 후 자동 삭제된다.
    /// (UI용 <see cref="TaskbarHero.Client.UI.SpriteSequenceAnimator"/>의 SpriteRenderer 버전)
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteSequenceEffect : MonoBehaviour
    {
        [Tooltip("재생할 프레임(순서대로)")]
        public Sprite[] frames;
        [Tooltip("초당 프레임 수")]
        public float fps = 24f;
        [Tooltip("반복 재생 여부")]
        public bool loop = false;
        [Tooltip("반복 시 왕복(핑퐁) 재생. 루프 이음새(마지막→처음)의 튐/깜빡임을 없앤다")]
        public bool pingPong = false;
        [Tooltip("재생 종료 시 GameObject 파괴(비반복일 때)")]
        public bool destroyOnFinish = true;

        private SpriteRenderer _sr;
        private int _index;
        private int _dir = 1;
        private float _timer;
        private bool _playing;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            Play();
        }

        public void Play()
        {
            _playing = frames != null && frames.Length > 0;
            _index = 0;
            _dir = 1;
            _timer = 0f;
            if (_playing && _sr != null)
            {
                _sr.sprite = frames[0];
            }
        }

        private void Update()
        {
            if (!_playing || frames == null || frames.Length == 0)
            {
                return;
            }

            _timer += Time.deltaTime;
            float frameDuration = 1f / Mathf.Max(1f, fps);

            while (_timer >= frameDuration)
            {
                _timer -= frameDuration;

                if (loop && pingPong && frames.Length > 1)
                {
                    // 왕복: 끝에 닿으면 방향을 뒤집어 이음새 없이 연속 재생(깜빡임 방지)
                    _index += _dir;
                    if (_index >= frames.Length)
                    {
                        _index = frames.Length - 2;
                        _dir = -1;
                    }
                    else if (_index < 0)
                    {
                        _index = 1;
                        _dir = 1;
                    }
                }
                else
                {
                    _index++;
                    if (_index >= frames.Length)
                    {
                        if (loop)
                        {
                            _index = 0;
                        }
                        else
                        {
                            _playing = false;
                            if (destroyOnFinish)
                            {
                                Destroy(gameObject);
                            }
                            return;
                        }
                    }
                }

                if (_sr != null)
                {
                    _sr.sprite = frames[_index];
                }
            }
        }
    }
}
