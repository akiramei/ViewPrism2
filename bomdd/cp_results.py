#!/usr/bin/env python3
"""Control Plan の行ごとの結果表(ECO-143・試行)。

機械受入(dotnet test tests/ViewPrism2.Tests)が出す xUnit v2+ XML(各テストの [Trait("cp","CP-…")] を含む)と
bomdd/33-control-plan.yaml を突き合わせ、CP の行ごとに区分を付けた表を出す。**判定ではない**(gate① 裁定 A=
承認に添えるだけ・機械では止めない)。区分の定義の正本= 60-change-order-eco-143.md §4.1:

    違反                    trait を持つテストに Fail が 1 件以上
    合格                    trait を持つテストが 1 件以上 Pass し、Fail 0(Skip は件数を併記)
    測定不能                trait を持つテストはあるが、全件 Skip / 未実行
    未実行(人の承認で検査)  trait を持つテストが無く、depth が G のみ
    未実行(検査なし)        trait を持つテストが無く、depth が unit / L1〜L3 を含む(unit+G も含む)

retired の行と、台帳に無い ID(trait にあり 33 に行が無い)は別欄。
結果ファイルが無い/読めない/テスト総数 0 は実行単位の測定不能= 行ごとの判定をしない。

終了コード: 0 = 表を出せた(違反があっても 0)/ 2 = 表を出せない(実行単位の測定不能・33 が読めない・引数の誤り)。
表の先頭に実行の素性(構成・終了日時)とテスト総数を出す — 古い結果ファイル・部分実行を今回の全件実行と取り違えないため。

使い方:
    python bomdd/cp_results.py                     # 既定の結果ファイルと 33 から表を出す
    python bomdd/cp_results.py --xml <path>        # 結果ファイルを指定
    python bomdd/cp_results.py --json              # 機械可読(同じ内容)
    python bomdd/cp_results.py --selftest          # 合成データで各区分の陽性対照
"""
import hashlib
import io
import json
import os
import sys
import tempfile
import xml.etree.ElementTree as ET

try:
    import yaml
except ImportError:
    print("FATAL: PyYAML required (pip install pyyaml)", file=sys.stderr)
    sys.exit(2)

BOMDD = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(BOMDD)
# ECO-143: csproj の既定引数(--results-directory / --report-xunit-filename)で固定した出力先
DEFAULT_XML = os.path.join(ROOT, "tests", "ViewPrism2.Tests", "TestResults", "cp-results.xml")
DEFAULT_CP = os.path.join(BOMDD, "33-control-plan.yaml")

VIOLATION, PASS, UNMEASURABLE = "違反", "合格", "測定不能"
NOT_RUN_HUMAN, NOT_RUN_NONE = "未実行(人の承認で検査)", "未実行(検査なし)"
ORDER = [VIOLATION, UNMEASURABLE, NOT_RUN_NONE, NOT_RUN_HUMAN, PASS]


class RunUnmeasurable(Exception):
    """実行単位の測定不能(行ごとの判定をしない)。"""


def load_run(xml_bytes):
    """xUnit v2+ XML(バイト列)→ (総数の辞書, {cp_id: [result, ...]}, 実行の素性)。読めなければ RunUnmeasurable。
    バイト列のまま解析する(復号の失敗を置換文字で黙って通さず、読めないとして出す)。"""
    try:
        root = ET.fromstring(xml_bytes)
    except ET.ParseError as e:
        raise RunUnmeasurable(f"結果ファイルを XML として読めない({e})")
    assemblies = root.findall("assembly") if root.tag == "assemblies" else ([root] if root.tag == "assembly" else [])
    if not assemblies:
        raise RunUnmeasurable("結果ファイルに assembly が無い")
    totals = {k: 0 for k in ("total", "passed", "failed", "skipped", "not-run", "errors")}
    by_cp = {}
    # 実行の素性(古い結果ファイル・別構成の結果を今回の実行と取り違えないため — ECO-143 R8 所見 1)
    origin = [{"assembly": os.path.basename(a.get("name", "")),
               "config": next((p for p in ("Release", "Debug") if f"\\{p}\\" in a.get("name", "")
                               or f"/{p}/" in a.get("name", "")), "不明"),
               "finished": a.get("finish-rtf") or f"{a.get('run-date', '')} {a.get('run-time', '')}".strip()}
              for a in assemblies]
    for asm in assemblies:
        for k in totals:
            try:
                totals[k] += int(asm.get(k, "0"))
            except ValueError:
                raise RunUnmeasurable(f"assembly の {k} が数値でない({asm.get(k)!r})")
        for t in asm.iter("test"):
            result = t.get("result", "")
            for tr in t.iter("trait"):
                if tr.get("name") == "cp" and tr.get("value"):
                    by_cp.setdefault(tr.get("value"), []).append(result)
    if totals["total"] == 0:
        raise RunUnmeasurable("テスト総数が 0(フィルタで全件が外れたか、実行が始まらなかった)")
    return totals, by_cp, origin


def human_only(depth):
    """depth が G だけ(自動の測定を持たない行)か。"""
    return isinstance(depth, str) and depth.strip() == "G"


def classify(rows, by_cp):
    """CP の行 × テスト結果 → 区分。rows は 33 の characteristics。"""
    out, retired = [], []
    ids = set()
    for r in rows:
        if not isinstance(r, dict) or not r.get("id"):
            continue
        cid = r["id"]
        ids.add(cid)
        results = by_cp.get(cid, [])
        if r.get("status") == "retired":
            retired.append({"id": cid, "retired_by": r.get("retired_by"), "tests": len(results),
                            "fail": sum(1 for x in results if x == "Fail")})
            continue
        n_pass = sum(1 for x in results if x == "Pass")
        n_fail = sum(1 for x in results if x == "Fail")
        n_other = len(results) - n_pass - n_fail
        if not results:
            cat = NOT_RUN_HUMAN if human_only(r.get("depth")) else NOT_RUN_NONE
        elif n_fail:
            cat = VIOLATION
        elif n_pass:
            cat = PASS
        else:
            cat = UNMEASURABLE
        out.append({"id": cid, "category": cat, "depth": r.get("depth"),
                    "characteristic": str(r.get("characteristic") or "").strip(),
                    "tests": len(results), "pass": n_pass, "fail": n_fail, "skip_or_not_run": n_other})
    orphans = sorted((cid, len(v), sum(1 for x in v if x == "Fail")) for cid, v in by_cp.items() if cid not in ids)
    return out, retired, orphans


def build(xml_path, cp_path):
    """表の元データ。実行単位の測定不能は RunUnmeasurable、33 が読めなければ ValueError。"""
    try:
        with open(cp_path, encoding="utf-8") as f:
            rows = (yaml.safe_load(f) or {}).get("control_plan", {}).get("characteristics")
    except (OSError, yaml.YAMLError) as e:
        raise ValueError(f"33 を読めない({e})")
    if not isinstance(rows, list) or not rows:
        raise ValueError("33 の control_plan.characteristics が無い・空")
    if not os.path.isfile(xml_path):
        raise RunUnmeasurable(f"結果ファイルが無い({xml_path})")
    with open(xml_path, "rb") as f:
        raw = f.read()
    totals, by_cp, origin = load_run(raw)
    out, retired, orphans = classify(rows, by_cp)
    return {"xml": xml_path, "xml_sha256": hashlib.sha256(raw).hexdigest(), "totals": totals, "origin": origin,
            "rows": out, "retired": retired, "orphans": orphans}


def render(d):
    t = d["totals"]
    counts = {c: sum(1 for r in d["rows"] if r["category"] == c) for c in ORDER}
    L = ["# Control Plan の行ごとの結果(ECO-143)", "",
         f"- 結果ファイル: `{os.path.relpath(d['xml'], ROOT)}`(sha256 {d['xml_sha256'][:12]})",
         "- **実行の素性: " + "; ".join(f"{o['assembly']}・{o['config']}・終了 {o['finished']}" for o in d["origin"])
         + "** — 今回の実行の日時・構成か確認する(古い結果ファイルが残っていれば、それは今回の表ではない)",
         f"- **実行のテスト総数: {t['total']}**(合格 {t['passed']}・不合格 {t['failed']}・skip {t['skipped']}・"
         f"未実行 {t['not-run']}・エラー {t['errors']})— dotnet test の出力の合計と一致するか確認する"
         "(フィルタ付きの部分実行なら、全件実行の表ではない)",
         "- 区分: " + " / ".join(f"{c} {counts[c]}" for c in ORDER),
         "- この表は判定ではない(gate① 裁定 A)。区分の定義= 60-change-order-eco-143.md §4.1", ""]
    for c in ORDER:
        rows = [r for r in d["rows"] if r["category"] == c]
        if not rows:
            continue
        L.append(f"## {c}({len(rows)})")
        L.append("")
        if c == PASS:
            L.append("、".join(f"{r['id']}({r['tests']})" for r in rows))
            L.append("")
            continue
        L.append("| ID | depth | テスト(Pass/Fail/他) | 特性(先頭) |")
        L.append("|---|---|---|---|")
        for r in rows:
            ch = r["characteristic"].replace("\n", " ").replace("|", "/")
            ch = ch[:60] + ("…" if len(ch) > 60 else "")
            L.append(f"| {r['id']} | {r['depth']} | {r['tests']}({r['pass']}/{r['fail']}/{r['skip_or_not_run']}) | {ch} |")
        L.append("")
    if d["retired"]:
        L.append("## 別欄: retired の行")
        L.append("")
        L.append("、".join(f"{r['id']}(retired_by {r['retired_by']}・テスト {r['tests']}・Fail {r['fail']})" for r in d["retired"]))
        L.append("")
    if d["orphans"]:
        L.append("## 別欄: 台帳に無い ID(trait にあり 33 に行が無い)")
        L.append("")
        L.append("、".join(f"{cid}(テスト {n}・Fail {nf})" for cid, n, nf in d["orphans"]))
        L.append("")
    return "\n".join(L)


# ---- 陽性対照(--selftest)— 各区分・別欄・実行単位の測定不能が期待どおりに出るか ----
_ST_CP = {"control_plan": {"characteristics": [
    {"id": "CP-A", "depth": "unit", "characteristic": "pass"},
    {"id": "CP-B", "depth": "unit", "characteristic": "fail+pass"},
    {"id": "CP-C", "depth": "L2", "characteristic": "skip only"},
    {"id": "CP-D", "depth": "G", "characteristic": "human only"},
    {"id": "CP-E", "depth": "unit+G", "characteristic": "no test but automated depth"},
    {"id": "CP-F", "depth": "G", "status": "retired", "retired_by": "ECO-000", "characteristic": "retired"},
    {"id": "CP-G", "depth": "unit", "characteristic": "pass+skip"},
    {"id": "CP-H", "depth": "unit", "characteristic": "second cp trait on a multi-trait test"},
    {"id": "CP-I", "depth": "unit", "characteristic": "not-run only"},
]}}


def _st_xml(tests, total=None):
    body = "".join(
        f'<test name="T{i}" result="{res}"><traits>'
        + "".join(f'<trait name="cp" value="{c}" />' for c in cps)
        + '<trait name="other" value="x" /></traits></test>'
        for i, (res, cps) in enumerate(tests))
    n = len(tests) if total is None else total
    return (f'<assemblies><assembly name="C:\\r\\tests\\X.Tests\\bin\\Debug\\net10.0\\X.Tests.dll" '
            f'finish-rtf="2026-10-02T00:00:00+09:00" total="{n}" passed="0" failed="0" skipped="0" not-run="0" errors="0">'
            f'<collection>{body}</collection></assembly></assemblies>')


def selftest():
    ok = True

    def expect(cond, msg):
        nonlocal ok
        if not cond:
            print(f"selftest FAIL: {msg}")
            ok = False

    tests = [("Pass", ["CP-A"]), ("Fail", ["CP-B"]), ("Pass", ["CP-B"]), ("Skip", ["CP-C"]),
             ("Fail", ["CP-F"]), ("Pass", ["CP-Z"]), ("Pass", ["CP-G"]), ("Skip", ["CP-G"]),
             ("Pass", []), ("NotRun", ["CP-C"]), ("Pass", ["CP-A", "CP-H"]), ("NotRun", ["CP-I"]),
             ("Fail", ["CP-Y"])]
    with tempfile.TemporaryDirectory() as td:
        cp = os.path.join(td, "33.yaml")
        with open(cp, "w", encoding="utf-8") as f:
            yaml.safe_dump(_ST_CP, f, allow_unicode=True)
        xmlp = os.path.join(td, "r.xml")
        with open(xmlp, "w", encoding="utf-8") as f:
            f.write(_st_xml(tests))
        d = build(xmlp, cp)
        cat = {r["id"]: r["category"] for r in d["rows"]}
        expect(cat.get("CP-A") == PASS, f"CP-A は合格のはず({cat.get('CP-A')})")
        expect(cat.get("CP-B") == VIOLATION, f"CP-B は違反のはず(Fail 1 件で違反)({cat.get('CP-B')})")
        expect(cat.get("CP-C") == UNMEASURABLE, f"CP-C は測定不能のはず(全件 Skip/NotRun)({cat.get('CP-C')})")
        expect(cat.get("CP-D") == NOT_RUN_HUMAN, f"CP-D は未実行(人の承認)のはず({cat.get('CP-D')})")
        expect(cat.get("CP-E") == NOT_RUN_NONE, f"CP-E は未実行(検査なし)のはず — unit+G は人の承認に畳まない({cat.get('CP-E')})")
        expect(cat.get("CP-G") == PASS, f"CP-G は合格のはず(Pass があれば Skip 併存でも合格)({cat.get('CP-G')})")
        expect(cat.get("CP-H") == PASS, f"複数の cp trait を持つテストは各行に数えるはず(CP-H)({cat.get('CP-H')})")
        expect(next(r for r in d["rows"] if r["id"] == "CP-A")["tests"] == 2, "CP-A は単独+複数 trait の 2 件のはず")
        expect(cat.get("CP-I") == UNMEASURABLE, f"全件 NotRun の CP-I は測定不能のはず({cat.get('CP-I')})")
        expect("CP-F" not in cat and [(r["id"], r["fail"]) for r in d["retired"]] == [("CP-F", 1)],
               "retired の CP-F は別欄で、Fail 件数を見せるはず")
        expect(d["orphans"] == [("CP-Y", 1, 1), ("CP-Z", 1, 0)], f"台帳に無い ID は CP-Y(Fail 1)・CP-Z のはず({d['orphans']})")
        g = next(r for r in d["rows"] if r["id"] == "CP-G")
        expect(g["skip_or_not_run"] == 1, "合格行の Skip 件数を併記するはず")
        expect(d["origin"][0]["config"] == "Debug" and d["origin"][0]["finished"].startswith("2026-10-02"),
               f"実行の素性(構成・終了日時)を取り出すはず({d['origin']})")
        md = render(d)
        expect(f"実行のテスト総数: {len(tests)}" in md, "表の先頭に実行のテスト総数を出すはず")
        expect("Debug・終了 2026-10-02" in md, "表の先頭に実行の構成と終了日時を出すはず")
        # 実行単位の測定不能: 無い/読めない/総数 0
        for name, content, why in [("missing.xml", None, "無い"), ("bad.xml", "<assemblies><assembly", "読めない"),
                                   ("zero.xml", _st_xml([], total=0), "総数 0"), ("empty.xml", "<assemblies/>", "assembly なし"),
                                   ("badenc.xml", b'<?xml version="1.0" encoding="utf-8"?><assemblies><assembly total="1">'
                                    b'<test result="Pass"><traits><trait name="cp" value="CP-\xff" /></traits></test>'
                                    b'</assembly></assemblies>', "UTF-8 として不正")]:
            p = os.path.join(td, name)
            if content is not None:
                with open(p, "wb") as f:
                    f.write(content if isinstance(content, bytes) else content.encode("utf-8"))
            try:
                build(p, cp)
                expect(False, f"結果ファイルが{why}のとき実行単位の測定不能にならない")
            except RunUnmeasurable:
                pass
        # 33 が読めない → ValueError(表を出せない)
        bad_cp = os.path.join(td, "bad33.yaml")
        with open(bad_cp, "w", encoding="utf-8") as f:
            f.write("control_plan: [unclosed")
        try:
            build(xmlp, bad_cp)
            expect(False, "33 が読めないとき止まらない")
        except ValueError:
            pass
    print("selftest: OK" if ok else "selftest: FAILED")
    return 0 if ok else 1


def main(argv):
    if "--selftest" in argv:
        return selftest()
    xml_path, cp_path = DEFAULT_XML, DEFAULT_CP
    for opt in ("--xml", "--cp"):
        if opt in argv:
            i = argv.index(opt)
            if i + 1 >= len(argv) or argv[i + 1].startswith("--"):
                print(f"FATAL: {opt} にパスが無い", file=sys.stderr)
                return 2
            if opt == "--xml":
                xml_path = os.path.abspath(argv[i + 1])
            else:
                cp_path = os.path.abspath(argv[i + 1])
    try:
        d = build(xml_path, cp_path)
    except RunUnmeasurable as e:
        print(f"# Control Plan の行ごとの結果(ECO-143)\n\n**実行単位の測定不能**: {e}\n"
              "行ごとの判定はしていない(全行が測定不能)。機械受入のテストを正規の経路で実行し直す。")
        return 2
    except ValueError as e:
        print(f"FATAL: {e}", file=sys.stderr)
        return 2
    print(json.dumps(d, ensure_ascii=False, indent=1) if "--json" in argv else render(d))
    return 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "buffer"):
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    if hasattr(sys.stderr, "buffer"):
        sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
