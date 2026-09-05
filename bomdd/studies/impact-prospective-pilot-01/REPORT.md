# impact-prospective-pilot-01 — ECO-142 予備試験の結果(探索的・2026-09-05)

> 手順は [PROTOCOL.md](PROTOCOL.md)、裁定の履歴は [ADJUDICATION-HISTORY.md](ADJUDICATION-HISTORY.md)、正本は
> `trials/PILOT-A`・`trials/PILOT-B`(trial.yaml + execution/evaluation receipt)、導出物は `projection.yaml`。
> **A/B 差の有無は成功条件ではない。** 本 report は「手順が回ったか」「事例選定を含めて何が分かったか」を記録する。

## 1. 実施記録

| 工程 | 設備 | 入力の封印 | 監査 |
|---|---|---|---|
| 改善(A→B) | Codex gpt-5.6-sol high・独立セッション | pkg-improver manifest `1c56700ad607…`(ECO-141 以降の記述 0 件) | コマンド 24 件・書込 4 ファイル(bom/ 3 + CHANGELOG)— パッケージ外 0 |
| 初回裁定 | 同・別セッション(read-only) | ADJUDICATION.sealed sha256 `5866bda1…` @ 02:40:48Z(予測より前) | コマンド 9 件・パッケージ外 0 |
| 予測 A ×2 | Codex gpt-5.6-sol medium | trial.yaml input `0795bb2f…` rubric `b5fb886d…` | コマンド 14 / 17・パッケージ外 0 |
| 予測 B ×2 | 同 | input `d0a27018…` rubric 同一 | コマンド 7 / 10・パッケージ外 0 |

- 変更要求は R1(maintainer 原報告ベース)のみ。order §1〜§3・register の findings は解答混入のため不使用。
- 改善者の編集: 30-ebom 19 行・32-mbom 39 行・33-control-plan 12 行(`artifacts/bom-A-to-B.diff`・根拠つき CHANGELOG)。
  内容は統合裁定面/relink 一本化/旧 Window 撤去の superseded 化と CP fixture の移管。**事前に固定した仮説(統合裁定の件数取得と
  M-DB-007 の写像が B に入る)は成立しなかった** — 改善者は ECO-139/140 の order が裁定した設計変更を反映したが、
  ECO-140 の diff が ImageRepository に加えた件数クエリの写像は追加しなかった。

## 2. 結果(封印裁定に対する evaluation receipt)

初回裁定(封印): change= {M-UI-IMAGETAB-035, M-DB-007, M-HARNESS-015} / investigate= {M-CORE-001}。

| run | change 予測 | 調査候補(追加分) | under(change) | over(change) | input tokens |
|---|---|---|---|---|---|
| A-01 | M-DB-007, M-UI-TRASH-032 | M-UI-IMAGETAB-035 | HARNESS-015, IMAGETAB-035 | TRASH-032 | 1,258,959 |
| A-02 | M-DB-007 | M-UI-013, IMAGETAB-035, INTEGRITY-055 | HARNESS-015, IMAGETAB-035 | — | 1,372,362 |
| B-01 | M-DB-007, M-UI-TRASH-032 | M-UI-013, IMAGETAB-035 | HARNESS-015, IMAGETAB-035 | TRASH-032 | 480,704 |
| B-02 | M-DB-007 | M-UI-013, IMAGETAB-035 | HARNESS-015, IMAGETAB-035 | — | 717,951 |

- 4 run すべて result= fail(under_change ≠ 空)。**A と B の M unit 集合は事実上同一**(B の変更が予測の根拠に使われなかった)。
- 4 run すべてが **M-DB-007 を change に入れた**。実 diff(55c491d)の src 変更は M-DB-007 のみで、これは一致。
- 4 run すべてが **M-HARNESS-015(tests)を予測しなかった**。裁定者は回帰ガード追加を change(certain)とし、実 diff にも
  90 行のテストが含まれる。予測者の出力様式に「テスト unit」を問う欄がなく、BOM 上も tests を担う unit が M-HARNESS-015 と
  明示されるだけで、変更要求から tests への辿り方が BOM に書かれていない。
- M-UI-IMAGETAB-035 は 4 run とも調査候補(change でない)。裁定者は change(certain)としたが、実 diff は触れていない
  (ADJUDICATION-HISTORY 訂正 1「是正方針の選択で不要化」)。訂正後の集合 {M-DB-007, M-HARNESS-015} に対しては、
  予測の change は DB-007 一致・HARNESS-015 欠落・run 1 系は TRASH-032 過剰。
- **根拠の出所**: A・B とも rationale は **ECO-098** の記録(30-ebom / 32-mbom の acceptance_note に残る「normal 6 件・
  hidden missing 262,045 件で約 1.1 秒停止」)を引いた。R1 のデータ形状と一致する過去事例が BOM に記録されており、
  予測はその記憶を辿った。B-01 は ECO-064/051 も引いた。
- コスト: B は A の 38〜52% の input tokens、コマンド数も半分(7/10 対 14/17)。B の superseded 表記が探索を減らした可能性が
  あるが、反復 2 では偶然と区別できない。

## 3. 計器欠陥(自己捕捉・較正で発見)

- 評価器 v1 は ```yaml フェンス内の YAML しか読まず、予測者 4 run 全てがフェンスなしで YAML を出力したため、A の初回評価が
  全 run missing-verdict になった。評価詳細と生出力の突合で捕捉し、v1 receipt を `runs/defective-evaluator-v1/` に隔離、
  評価器 v2(フェンス → 文書全体 → key 行からのブロックの順に読む)で再評価した。rubric は不変。
  新設計器の初回実使用で欠陥が出た 3 例目(ET-001 実行器・ET-002 評価器・本 pilot 評価器)。

## 4. 言えること / 言えないこと

**言えること(手順)**: 履歴のない許可ファイル集合・3 役の分離・裁定の事前封印・訂正の追記履歴・パッケージ外アクセスの監査は、
Codex の独立セッションで運用として回った。所要は改善 13 分・裁定 4 分・予測 4 run 各 5〜10 分。

**言えること(事例)**: この事例では BOM A だけで実 diff の src 変更 unit(M-DB-007)に 4/4 で到達したが、その根拠は
BOM に残る類似事例(ECO-098)の記録であり、E/M の依存関係や契約からの導出ではない。tests unit は BOM 経路で辿られなかった。
B の改善(統合面の superseded 化)はこの事例の予測に寄与しなかった。

**言えないこと**: BOM 改善が変更予測を改善するか(N=1・改善内容が事例の要所に触れていない)/ A と B のコスト差の再現性 /
到達モデル・到達 effort / 裁定者の行動上の盲検(構造でのみ担保)。

## 5. 前向き主評価への持ち越し

1. 改善対象の選び方: 隣接 ECO の order から導く改善は設計変更の反映に偏り、diff が生んだ実装写像(どのメソッドがどの品目の
   件数を数えるか)を落とす。改善者の入力に「diff から実装写像を抽出する」工程を明示するか、`(observed@rev)` 欄を
   必須にする案を、次の事例で比較する。
2. 出力様式に「テスト/検査 unit」の欄を持たせる(裁定者は含め、予測者は落とした — 様式差が測定に混入)。
3. BOM の履歴記述(acceptance_note の過去事例)は予測の主要な根拠源になる。これは BOM の価値(過去事例の記憶)であると同時に、
   「依存関係からの導出」を測るときの交絡でもある。次は履歴記述を含む/含まない BOM で分ける案を検討する。
4. 裁定の訂正語彙に「是正方針の選択で不要化」を追加した(実装の逸脱・設計変更・抽出限界に当たらない訂正)。
5. 主評価は前向き事例(ECO-082 の着手、または次の実変更)で、同じ手順を用いる。
