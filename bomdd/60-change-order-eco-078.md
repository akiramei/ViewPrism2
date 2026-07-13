# ECO-078(applied / クローズ済み 2026-07-13): ライフサイクル検査器の一括是正 — E19 マージ誤検知+E15/commit-msg hook の trailer 解釈不一致

- 起票: 2026-07-13(maintainer 指示。51-cheat-log の記録 2 件=ECO-076 accept 時+ECO-076/077 統合時を一括起票)
- 種別: 検査器/台帳の欠陥是正(工程ハーネス。製品コード不変 — ECO-061 と同型)
- 状態: applied(2026-07-13 gate②合格=§8)
- 関連: ECO-061(検査器の導入元=E14〜E19+--commit-msg+--selftest-lifecycle)/
  ECO-076・ECO-077(両欠陥の実発生現場)

## 1. 症状(実発生 2 件・いずれも 2026-07-13)

### 症状A: commit-msg hook と E15(履歴検査)の trailer 解釈不一致

- `git commit -m "<subject>" -m "BomDD-ECO-Fix: ECO-NNN" -m "Co-Authored-By: …"` のように
  trailer を**中間段落**に置くと、commit-msg hook(fail-closed のはずの検査)は**通過**するが、
  後続の validate_bom E15(履歴証拠検査)が**不合格**になる。
- 実発生 2 回: ECO-076(fix `2ceb938` → message のみ amend `542ef87` で解消)・
  ECO-077(fix `00ba06e` → amend `ee9ecd4`)。いずれも内容同一の amend を要した。

### 症状B: E19 がマージコミットを線形履歴前提で誤検知

- 別ブランチで正規に staged→implemented→applied を歩んだ ECO を main へマージする際、
  pre-commit の E19 が「新規エントリが applied で登場(起票を経ない状態)」と誤報しブロック。
- 実発生: ECO-077 の main 統合(merge `3ce339b`)。トレーラ証拠(E15〜E17)はマージ後の
  祖先関係で正しく成立するため、マージコミットのみ `--no-verify` で通し、直後の
  `validate_bom` 0/0+`--selftest-lifecycle` OK を成立証拠として 51-cheat-log へ記録した。

## 2. 工程診断(R2)

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD/BOM/実装(製品) | 無関係 | 製品コード・台帳データとも健全。欠陥は検査器(工程ハーネス)に閉じる |
| 検査器(症状A) | **欠陥=検査基準の二重定義** | hook 側=validate_bom.py:336 `re.search(rf"^{_t}: {cid}\s*$", _msg, re.M)`(メッセージ全行 grep=**段落位置を問わない**)。履歴側=validate_bom.py:212-214 `%(trailers:key=…)`(git の trailer 解釈=**最終段落ブロックのみ**)。同じ契約を 2 つの実装が異なる緩さで検査しており、hook の方が緩い(fail-closed の破れ) |
| 検査器(症状B) | **欠陥=線形履歴の暗黙前提** | E19=lifecycle_edge_findings(validate_bom.py:185-198)は比較元 old を `head_register_changes(repo, ref="HEAD")`(:236)=**第 1 親のみ**から取る。マージ進行中(`.git/MERGE_HEAD` 存在)は MERGE_HEAD 側で正規遷移済みの status が old に反映されず、:193-195 の「新規エントリは staged で登場」が誤爆 |

**結論: 検査器の欠陥 2 件(doc/tooling-only)。製品 src/tests に触れない。**
裁定は不要(ECO-061 前例=検査器/台帳の欠陥是正はハーネス変更として設計者が進め、
gate②=maintainer による selftest 実行で受け入れる)。

## 3. 切り分け済みの事実

確定:

1. 症状A の再現条件は「trailer が最終段落ブロック以外にある」こと。git の公式解釈
   (`git interpret-trailers` / `%(trailers)`)は最終段落ブロックのみを trailer と認める。
   hook の grep はこれより緩い=中間段落を誤って合格させる。逆(hook が厳しすぎる)方向の
   不一致は未観測。
2. 症状B のマージ pre-commit で誤報したのは **E19 のみ**(E15〜E17 は発火せず)。
   マージコミット確定後は HEAD 祖先に両親系列が入るため、E15〜E17・E19 とも 0 error
   (merge `3ce339b` 直後の実測)。つまり欠陥は「マージ進行中の pre-commit」に限局する。
3. `--selftest-lifecycle`(一時リポの陽性対照)には「中間段落 trailer を hook 検査が拒否する」
   「ブランチ正規遷移→マージで E19 が誤報しない」の 2 ケースが**存在しない**(検査の谷間が
   selftest の網にも空いていた)。
4. 逃げ道(`--no-verify`)の使用は 1 回(merge `3ce339b`)・cheat-log 記録済み・直後の
   validate で成立証明済み。台帳の実状態に不整合はない。

疑い(未検証):

- E19 と同じ「HEAD=第 1 親のみ」前提が --commit-msg 経路(validate_bom.py:326-329)にもある。
  マージ中に register へ触れるコミットを作る場合、hook 側でも同種の誤爆があり得る
  (今回のマージは commit-msg hook は通過している=`--no-verify` は pre-commit と commit-msg の
  両方を飛ばすため未観測。是正時に selftest で実測する)。

## 4. 是正方針(案・着手時確定)

- **A(hook の trailer 解釈を git 基準へ)**: --commit-msg 検査(:336)の行 grep を
  `git interpret-trailers --parse` 相当(msgfile を対象に trailer ブロックのみ抽出)へ置換し、
  履歴側 `%(trailers)` と**単一の解釈**に揃える。diff 小(validate_bom.py 1 関数)。
- **B(E19 のマージ対応)**: lifecycle 検査(E19 と --commit-msg 内の同型比較)で、
  `.git/MERGE_HEAD` が存在する場合は old を HEAD と MERGE_HEAD の status 合算
  (いずれかに存在すれば採用・両方に存在すれば**遷移がより進んだ側**)で構成する。
  検査を保ったまま誤検知だけを除く(informational 降格はしない)。diff 小。
- **プローブ先行(R5)**: `--selftest-lifecycle` へ赤ケース 2 本を先に追加し不合格を実測 —
  ①中間段落 trailer のメッセージを --commit-msg が拒否すること(現状=合格してしまう)
  ②一時リポで「ブランチ上の正規遷移→main へ merge」の E19 が 0 error であること
  (現状=誤報)。是正後に緑転を確認。
- 既存の検査次元(E14〜E18・ずるチェック)と fail-open 縮退規約(PyYAML 不在時 skip)は不変。

## 5. 影響 BOM

- 検査器: `bomdd/validate_bom.py`(--commit-msg 節:306-343・lifecycle_edge_findings:185-198・
  git 抽出層:236 近傍・--selftest-lifecycle:345 以降)。`bomdd/hooks/commit-msg` は
  validate_bom 呼び出しのみのため原則不変(メッセージ文言の追随があれば同時)。
- 台帳: 51-cheat-log の記録 2 件(ECO-076 accept 時・ECO-076/077 統合時)を本 ECO で処置済み化。
- 製品 src/tests・REQ/spec/E-BOM/M-BOM/CP(製品系)・Oracle: **変更なし**。
- 受入: 機械=validate_bom 0/0+--selftest-lifecycle OK(新ケース 2 本込み)+
  dotnet build/Tests/Oracle 全緑(不変確認)。

## 6. 残ゲート

- gate①(裁定): 不要(検査器の欠陥是正・ECO-061 前例)。
- gate②(golden): maintainer による実行確認=`python bomdd/validate_bom.py`(0/0)+
  `python bomdd/validate_bom.py --selftest-lifecycle`(OK・新ケース 2 本を含む)+
  中間段落 trailer コミットの手元再現が hook でブロックされること。

## 7. 実施記録(2026-07-13 /eco-fix)

### 7.1 先行 probe(R5)

- `--selftest-lifecycle` へ赤ケース 2 本を**製品変更前に**追加:
  (d)=中間段落 trailer を trailer と誤認しない+最終段落ブロックは認識する(症状A)、
  (e)=一時リポでブランチ正規遷移(staged→implemented→applied)→ main へ non-ff merge 進行中
  (MERGE_HEAD 存在)の E19 が誤報しない(症状B)。
- **是正前実測: 2 件とも FAIL**(`msg_has_trailer が存在しない`/`combined_head_status が存在しない`
  =検査の谷間が selftest の網にも空いていた事実の固定)。

### 7.2 是正 diff(validate_bom.py のみ・製品不変)

- **案A(trailer 解釈の単一定義)**: 純粋層に `message_trailer_block`(git の解釈=最終段落
  ブロックのみ・全行 `Key: value` 形のときだけ trailer と認める)+`msg_has_trailer` を新設。
  --commit-msg 経路の全行 grep(旧 :336)を `msg_has_trailer` へ置換=履歴側 `%(trailers)` と
  単一解釈。厳しすぎる側の誤差は commit 時の明瞭なエラーで止まる(fail-closed 側に倒す)。
- **案B(E19 のマージ対応)**: git 抽出層に `merge_parent_refs`(MERGE_HEAD 存在時は
  [HEAD, MERGE_HEAD])+`combined_head_status`(全親の status 合算・同一 ECO は
  STATUS_ORDER でより進んだ側)を新設。E19 の比較元 old を本経路・--commit-msg 経路の
  **両方**で合算値へ切替(§3 疑い=--commit-msg 経路の同居も同時に解消)。
  E15〜E17(証拠検査)は従来どおり HEAD 祖先基準(マージ中の未合流 ECO は HEAD 台帳に
  現れないため誤検知しない — 注記コメントで固定)。
- hook(bomdd/hooks/commit-msg)は validate_bom 呼び出しのみのため不変。エラーメッセージへ
  「中間段落は不可」の説明を追加。

### 7.3 機械受入

- `--selftest-lifecycle`: **OK(新ケース 2 本が緑転・既存 a/b/c 不変)**。`--selftest`: OK。
- `validate_bom`: 0 error / 0 warning。
- 製品不変確認: `dotnet build` 0/0・`ViewPrism2.Tests` 655/655・`ViewPrism2.Oracle` 109+2 known skip
  (R6 不変)。R7 並置=対象外(UI サーフェスに触れない)。

## 8. クローズ(2026-07-13 gate②合格)

### 8.1 実行確認(maintainer)

- `validate_bom` 0/0・`--selftest-lifecycle` OK(新ケース (d)(e) 込み)・中間段落 trailer
  コミットの手元再現が hook でブロックされる(「末尾の trailer ブロックに置く — 中間段落は不可」
  の説明つき)ことを確認。
- 副次の実地証拠: fix コミット `609b59e` 自体が staged→implemented の遷移コミットとして
  **修正後の hook を実運用で通過**し、`%(trailers)` でも認識される(E15 との単一解釈の実地確認)。

### 8.2 再発防止(恒久化の所在)

- `--selftest-lifecycle` (d)(e) が両欠陥様式の陽性対照として恒久 pin(検査器の網の穴自体を塞いだ)。
- trailer 解釈の正本=`message_trailer_block`(単一実装)。E19 の比較元=`combined_head_status`
  (親合算)。E15〜E17 が HEAD 祖先基準で足りる理由は本経路に注記コメントで固定。
- 51-cheat-log の記録 2 件(ECO-076 accept 時・ECO-076/077 統合時)は処置済み注記で closed。

### 8.3 教訓

- **同じ契約を検査する実装が 2 つあるとき、緩い側が fail-closed を破る**: trailer 契約は
  「hook(メッセージ検査)」と「E15(履歴検査)」の 2 実装を持ち、hook の全行 grep が git の
  `%(trailers)` より緩かったため、「hook は通るが履歴検査で落ちる」谷間が 2 回実発生した。
  検査契約は**単一の解釈関数**(message_trailer_block)に集約し、両経路がそれを共有する。
  ECO-077 教訓「導線変更=パラメータ供給経路の変更」の検査器版=**契約の二重定義は
  暗黙の分岐であり、どちらかが必ず緩む**。
- **検査器は自分の暗黙前提(今回=線形履歴)を selftest の陽性対照として持つ**: E19 の
  マージ盲点は「検査の谷間が selftest の網にも空いていた」二重の欠落だった。欠陥を直すとき、
  同じ欠陥様式の陽性対照を selftest へ同時に刻む(GF-073-08 教訓「検査手順も工程診断の対象」の
  検査器実装版・rule of three 2 例目)。
