using System.Collections.Generic;
using UnityEngine;

namespace TaskbarHero.Client.Managers
{
    /// <summary>
    /// 픽셀 폰트의 <b>아틀라스 텍스처 필터를 Point로 고정</b>해 글리프가 보간으로 뭉개지지 않게 하는 헬퍼.
    /// <para>동적 폰트(TTF)는 요청한 크기로 글리프를 아틀라스에 굽고, 화면에 그릴 때 텍스처를 샘플링한다.
    /// 기본 필터(Bilinear)는 획 경계를 부드럽게 섞어 픽셀 폰트를 흐릿하게 만들므로 Point로 바꾼다.
    /// <b>아틀라스는 새 글리프가 추가되면 다시 구워지며 텍스처가 교체되므로</b>
    /// <see cref="Font.textureRebuilt"/>를 한 번 구독해 두고 그때마다 다시 걸어 준다.</para>
    /// <para>같은 폰트를 여러 곳(전투 데미지 숫자·보스러시 HUD 타이머)에서 쓰므로, 각자 필터를 만지지 않고
    /// 이 헬퍼에 등록만 한다. 등록은 폰트당 한 번이면 되고 중복 호출은 무해하다.</para>
    /// </summary>
    public static class PixelFontAtlas
    {
        private static readonly HashSet<Font> Registered = new HashSet<Font>();
        private static bool _hooked;

        /// <summary>그 폰트의 아틀라스를 Point 필터로 유지한다(재생성 시에도 다시 적용).</summary>
        public static void KeepCrisp(Font font)
        {
            if (font == null)
            {
                return;
            }
            if (!_hooked)
            {
                Font.textureRebuilt += OnTextureRebuilt;
                _hooked = true;
            }
            Registered.Add(font);
            ApplyPoint(font);
        }

        /// <summary>아틀라스가 다시 구워진 폰트가 등록 대상이면 필터를 다시 Point로 돌린다.</summary>
        private static void OnTextureRebuilt(Font font)
        {
            if (font != null && Registered.Contains(font))
            {
                ApplyPoint(font);
            }
        }

        /// <summary>폰트 머티리얼이 들고 있는 아틀라스 텍스처의 필터를 Point로 바꾼다.</summary>
        private static void ApplyPoint(Font font)
        {
            var tex = font.material != null ? font.material.mainTexture : null;
            if (tex != null && tex.filterMode != FilterMode.Point)
            {
                tex.filterMode = FilterMode.Point;
            }
        }
    }
}
