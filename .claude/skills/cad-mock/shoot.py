#!/usr/bin/env python
"""CAD mock サーフェス撮影ツール(/cad-mock 同梱・再現性の恒久化)。

standalone mock HTML の `?face=<ID>` 単面を headless Edge で描画し、PIL で
背景をタイトに autocrop して captures PNG を生成する。

なぜツール化したか(ハマり所=毎回再導出すると事故る):
  1. msedge の --screenshot は **ファイルが遅延生成**される。起動プロセスは即 exit するが
     子プロセスが後から書き込むため、直後に存在確認すると「無い」。→ サイズ安定までポーリング。
     ポーリングは書込完了の検知であって描画完了の保証ではない → **--virtual-time-budget=30000**
     で遅延 DOM 描画(非同期 JS)を仮想時間で消化してから撮る(ViewPrismUI CLAUDE.md 手順 3)。
  2. window-size のビューポートを撮るだけなので、余白は autocrop で落とす(閾値/余白を固定)。
     autocrop の背景基準は BG トークン(左上ピクセル推定はコンテンツが (0,0) に接すると誤る)。
  3. 実寸を既存 captures と揃える(--force-device-scale-factor=1)。
  4. temp は実行ごとに一意な専用ディレクトリ(固定名の共有 temp は並列実行で競合)。
     subprocess 失敗は check=True で即 fail(黙殺しない)。失敗時は診断用に temp を残す。

使い方:
  python shoot.py --html "<mock.html>" --faces PD-5,PD-6 --outdir "<captures dir>" \
      [--win 1000,900] [--win-for PD-6=900,520] [--edge "<msedge.exe>"]

出力: <outdir>/<face>.png(タイト切り出し済み)。各面の寸法を stdout に出す。
"""
import argparse, subprocess, tempfile, pathlib, shutil, time, sys
from PIL import Image, ImageChops

DEFAULT_EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
BG = "eaedf1ff"  # mock sheet 背景(トークン)。autocrop の基準にもなる。
BG_RGB = tuple(int(BG[i:i + 2], 16) for i in (0, 2, 4))


def wait_stable(p, timeout=60):
    p = pathlib.Path(p)
    last, stable, t0 = -1, 0, time.time()
    while time.time() - t0 < timeout:
        if p.exists():
            sz = p.stat().st_size
            if sz > 0 and sz == last:
                stable += 1
                if stable >= 3:
                    return True
            else:
                stable = 0
            last = sz
        time.sleep(0.5)
    return p.exists() and p.stat().st_size > 0


def shoot(edge, base_uri, face, win, tmpd):
    tmp = tmpd / f"cap_{face}.png"
    if tmp.exists():
        tmp.unlink()
    cmd = [edge, "--headless=new", f"--screenshot={tmp}", f"--window-size={win}",
           "--force-device-scale-factor=1", "--hide-scrollbars", "--no-first-run",
           "--disable-gpu", "--virtual-time-budget=30000",
           f"--user-data-dir={tmpd / ('ud_' + face)}",
           f"--default-background-color={BG}", f"{base_uri}?face={face}"]
    subprocess.run(cmd, timeout=120, check=True)
    if not wait_stable(tmp):
        raise SystemExit(f"screenshot not produced (delayed write timed out): {tmp}")
    return tmp


def autocrop(src, dst, margin=12, thresh=12):
    im = Image.open(src).convert("RGB")
    bg = Image.new("RGB", im.size, BG_RGB)
    diff = ImageChops.difference(im, bg).convert("L").point(lambda p: 255 if p > thresh else 0)
    bbox = diff.getbbox()
    if not bbox:
        raise SystemExit(f"empty bbox (blank render?): {src}")
    l, t, r, b = bbox
    box = (max(0, l - margin), max(0, t - margin), min(im.width, r + margin), min(im.height, b + margin))
    im.crop(box).save(dst)
    return (box[2] - box[0], box[3] - box[1])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--html", required=True, help="standalone mock HTML の絶対パス")
    ap.add_argument("--faces", required=True, help="カンマ区切りの face ID(例 PD-5,PD-6)")
    ap.add_argument("--outdir", required=True, help="captures 出力ディレクトリ")
    ap.add_argument("--win", default="1000,900", help="既定 window-size(W,H)")
    ap.add_argument("--win-for", action="append", default=[], help="面別上書き 例 PD-6=900,520")
    ap.add_argument("--edge", default=DEFAULT_EDGE)
    a = ap.parse_args()

    per_face = {}
    for kv in a.win_for:
        k, v = kv.split("=", 1)
        per_face[k.strip()] = v.strip()

    base_uri = pathlib.Path(a.html).as_uri()
    outdir = pathlib.Path(a.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    tmpd = pathlib.Path(tempfile.mkdtemp(prefix="cad_shoot_"))

    for face in [f.strip() for f in a.faces.split(",") if f.strip()]:
        tmp = shoot(a.edge, base_uri, face, per_face.get(face, a.win), tmpd)
        w, h = autocrop(tmp, outdir / f"{face}.png")
        print(f"{face}.png  {w} x {h}")
    shutil.rmtree(tmpd, ignore_errors=True)  # 成功時のみ掃除(失敗時は診断用に残す)


if __name__ == "__main__":
    main()
