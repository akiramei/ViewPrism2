# 変更管理手順(ViewPrism2 運用プロファイル)

> 典拠: BomDD playbook §8(Phase 7)+ `method/prompts/phase7-change-order.md`(方法論リポ)。
> 本書はそれを **ViewPrism2 の 4 リポ実運用へ写像した運用プロファイル**であり、方法論と矛盾した場合は
> 方法論が正。運用で得た教訓の一般形は方法論リポへ昇格させる(本書には固有名を残す)。
> 実演ケーススタディ: **ECO-038**(§6)。

## 1. リポジトリの役割と変更の流れる方向

| リポ | 役割 | 変更の入口になるケース |
|---|---|---|
| `BomDD` | 方法論(法規・手順書・テンプレ) | 手順自体の欠陥・教訓の一般形昇格 |
| `BomDD-Plm` | PLM 工具(lint/viewer・DB なし・git が正本) | 準拠 findings → `bomdd/plm-intake/` 修復票 |
| `ViewPrismUI` | **CAD(設計原器)**: mock+screens docs+review_points 裁定 | 設計判断・視覚仕様・未確定事項の裁定 |
| `ViewPrism2` | 製品: `bomdd/` 台帳+src+tests | ECO の実行と記録(本書の主対象) |

原則は一方向: **裁定・設計は CAD へ、実行・記録は製品へ、手順の改善は方法論へ**。
乖離時の権威は常に ViewPrismUI(`docs/02_mock_fidelity_policy.md` P3)。

## 2. 共通規律(全シナリオ共通・違反は cheat 扱い)

- **R1 起票なき製品コード変更の禁止**。src/tests への diff は、register に対応する ECO
  エントリが先に存在しなければならない。(機械化候補: bomdd-lint R-052 diff_audit の拡張)
- **R2 修正着手前の工程診断**。所見はまず mock(CAD)/UI-IR/BOM/実装 のどの工程の欠陥かを
  診断し、上流欠陥なら上流(CAD→BOM)を先に直す。コードから入る経路は存在しない。
- **R3 スコープ外所見の「ついで修正」禁止**。工程内で見つけた別問題は
  (a) 51-cheat-log へ記録 (b) 分離起票(ECO-036→ECO-037 が模範) の二択のみ。
  現 ECO の diff に混ぜない(63 diff 監査/R-052 が機械検出)。
- **R4 human gate は 2 つだけ**: **裁定**(設計判断・選択肢選び)と **golden**(実機承認)。
  AI は次の gate まで止まらずに進め、gate 到達時は「人間がやることはこれだけ」を明示して
  停止する。調査・起票・診断・プローブ・実装・機械受入・文書同期・register 更新は AI の作業。
- **R5 是正はプローブ先行**。欠陥是正は、是正前に不合格となる回帰テスト(または実測プローブ)で
  真因を裏取りしてからコードに触る(ECO-037/038 の規律)。プローブが不合格にならなければ
  診断が誤り — R2 へ差し戻す。
- **R6 既存固定オラクル行は変更しない**(回帰のヤードスティック)。変更分の受入は新規行として追加。
- **R7 セルフゴールデン(出荷前並置)**。UI サーフェスに触れる fix は、golden 提示の**前**に
  AI 自身が是正対象の各サーフェスを CAD 視覚原器(captures)と並置し、差分を全列挙して
  「裁定済み許容差分/転写漏れ」に分類する。**転写漏れ 0 になるまで golden に出さない**。
  並置の手段は headless レンダリング(Avalonia.Headless の CaptureRenderedFrame 等)または
  実機スクリーンショット。視覚 probe は CAD の**視覚契約チェックリスト**
  (ViewPrismUI テンプレ必須節)から fix 時に**先行生成**する(GF 後追い禁止)。
  ダイアログ共通言語(ViewPrismUI `docs/03_dialog_language.md`)に触れた場合は、直した面だけでなく
  **適用面マトリクスの該当列の全面**を検査対象にする。
  背景: ECO-073 GF-073-01〜07 = captures 同梱後も「実装と mock を初めて並置する人」が maintainer
  になっており、同一言語の転写漏れが面を変えて 4 回連鎖した(機械受入 4 点は視覚を検査しない)。

## 3. シナリオ別経路

| シナリオ | 入口 | human gate | 実例 |
|---|---|---|---|
| 1. 新機能追加 | 要求(REQ 追加) | 裁定+golden | ECO-020/021(作業タブ) |
| 2. 既存機能拡張 | 要求 or 未確定事項(FL-* / VE-* 等) | 裁定+golden | ECO-025(表示列モデル) |
| 3. 不具合修正 | 症状の観測 | (症状報告)+golden | **ECO-038**(§6) |
| 4. OSS セキュリティ | アドバイザリ(CVE/NU 警告) | 処置裁定 | NU1903(ECO-026 内で根本解消) |

### 3.1 新機能追加 — CAD から入る

1. 要求を REQ として受理(方法論 Phase 1 の根拠精度 G1)。
2. **CAD 先行**: ViewPrismUI で mock+`docs/screens/*.md` を原器化し、設計裁定
   (review_points)を maintainer が確定。← gate①裁定
3. 製品側 ECO 起票(`/eco-file`)→ 影響分析(61 相当を ECO 本文へ直書き)→
   BOM 改訂(**オラクル・ファースト**: 受入行を先に追加)。
4. 製造 → 機械受入(build 0 / Tests / Oracle / validate_bom 0-0)。
5. golden(maintainer 実機)← gate② → `/eco-accept` でクローズ+M4 同期。

### 3.2 既存機能拡張 — 新機能+read-across

経路は 3.1 と同じ。追加規律:
- **既存 golden の再検査範囲を影響分析で事前宣言**する(共有コンテナ/共有 VM を触る場合、
  非表示状態=条件付き IsVisible の裏面も対象 — ECO-037 教訓)。
- 未確定事項(FL-*/VE-*)の裁定を伴う場合、裁定はまず ViewPrismUI の review_points で確定
  してから製品 ECO に取り込む(裁定と実装の ECO を分けない場合も、CAD コミットが先)。

### 3.3 不具合修正 — 診断から入る(ECO-038 型)

1. `/eco-file <症状>`: 採番 → **症状記録**(再現手順・観測日・「事実」と「疑い(未検証)」の
   分離 — ECO-037 書式)→ **工程診断(R2)**: CAD に定義があるか/BOM は健全か/実装が逸脱か。
2. 診断分岐:
   - 実装逸脱 → 製品側の欠陥是正 ECO として続行。
   - CAD 未定義・曖昧 → **先に ViewPrismUI を是正・裁定**(gate①)してから BOM→実装。
   - 検査器/台帳の欠陥 → 該当上流成果物の是正 ECO(doc-only もあり得る)。
3. `/eco-fix <ECO>`: **プローブ先行(R5)** → 最小是正(真因構造を消す案を優先: ECO-038 では
   通知 2 行追加でなく全通知化を採択 — 手書きリスト構造自体が真因のため)→ 機械受入 →
   **UI サーフェスに触れた場合はセルフゴールデン(R7)= captures 並置で転写漏れ 0 を確認** →
   golden 合格基準を提示して停止(gate②)。
4. `/eco-accept <ECO>`: クローズ 3 点セット(CP 観点明記=再発防止・register applied・
   本文クローズ節+教訓)。

### 3.4 OSS セキュリティ — 台帳の逆引きから入る

1. `/sec-advisory <アドバイザリ>`: K-BOM/Service BOM を逆引きし、**パッケージグラフの実測**
   (アドバイザリ本文+scratch 交換検証)を一次資料に影響を判定(BOM トレースだけに頼らない —
   playbook §8/forward-03)。
2. 処置裁定(gate①): (a) **調達交換のみ**(コード製造なし)= fresh 工場を使わず
   **設計者適用+全再認証**で閉じる / (b) コード変更を伴う = 3.3 の経路へ合流。
3. いずれも ECO として register に記録(発生源=DEG イベントを明記)。

## 4. コミット規約(ECO ライフサイクル)

| 段階 | prefix | 内容 |
|---|---|---|
| 起票 | `起票(eco-NNN):` | ECO 本文+register エントリ(staged)。診断結果を要約 |
| 裁定 | `decide(eco-NNN):` | 選択肢の裁定記録(実装なし)。系列 ECO の中止裁定等も |
| 是正 | `fix(eco-NNN):` | コード+テスト+本文実施記録。機械受入結果を要約 |
| 受入 | `accept(eco-NNN):` | golden 合格・クローズ 3 点セット。系列は `accept(eco-NNN/K)` |

全コミットで pre-commit の validate_bom(0 error)を通過すること。

### 4.1 状態遷移とライフサイクル証拠(ECO-061・ECO-061 以降に適用)

**背景**: ECO-060 運用時、fix コミットなしで register が applied 化される違反が発生し、validator が
0-0 で素通しした(一次資料: `reports/incident-eco060-lifecycle-2026-07-11.md`)。入口スキルの散文
前提は防御にならない — 状態遷移は状態不変条件として機械検査する(BomDD playbook §13)。

**許可遷移(正本)**: 下表以外の遷移(飛び越し・逆行・superseded からの復帰)は禁止。
新規エントリは必ず `staged` で登場する。doc-only ECO も fix→accept の 2 段を踏む。

| 遷移 | 必須 trailer(遷移コミット自身に携行) | 対応 prefix |
|---|---|---|
| (新規)→ staged | なし | `起票(eco-NNN):` |
| staged → implemented | `BomDD-ECO-Fix: ECO-NNN` | `fix(eco-NNN):` |
| implemented → applied | `BomDD-ECO-Accept: ECO-NNN` | `accept(eco-NNN):` |
| staged/implemented/applied → superseded | なし(`superseded_by` 参照を E11 が検査) | `decide(eco-NNN):` 等 |

**機械検査(fail-closed)**:
- `validate_bom.py` [E14]〜[E19]: 証拠の実在(E15/E16)・祖先関係と順序(E17)・参照先実在(E18)・
  遷移エッジ(E19)・宣言と git 可用性(E14)。適用範囲は register の `lifecycle_evidence` ブロックが
  宣言(ECO-001..060 は遡及免除を**明示** — 黙って除外しない)。
- `hooks/commit-msg`: 遷移コミット自身への trailer を commit 時点で強制(pre-commit では遷移コミットの
  trailer が履歴未出現という自己参照制約があるため、メッセージ側で塞ぐ)。
- trailer はコミットメッセージ末尾の trailer ブロック(空行の後)に置く。例:
  `git commit -m "fix(eco-061): <要約>" -m "BomDD-ECO-Fix: ECO-061"`。
- `/eco-accept` は事前条件(fix 証拠の実在)に加え、accept コミット後に validator 0-0 を確認する
  **post-condition** を持つ(受入条件7)。

## 5. 導線(スキル)

作業者は自由文プロンプトではなく、以下のスキルを入口にする(`.claude/skills/`)。
各スキルは対応する手順を実行し、**human gate で「人間がやること」を明示して停止**する(R4)。

| スキル | 対応 | 停止点 |
|---|---|---|
| `/eco-file` | 起票+工程診断(§3 全シナリオの入口) | 診断分岐の提示(裁定要なら gate①) |
| `/eco-fix` | プローブ先行の是正+機械受入 | golden 合格基準の提示(gate②) |
| `/eco-accept` | golden 合格後のクローズ 3 点セット | 完了報告 |
| `/sec-advisory` | OSS アドバイザリの逆引き+処置選択肢 | 処置裁定の提示(gate①) |

## 6. ケーススタディ: ECO-038(2026-07-04・3 コミットで完結)

| 手順 | 実測 |
|---|---|
| 起票+工程診断 | 症状(切替不能)→ CAD 健全(work_tab.md L81)・BOM 健全(E-UI-WORKSPACE-043)→ **実装層に局在**と確定。FL-001(未確定事項)との無関係も判定。`70b5c84` |
| プローブ先行(R5) | 回帰テストを先に追加 → **是正前に 527 中 1 不合格** = 真因(NotifyLayout の派生プロパティ通知漏れ)を実測裏取り |
| 是正 | 最小 2 行案でなく全通知化(CR-6 同型)を採択 — 手書き通知リストという真因構造を消す。機械受入 4 点緑。`11b69a7` |
| golden(gate②) | 合格基準を明示提示 → maintainer 実機で即時切替往復 OK |
| クローズ | CP-UI-G1 観点明記+register applied+本文教訓(ECO-037 の VM 版 read-across)。`9778ace` |

想定問題との対応: 「いきなりコード修正」なし(R1/R2)・human の作業は golden のみ(R4)・
スコープ外所見(DisplayMode 永続の非対称)は**ついで修正せず**後続裁定(FL-002)へ送付(R3)。
