#!/usr/bin/env python3
"""bomdd integrity validator — ViewPrism2 BomDD 成果物の参照整合性・規律チェック。

実行:  python bomdd/validate_bom.py            (リポ直下 or どこからでも)
        python bomdd/validate_bom.py --quiet     (ERROR のみ表示)
        python bomdd/validate_bom.py --selftest  (検査器の自己検査 — ECO-053 素通し様式の再発防止)

終了コード:  0 = ERROR なし(WARN は許容) / 1 = ERROR あり / 2 = 実行不能(parse 失敗等)

設計意図(会話 2026-06-22):
  ECO-016 の再分割で「手書き検査を2回」やった内容を常設化する。PLM 風フィールドを
  足す前に、まず参照整合性を validator で担保する(field を信用できるものにする前提)。
  ここでは E-BOM の参照整合性・core/surface 規律・M-BOM 双方向トレース・register 語彙・
  manifest↔register 整合を検査する。effectivity/variant は本プロジェクトでは YAGNI のため対象外。

検査一覧(E=ERROR / W=WARN):
  E-BOM (30-ebom.yaml)
    [E1] item id は一意
    [E2] depends_on の E-* 参照は active id(dangling 禁止)
    [E3] graph_edges.consumers の E-* 参照は active id
    [E4] graph_edges.owner は自身の id
    [E5] supersedes の target は active でない(retired 座標のみに残す)
    [E6] supersedes された(retired)id には後継 active 部品が存在する
    [E7] core は requirement_refs 必須 / surface は external_source_ref 必須
    [W1] active item は acceptance_refs を持つ
  M-BOM (32-mbom.yaml)
    [E8] ebom_refs の E-* 参照は active E-BOM id(retired への dangling 禁止)
    [W2] active surface 部品は M-BOM 製造トレースを持つ(orphan 検出)
  Register (60-change-register.yaml)
    [E9]  change.status は宣言語彙のみ(ECO-053: リストキーを changes: へ追随 — ECO-034 改名の未追随で
          E9〜E11 が no-op 化していた欠陥の是正)
    [E10] change.golden は宣言語彙で始まる(prefix 一致 — 運用の approved(日付 …) 形式に追随。欄なしも違反)
    [E11] superseded_by / reattributed_by は実在 ECO id
  YAML 共通
    [E13] 重複キー禁止(ECO-053: PyYAML safe_load の警告なし後勝ちが register 誤挿入事故を素通しした欠陥の是正。
          全 YAML 台帳ロードに適用・ファイル名+キー+行番号を報告)
  UI-BOM (ui/image-tab/ui-bom.json・存在時のみ)
    [E12] ebomCandidate / ebomItemsReferenced の E-* 参照は active(superseded 欄は除外)
  Cross (00-manifest.yaml ↔ 60-change-register.yaml)
    [W3] active baseline_commit / bom_version / frozen_oracle_tag / eco_range が整合
"""
import os, sys, json

try:
    import yaml
except ImportError:
    print("FATAL: PyYAML required (pip install pyyaml)", file=sys.stderr)
    sys.exit(2)

# Windows の cp932 コンソールでも日本語/記号を出力できるよう UTF-8 を強制(CI 移植性)
for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass

BOMDD = os.path.dirname(os.path.abspath(__file__))

# register ヘッダの宣言語彙をミラー(変更時は 60-change-register.yaml と同期)
STATUS_VOCAB = {"staged", "applied", "implemented", "superseded"}
GOLDEN_VOCAB = {"n/a", "approved", "pending", "round1-fixed"}

findings = []  # (severity, code, message)
def err(code, msg):  findings.append(("ERROR", code, msg))
def warn(code, msg): findings.append(("WARN",  code, msg))

# ---- [E13] 重複キー検出ローダ(ECO-053) ----
# PyYAML の SafeLoader は YAML 仕様のキー一意性を強制せず警告なしに後勝ちマージする。
# register 誤挿入事故(ECO-049・da155ad で修復)を 0-0 で素通しした真因。
# mapping 構築時に重複を検出し、ロード継続のうえ E13 として報告する(他検査も走らせる)。
class _DupKeyLoader(yaml.SafeLoader):
    duplicates = None  # ロード単位で差し替える

def _dup_checking_mapping(loader, node, deep=False):
    seen = {}
    for key_node, _value_node in node.value:
        try:
            key = loader.construct_object(key_node, deep=True)
            hashable = True
        except Exception:
            hashable = False
        if hashable:
            try:
                if key in seen:
                    loader.duplicates.append((key, key_node.start_mark.line + 1, seen[key]))
                else:
                    seen[key] = key_node.start_mark.line + 1
            except TypeError:
                pass  # unhashable キー(list 等)は一意性検査の対象外
    return yaml.SafeLoader.construct_mapping(loader, node, deep)

_DupKeyLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, _dup_checking_mapping)

def _load_yaml_text(text, label):
    """text を重複キー検査つきでロードし、重複は E13 として findings へ。"""
    loader = _DupKeyLoader(text)
    loader.duplicates = []
    try:
        data = loader.get_single_data()
    finally:
        dups = loader.duplicates
        loader.dispose()
    for key, line, first_line in dups:
        err("E13", f"{label}: キー {key!r} が重複(行 {line}・初出 行 {first_line})— "
                   "YAML は警告なく後勝ちになり値が化ける(ECO-049 事故様式)")
    return data

def load_yaml(name):
    with open(os.path.join(BOMDD, name), encoding="utf-8") as f:
        return _load_yaml_text(f.read(), name)
def load_json(name):
    return json.load(open(os.path.join(BOMDD, name), encoding="utf-8"))

def golden_ok(gd):
    """[E10] golden は宣言語彙で始まる(prefix 一致 — 運用の approved(日付 …) 形式に追随)。"""
    return isinstance(gd, str) and any(gd == v or gd.startswith(v) for v in GOLDEN_VOCAB)

# ---- --selftest(ECO-053): 素通し様式の再発防止 — 検査器自身の検出能力を合成フィクスチャで自己検査 ----
if "--selftest" in sys.argv:
    ok = True
    # (1) 重複キー検出: ECO-049 事故様式の最小再現(同一マッピングに baseline が 2 回)
    findings.clear()
    _load_yaml_text("changes:\n  - id: X\n    baseline: a\n    notes: n\n    baseline: b\n", "selftest.yaml")
    if not any(c == "E13" for _, c, _ in findings):
        print("selftest FAIL: 重複キーを検出できない(E13)"); ok = False
    # (2) 語彙検査の意味論: prefix 一致と違反
    if not golden_ok("approved(2026-07-06 maintainer 実機: …)"):
        print("selftest FAIL: approved(…) 形式を許容しない(E10 prefix)"); ok = False
    if golden_ok("各段で再ウォークスルー") or golden_ok(None):
        print("selftest FAIL: 語彙外 golden を通してしまう(E10)"); ok = False
    if "bogus-vocab-violation" in STATUS_VOCAB:
        print("selftest FAIL: STATUS_VOCAB が汚染"); ok = False
    # (3) register リストキー: 実台帳の changes: が空でない(E9〜E11 が no-op でない)
    findings.clear()
    _reg = load_yaml("60-change-register.yaml")
    if not _reg.get("changes"):
        print("selftest FAIL: register の changes: が読めない(E9〜E11 が no-op — ECO-034 未追随様式)"); ok = False
    findings.clear()
    print("selftest:", "OK" if ok else "FAIL")
    sys.exit(0 if ok else 1)

def is_e(x):  # E-BOM 部品 id か
    return isinstance(x, str) and x.startswith("E-")

# ---------------------------------------------------------------- E-BOM
eb = load_yaml("30-ebom.yaml")
items = eb["ebom"]["items"]
ids = [it["id"] for it in items]
active = set(ids)

# [E1] id 一意
seen = set()
for i in ids:
    if i in seen:
        err("E1", f"E-BOM item id が重複: {i}")
    seen.add(i)

superseded_targets = set()
for it in items:
    iid = it["id"]
    # [E2] depends_on
    for d in it.get("depends_on", []) or []:
        if is_e(d) and d not in active:
            err("E2", f"{iid}.depends_on が retired/不在 id を参照: {d}")
    # [E3]/[E4] graph_edges
    ge = it.get("graph_edges") or {}
    if ge:
        if ge.get("owner") and ge["owner"] != iid:
            err("E4", f"{iid}.graph_edges.owner が自身と不一致: {ge['owner']}")
        for c in ge.get("consumers", []) or []:
            if is_e(c) and c not in active:
                err("E3", f"{iid}.graph_edges.consumers が retired/不在 id を参照: {c}")
    # [E5] supersedes は active を指さない
    sup = it.get("supersedes")
    if sup:
        superseded_targets.add(sup)
        if sup in active:
            err("E5", f"{iid}.supersedes が active id を指している(retired のはず): {sup}")
    # [E7] core/surface 規律
    cls = it.get("classification")
    if cls == "core" and not (it.get("requirement_refs")):
        err("E7", f"{iid}(core)に requirement_refs が無い")
    if cls == "surface" and not (it.get("external_source_ref")):
        err("E7", f"{iid}(surface)に external_source_ref が無い")
    # [W1] acceptance_refs
    if not it.get("acceptance_refs"):
        warn("W1", f"{iid} に acceptance_refs が無い(受入未接続)")

# [E6] retired id に後継 active が存在
declared_successors = {it.get("supersedes") for it in items if it.get("supersedes")}
for t in superseded_targets:
    if t not in declared_successors:  # 念のため(supersedes 由来なので通常満たす)
        err("E6", f"retired id {t} を後継宣言する active 部品が無い")

# ---------------------------------------------------------------- M-BOM
mb = load_yaml("32-mbom.yaml")
def mbom_units(o, cur=None):
    if isinstance(o, dict):
        uid = o["id"] if isinstance(o.get("id"), str) and o["id"].startswith("M-") else cur
        if isinstance(o.get("ebom_refs"), list):
            yield uid, o["ebom_refs"]
        for v in o.values():
            yield from mbom_units(v, uid)
    elif isinstance(o, list):
        for x in o:
            yield from mbom_units(x, cur)

manufactured = set()
for uid, refs in mbom_units(mb):
    for r in refs:
        if is_e(r):
            manufactured.add(r)
            if r not in active:  # [E8]
                err("E8", f"M-BOM {uid}.ebom_refs が retired/不在 E-BOM id を参照: {r}")

# [W2] active surface 部品の orphan(製造トレース無し)
for it in items:
    if it.get("classification") == "surface" and it["id"] not in manufactured:
        warn("W2", f"surface 部品 {it['id']} に M-BOM 製造トレースが無い(orphan)")

# ---------------------------------------------------------------- Register
reg = load_yaml("60-change-register.yaml")
# ECO-053: リストキーは changes:(ECO-034 で change_orders: から改名 — validator 未追随で
# E9〜E11 が 3 日間 no-op 化していた。「読めなければ空」でなく明示エラーにして再発を封じる)
cos = reg.get("changes")
if cos is None:
    err("E9", "register のリストキー changes: が見つからない(E9〜E11 が実行不能 — キー改名時は validator を同期)")
    cos = []
eco_ids = {c["id"] for c in cos}
for c in cos:
    cid = c.get("id", "?")
    st = c.get("status")
    if st not in STATUS_VOCAB:          # [E9]
        err("E9", f"{cid}.status が宣言語彙外: {st!r}(許可: {sorted(STATUS_VOCAB)})")
    gd = c.get("golden")
    if not golden_ok(gd):               # [E10] prefix 一致(欄なし=None も違反)
        err("E10", f"{cid}.golden が宣言語彙で始まらない: {str(gd)[:40]!r}(許可 prefix: {sorted(GOLDEN_VOCAB)})")
    for key in ("superseded_by", "reattributed_by"):  # [E11]
        v = c.get(key)
        if v and v not in eco_ids:
            err("E11", f"{cid}.{key} が実在しない ECO を参照: {v}")

# ---------------------------------------------------------------- UI-BOM(任意)
ui_path = os.path.join(BOMDD, "ui", "image-tab", "ui-bom.json")
if os.path.exists(ui_path):
    ui = json.load(open(ui_path, encoding="utf-8"))
    ui_refs = list(ui.get("meta", {}).get("ebomItemsReferenced", []))
    for it in ui.get("items", []):
        cand = it.get("ebomCandidate") or {}
        for k in ("items", "coreRefs", "readAcross", "designToken"):
            ui_refs += cand.get(k, []) or []
    for r in ui_refs:                   # [E12](superseded 欄は収集対象外なので除外済み)
        if is_e(r) and r not in active:
            err("E12", f"UI-BOM が retired/不在 E-BOM id を参照: {r}")

# ---------------------------------------------------------------- Cross: manifest ↔ register
man = load_yaml("00-manifest.yaml")
ma, ra = man.get("active", {}), reg.get("active_scope", {})
for mk, rk, label in [
    ("baseline_commit", "baseline_commit", "baseline_commit"),
    ("bom_version", "bom_version", "bom_version"),
    ("frozen_oracle_tag", "frozen_oracle_tag", "frozen_oracle_tag"),
    ("eco_range", "eco_range", "eco_range"),
]:
    mv, rv = ma.get(mk), ra.get(rk)
    if mv is not None and rv is not None and str(mv).strip() != str(rv).strip():
        warn("W3", f"manifest.active.{label}={mv!r} と register.active_scope.{label}={rv!r} が不一致")

# ---------------------------------------------------------------- report
quiet = "--quiet" in sys.argv
errors = [f for f in findings if f[0] == "ERROR"]
warns  = [f for f in findings if f[0] == "WARN"]

def emit(group, items_):
    if not items_:
        return
    print(f"\n{group} ({len(items_)})")
    for sev, code, msg in items_:
        print(f"  [{code}] {msg}")

print(f"bomdd integrity validator — E-BOM active items: {len(active)} / dangling check")
emit("ERROR", errors)
if not quiet:
    emit("WARN", warns)

print(f"\nresult: {len(errors)} error(s), {len(warns)} warning(s)")
sys.exit(1 if errors else 0)
