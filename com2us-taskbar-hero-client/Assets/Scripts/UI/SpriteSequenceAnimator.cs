using UnityEngine;
using UnityEngine.UI;

namespace TaskbarHero.Client.UI
{
    /// <summary>
    /// UI <see cref="Image"/>의 sprite를 프레임 배열로 순환 재생하는 스프라이트 시퀀스 애니메이터.
    /// 캠프파이어 불꽃 같은 프레임 애니메이션 이펙트에 사용한다.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SpriteSequenceAnimator : MonoBehaviour
    {
        [Tooltip("재생할 프레임(순서대로).")]
        [SerializeField] private Sprite[] frames;

        [Tooltip("초당 프레임 수.")]
        [SerializeField] private float fps = 15f;

        [Tooltip("반복 재생 여부.")]
        [SerializeField] private bool loop = true;

        [Tooltip("활성화 시 자동 재생.")]
        [SerializeField] private bool playOnEnable = true;

        private Image _image;
        private int _index;
        private float _timer;
        private bool _playing;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void OnEnable()
        {
            if (playOnEnable)
            {
                Play();
            }
        }

        /// <summary>프레임·재생 속도·반복 여부를 코드로 설정한다(에디터 빌더가 스프라이트 시퀀스를 배선할 때 사용).</summary>
        public void Configure(Sprite[] frames, float fps, bool loop)
        {
            this.frames = frames;
            this.fps = fps;
            this.loop = loop;
        }

        /// <summary>처음부터 재생을 시작한다.</summary>
        public void Play()
        {
            _playing = frames != null && frames.Length > 0;
            _index = 0;
            _timer = 0f;
            if (_playing)
            {
                _image.sprite = frames[0];
            }
        }

        private void Update()
        {
            if (!_playing || frames == null || frames.Length == 0)
            {
                return;
            }

            _timer += Time.deltaTime;
            float frameDuration = 1f / Mathf.Max(0.01f, fps);

            while (_timer >= frameDuration)
            {
                _timer -= frameDuration;
                _index++;

                if (_index >= frames.Length)
                {
                    if (loop)
                    {
                        _index = 0;
                    }
                    else
                    {
                        _index = frames.Length - 1;
                        _playing = false;
                        break;
                    }
                }

                _image.sprite = frames[_index];
            }
        }
    }
}
