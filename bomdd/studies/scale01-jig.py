# scale-01 遡及測定治具(設計者側)— 規模の壁(gap-analysis B3/C)の実データ化
# 目的: ViewPrism2 の実 ECO 台帳(34件)で「予測影響集合(impacted_bom) vs 実 diff」の
#       under/over-inclusion を部品数十(29 unit)の規模で全数採点する。
#
# 採点規約(凍結):
# 1. 対象 = impacted_bom を持ち status∈{implemented,applied} かつ件名対応コミット≥1 の ECO。
#    件名(%s)に "ECO-0NN" を含むコミットのみ対応付け(本文言及は履歴註の誤帰属リスクがあるため不使用)。
#    対応コミット 0 の ECO は「対応不能(記録ギャップ)」として除外数を報告(母集団の正直な開示)。
# 2. 予測ファイル集合 P: impacted_bom の M-* は直接、E-* は M-BOM ebom_refs 逆引きで unit→artifact.path。
#    CP-/DC-/K-/20-spec/散文要素はファイル写像外(BOM 層参照としてカウントのみ)。
# 3. 実 diff A: 対応コミット全体の union diff。bomdd/**・*.md は除外(台帳改訂は常時許容 —
#    R-052「bomdd/ 常時許容」規約の遡及適用)。bin/obj も除外。
# 4. unit 帰属は artifact.path の最長前方一致(dir unit と配下 file unit の重なりは深い方)。
# 5. under-inclusion = A のうち予測 unit に帰属しないファイル(unit 単位で集計)。
#    over-inclusion = P の unit のうち A に1ファイルも現れない unit。
# 6. B3 指標: ECO ごとの実変更 unit 数(組立面=複数 unit に触る変更の頻度)。
import subprocess, yaml, re, json, sys
from collections import Counter, defaultdict

ROOT = r'C:\Demo\source\repos\ViewPrism2'

def git(*args):
    return subprocess.run(['git', '-C', ROOT] + list(args), capture_output=True, text=True, encoding='utf-8').stdout

mb = yaml.safe_load(open(f'{ROOT}/bomdd/32-mbom.yaml', encoding='utf-8'))
units = mb['mbom']['manufacturing_units']
unit_path = {}
e2units = defaultdict(set)
for u in units:
    a = u.get('artifact')
    p = (a.get('path') if isinstance(a, dict) else a) or ''
    unit_path[u['id']] = p.replace('\\', '/')
    for e in u.get('ebom_refs', []):
        e2units[e].add(u['id'])

def attribute(f):
    """ファイルを最長一致で unit に帰属(なければ None)"""
    best, blen = None, -1
    for uid, p in unit_path.items():
        if not p: continue
        if f == p or f.startswith(p.rstrip('/') + '/') or f.startswith(p + '/'):
            if len(p) > blen: best, blen = uid, len(p)
        elif f == p:  # exact file
            if len(p) > blen: best, blen = uid, len(p)
    return best

reg = yaml.safe_load(open(f'{ROOT}/bomdd/60-change-register.yaml', encoding='utf-8'))
changes = reg['changes']

# 件名→ECO 対応
log = git('log', '--format=%H\t%s')
eco_commits = defaultdict(list)
for line in log.strip().split('\n'):
    h, s = line.split('\t', 1)
    for m in set(re.findall(r'ECO-0\d\d', s)):
        eco_commits[m].append(h)

rows = []
skipped = {'no_impacted_bom': [], 'not_applied': [], 'no_commits': []}
for c in changes:
    eid = c['id']
    ib = c.get('impacted_bom')
    status = (c.get('status') or '').split()[0]
    if not ib:
        skipped['no_impacted_bom'].append(eid); continue
    if status not in ('implemented', 'applied'):
        skipped['not_applied'].append(eid); continue
    commits = eco_commits.get(eid, [])
    if not commits:
        skipped['no_commits'].append(eid); continue

    # 予測 unit 集合
    pred_units, nonfile_refs = set(), []
    for x in ib:
        x = str(x)
        ids = re.findall(r'\b([EM]-[A-Z0-9-]+)\b', x)
        mapped = False
        for i in ids:
            if i.startswith('M-') and i in unit_path:
                pred_units.add(i); mapped = True
            elif i in e2units:
                pred_units.update(e2units[i]); mapped = True
        if not mapped:
            nonfile_refs.append(x)

    # 実 diff(union)
    actual = set()
    for h in commits:
        for f in git('show', '--name-only', '--format=', h).strip().split('\n'):
            f = f.strip()
            if not f or f.startswith('bomdd/') or f.endswith('.md'): continue
            if '/obj/' in f or '/bin/' in f: continue
            actual.add(f)

    actual_units = Counter()
    under_files = []
    for f in sorted(actual):
        uid = attribute(f)
        if uid: actual_units[uid] += 1
        if uid not in pred_units:
            under_files.append((f, uid))
    over_units = sorted(u for u in pred_units if u not in actual_units)
    rows.append({
        'eco': eid, 'status': status, 'commits': len(commits),
        'pred_units': sorted(pred_units), 'nonfile_refs': len(nonfile_refs),
        'actual_files': len(actual), 'actual_units': sorted(actual_units),
        'under_files': under_files, 'over_units': over_units,
    })

print(json.dumps({'scored': rows, 'skipped': skipped}, ensure_ascii=False, indent=1))
