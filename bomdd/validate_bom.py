#!/usr/bin/env python3
"""bomdd integrity validator — ViewPrism2 BomDD 成果物の参照整合性・規律チェック。

実行:  python bomdd/validate_bom.py            (リポ直下 or どこからでも)
        python bomdd/validate_bom.py --quiet     (ERROR のみ表示)
        python bomdd/validate_bom.py --selftest  (検査器の自己検査 — ECO-053 素通し様式の再発防止)

終了コード:  0 = ERROR なし(WARN は許容) / 1 = ERROR あり(台帳 YAML の構文破壊= E20 を含む。
              pre-commit は rc=2 を fail-open 扱いするため、台帳起因の失敗は必ず 1= ECO-120)
              / 2 = 実行不能(PyYAML 不在等の環境起因のみ)

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
  Lifecycle (ECO-061: 台帳状態 × git 履歴証拠の状態不変条件。applies_from 以降に適用・legacy は宣言免除)
    [E14] lifecycle_evidence 宣言の実在・妥当性 / git 不能・shallow の fail-closed(--no-git のみ明示スキップ)
    [E15] status implemented/applied ⇒ BomDD-ECO-Fix trailer コミットが HEAD 祖先に実在
    [E16] status applied ⇒ BomDD-ECO-Accept trailer コミットが HEAD 祖先に実在
    [E17] fix 証拠が accept 証拠の祖先(順序)/ accept 証拠があるのに未 applied(逆行・乖離)
    [E18] trailer の参照先 ECO が台帳に実在
    [E19] HEAD→作業ツリーの status 遷移が許可エッジ(飛び越し・逆行・非 staged 登場の禁止)
    モード: --commit-msg <file>(遷移コミットへの trailer 強制 — commit-msg hook 用)/
            --selftest-lifecycle(実 git 統合の陽性対照: 正常・別系統ブランチ・順序逆転)
  YAML 共通
    [E20] 構文床(ECO-120: bomdd/*.yaml 全数の parse 健全性。未読台帳の構文破壊が 0/0 で
          素通しだった実測の是正。検出時は意味検査へ進まず exit 1 で停止)
    [E13] 重複キー禁止(ECO-053: PyYAML safe_load の警告なし後勝ちが register 誤挿入事故を素通しした欠陥の是正。
          ECO-120 で bomdd/*.yaml 全数へ拡張〔未読台帳= sweep が報告・本流 load の 4 本= load_yaml が報告〕。
          ファイル名+キー+行番号を報告)
  UI-BOM (ui/image-tab/ui-bom.json・存在時のみ)
    [E12] ebomCandidate / ebomItemsReferenced の E-* 参照は active(superseded 欄は除外)
  Cross (00-manifest.yaml ↔ 60-change-register.yaml)
    [W3] active baseline_commit / bom_version / frozen_oracle_tag / eco_range が整合
"""
import os, re, subprocess, sys, json

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

# ---- [E20] YAML 構文床(ECO-120) ----
# 本検査器が意味検査のために load するのは MAIN_LOADED の 4 本だけで、残る台帳(33-control-plan=
# golden CP 正本ほか 10 本)は構文が壊れていても 0/0 で素通しだった(2026-07-20 実測: 33 の
# flow mapping 破壊を検出したのは隣接リポの bomdd-lint のみ=単一障害点かつ pre-commit では
# fail-open)。ここで bomdd/*.yaml 全数の構文健全性を床として検査する(意味検査= 参照解決等は
# bomdd-lint の役割のまま。役割分担: 構文床= 本検査器 fail-closed / 意味= bomdd-lint)。
# _DupKeyLoader を再利用するため、E13(重複キー= ECO-053)も未読台帳へ自動拡張される
# (MAIN_LOADED の 4 本は後段 load_yaml が E13 を報告するため二重報告を避けてここでは構文のみ)。
MAIN_LOADED = {"30-ebom.yaml", "32-mbom.yaml", "60-change-register.yaml", "00-manifest.yaml"}

def _yaml_syntax_check(text, name, report_dups):
    """構文 parse(E20)+必要なら重複キー(E13)。parse 成功で True。"""
    loader = _DupKeyLoader(text)
    loader.duplicates = []
    try:
        loader.get_single_data()
    except yaml.YAMLError as e:
        first = str(e).splitlines()
        err("E20", f"{name}: YAML 構文エラー — {' / '.join(first[:2])}"
                   "(壊れた台帳は警告なく検査圏外に落ちる= ECO-120 実測様式)")
        return False
    finally:
        dups = loader.duplicates
        loader.dispose()
    if report_dups:
        for key, line, first_line in dups:
            err("E13", f"{name}: キー {key!r} が重複(行 {line}・初出 行 {first_line})— "
                       "YAML は警告なく後勝ちになり値が化ける(ECO-049 事故様式)")
    return True

def sweep_yaml_syntax():
    """bomdd/*.yaml 全数の構文床検査。E20 の件数を返す。"""
    bad = 0
    for fn in sorted(os.listdir(BOMDD)):
        if not fn.endswith(".yaml"):
            continue
        with open(os.path.join(BOMDD, fn), encoding="utf-8") as f:
            text = f.read()
        if not _yaml_syntax_check(text, fn, report_dups=fn not in MAIN_LOADED):
            bad += 1
    return bad

def golden_ok(gd):
    """[E10] golden は宣言語彙で始まる(prefix 一致 — 運用の approved(日付 …) 形式に追随)。"""
    return isinstance(gd, str) and any(gd == v or gd.startswith(v) for v in GOLDEN_VOCAB)

# ---- ECO-061: ライフサイクル状態不変条件(E14〜E19) ----
# ECO-060 運用違反(fix コミットなしで register を applied 化 — 本検査器が 0-0 で素通し)の是正。
# 台帳の status 遷移を、履歴証拠(遷移コミット自身が携行する git trailer — 案a・自己参照回避)と
# 突合する。適用範囲は register の lifecycle_evidence ブロックで宣言(applies_from 未満は遡及免除を
# 明示 — 黙って適用除外しない)。純粋検査層(lifecycle_*_findings)と git 抽出層を分離し、前者は
# --selftest の合成変異、後者は --selftest-lifecycle の実 git 統合で陽性対照を維持する。
REG_RELPATH = "bomdd/60-change-register.yaml"
TRAILER_FIX, TRAILER_ACCEPT = "BomDD-ECO-Fix", "BomDD-ECO-Accept"
# 許可遷移(正本: bomdd/change-management.md §4)。staged→applied の飛び越し・逆行・
# superseded(終端)からの遷移は禁止。doc-only でも fix→accept の 2 段を踏む。
ALLOWED_EDGES = {("staged", "implemented"), ("implemented", "applied"),
                 ("staged", "superseded"), ("implemented", "superseded"), ("applied", "superseded")}

def eco_num(cid):
    m = re.match(r"ECO-(\d+)$", str(cid or ""))
    return int(m.group(1)) if m else None

def lifecycle_scheme(reg_data):
    """register の lifecycle_evidence 宣言を検証。戻り: (applies_from 番号, エラー文 or None)"""
    le = (reg_data or {}).get("lifecycle_evidence")
    if not isinstance(le, dict):
        return None, "register に lifecycle_evidence ブロックが無い(ECO-061 で必須化 — 適用範囲を宣言する)"
    if le.get("scheme") != "trailer-v1":
        return None, f"lifecycle_evidence.scheme が未知: {le.get('scheme')!r}(既知: trailer-v1)"
    n = eco_num(le.get("applies_from"))
    if n is None:
        return None, f"lifecycle_evidence.applies_from が ECO-NNN 形式でない: {le.get('applies_from')!r}"
    return n, None

def register_status_map(changes):
    return {c.get("id"): c.get("status") for c in (changes or []) if isinstance(c, dict) and c.get("id")}

# 遷移の進行度(ECO-078 症状B: マージ親の合算で「より進んだ側」を採用するための順序)
STATUS_ORDER = {"staged": 0, "implemented": 1, "applied": 2, "superseded": 3}

def message_trailer_block(msg):
    """コミットメッセージの trailer 行群(ECO-078 症状A: git の解釈=最終段落ブロックのみ、に統一)。
    最終段落の全行が `Key: value` 形のときだけ trailer と認める。中間段落は本文であり trailer でない
    (%(trailers) と同じ側へ寄せる。厳しすぎる側の誤差は commit 時に明瞭なエラーで止まる=fail-closed)。"""
    paras = [p for p in re.split(r"\n\s*\n", (msg or "").strip()) if p.strip()]
    if len(paras) <= 1:
        return []  # 件名(+本文なし)だけのメッセージに trailer は無い
    lines = [l.rstrip() for l in paras[-1].splitlines() if l.strip() and not l.lstrip().startswith("#")]
    if lines and all(re.match(r"^[A-Za-z0-9-]+:\s?\S", l) for l in lines):
        return lines
    return []

def msg_has_trailer(msg, key, value):
    """メッセージが trailer 「key: value」を(git 基準の位置で)携行しているか。"""
    return any(re.match(rf"^{re.escape(key)}:\s*{re.escape(value)}\s*$", l)
               for l in message_trailer_block(msg))

def lifecycle_evidence_findings(status_by_id, applies_from_num, evidence, is_ancestor, known_ids=None):
    """E15〜E18: コミット済み台帳状態 × 履歴証拠(trailer)の状態不変条件(純粋層・git 非依存)。
    evidence: {eco_id: {"fix": [sha...], "accept": [sha...]}}(HEAD 祖先のみ — 別系統ブランチは抽出層が除外)"""
    out = []
    known = known_ids if known_ids is not None else set(status_by_id)
    for cid in sorted(evidence):
        if cid not in known:                                    # [E18] 参照先不在
            out.append(("E18", f"履歴の trailer が実在しない ECO を参照: {cid}"))
    for cid in sorted(status_by_id):
        st, n = status_by_id[cid], eco_num(cid)
        if n is None or n < applies_from_num:
            continue  # 遡及免除(lifecycle_evidence.legacy で宣言 — E14 が宣言自体を検査)
        ev = evidence.get(cid, {})
        fixes, accepts = ev.get("fix", []), ev.get("accept", [])
        if st in ("implemented", "applied") and not fixes:      # [E15] fix 証拠の実在
            out.append(("E15", f"{cid}.status={st} だが {TRAILER_FIX} trailer を持つコミットが HEAD 祖先に無い"))
        if st == "applied" and not accepts:                     # [E16] accept 証拠の実在
            out.append(("E16", f"{cid}.status=applied だが {TRAILER_ACCEPT} trailer を持つコミットが HEAD 祖先に無い"))
        if st == "applied" and fixes and accepts:               # [E17] 祖先関係(fix が accept に先行)
            if not any(f != a and is_ancestor(f, a) for f in fixes for a in accepts):
                out.append(("E17", f"{cid}: どの fix 証拠コミットも accept 証拠コミットの祖先でない(順序逆転)"))
        if accepts and st in ("staged", "implemented"):         # [E17] 証拠と状態の乖離(逆行)
            out.append(("E17", f"{cid}: accept 証拠が履歴に存在するのに status={st}(逆行または台帳と履歴の乖離)"))
    return out

def lifecycle_edge_findings(head_status_by_id, wt_status_by_id, applies_from_num):
    """E19: HEAD → 作業ツリーの status 遷移が許可エッジか(純粋層)。新規エントリは staged で登場する。"""
    out = []
    for cid in sorted(wt_status_by_id):
        new, n = wt_status_by_id[cid], eco_num(cid)
        if n is None or n < applies_from_num:
            continue
        old = head_status_by_id.get(cid)
        if old is None:
            if new != "staged":
                out.append(("E19", f"{cid}: 新規エントリが staged 以外({new})で登場(起票を経ない状態)"))
        elif old != new and (old, new) not in ALLOWED_EDGES:
            out.append(("E19", f"{cid}: 禁止遷移 {old} → {new}(飛び越し/逆行 — 許可遷移は change-management.md §4)"))
    return out

# ---- git 抽出層(lifecycle 用) ----
def _git(repo_dir, *args):
    try:
        p = subprocess.run(["git", "-C", repo_dir] + list(args),
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        return p.returncode, (p.stdout or "")
    except OSError:
        return 127, ""

def collect_trailer_evidence(repo_dir, ref="HEAD"):
    """HEAD 祖先の全コミットから trailer 証拠を収集(別系統ブランチは対象外 = 祖先関係の要求)。
    戻り: {eco_id: {"fix": [...], "accept": [...]}} / None = git 実行不能"""
    fmt = ("%H%x09%(trailers:key=" + TRAILER_FIX + ",valueonly,separator=|)"
           "%x09%(trailers:key=" + TRAILER_ACCEPT + ",valueonly,separator=|)")
    rc, out = _git(repo_dir, "log", ref, "--format=" + fmt)
    if rc != 0:
        return None
    ev = {}
    for line in out.splitlines():
        parts = line.split("\t")
        if len(parts) != 3:
            continue
        sha, fixv, accv = parts
        for kind, vals in (("fix", fixv), ("accept", accv)):
            for v in vals.split("|"):
                v = v.strip()
                if v:
                    ev.setdefault(v, {"fix": [], "accept": []})[kind].append(sha)
    return ev

def git_is_ancestor(repo_dir):
    def _f(a, b):
        rc, _ = _git(repo_dir, "merge-base", "--is-ancestor", a, b)
        return rc == 0
    return _f

def head_register_changes(repo_dir, ref="HEAD"):
    """ref 時点の register の changes リスト。ref に存在しない/読めない場合は None。"""
    rc, out = _git(repo_dir, "show", f"{ref}:{REG_RELPATH}")
    if rc != 0:
        return None
    try:
        return (yaml.safe_load(out) or {}).get("changes") or []
    except yaml.YAMLError:
        return None

def merge_parent_refs(repo_dir):
    """比較元の親 ref 一覧。マージ進行中(MERGE_HEAD 存在)は [HEAD, MERGE_HEAD](ECO-078 症状B:
    E19 の old を第 1 親だけから取る線形履歴前提の除去)。通常は [HEAD]。"""
    rc, out = _git(repo_dir, "rev-parse", "-q", "--verify", "MERGE_HEAD")
    return ["HEAD", "MERGE_HEAD"] if rc == 0 and out.strip() else ["HEAD"]

def combined_head_status(repo_dir):
    """E19 の比較元 old: 全親(HEAD+マージ中は MERGE_HEAD)の register status を合算する。
    同一 ECO が複数親に居る場合は遷移がより進んだ側(STATUS_ORDER)を採用 — ブランチで正規に
    歩んだ遷移をマージが「新規登場」と誤認しない(ECO-078 症状B)。どの親からも読めなければ None。"""
    combined, seen_any = {}, False
    for ref in merge_parent_refs(repo_dir):
        ch = head_register_changes(repo_dir, ref)
        if ch is None:
            continue
        seen_any = True
        for cid, st in register_status_map(ch).items():
            cur = combined.get(cid)
            if cur is None or STATUS_ORDER.get(st, -1) > STATUS_ORDER.get(cur, -1):
                combined[cid] = st
    return combined if seen_any else None

# ---- --selftest(ECO-053): 素通し様式の再発防止 — 検査器自身の検出能力を合成フィクスチャで自己検査 ----
if "--selftest" in sys.argv:
    ok = True
    # (1) 重複キー検出: ECO-049 事故様式の最小再現(同一マッピングに baseline が 2 回)
    findings.clear()
    _load_yaml_text("changes:\n  - id: X\n    baseline: a\n    notes: n\n    baseline: b\n", "selftest.yaml")
    if not any(c == "E13" for _, c, _ in findings):
        print("selftest FAIL: 重複キーを検出できない(E13)"); ok = False
    # [E20] 構文破壊の陽性対照(ECO-120: 未読台帳の破壊が 0/0 で素通しだった様式の再発防止)
    if _yaml_syntax_check("a: [1,\n", "selftest-broken.yaml", report_dups=False):
        print("selftest FAIL: YAML 構文破壊を検出できない(E20)"); ok = False
    if not any(f[1] == "E20" for f in findings):
        print("selftest FAIL: E20 finding が記録されない"); ok = False
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
    # (4) ECO-061 lifecycle 状態不変条件 — 変異検出の陽性対照(検査が存在しなければ FAIL = 先行赤の恒久化)
    try:
        _lef, _leg = lifecycle_evidence_findings, lifecycle_edge_findings
    except NameError:
        print("selftest FAIL: lifecycle 検査が存在しない(E14〜E19 未実装 — ECO-061)"); ok = False
    else:
        def _anc(pairs):
            return lambda a, b: (a, b) in pairs
        # 正常系: fix→accept の順で証拠あり → 所見ゼロ
        if _lef({"ECO-100": "applied"}, 100, {"ECO-100": {"fix": ["f"], "accept": ["a"]}}, _anc({("f", "a")})):
            print("selftest FAIL: 正常系(fix→accept)を誤検出"); ok = False
        # 遡及免除: applies_from 未満は宣言済み免除(黙って除外しない — 宣言は E14 が検査)
        if _lef({"ECO-050": "applied"}, 100, {}, _anc(set())):
            print("selftest FAIL: legacy ECO(applies_from 未満)を誤って検査対象にしている"); ok = False
        # 変異1 fixなし: implemented/applied なのに fix 証拠なし(ECO-060 違反の直接様式)
        if not any(c == "E15" for c, _ in _lef({"ECO-100": "implemented"}, 100, {}, _anc(set()))):
            print("selftest FAIL: fix 証拠なし implemented を検出できない(E15)"); ok = False
        if not any(c == "E16" for c, _ in _lef({"ECO-100": "applied"}, 100, {"ECO-100": {"fix": ["f"], "accept": []}}, _anc(set()))):
            print("selftest FAIL: accept 証拠なし applied を検出できない(E16)"); ok = False
        # 変異2 順序逆転: accept が fix より先(fix が accept の祖先でない)
        if not any(c == "E17" for c, _ in _lef({"ECO-100": "applied"}, 100, {"ECO-100": {"fix": ["f"], "accept": ["a"]}}, _anc({("a", "f")}))):
            print("selftest FAIL: fix/accept 順序逆転を検出できない(E17)"); ok = False
        # 変異2' 状態と証拠の乖離: staged なのに accept 証拠が履歴に存在(逆行)
        if not any(c == "E17" for c, _ in _lef({"ECO-100": "staged"}, 100, {"ECO-100": {"fix": [], "accept": ["a"]}}, _anc(set()))):
            print("selftest FAIL: staged+accept 証拠の乖離を検出できない(E17)"); ok = False
        # 変異3 参照先不在: 台帳に実在しない ECO への trailer(存在しない参照先の一般形)
        if not any(c == "E18" for c, _ in _lef({"ECO-100": "staged"}, 100, {"ECO-999": {"fix": ["f"], "accept": []}}, _anc(set()))):
            print("selftest FAIL: 実在しない ECO への証拠を検出できない(E18)"); ok = False
        # 変異4 状態飛び越し/逆行/非 staged 登場(遷移エッジ)
        if not any(c == "E19" for c, _ in _leg({"ECO-100": "staged"}, {"ECO-100": "applied"}, 100)):
            print("selftest FAIL: staged→applied 飛び越しを検出できない(E19)"); ok = False
        if not any(c == "E19" for c, _ in _leg({"ECO-100": "applied"}, {"ECO-100": "implemented"}, 100)):
            print("selftest FAIL: 状態逆行を検出できない(E19)"); ok = False
        if not any(c == "E19" for c, _ in _leg({}, {"ECO-101": "applied"}, 100)):
            print("selftest FAIL: 新規エントリの非 staged 登場を検出できない(E19)"); ok = False
        # 変異5(別系統ブランチの証拠を数えない)は git 抽出層の性質 — --selftest-lifecycle(実 git 統合)で検証
    print("selftest:", "OK" if ok else "FAIL")
    sys.exit(0 if ok else 1)

# ---- --commit-msg(ECO-061): 遷移コミット自身への trailer 強制(commit-msg hook から呼ぶ)----
# pre-commit 時点では遷移コミットの trailer が履歴に未出現(自己参照制約)のため、
# メッセージ側を commit-msg hook で fail-closed 検査する。E15〜E17 は次回以降の実行が履歴側から検査。
if "--commit-msg" in sys.argv:
    _i = sys.argv.index("--commit-msg")
    if _i + 1 >= len(sys.argv):
        print("usage: validate_bom.py --commit-msg <msgfile>"); sys.exit(2)
    with open(sys.argv[_i + 1], encoding="utf-8", errors="replace") as _f:
        _msg = _f.read()
    _repo = os.path.dirname(BOMDD)
    _rc, _staged_text = _git(_repo, "show", ":" + REG_RELPATH)
    if _rc != 0:
        sys.exit(0)  # register が index に無い(merge 中間状態等)— 本検査の対象外
    try:
        _staged = yaml.safe_load(_staged_text) or {}
    except yaml.YAMLError as e:
        print(f"[lifecycle] staged register が YAML として読めない: {e}"); sys.exit(1)
    _afn, _scheme_err = lifecycle_scheme(_staged)
    if _scheme_err:
        print(f"[lifecycle] {_scheme_err}"); sys.exit(1)
    # ECO-078 症状B: 比較元 old はマージ中 MERGE_HEAD も合算(ブランチ正規遷移を新規登場と誤認しない)
    _head_st = combined_head_status(_repo) or {}
    _st_st = register_status_map(_staged.get("changes"))
    _problems = [m for _, m in lifecycle_edge_findings(_head_st, _st_st, _afn)]
    _NEED = {("staged", "implemented"): TRAILER_FIX, ("implemented", "applied"): TRAILER_ACCEPT}
    for _cid in sorted(_st_st):
        _n = eco_num(_cid)
        if _n is None or _n < _afn:
            continue
        _t = _NEED.get((_head_st.get(_cid), _st_st[_cid]))
        # ECO-078 症状A: trailer は git 基準(最終段落ブロックのみ)で判定 — 履歴側 %(trailers) と単一解釈
        if _t and not msg_has_trailer(_msg, _t, _cid):
            _problems.append(f"{_cid}: 遷移 {_head_st.get(_cid)}→{_st_st[_cid]} のコミットに trailer 「{_t}: {_cid}」が無い(メッセージ末尾の trailer ブロックに置く — 中間段落は不可)")
    if _problems:
        print("[lifecycle] commit-msg 検査 FAIL:")
        for _p in _problems:
            print("  - " + _p)
        sys.exit(1)
    sys.exit(0)

# ---- --selftest-lifecycle(ECO-061): git 抽出層の実 git 統合陽性対照 ----
# 一時リポで「正常(fix→accept)」「別系統ブランチの証拠は数えない」「順序逆転」を実測する。
# 一時リポ生成を伴うため pre-commit には載せず、eco-accept の受入と手動/CI で回す。
if "--selftest-lifecycle" in sys.argv:
    import tempfile, shutil, stat
    ok = True
    _REG_MIN = ("lifecycle_evidence:\n  scheme: trailer-v1\n  applies_from: ECO-100\n"
                "changes:\n  - id: ECO-100\n    status: {st}\n")
    def _mkrepo():
        d = tempfile.mkdtemp(prefix="vp2-lifecycle-")
        for a in (["init", "-q", "-b", "main"],):
            _git(d, *a)
        os.makedirs(os.path.join(d, "bomdd"), exist_ok=True)
        return d
    def _wreg(d, st):
        with open(os.path.join(d, REG_RELPATH), "w", encoding="utf-8") as f:
            f.write(_REG_MIN.format(st=st))
    def _commit(d, msg):
        _git(d, "add", "-A")
        _git(d, "-c", "user.name=selftest", "-c", "user.email=selftest@local",
             "commit", "-q", "--allow-empty", "-m", msg)
    def _check(d):
        with open(os.path.join(d, REG_RELPATH), encoding="utf-8") as f:
            reg_l = yaml.safe_load(f.read())
        afn, e = lifecycle_scheme(reg_l)
        if e:
            return [("E14", e)]
        ev = collect_trailer_evidence(d)
        hs = register_status_map(head_register_changes(d) or [])
        return lifecycle_evidence_findings(hs, afn, ev, git_is_ancestor(d))
    def _rm(d):
        def onerr(fn, path, _exc):
            os.chmod(path, stat.S_IWRITE); fn(path)
        shutil.rmtree(d, onerror=onerr)
    # (a) 正常系: 起票→fix(trailer)→accept(trailer)→ 所見ゼロ
    d = _mkrepo()
    try:
        _wreg(d, "staged"); _commit(d, "起票(eco-100)")
        _wreg(d, "implemented"); _commit(d, "fix(eco-100)\n\nBomDD-ECO-Fix: ECO-100")
        _wreg(d, "applied"); _commit(d, "accept(eco-100)\n\nBomDD-ECO-Accept: ECO-100")
        r = _check(d)
        if r:
            print(f"selftest-lifecycle FAIL: 正常系で所見 {r}"); ok = False
    finally:
        _rm(d)
    # (b) 変異5 別系統ブランチ: fix trailer が未マージ side ブランチにのみ存在 → E15
    d = _mkrepo()
    try:
        _wreg(d, "staged"); _commit(d, "起票(eco-100)")
        _git(d, "checkout", "-q", "-b", "side")
        _commit(d, "fix(eco-100) on side\n\nBomDD-ECO-Fix: ECO-100")
        _git(d, "checkout", "-q", "main")
        _wreg(d, "implemented"); _commit(d, "implemented without trailer")
        r = _check(d)
        if not any(c == "E15" for c, _ in r):
            print(f"selftest-lifecycle FAIL: 別系統ブランチの fix 証拠を誤って採用(E15 が出ない: {r})"); ok = False
    finally:
        _rm(d)
    # (c) 変異2 順序逆転(実 DAG): accept コミットが fix コミットに先行 → E17
    d = _mkrepo()
    try:
        _wreg(d, "staged"); _commit(d, "起票(eco-100)")
        _commit(d, "accept first\n\nBomDD-ECO-Accept: ECO-100")
        _wreg(d, "applied"); _commit(d, "fix later\n\nBomDD-ECO-Fix: ECO-100")
        r = _check(d)
        if not any(c == "E17" for c, _ in r):
            print(f"selftest-lifecycle FAIL: 実 DAG の順序逆転を検出できない(E17 が出ない: {r})"); ok = False
    finally:
        _rm(d)
    # (d) ECO-078 症状A: trailer の解釈は git 基準(最終段落ブロックのみ)に統一する。
    # 中間段落の trailer は「無い」と判定しなければならない(hook が E15 より緩い=fail-closed の破れ)。
    try:
        _mid = "fix(eco-100)\n\nBomDD-ECO-Fix: ECO-100\n\nCo-Authored-By: a <a@b.c>"
        _fin = "fix(eco-100)\n\nBomDD-ECO-Fix: ECO-100\nCo-Authored-By: a <a@b.c>"
        if msg_has_trailer(_mid, TRAILER_FIX, "ECO-100"):
            print("selftest-lifecycle FAIL: 中間段落 trailer を trailer と誤認(%(trailers) と不一致 — ECO-078 症状A)"); ok = False
        if not msg_has_trailer(_fin, TRAILER_FIX, "ECO-100"):
            print("selftest-lifecycle FAIL: 最終段落ブロックの trailer を認識できない(ECO-078 症状A 過剰是正)"); ok = False
    except NameError:
        print("selftest-lifecycle FAIL: msg_has_trailer が存在しない(hook 側 trailer 解釈が git 基準に未統一 — ECO-078 症状A)"); ok = False
    # (e) ECO-078 症状B: ブランチで正規遷移(staged→implemented→applied)した ECO を main へ
    # マージする pre-commit(MERGE_HEAD 存在)で、E19 が「非 staged 登場」と誤報しないこと。
    d = _mkrepo()
    try:
        _wreg(d, "staged"); _commit(d, "起票(eco-100)")
        _git(d, "checkout", "-q", "-b", "side")
        _wreg(d, "implemented"); _commit(d, "fix(eco-100)\n\nBomDD-ECO-Fix: ECO-100")
        _wreg(d, "applied"); _commit(d, "accept(eco-100)\n\nBomDD-ECO-Accept: ECO-100")
        _git(d, "checkout", "-q", "main")
        with open(os.path.join(d, "other.txt"), "w", encoding="utf-8") as f:
            f.write("x")  # main を前進させ non-ff にする(マージコミット=MERGE_HEAD が発生する形)
        _commit(d, "unrelated on main")
        _git(d, "merge", "--no-commit", "--no-ff", "side")
        try:
            _comb = combined_head_status(d)
        except NameError:
            print("selftest-lifecycle FAIL: combined_head_status が存在しない(E19 が線形履歴前提のまま — ECO-078 症状B)"); ok = False
        else:
            with open(os.path.join(d, REG_RELPATH), encoding="utf-8") as f:
                _wt = register_status_map((yaml.safe_load(f.read()) or {}).get("changes"))
            r = lifecycle_edge_findings(_comb or {}, _wt, 100)
            if any(c == "E19" for c, _ in r):
                print(f"selftest-lifecycle FAIL: マージ進行中の正規遷移済み ECO を E19 が誤報({r} — ECO-078 症状B)"); ok = False
    finally:
        _rm(d)
    print("selftest-lifecycle:", "OK" if ok else "FAIL")
    sys.exit(0 if ok else 1)

def is_e(x):  # E-BOM 部品 id か
    return isinstance(x, str) and x.startswith("E-")

# ---------------------------------------------------------------- [E20] 構文床(ECO-120)
# 床が破れていたら意味検査に進まない(MAIN_LOADED が壊れている場合の生 traceback 回避も兼ねる)。
if sweep_yaml_syntax():
    _errs = [f for f in findings if f[0] == "ERROR"]
    print("bomdd integrity validator — YAML 構文床(E20)で停止")
    for _sev, _code, _msg in _errs:
        print(f"  [{_code}] {_msg}")
    print(f"\nresult: {len(_errs)} error(s), 0 warning(s)")
    sys.exit(1)

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

# ---------------------------------------------------- Lifecycle(ECO-061・E14〜E19)
# 状態不変条件: E15〜E17 は「コミット済み HEAD の台帳状態」を基準に履歴証拠と突合する
# (遷移進行中のコミットは trailer が未出現のため — その面は commit-msg hook が塞ぐ)。
# E19 は HEAD → 作業ツリーの遷移エッジを検査する。git 不能・shallow は fail-closed
# (--no-git のみ明示スキップ = 宣言された縮退。黙って skip しない)。
applies_from_num, scheme_err = lifecycle_scheme(reg)
if scheme_err:
    err("E14", scheme_err)
elif "--no-git" in sys.argv:
    print("lifecycle: --no-git 指定により E15〜E19 をスキップ(明示宣言 — 履歴証拠は未検査)")
else:
    _repo = os.path.dirname(BOMDD)
    _rc, _shallow = _git(_repo, "rev-parse", "--is-shallow-repository")
    if _rc != 0:
        err("E14", "git が利用できず lifecycle 検査(E15〜E19)を実行できない(fail-closed。git 不在環境は --no-git で明示スキップ)")
    elif _shallow.strip() == "true":
        err("E14", "shallow clone のため履歴証拠を検査できない(fail-closed。git fetch --unshallow を実行)")
    else:
        _evidence = collect_trailer_evidence(_repo)
        if _evidence is None:
            err("E14", "git log から trailer 証拠を収集できない(fail-closed)")
        else:
            _head_ch = head_register_changes(_repo)
            _head_st = register_status_map(_head_ch) if _head_ch is not None else {}
            _wt_st = register_status_map(cos)
            _base_st = _head_st if _head_ch is not None else _wt_st
            _known = set(_head_st) | set(_wt_st)
            for _code, _msg in lifecycle_evidence_findings(
                    _base_st, applies_from_num, _evidence, git_is_ancestor(_repo), known_ids=_known):
                err(_code, _msg)
            if _head_ch is not None:
                # ECO-078 症状B: E19 の old はマージ中 MERGE_HEAD も合算(線形履歴前提の除去)。
                # E15〜E17(証拠検査)は従来どおり HEAD 祖先基準 — マージ中の未合流 ECO は
                # _base_st(HEAD 台帳)に現れないため誤検知しない。
                _edge_st = combined_head_status(_repo)
                for _code, _msg in lifecycle_edge_findings(
                        _edge_st if _edge_st is not None else _head_st, _wt_st, applies_from_num):
                    err(_code, _msg)

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
