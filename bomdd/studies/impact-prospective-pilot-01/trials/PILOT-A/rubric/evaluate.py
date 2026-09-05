#!/usr/bin/env python3
"""予備試験の評価者(oracle スクリプト・構造的盲検): 予測出力(RESULT.md 内の YAML)と、封印した初回裁定
(rubric.md 内の YAML)を M unit 集合で突合する。treatment を受け取る引数は持たない。

主評価= M unit の「変更予測」と「調査候補」の 2 集合。
  change:      予測 change_units  ⇔ 裁定 role=change
  scope:       予測 change∪investigate ⇔ 裁定 change∪investigate
result: pass = 裁定 change 集合が予測 change_units に全て含まれる(under_change=0)。over は記録のみ。
symptom: under-change / under-scope / over-change / over-scope / missing-verdict / format-violation
探索的試験のため pass/fail は成功条件ではない(記録の形式を測定器に揃えるための便宜)。

v2(2026-09-05): v1 は ```yaml フェンス内の YAML しか読まず、予測者がフェンスなしで YAML を出力した 2 run が
missing-verdict になった(計器欠陥・較正で捕捉・v1 receipt は runs/defective-evaluator-v1/ に隔離)。
v2 はフェンス → 文書全体 → 「<key>:」行から始まる連続ブロック、の順に読む。
"""
from __future__ import annotations
import json, re, sys
from pathlib import Path
import yaml

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
YAML_BLOCK = re.compile(r"```ya?ml\s*\n(.*?)\n```", re.S)


def _try(text: str, key: str):
    try:
        d = yaml.safe_load(text)
    except yaml.YAMLError:
        return None
    return d[key] if isinstance(d, dict) and key in d else None


def load_yaml_block(text: str, key: str):
    for m in YAML_BLOCK.finditer(text):
        v = _try(m.group(1), key)
        if v is not None:
            return v
    v = _try(text, key)
    if v is not None:
        return v
    m = re.search(rf"^{re.escape(key)}:\s*$", text, re.M)
    if m:
        block = []
        for line in text[m.start():].splitlines():
            if block and line.strip() and not line.startswith((" ", "\t")) and not line.startswith(key + ":"):
                break
            block.append(line)
        return _try("\n".join(block), key)
    return None


def units(entries, roles=None) -> set:
    out = set()
    for e in entries or []:
        if not isinstance(e, dict) or not e.get("unit"):
            continue
        if roles is None or e.get("role") in roles:
            out.add(str(e["unit"]).strip())
    return out


def evaluate(result_text: str, rubric_text: str) -> dict:
    adj = load_yaml_block(rubric_text, "initial_adjudication")
    if adj is None:
        raise SystemExit("rubric に initial_adjudication が無い(評価不能は合格ではない)")
    adj_change = units(adj.get("units"), {"change"})
    adj_scope = units(adj.get("units"), {"change", "investigate"})
    pred = load_yaml_block(result_text, "prediction")
    if pred is None:
        return {"result": "fail", "observed_failures": ["missing-verdict"], "details": {"reason": "prediction YAML なし"}}
    p_change = units(pred.get("change_units"))
    p_scope = p_change | units(pred.get("investigate_units"))
    d = {"adj_change": sorted(adj_change), "adj_scope": sorted(adj_scope),
         "pred_change": sorted(p_change), "pred_scope": sorted(p_scope),
         "under_change": sorted(adj_change - p_change), "over_change": sorted(p_change - adj_change),
         "under_scope": sorted(adj_scope - p_scope), "over_scope": sorted(p_scope - adj_scope)}
    failures = []
    if d["under_change"]: failures.append("under-change")
    if d["under_scope"]: failures.append("under-scope")
    if d["over_change"]: failures.append("over-change")
    if d["over_scope"]: failures.append("over-scope")
    return {"result": "pass" if not d["under_change"] else "fail", "observed_failures": sorted(set(failures)),
            "correct": len(adj_change & p_change), "expected": len(adj_change), "details": d}


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__); return 2
    rubric = Path(sys.argv[2]) if len(sys.argv) > 2 else HERE / "rubric.md"
    print(json.dumps(evaluate(Path(sys.argv[1]).read_text(encoding="utf-8"), rubric.read_text(encoding="utf-8")),
                     ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
