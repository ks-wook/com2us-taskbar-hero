#!/usr/bin/env python3
r"""
Poe(OpenAI 호환 API)로 오디오 생성 봇을 호출해 효과음(SFX)을 생성·저장한다.
Poe API 키(POE_API_KEY 환경변수)만 사용한다(별도 ElevenLabs/Stability 키 불필요).

의존성 없음(파이썬 표준 라이브러리만 사용).

키 지정(둘 중 하나):
  - tools/poe-sfx/.env 파일에  POE_API_KEY=<Poe 키>  (권장, 로컬 전용 — .gitignore로 커밋 제외)
  - 또는 환경변수:  $env:POE_API_KEY = "<Poe 키>"   (환경변수가 .env보다 우선)

사용법:
  python generate_sound.py "single sword swing whoosh, metallic blade slash, dry" sword_swing.mp3
  python generate_sound.py "..." out.mp3 --duration 1
  python generate_sound.py "..." out.mp3 --model stable-audio-2.0
  python generate_sound.py "..." out.mp3 --env-file C:\path\to\.env

중요 — 반드시 "오디오 생성" 봇을 쓸 것(기본값 stable-audio-2.5):
- TTS(음성 합성) 봇(elevenlabs-v3, gemini-2.5-pro-tts, orpheus-tts 등)에게는 메시지 본문이
  곧 "낭독할 텍스트"다. 그래서 효과음 설명을 그대로 넘기면 그 설명을 사람 목소리로 읽은
  파일이 돌아온다(실제로 sword_swing.mp3가 11.9초 낭독 음성으로 생성된 적 있음).
  프롬프트를 어떻게 다듬어도 TTS 봇으로는 SFX를 만들 수 없으므로 모델을 바꿔야 한다.
- 이 스크립트는 --model 이 TTS 계열로 보이면 경고하고, 저장 후 길이를 검사해
  요청 길이보다 크게 길면(낭독으로 의심되면) 경고한다.

기타:
- 프롬프트는 영어가 더 잘 먹는다. "no music, no speech, dry, single hit" 처럼 배제 조건을 넣는다.
- stable-audio 계열은 기본 길이가 매우 길다(약 190초). 짧은 SFX는 --duration 을 꼭 지정한다.
- Poe가 미디어 결과를 반환하는 형식(첨부/URL)은 봇마다 다를 수 있어, URL을 못 찾으면 원본 응답을 출력한다.
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

BASE_URL = "https://api.poe.com/v1"
AUDIO_EXT = (".mp3", ".wav", ".ogg", ".m4a", ".flac", ".webm", ".aac")

# 기본 모델: Stability AI의 텍스트→오디오 생성 봇(SFX 생성용). TTS가 아니므로 낭독되지 않는다.
DEFAULT_MODEL = "stable-audio-2.5"

# 모델 이름이 이 조각을 포함하면 TTS(음성 합성) 계열로 보고 경고한다.
TTS_HINTS = ("tts", "elevenlabs-v", "speech", "voice", "whisper", "orpheus")

# MP3 프레임 헤더 해석 표(MPEG1 Layer3 기준) — 저장 결과 길이 검증용.
_MP3_BITRATES = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0]
_MP3_RATES = [44100, 48000, 32000, 0]
_MP3_MODES = ["stereo", "joint-stereo", "dual-mono", "mono"]


def load_env_file(path):
    """간단한 .env 로더(표준 라이브러리만). KEY=VALUE 라인을 읽어 os.environ에 채운다.
    이미 설정된 환경변수는 덮어쓰지 않는다(실제 환경변수 우선). 주석(#)·빈 줄·따옴표를 처리한다."""
    if not path or not os.path.isfile(path):
        return False
    with open(path, "r", encoding="utf-8") as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            if line.lower().startswith("export "):
                line = line[len("export "):].lstrip()
            key, val = line.split("=", 1)
            key = key.strip()
            val = val.strip().strip('"').strip("'")
            if key and key not in os.environ:
                os.environ[key] = val
    return True


def resolve_env_file(explicit):
    """사용할 .env 경로를 결정한다: --env-file 지정값 > 스크립트 폴더의 .env > 현재 폴더의 .env."""
    if explicit:
        return explicit
    here = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env")
    if os.path.isfile(here):
        return here
    return os.path.join(os.getcwd(), ".env")


def call_poe(api_key, model, prompt, base_url):
    """Poe OpenAI 호환 chat/completions 를 non-stream 으로 호출해 JSON 응답을 돌려준다."""
    body = json.dumps({
        "model": model,
        "messages": [{"role": "user", "content": prompt}],
        "stream": False,  # 미디어(이미지/오디오/비디오) 봇은 non-stream 권장(Poe 문서)
    }).encode("utf-8")
    req = urllib.request.Request(
        base_url.rstrip("/") + "/chat/completions",
        data=body,
        headers={
            "Authorization": "Bearer " + api_key,
            "Content-Type": "application/json",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=180) as resp:
        return json.loads(resp.read().decode("utf-8"))


def extract_media_url(data):
    """응답 메시지 content 에서 오디오 URL 을 찾는다(마크다운 링크/평문 URL). (url, content) 반환."""
    try:
        content = data["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError):
        return None, None
    if not isinstance(content, str):
        content = json.dumps(content, ensure_ascii=False)
    urls = re.findall(r"https?://[^\s)\]\"'>]+", content)
    for u in urls:  # 오디오 확장자 우선
        if u.lower().split("?")[0].endswith(AUDIO_EXT):
            return u, content
    return (urls[0] if urls else None), content


USER_AGENT = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
              "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")


def download_file(url, output):
    """오디오 URL을 내려받아 파일로 저장한다.
    poecdn 등 CDN이 기본 urllib 요청을 막으므로 브라우저 헤더를 붙이고, 실패 시 curl로 폴백한다."""
    out_dir = os.path.dirname(os.path.abspath(output))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    headers = {
        "User-Agent": USER_AGENT,
        "Accept": "*/*",
        "Accept-Language": "en-US,en;q=0.9",
        "Referer": "https://poe.com/",
    }
    # 1) urllib 시도(브라우저 헤더)
    try:
        req = urllib.request.Request(url, headers=headers)
        with urllib.request.urlopen(req, timeout=180) as resp:
            data = resp.read()
        with open(output, "wb") as f:
            f.write(data)
        return len(data)
    except Exception as e_urllib:  # noqa: BLE001
        # 2) curl 폴백(CDN 호환성이 더 좋음)
        import shutil
        import subprocess
        curl = shutil.which("curl")
        if not curl:
            raise
        cmd = [curl, "-fsSL", "-A", USER_AGENT, "-e", "https://poe.com/", "-o", output, url]
        r = subprocess.run(cmd, capture_output=True, text=True)
        if r.returncode != 0:
            raise RuntimeError("urllib 실패({}) + curl 실패(rc={}): {}".format(
                e_urllib, r.returncode, (r.stderr or "").strip()))
        return os.path.getsize(output)


def probe_mp3(path):
    """MP3 프레임 헤더를 훑어 (길이초, 비트레이트목록, 채널모드목록)을 돌려준다.
    표준 라이브러리만 쓰므로 디코딩은 하지 않고 프레임 헤더만 읽는다. MP3가 아니면 (None, [], [])."""
    with open(path, "rb") as f:
        d = f.read()
    off = 0
    if d[:3] == b"ID3":  # ID3v2 태그는 syncsafe 길이만큼 건너뛴다
        off = 10 + ((d[6] & 0x7F) << 21 | (d[7] & 0x7F) << 14 | (d[8] & 0x7F) << 7 | (d[9] & 0x7F))
    dur = 0.0
    bitrates, modes = set(), set()
    while off + 4 <= len(d):
        h = d[off:off + 4]
        if h[0] != 0xFF or (h[1] & 0xE0) != 0xE0:  # 프레임 싱크워드 아님
            off += 1
            continue
        bitrate = _MP3_BITRATES[(h[2] >> 4) & 0xF]
        rate = _MP3_RATES[(h[2] >> 2) & 0x3]
        if bitrate == 0 or rate == 0:  # 예약값 → 오탐이므로 건너뛴다
            off += 1
            continue
        bitrates.add(bitrate)
        modes.add(_MP3_MODES[(h[3] >> 6) & 0x3])
        dur += 1152.0 / rate  # Layer3 프레임 = 1152 샘플
        off += int(144000 * bitrate / rate) + ((h[2] >> 1) & 1)
    if dur == 0.0:
        return None, [], []
    return dur, sorted(bitrates), sorted(modes)


def verify_output(path, want_duration):
    """저장한 파일의 길이/채널을 검사해 사람이 알아야 할 이상 징후를 경고로 출력한다.
    요청 길이보다 훨씬 길면 TTS 낭독(프롬프트를 읽은 음성)일 가능성이 높다."""
    dur, bitrates, modes = probe_mp3(path)
    if dur is None:
        print("검증 생략: MP3 프레임을 찾지 못했습니다(다른 포맷일 수 있음).")
        return
    print("검증: 길이 {:.2f}초, {} kbps, {}".format(
        dur, "/".join(str(b) for b in bitrates), "/".join(modes)))
    if want_duration and dur > want_duration * 2:
        print("경고: 요청 {}초보다 훨씬 깁니다({:.2f}초). --duration 이 무시됐거나 "
              "프롬프트를 낭독한 TTS 결과일 수 있습니다. 재생해 확인하세요.".format(want_duration, dur))
    elif not want_duration and dur > 10:
        print("경고: {:.2f}초로 효과음치고 깁니다. --duration 으로 길이를 지정하세요.".format(dur))


def build_prompt(prompt, duration):
    """프롬프트에 길이 지시(--duration N)를 붙인다. 이미 들어 있으면 그대로 둔다.
    stable-audio 계열 봇은 프롬프트 안의 --duration 플래그로 생성 길이를 받는다."""
    if not duration or "--duration" in prompt:
        return prompt
    return "{} --duration {}".format(prompt, duration)


def main():
    ap = argparse.ArgumentParser(description="Poe로 효과음 생성(Poe 키만 사용)")
    ap.add_argument("prompt", help="생성할 사운드 설명(영어 권장, 'no music, no speech' 등 배제 조건 포함)")
    ap.add_argument("output", nargs="?", default="out.mp3", help="저장 파일 경로(기본 out.mp3)")
    ap.add_argument("--model", default=DEFAULT_MODEL,
                    help="Poe 봇 이름(기본 {} — 반드시 오디오 생성 봇, TTS 봇 금지)".format(DEFAULT_MODEL))
    ap.add_argument("--duration", type=float, default=None,
                    help="생성 길이(초). 짧은 SFX는 1~2 권장. 미지정 시 봇 기본값(매우 길 수 있음)")
    ap.add_argument("--base-url", default=BASE_URL)
    ap.add_argument("--env-file", default=None, help="키를 읽을 인수 .env 경로(기본: 스크립트 폴더의 .env)")
    args = ap.parse_args()

    if any(hint in args.model.lower() for hint in TTS_HINTS):
        print("경고: '{}' 은 TTS(음성 합성) 계열로 보입니다. TTS 봇은 프롬프트를 그대로 낭독한 "
              "음성을 반환하므로 효과음이 만들어지지 않습니다. --model {} 등 오디오 생성 봇을 쓰세요."
              .format(args.model, DEFAULT_MODEL))

    # .env 로드(이미 설정된 환경변수는 유지). 실제 환경변수 > .env 우선순위.
    load_env_file(resolve_env_file(args.env_file))

    api_key = os.environ.get("POE_API_KEY")
    if not api_key:
        sys.exit("오류: POE_API_KEY 를 찾을 수 없습니다. 환경변수로 설정하거나 tools/poe-sfx/.env 에 POE_API_KEY=... 를 추가하세요.")

    try:
        data = call_poe(api_key, args.model, build_prompt(args.prompt, args.duration), args.base_url)
    except urllib.error.HTTPError as e:
        sys.exit("Poe API 오류 {}: {}".format(e.code, e.read().decode("utf-8", "replace")))
    except urllib.error.URLError as e:
        sys.exit("네트워크 오류: {}".format(e))

    url, content = extract_media_url(data)
    if not url:
        sys.exit("오디오 URL을 찾지 못했습니다. 봇 이름(--model)·프롬프트를 확인하세요.\n원본 응답:\n"
                 + (content or json.dumps(data, ensure_ascii=False)))

    try:
        size = download_file(url, args.output)
    except urllib.error.HTTPError as e:
        sys.exit("다운로드 실패 {} {}: {}".format(url, e.code, e.reason))
    except Exception as e:  # noqa: BLE001
        sys.exit("다운로드 실패({}): {}".format(url, e))
    print("저장 완료: {}  ({:,} bytes, 원본: {})".format(args.output, size, url))
    verify_output(args.output, args.duration)


if __name__ == "__main__":
    main()
