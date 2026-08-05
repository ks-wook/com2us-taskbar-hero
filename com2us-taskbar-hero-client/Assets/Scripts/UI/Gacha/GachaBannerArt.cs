using System;
using UnityEngine;

namespace TaskbarHero.Client.UI.Gacha
{
    /// <summary>
    /// 배너 하나의 아트 묶음(<c>Assets/Art/UI/Gacha</c>). 파일명이 가챠 코드를 담고 있어
    /// (<c>gacha_banner_60001.png</c>·<c>gacha_btn_normal_60001.png</c>·<c>gacha_btn_selected_60001.png</c>)
    /// 에디터 빌더(<c>GachaUiBuilder</c>)가 폴더를 훑어 코드별로 이 항목을 만들어 배선한다.
    /// <para>배너를 추가할 때 <b>코드를 고칠 필요가 없다</b> — 같은 규칙으로 png 3장을 넣고 빌더를 다시 실행하면
    /// 새 배너의 탭·이미지가 배선된다. 마스터 데이터의 <c>bannerImage</c>(리소스 키)가 아니라 가챠 코드로 찾는 이유는
    /// 실제 파일명이 코드 기준이기 때문이며, 아트가 없는 배너는 폴백(단색 + 이름 텍스트)으로 그려진다.</para>
    /// </summary>
    [Serializable]
    public class GachaBannerArt
    {
        [Tooltip("가챠(배너) 코드 — gacha_master.gacha_code")]
        public int gachaCode;

        [Tooltip("배너 이미지(gacha_banner_<code>.png). 배너 이름이 아트에 새겨져 있어 이름 텍스트를 겹쳐 그리지 않는다. " +
                 "애니메이션 프레임(bannerFrames)이 있으면 그쪽이 우선이고 이 이미지는 폴백으로만 쓰인다.")]
        public Sprite banner;

        [Tooltip("움직이는 배너 프레임(BannerFrames_<code>/frame_000… 순서). 1장 이상이면 정적 이미지 대신 " +
                 "이 시퀀스를 반복 재생한다. Unity는 GIF를 첫 프레임짜리 정지 텍스처로만 임포트하므로, " +
                 "원본 GIF에서 뽑아 둔 프레임을 이 배열로 돌린다.")]
        public Sprite[] bannerFrames;

        [Tooltip("배너 프레임 재생 속도(초당 프레임). 원본 GIF의 프레임 간격과 같게 둔다(60002 = 12fps).")]
        public float bannerFps = 12f;

        [Tooltip("탭 버튼 기본 상태(gacha_btn_normal_<code>.png)")]
        public Sprite tabNormal;

        [Tooltip("탭 버튼 선택 상태(gacha_btn_selected_<code>.png)")]
        public Sprite tabSelected;
    }
}
