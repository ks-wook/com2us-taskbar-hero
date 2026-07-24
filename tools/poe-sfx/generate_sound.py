#!/usr/bin/env python3
r"""
Poe(OpenAI 호환 API)로 오디오 봇(예: ElevenLabs-v3)을 호출해 사운드를 생성·저장한다.
Poe API 키(POE_API_KEY 환경변수)만 사용한다(별도 ElevenLabs 키 불필요).

의존성 없음(파이썬 표준 라이브러리만 사용).

키 지정(둘 중 하나):
  - tools/poe-sfx/.env 파일에  POE_API_KEY=<Poe 키>  (권장, 로컬 전용 — .gitignore로 커밋 제외)
  - 또는 환경변수:  $env:POE_API_KEY = "<Poe 키>"   (환경변수가 .env보다 우선)

사용법:
  python generate_sound.py "검 스윙 효과음, 금속 마찰음, 약 400ms" sword_swing.mp3
  python generate_sound.py "..." out.mp3 --model ElevenLabs-v3
  python generate_sound.py "..." out.mp3 --env-file C:\path\to\.env

주의:
- 정확한 Poe 봇 이름은 poe.com에서 확인해 --model 로 지정한다(기본 "ElevenLabs-v3").
- ElevenLabs v3는 주로 TTS(음성)다. 비음성 효과음(SFX)은 poe.com의 사운드이펙트 계열 봇이 더 적합할 수 있다.
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


def main():
    ap = argparse.ArgumentParser(description="Poe로 사운드 생성(Poe 키만 사용)")
    ap.add_argument("prompt", help="생성할 사운드 설명")
    ap.add_argument("output", nargs="?", default="out.mp3", help="저장 파일 경로(기본 out.mp3)")
    ap.add_argument("--model", default="ElevenLabs-v3", help="Poe 봇 이름(기본 ElevenLabs-v3)")
    ap.add_argument("--base-url", default=BASE_URL)
    ap.add_argument("--env-file", default=None, help="키를 읽을 .env 경로(기본: 스크립트 폴더의 .env)")
    args = ap.parse_args()

    # .env 로드(이미 설정된 환경변수는 유지). 실제 환경변수 > .env 우선순위.
    load_env_file(resolve_env_file(args.env_file))

    api_key = os.environ.get("POE_API_KEY")
    if not api_key:
        sys.exit("오류: POE_API_KEY 를 찾을 수 없습니다. 환경변수로 설정하거나 tools/poe-sfx/.env 에 POE_API_KEY=... 를 추가하세요.")

    try:
        data = call_poe(api_key, args.model, args.prompt, args.base_url)
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


if __name__ == "__main__":
    main()
