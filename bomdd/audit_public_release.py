#!/usr/bin/env python3
"""ECO-057: repository publication-safety gate.

The default scan covers the working tree, including ignored media that could be
copied by a ZIP/manual upload. ``--history`` additionally scans every local Git
ref, commit message, path, and unique blob. Findings print only category+location;
matched values are deliberately not echoed.
"""

from __future__ import annotations

import argparse
import io
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SKIP_PARTS = {".git", "bin", "obj", "TestResults", ".vs", ".idea"}
MEDIA_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff",
    ".heic", ".avif", ".ico", ".mp4", ".mov", ".avi", ".mkv", ".webm",
    ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
    ".zip", ".7z", ".rar", ".tar", ".gz", ".db", ".sqlite", ".sqlite3",
}
ALLOWED_TRACKED_MEDIA = set()  # Add only reviewed, redistributable assets with provenance.


def private_terms() -> list[tuple[str, re.Pattern[str]]]:
    private_user = "".join(chr(x) for x in (97, 107, 105, 114, 97))
    # The public GitHub handle is the private first name + this suffix. Allow the
    # handle (e.g. in LICENSE / attribution) while still catching the bare name.
    public_handle_suffix = "".join(chr(x) for x in (109, 101, 105))
    real_fixture = "".join(chr(x) for x in (73, 77, 71, 95, 49, 51, 56, 54))
    third_party_title = "".join(chr(x) for x in (69, 78, 68, 70, 73, 69, 76, 68))
    private_filename = "".join(chr(x) for x in (22818, 20154))
    return [
        ("private-user", re.compile(
            re.escape(private_user) + rf"(?!{re.escape(public_handle_suffix)})", re.IGNORECASE)),
        ("real-image-fixture", re.compile(re.escape(real_fixture), re.IGNORECASE)),
        ("third-party-title", re.compile(re.escape(third_party_title), re.IGNORECASE)),
        ("private-image-name", re.compile(re.escape(private_filename) + r"\.(?:jpe?g|png)", re.IGNORECASE)),
    ]


GENERIC_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("private-key", re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----")),
    ("github-token", re.compile(r"gh[pousr]_[A-Za-z0-9]{20,}")),
    ("aws-access-key", re.compile(r"AKIA[0-9A-Z]{16}")),
    ("jwt", re.compile(r"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")),
    ("credential-assignment", re.compile(
        r"(?i)(?:api[_-]?key|client[_-]?secret|access[_-]?token|auth[_-]?token|password|passwd|pwd)"
        r"[\s\"']*[:=][\s\"']*[A-Za-z0-9_./+~$-]{8,}"
    )),
    ("private-profile-path", re.compile(r"(?i)[A-Z]:[\\/]Users[\\/](?!Demo(?:[\\/]|$))[^\\/\s\"']+")),
    ("unix-home-path", re.compile(r"(?<![A-Za-z0-9_<>])/(?:Users|home)/[^/\s\"']+/")),
    ("embedded-image", re.compile(r"data:image/[A-Za-z0-9.+-]+;base64,")),
]

MAGIC = [
    ("png", b"\x89PNG\r\n\x1a\n"), ("jpeg", b"\xff\xd8\xff"),
    ("gif", b"GIF8"), ("pdf", b"%PDF-"), ("zip-or-office", b"PK\x03\x04"),
    ("7z", b"7z\xbc\xaf\x27\x1c"), ("rar", b"Rar!\x1a\x07"),
]


def git(*args: str, input_bytes: bytes | None = None) -> bytes:
    return subprocess.run(
        ["git", *args], cwd=ROOT, input=input_bytes, stdout=subprocess.PIPE,
        stderr=subprocess.PIPE, check=True,
    ).stdout


def text_findings(text: str, location: str) -> list[str]:
    findings: list[str] = []
    for category, pattern in [*private_terms(), *GENERIC_PATTERNS]:
        if pattern.search(text):
            findings.append(f"{category}\t{location}")
    return findings


def magic_finding(data: bytes, location: str) -> str | None:
    for kind, signature in MAGIC:
        if data.startswith(signature):
            return f"binary-{kind}\t{location}"
    return None


def scan_worktree() -> list[str]:
    findings: list[str] = []
    tracked = {p.replace("\\", "/") for p in git("ls-files", "-z").decode().split("\0") if p}
    for path in ROOT.rglob("*"):
        if not path.is_file() or any(part in SKIP_PARTS for part in path.relative_to(ROOT).parts):
            continue
        rel = path.relative_to(ROOT).as_posix()
        data = path.read_bytes()
        if path.suffix.lower() in MEDIA_SUFFIXES and rel not in ALLOWED_TRACKED_MEDIA:
            scope = "tracked" if rel in tracked else "workspace"
            findings.append(f"{scope}-media\t{rel}")
        magic = magic_finding(data, rel)
        if magic and rel not in ALLOWED_TRACKED_MEDIA:
            findings.append(magic)
        if b"\x00" not in data[:8192]:
            findings.extend(text_findings(data.decode("utf-8", errors="replace"), rel))
    return findings


def scan_history() -> list[str]:
    findings: list[str] = []
    messages = git("log", "--all", "--format=%H%x00%B%x00").decode("utf-8", errors="replace")
    findings.extend(text_findings(messages, "git-commit-messages"))

    object_lines = git("rev-list", "--objects", "--all").decode("utf-8", errors="replace").splitlines()
    object_paths: dict[str, str] = {}
    for line in object_lines:
        object_id, _, object_path = line.partition(" ")
        if object_path and object_id not in object_paths:
            object_paths[object_id] = object_path

    check_input = "".join(f"{object_id}\n" for object_id in object_paths).encode()
    check = subprocess.run(
        ["git", "cat-file", "--batch-check=%(objectname) %(objecttype) %(objectsize)"],
        cwd=ROOT, input=check_input, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
    ).stdout.decode("ascii", errors="replace")
    blobs: list[tuple[str, str, int]] = []
    for line in check.splitlines():
        parts = line.split(" ", 2)
        if len(parts) != 3 or parts[1] != "blob":
            continue
        object_id, _, raw_size = parts
        try:
            size = int(raw_size)
        except ValueError:
            findings.append(f"unreadable-history-object\t{object_id}")
            continue
        object_path = object_paths[object_id]
        if size > 10 * 1024 * 1024:
            findings.append(f"oversized-history-blob\t{object_path}")
        else:
            blobs.append((object_id, object_path, size))

    batch_input = "".join(f"{object_id}\n" for object_id, _, _ in blobs).encode()
    batch = subprocess.run(
        ["git", "cat-file", "--batch"], cwd=ROOT, input=batch_input,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
    ).stdout
    stream = io.BytesIO(batch)
    for expected_id, object_path, expected_size in blobs:
        header = stream.readline().decode("ascii", errors="replace").strip().split()
        if len(header) != 3 or header[0] != expected_id or header[1] != "blob":
            findings.append(f"unreadable-history-object\t{expected_id}")
            break
        size = int(header[2])
        data = stream.read(size)
        stream.read(1)  # batch record newline
        if size != expected_size:
            findings.append(f"history-size-mismatch\t{object_path}")
        suffix = Path(object_path).suffix.lower()
        if suffix in MEDIA_SUFFIXES and object_path not in ALLOWED_TRACKED_MEDIA:
            findings.append(f"history-media\t{object_path}")
        magic = magic_finding(data, object_path)
        if magic and object_path not in ALLOWED_TRACKED_MEDIA:
            findings.append("history-" + magic)
        if b"\x00" not in data[:8192]:
            findings.extend(text_findings(data.decode("utf-8", errors="replace"), object_path))
    return findings


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--history", action="store_true", help="also scan every local Git ref and blob")
    parser.add_argument("--summary-only", action="store_true", help="print only the result and finding count")
    args = parser.parse_args()
    findings = scan_worktree()
    if args.history:
        findings.extend(scan_history())
    unique = sorted(set(findings))
    if unique:
        print(f"public-release audit: FAIL ({len(unique)} finding(s))")
        if not args.summary_only:
            for finding in unique:
                print(f"  {finding}")
        return 1
    print("public-release audit: PASS (0 findings)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
