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
さらに、33 の requirement_refs / invariant_refs とテストの req / inv trait を突き合わせ、
裁定層の ID ごとの結果表を既存表の後ろに出す。行の合格に埋もれる ID 単位の測定不能も表示する。
refs に無い req / inv trait は裁定層の別欄に出す。
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
import re
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
DEFAULT_EBOM = os.path.join(BOMDD, "30-ebom.yaml")

VIOLATION, PASS, UNMEASURABLE = "違反", "合格", "測定不能"
NOT_RUN_HUMAN, NOT_RUN_NONE = "未実行(人の承認で検査)", "未実行(検査なし)"
ORDER = [VIOLATION, UNMEASURABLE, NOT_RUN_NONE, NOT_RUN_HUMAN, PASS]


class RunUnmeasurable(Exception):
    """実行単位の測定不能(行ごとの判定をしない)。"""


class CpResults(dict):
    """既存の CP 結果辞書。ruled に req / inv の結果を併載して戻り値の形を保つ。"""

    def __init__(self):
        super().__init__()
        self.ruled = {}


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
    by_cp = CpResults()
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
                name, value = tr.get("name"), tr.get("value")
                if name == "cp" and value:
                    by_cp.setdefault(value, []).append(result)
                elif name in ("req", "inv") and isinstance(value, str) and value.strip():
                    by_cp.ruled.setdefault(value.strip(), []).append(result)
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


def load_ebom_items(ebom_path):
    """E-BOM の items を読む。読めない場合は空と理由を返す。"""
    try:
        with open(ebom_path, encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
    except (OSError, ValueError, yaml.YAMLError) as e:
        return [], str(e)
    try:
        items = data.get("ebom", {}).get("items")
    except AttributeError:
        items = None
    if not isinstance(items, list):
        return [], "ebom.items が無い・list でない"
    return items, None


def classify_ruled(rows, by_id, ebom_items=None):
    """33 の active 行と、それを受入に持つ E 品目が負う ID を ID 単位で区分する。"""
    refs = {}
    participating_rows = set()
    for r in rows:
        if not isinstance(r, dict) or not r.get("id") or r.get("status") == "retired":
            continue
        row_has_ref = False
        for key in ("requirement_refs", "invariant_refs"):
            values = r.get(key)
            if not isinstance(values, list):
                continue
            for rid in values:
                if isinstance(rid, str) and rid.strip():
                    rid = rid.strip()
                    refs.setdefault(rid, []).append((r["id"], r.get("depth")))
                    row_has_ref = True
        if row_has_ref:
            participating_rows.add(r["id"])
    ebom_by_id = {rid: [] for rid in refs}
    via_rows_by_id = {rid: {} for rid in refs}
    for item in ebom_items or []:
        if (not isinstance(item, dict) or not isinstance(item.get("id"), str) or not item["id"]
                or item.get("lifecycle_state") in ("retired", "superseded")):
            continue
        acceptance = item.get("acceptance_refs")
        accepted_rows = ([cp for cp in acceptance
                          if isinstance(cp, str) and cp in participating_rows]
                         if isinstance(acceptance, list) else [])
        if not accepted_rows:
            continue
        carried = []
        requirements = item.get("requirement_refs")
        if isinstance(requirements, list):
            carried.extend(rid.strip() for rid in requirements
                           if isinstance(rid, str) and rid.strip())
        invariants = item.get("invariants")
        if isinstance(invariants, list):
            for line in invariants:
                if isinstance(line, (str, dict)):
                    carried.extend(re.findall(r"\bINV-[A-Z]*\d+\b", str(line)))
        for rid in carried:
            refs.setdefault(rid, [])
            owners = ebom_by_id.setdefault(rid, [])
            if item["id"] not in owners:
                owners.append(item["id"])
            via_rows_by_id.setdefault(rid, {})[item["id"]] = list(dict.fromkeys(accepted_rows))
    out = []
    for rid, referenced in refs.items():
        results = by_id.get(rid, [])
        n_pass = sum(1 for x in results if x == "Pass")
        n_fail = sum(1 for x in results if x == "Fail")
        n_other = len(results) - n_pass - n_fail
        if n_fail:
            cat = VIOLATION
        elif n_pass:
            cat = PASS
        elif results:
            cat = UNMEASURABLE
        elif referenced and all(human_only(depth) for _, depth in referenced):
            cat = NOT_RUN_HUMAN
        else:
            cat = NOT_RUN_NONE
        out.append({"id": rid, "category": cat, "tests": len(results), "pass": n_pass,
                    "fail": n_fail, "skip_or_not_run": n_other,
                    "rows": [row_id for row_id, _ in referenced],
                    "ebom_items": ebom_by_id.get(rid, []),
                    "via_rows": via_rows_by_id.get(rid, {})})
    out.sort(key=lambda r: (ORDER.index(r["category"]), r["id"]))
    orphans = [{"id": rid, "tests": len(results),
                "fail": sum(1 for x in results if x == "Fail")}
               for rid, results in sorted(by_id.items()) if rid not in refs]
    return out, orphans


def build(xml_path, cp_path, ebom_path=None):
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
    result = {"xml": xml_path, "xml_sha256": hashlib.sha256(raw).hexdigest(), "totals": totals, "origin": origin,
            "rows": out, "retired": retired, "orphans": orphans}
    ebom_unreadable = None
    try:
        if ebom_path is None:
            ebom_items, ebom_unreadable = [], "E-BOM のパスが渡されていない"
        else:
            ebom_items, ebom_unreadable = load_ebom_items(ebom_path)
        ruled_ids, ruled_orphans = classify_ruled(rows, by_cp.ruled, ebom_items)
        result.update({"ruled_ids": ruled_ids, "ruled_orphans": ruled_orphans})
    except Exception as e:
        result.update({"ruled_ids": [], "ruled_orphans": [],
                       "ruled_error": f"{type(e).__name__}: {e}"})
    if ebom_unreadable:
        result["ebom_unreadable"] = ebom_unreadable
    return result


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
    L.append("# 裁定層の ID ごとの結果(ECO-145)")
    L.append("")
    if d.get("ruled_error"):
        L.append(f"- **ID ごとの表を作れない({d['ruled_error']})— 行ごとの表だけを出した**")
        return "\n".join(L)
    if d.get("ebom_unreadable"):
        L.append(f"- **E-BOM を読めない({d['ebom_unreadable']})— 下の表は 33 の行の refs だけから作った。E 品目が負う ID の届かないものは見えていない**")
    if not d["ruled_ids"]:
        L.append("refs を持つ行が無い")
    else:
        counts = {c: sum(1 for r in d["ruled_ids"] if r["category"] == c) for c in ORDER}
        L.append("- 区分: " + " / ".join(f"{c} {counts[c]}" for c in ORDER))
        L.append("- 対象は、33 の行が requirement_refs / invariant_refs で参照する ID と、その行を acceptance_refs に持つ E 品目が負う ID(requirement_refs・invariants の INV)。他の行のテストが検査していても、trait req / inv が無ければここでは「検査なし」と出る。判定ではない")
        L.append("")
        L.append("| ID | 区分 | テスト(Pass/Fail/他) | 参照する CP 行 | 負う E 品目 |")
        L.append("|---|---|---|---|---|")
        for r in d["ruled_ids"]:
            cp_rows = ", ".join(str(x) for x in r["rows"]) if r["rows"] else "(どの行の refs にも無い)"
            if r["ebom_items"]:
                owners = ", ".join(
                    (f"{item}({', '.join(str(x) for x in r['via_rows'].get(item, []))} 経由)"
                     if not r["rows"] and r["via_rows"].get(item) else str(item))
                    for item in r["ebom_items"])
            else:
                owners = "-"
            L.append(f"| {r['id']} | {r['category']} | {r['tests']}({r['pass']}/{r['fail']}/{r['skip_or_not_run']}) | {cp_rows} | {owners} |")
        L.append("")
    if d["ruled_orphans"]:
        L.append("## 別欄: どの行も参照しない ID(trait にあり 33 の refs に無い)")
        L.append("")
        L.append("、".join(f"{str(r['id'])}(テスト {r['tests']}・Fail {r['fail']})" for r in d["ruled_orphans"]))
        L.append("")
    return "\n".join(L)


# ---- 陽性対照(--selftest)— 各区分・別欄・実行単位の測定不能が期待どおりに出るか ----
_ST_CP = {"control_plan": {"characteristics": [
    {"id": "CP-A", "depth": "unit", "characteristic": "pass", "requirement_refs": ["REQ-PASS", "REQ-TWO", None, 17]},
    {"id": "CP-B", "depth": "unit", "characteristic": "fail+pass", "requirement_refs": ["REQ-FAIL", "REQ-SKIP", "REQ-NONE", "REQ-MIXED"]},
    {"id": "CP-C", "depth": "L2", "characteristic": "skip only", "requirement_refs": "REQ-IGNORED"},
    {"id": "CP-D", "depth": "G", "characteristic": "human only", "requirement_refs": ["REQ-HUMAN", "REQ-MIXED"]},
    {"id": "CP-E", "depth": "unit+G", "characteristic": "no test but automated depth"},
    {"id": "CP-F", "depth": "G", "status": "retired", "retired_by": "ECO-000", "characteristic": "retired", "requirement_refs": ["REQ-RETIRED"]},
    {"id": "CP-G", "depth": "unit", "characteristic": "pass+skip", "requirement_refs": ["REQ-SKIP", " REQ-SPACE "]},
    {"id": "CP-H", "depth": "unit", "characteristic": "second cp trait on a multi-trait test"},
    {"id": "CP-I", "depth": "unit", "characteristic": "not-run only", "requirement_refs": ["REQ-NOTRUN"]},
    {"id": "CP-J", "depth": "unit", "characteristic": "invariant ref", "invariant_refs": ["INV-CP1"]},
]}}


def _st_xml(tests, total=None):
    body = ""
    for i, test in enumerate(tests):
        res, cps = test[:2]
        ruled = test[2] if len(test) > 2 else []
        body += (f'<test name="T{i}" result="{res}"><traits>'
                 + "".join(f'<trait name="cp" value="{c}" />' for c in cps)
                 + "".join(f'<trait name="{name}" value="{value}" />' for name, value in ruled)
                 + '<trait name="other" value="x" /></traits></test>')
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

    tests = [("Pass", ["CP-A"], [("req", "REQ-PASS"), ("req", "REQ-TWO")]),
             ("Fail", ["CP-B"], [("req", "REQ-FAIL")]),
             ("Pass", ["CP-B"], [("req", "REQ-FAIL")]),
             ("Skip", ["CP-C"], [("req", "REQ-SKIP")]),
             ("Fail", ["CP-F"]), ("Pass", ["CP-Z"]), ("Pass", ["CP-G"]),
             ("Skip", ["CP-G"], [("req", "REQ-SKIP"), ("req", "REQ-SPACE")]),
             ("Pass", []), ("NotRun", ["CP-C"]), ("Pass", ["CP-A", "CP-H"]), ("NotRun", ["CP-I"]),
             ("Skip", ["CP-I"], [("req", "REQ-NOTRUN")]),
             ("Pass", ["CP-J"], [("inv", "INV-CP1")]),
             ("Fail", ["CP-Y"], [("inv", "INV-ORPHAN")]),
             ("Pass", [], [("req", "REQ-UNREFERENCED")])]
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
        expect(next((r for r in d["rows"] if r["id"] == "CP-A"), {}).get("tests") == 2,
               "CP-A は単独+複数 trait の 2 件のはず")
        expect(cat.get("CP-I") == UNMEASURABLE, f"全件 NotRun の CP-I は測定不能のはず({cat.get('CP-I')})")
        expect("CP-F" not in cat and [(r["id"], r["fail"]) for r in d["retired"]] == [("CP-F", 1)],
               "retired の CP-F は別欄で、Fail 件数を見せるはず")
        expect(d["orphans"] == [("CP-Y", 1, 1), ("CP-Z", 1, 0)], f"台帳に無い ID は CP-Y(Fail 1)・CP-Z のはず({d['orphans']})")
        ruled = {r["id"]: r for r in d["ruled_ids"]}
        expect(ruled.get("REQ-PASS", {}).get("category") == PASS, "REQ-PASS は合格のはず")
        expect(ruled.get("REQ-FAIL", {}).get("category") == VIOLATION and ruled.get("REQ-FAIL", {}).get("pass") == 1,
               "REQ-FAIL は Fail+Pass でも違反のはず")
        expect(ruled.get("REQ-SKIP", {}).get("category") == UNMEASURABLE,
               "行が合格でも全件 Skip の ID は測定不能のはず")
        expect(ruled.get("REQ-NOTRUN", {}).get("category") == UNMEASURABLE,
               "全件 NotRun の ID は測定不能のはず")
        expect(ruled.get("REQ-NONE", {}).get("category") == NOT_RUN_NONE, "unit 行のテスト無し ID は検査なしのはず")
        expect(ruled.get("REQ-HUMAN", {}).get("category") == NOT_RUN_HUMAN, "G のみの ID は人の承認のはず")
        expect(ruled.get("REQ-MIXED", {}).get("category") == NOT_RUN_NONE, "G+unit 参照の ID は検査なしのはず")
        expect(ruled.get("REQ-TWO", {}).get("tests") == 1 and ruled.get("REQ-PASS", {}).get("tests") == 1,
               "1 テストの複数 req trait は両 ID に数えるはず")
        expect(ruled.get("INV-CP1", {}).get("category") == PASS,
               "invariant_refs の ID を集合へ加えるはず")
        expect(ruled.get("REQ-SPACE", {}).get("category") == UNMEASURABLE,
               "refs と trait の前後空白を落として突き合わせるはず")
        expect(17 not in ruled and None not in ruled, "refs の非文字列要素は集合に入れないはず")
        expect("REQ-RETIRED" not in ruled and "REQ-IGNORED" not in ruled,
               "retired の refs と list でない refs は集合に入れないはず")
        expect(d["ruled_orphans"] == [{"id": "INV-ORPHAN", "tests": 1, "fail": 1},
                                        {"id": "REQ-UNREFERENCED", "tests": 1, "fail": 0}],
               f"refs に無い trait は裁定層の別欄のはず({d['ruled_orphans']})")
        g = next(r for r in d["rows"] if r["id"] == "CP-G")
        expect(g["skip_or_not_run"] == 1, "合格行の Skip 件数を併記するはず")
        expect(d["origin"][0]["config"] == "Debug" and d["origin"][0]["finished"].startswith("2026-10-02"),
               f"実行の素性(構成・終了日時)を取り出すはず({d['origin']})")
        md = render(d)
        expect(f"実行のテスト総数: {len(tests)}" in md, "表の先頭に実行のテスト総数を出すはず")
        expect("Debug・終了 2026-10-02" in md, "表の先頭に実行の構成と終了日時を出すはず")
        ebom = os.path.join(td, "30.yaml")
        ebom_data = {"ebom": {"items": [
            {"id": "E-A", "acceptance_refs": ["CP-A"],
             "requirement_refs": ["REQ-EBOM", "REQ-TESTED"],
            "invariants": ["文字列 INV-101", {"note": "辞書 INV-102"}, "INV-W1 XINV-12 INV-009a"]},
            {"id": "E-NOT-PARTICIPATING", "acceptance_refs": ["CP-E"],
             "requirement_refs": ["REQ-NOT-IN"]},
            {"id": "E-RETIRED", "lifecycle_state": "retired", "acceptance_refs": ["CP-A"],
             "requirement_refs": ["REQ-RETIRED-ITEM"]},
            {"id": "E-SUPERSEDED", "lifecycle_state": "superseded", "acceptance_refs": ["CP-A"],
             "requirement_refs": ["REQ-SUPERSEDED-ITEM"]},
        ]}}
        with open(ebom, "w", encoding="utf-8") as f:
            yaml.safe_dump(ebom_data, f, allow_unicode=True)
        ebom_tests = tests + [("Pass", [], [("req", "REQ-TESTED")])]
        with open(xmlp, "w", encoding="utf-8") as f:
            f.write(_st_xml(ebom_tests))
        ed = build(xmlp, cp, ebom)
        eruled = {r["id"]: r for r in ed["ruled_ids"]}
        expect(eruled.get("REQ-EBOM", {}).get("category") == NOT_RUN_NONE
               and eruled.get("REQ-EBOM", {}).get("rows") == []
               and eruled.get("REQ-EBOM", {}).get("ebom_items") == ["E-A"],
               "E 品目だけが負う未検査 REQ は rows 空・検査なし・品目 ID 付きのはず")
        expect(eruled.get("INV-101", {}).get("ebom_items") == ["E-A"]
               and eruled.get("INV-102", {}).get("ebom_items") == ["E-A"],
               "invariants の文字列行・dict 行にある INV を集合へ加えるはず")
        expect("INV-W1" in eruled and "INV-12" not in eruled and "INV-009" not in eruled,
               "英大文字接頭の INV を拾い、語中・末尾英字付きは拾わないはず")
        expect("REQ-NOT-IN" not in eruled, "参加する行を受入に持たない E 品目の REQ は集合外のはず")
        expect("REQ-RETIRED-ITEM" not in eruled and "REQ-SUPERSEDED-ITEM" not in eruled,
               "retired / superseded の E 品目は集合外のはず")
        expect(eruled.get("REQ-TESTED", {}).get("category") == PASS
               and eruled.get("REQ-TESTED", {}).get("rows") == [],
               "E-BOM 由来だけの ID も trait のテストがあれば合格のはず")
        expect(all(r["id"] != "REQ-TESTED" for r in ed["ruled_orphans"]),
               "E-BOM 由来 ID の trait は別欄に出ないはず")
        expect("(どの行の refs にも無い)" in render(ed) and "E-A(CP-A 経由)" in render(ed),
               "markdown は空 rows と経由する行つきの E 品目を表示するはず")
        expect(eruled.get("REQ-EBOM", {}).get("via_rows") == {"E-A": ["CP-A"]},
               "JSON は E 品目ごとの経由する行を持つはず")
        # E-BOM が無い / YAML 不正 / ebom.items 非 list でも 33 の refs だけで続行する。
        baseline_ids = {r["id"] for r in build(xmlp, cp)["ruled_ids"]}
        bad_eboms = [(os.path.join(td, "missing-ebom.yaml"), None),
                     (os.path.join(td, "bad-ebom.yaml"), "ebom: [unclosed"),
                     (os.path.join(td, "shape-ebom.yaml"), "ebom:\n  items: nope\n")]
        for bad_path, content in bad_eboms:
            if content is not None:
                with open(bad_path, "w", encoding="utf-8") as f:
                    f.write(content)
            bd = build(xmlp, cp, bad_path)
            expect({r["id"] for r in bd["ruled_ids"]} == baseline_ids,
                   "読めない E-BOM では ID 集合が 33 refs のままのはず")
            expect("ebom_unreadable" in bd and "E-BOM を読めない(" in render(bd),
                   "読めない E-BOM は理由と markdown 注意行を出すはず")
        no_refs_cp = os.path.join(td, "no-refs.yaml")
        with open(no_refs_cp, "w", encoding="utf-8") as f:
            yaml.safe_dump({"control_plan": {"characteristics": [{"id": "CP-X", "depth": "unit"}]}}, f)
        no_refs = build(xmlp, no_refs_cp)
        no_refs_md = render(no_refs)
        expect(no_refs["ruled_ids"] == [] and "refs を持つ行が無い" in no_refs_md,
               "refs が無い 33 は例外にせず『refs を持つ行が無い』と出すはず")
        expect("REQ-UNREFERENCED" in no_refs_md,
               "refs が無くても、どの行も参照しない trait の ID を別欄に出すはず")
        # ID ごとの経路の不正入力は、行ごとの表を失わせない。
        odd_cp = os.path.join(td, "odd33.yaml")
        with open(odd_cp, "w", encoding="utf-8") as f:
            yaml.safe_dump({"control_plan": {"characteristics": [
                {"id": 123, "depth": "unit", "requirement_refs": ["REQ-INT-ROW"]},
                {"id": "CP-A", "depth": "unit", "requirement_refs": ["REQ-A"]},
            ]}}, f)
        odd_ebom = os.path.join(td, "odd30.yaml")
        with open(odd_ebom, "w", encoding="utf-8") as f:
            yaml.safe_dump({"ebom": {"items": [
                {"id": "E-LIST", "acceptance_refs": [["CP-A"], "CP-A"],
                 "requirement_refs": ["REQ-LIST"]},
                {"id": 456, "acceptance_refs": ["CP-A"], "requirement_refs": ["REQ-INT-ITEM"]},
            ]}}, f)
        odd = build(xmlp, odd_cp, odd_ebom)
        expect(len(odd["rows"]) == 2 and "ID ごとの表を作れない" not in render(odd),
               "list acceptance_refs・整数 ID でも行ごとのデータを返し render できるはず")
        expect("REQ-LIST" in {r.get("id") for r in odd["ruled_ids"]}
               and "REQ-INT-ITEM" not in {r.get("id") for r in odd["ruled_ids"]},
               "acceptance_refs は文字列だけ、E 品目 ID も文字列だけを対象にするはず")
        bad_utf8 = os.path.join(td, "bad-utf8-ebom.yaml")
        with open(bad_utf8, "wb") as f:
            f.write(b"ebom:\n  items:\n  - id: E-\xff\n")
        bad_utf8_result = build(xmlp, cp, bad_utf8)
        expect(len(bad_utf8_result["rows"]) == len(d["rows"])
               and "ebom_unreadable" in bad_utf8_result,
               "UTF-8 として不正な E-BOM でも行ごとのデータを返すはず")
        no_ebom = build(xmlp, cp)
        expect(no_ebom.get("ebom_unreadable") == "E-BOM のパスが渡されていない",
               "build の ebom_path 省略は作り物のパスでなく専用理由を出すはず")

        # main の --ebom 配線、--cp 単独、既定 DEFAULT_EBOM の配線を合成ファイルで確認する。
        def call_main(args):
            saved = sys.stdout
            capture = io.StringIO()
            try:
                sys.stdout = capture
                rc = main(args)
            finally:
                sys.stdout = saved
            return rc, capture.getvalue()

        rc, main_custom = call_main(["--xml", xmlp, "--cp", cp, "--ebom", ebom])
        expect(rc == 0 and "REQ-EBOM" in main_custom,
               "main の --ebom で指定した E-BOM を読むはず")
        rc, main_cp_only = call_main(["--xml", xmlp, "--cp", cp])
        expect(rc == 0 and "--cp を指定し --ebom を指定していない" in main_cp_only,
               "--cp 単独では既定 E-BOM を結びつけず専用理由を出すはず")
        default_dir = os.path.join(td, "default-location")
        os.mkdir(default_dir)
        default_ebom = os.path.join(default_dir, "30-ebom.yaml")
        with open(default_ebom, "w", encoding="utf-8") as f:
            yaml.safe_dump(ebom_data, f, allow_unicode=True)
        global DEFAULT_XML, DEFAULT_CP, DEFAULT_EBOM
        saved_defaults = DEFAULT_XML, DEFAULT_CP, DEFAULT_EBOM
        try:
            DEFAULT_XML, DEFAULT_CP, DEFAULT_EBOM = xmlp, cp, default_ebom
            rc, main_default = call_main([])
        finally:
            DEFAULT_XML, DEFAULT_CP, DEFAULT_EBOM = saved_defaults
        expect(rc == 0 and "REQ-EBOM" in main_default,
               "main の既定値は DEFAULT_EBOM を読むはず")
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
    ebom_path = None if "--cp" in argv and "--ebom" not in argv else DEFAULT_EBOM
    for opt in ("--xml", "--cp", "--ebom"):
        if opt in argv:
            i = argv.index(opt)
            if i + 1 >= len(argv) or argv[i + 1].startswith("--"):
                print(f"FATAL: {opt} にパスが無い", file=sys.stderr)
                return 2
            if opt == "--xml":
                xml_path = os.path.abspath(argv[i + 1])
            elif opt == "--cp":
                cp_path = os.path.abspath(argv[i + 1])
            else:
                ebom_path = os.path.abspath(argv[i + 1])
    try:
        d = build(xml_path, cp_path, ebom_path)
        if "--cp" in argv and "--ebom" not in argv:
            d["ebom_unreadable"] = "--cp を指定し --ebom を指定していない"
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
