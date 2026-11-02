"""按名字拉取 Microsoft-Rocketbox 角色：FBX + 贴图（TGA→PNG 2048），git blob SHA-1 校验。

用法（在仓库根目录执行）：
    python Tools/fetch_rocketbox_avatar.py Male_Adult_01
    python Tools/fetch_rocketbox_avatar.py Female_Child_01 --group Children

GitHub 的 raw.githubusercontent.com 在部分网络下不可达，这里统一走 api.github.com 的 blobs 接口。
下载物落在 Assets/Characters/Rocketbox/Resources/Characters/Rocketbox/，与现有 Female_Adult_01 同构。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
import urllib.request
from io import BytesIO
from pathlib import Path

REPO = "microsoft/Microsoft-Rocketbox"
API = f"https://api.github.com/repos/{REPO}"
DEST = (
    Path(__file__).resolve().parents[1]
    / "Assets/Characters/Rocketbox/Resources/Characters/Rocketbox"
)


def _get(url: str, accept: str, attempts: int = 4) -> bytes:
    last: Exception | None = None
    for attempt in range(1, attempts + 1):
        try:
            request = urllib.request.Request(
                url, headers={"Accept": accept, "User-Agent": "dsh-agent"}
            )
            with urllib.request.urlopen(request, timeout=300) as response:
                declared = response.headers.get("Content-Length")
                chunks: list[bytes] = []
                while True:
                    chunk = response.read(1 << 20)
                    if not chunk:
                        break
                    chunks.append(chunk)
                body = b"".join(chunks)
                if declared is not None and len(body) != int(declared):
                    raise IOError(f"截断：收到 {len(body)} / 声明 {declared} 字节")
                return body
        except Exception as error:  # 网络抖动 / IncompleteRead，重试
            last = error
            print(f"retry {attempt}/{attempts}: {error}", file=sys.stderr)
            time.sleep(2 * attempt)
    raise SystemExit(f"下载失败：{url}（{last}）")


def api_json(url: str) -> dict:
    return json.loads(_get(url, "application/vnd.github+json").decode("utf-8"))


def blob(sha: str) -> bytes:
    return _get(f"{API}/git/blobs/{sha}", "application/vnd.github.raw")


def git_blob_sha(data: bytes) -> str:
    header = b"blob %d\0" % len(data)
    return hashlib.sha1(header + data).hexdigest()


def repo_tree() -> dict[str, dict]:
    payload = api_json(f"{API}/git/trees/master?recursive=1")
    if payload.get("truncated"):
        raise SystemExit("仓库树被截断，拒绝继续（无法保证完整性校验）")
    return {item["path"]: item for item in payload["tree"] if item["type"] == "blob"}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("name", help="例如 Male_Adult_01")
    parser.add_argument("--group", default="Adults", help="Adults / Children / Professions")
    parser.add_argument("--max-size", type=int, default=2048)
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument(
        "--prefix",
        default="",
        help="按贴图前缀补抓（如 m002 / cf001）：资产里贴图目录名与角色目录名不一定一致",
    )
    args = parser.parse_args()

    from PIL import Image  # 只在校验通过后才需要

    entries = repo_tree()
    DEST.mkdir(parents=True, exist_ok=True)

    wanted: dict[str, Path] = {
        f"Assets/Avatars/{args.group}/{args.name}/Export/{args.name}.fbx": DEST / f"{args.name}.fbx",
        f"Assets/Avatars/{args.group}/{args.name}/{args.name}.png": DEST / f"{args.name}_portrait.png",
    }
    marker = f"/{args.name}/textures/".lower()
    prefix = args.prefix.lower().strip()
    for path in entries:
        low = path.lower()
        if not low.endswith(".tga"):
            continue
        if marker in low or (prefix and Path(low).name.startswith(prefix + "_")):
            wanted[path] = DEST / (Path(path).stem.lower() + ".png")

    if len(wanted) < 3:
        print(f"没找到 {args.name} 的资产（找到 {len(wanted)} 个），检查 --group", file=sys.stderr)
        return 1

    total = 0
    for path, out in sorted(wanted.items()):
        if out.exists() and not args.overwrite:
            print(f"skip  {out.name}")
            total += out.stat().st_size
            continue

        entry = entries[path]
        data = b""
        for attempt in range(1, 4):  # 传输截断时长度/哈希都对不上，重下一次
            data = blob(entry["sha"])
            if len(data) == entry["size"] and git_blob_sha(data) == entry["sha"]:
                break
            print(
                f"retry {attempt}/3 {Path(path).name}: "
                f"len {len(data)} != {entry['size']}",
                file=sys.stderr,
            )
            data = b""
            time.sleep(2 * attempt)
        if not data:
            print(f"FAIL  download integrity: {path}", file=sys.stderr)
            return 1

        if out.suffix == ".png":
            image = Image.open(BytesIO(data))
            image.thumbnail((args.max_size, args.max_size), Image.LANCZOS)
            image = image.convert("RGB") if image.mode in ("P", "LA") else image
            image.save(out, "PNG", optimize=True)
        else:
            out.write_bytes(data)

        total += out.stat().st_size
        print(f"ok    {out.name:34s} {out.stat().st_size / 1048576:6.2f} MB")

    print(f"{args.name}: {len(wanted)} 个文件，合计 {total / 1048576:.1f} MB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
