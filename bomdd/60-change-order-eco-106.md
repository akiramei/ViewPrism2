# Change Order — ECO-106(staged): タグパレットの常駐メッセージが言語切替へ追随しない(ECO-104 golden 中の実機所見)

- 起票: 2026-07-17(maintainer 実機所見・ECO-104 golden 実施中に発見)
- 種別: 不具合是正(ECO-046 混入・実装層)
- baseline: main `9d0299e`

## 1. 症状(maintainer 実機・2026-07-17・スクリーンショットあり)

タグパレットで削除拒否メッセージ「編集中のビュー階層に配置されているタグは削除できません。…」
(`error.tagInUnsavedEdit`・ECO-046 U-a ガード)を表示中に設定で言語を en へ切り替えても、
**周辺 UI(Tag Palette/Search tags/Add 等)は英語化されるのにメッセージだけ日本語のまま残る**。

発見経緯: ECO-104 golden 基準③(保存失敗の作為)を実施しようとして「パレットで配置済みタグを削除」
した際、ECO-046 ガードの拒否メッセージが表示され、それが言語に追随しないことを発見。
(基準③自体の到達不能問題は ECO-104 §6 改訂で処置 — 本 ECO の対象外)

## 2. 工程診断 — 実装層(ECO-046 混入・潜伏 12 日)。gate① 不要

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 対象外 | i18n 追随は横断規約の領分(K-AVALONIA/REQ-051=言語切替の即時反映)。 |
| BOM | 健全 | 規約は宣言済み(REQ-050/051)。検査の谷間は fix 時プローブ+lint 候補(§4-3)で埋める。 |
| 実装 | **欠陥(変更対象)** | §3。混入= `9a0f3c6`(ECO-046・2026-07-05)= 潜伏 12 日。ECO-104 §1.2 と同一アンチパターン(解決済み文字列の保持)の別サーフェス。 |

- マスキング要因: ECO-079/080 の i18n lint は**静的なキー消費**を検査する — 「行動時に解決して
  VM 状態へ保持し、次の行動まで表示し続ける」常駐メッセージの**解決タイミング**は静的検査に映らない。
  golden も単一ロケールで実施される限り顕在化しない(今回は ECO-104 の言語切替 golden が偶然照らした)。

## 3. 切り分け済みの事実(2026-07-17 コード読解+全数調査)

- `TagPaletteViewModel:288-291`= `IsTagInUnsavedEdit` ガード成立時に
  `StatusMessage = _localization.T("error.tagInUnsavedEdit")`(**解決済み文字列の保持**)。
  `StatusMessage` は次の削除操作まで残る**常駐メッセージ**。CultureChanged ハンドラ(:206-)は
  Loc 差し替え(バインド再評価)のみで StatusMessage を再解決しない。
- 同 VM `:304`= 削除失敗時の `StatusMessage = ErrorMessages.Resolve(...)` も同型(TagInUse 等)。
- **全数調査(`= _localization.T(` / `= ErrorMessages.Resolve` の代入)**: 同イディオムは約 15 VM・
  50 箇所超に存在。ただし多くは (a) モーダル内の一時表示(言語切替と共存しない/閉じて再開で再解決)
  (b) 状態更新のたび再計算される算出的ラベル(次の再計算で追随)。**脆弱クラスは「メインタブ面に
  常駐し、次のユーザー操作まで再解決機会がない」メッセージ**で、実機観測済みは本件パレット面。
  他候補(ImageTab の catalogError/contentError 等)の悉皆判定は本 ECO では**やらない**(R3)—
  §4-3 の lint 候補へ送る。

## 4. 是正方針(案・着手時確定)

1. **案A(推奨・ECO-104 1.2 と同型)**: `StatusMessage`(解決済み文字列)を
   `StatusMessageKey`(i18n キー=権威値。ErrorCode 由来は `ErrorMessages.KeyOf` でキー化)へ置換し、
   表示文言は**表示時に現在ロケールで解決する算出プロパティ**にする。CultureChanged で再通知。
   公開名 `StatusMessage` は算出プロパティとして維持(バインド・テスト不変)。
2. 案B: CultureChanged 時に StatusMessage を null 化(消す)— 追随でなく消去。情報が失われるため
   A に劣るが diff 最小。不採用方向。
3. **プローブ(R5・是正前赤)**: パレットで dirty 配置タグの削除拒否 → StatusMessage= ja 文言
   → SetLocale("en") → en 文言へ追随(ja↔en 往復)。
4. **再発防止(lint 候補・本 ECO では記録のみ)**: 「`_localization.T`/`ErrorMessages.Resolve` の
   結果を ObservableProperty へ代入する」箇所を検出する静的 lint — i18n lint 拡充候補
   (重複キー N=1・未使用キー N=2)と合流。**解決タイミング次元は既存 lint の死角**(§2)。

## 5. 影響 BOM(fix 時 M4 で同期)

- **src**: `TagPaletteViewModel` のみ(StatusMessage のキー保持化・:290/:304 の 2 代入)。XAML 不変。
- **tests**: プローブ 1 本(§4-3)。既存固定 Oracle 不変(R6)。
- **CP**: CP-UI-G6(タグパレット面)へ検査ケース刻印は accept 時。

## 6. 残ゲート

- gate①: **不要**(実装層確定)。
- gate②(golden): **必要(軽量・実機到達可能)**: タグをビュー階層へ配置(未保存のまま)→
  パレットで当該タグの削除を試行 → 赤字の拒否メッセージ表示 → 設定で言語 ja↔en 切替 →
  **メッセージが切替先言語へ即追随**する。

## 7. `/eco-fix` 実施記録(2026-07-17)

### 7.1 プローブ先行(R5)

CpUiG6HierarchyEditorTests へ「削除拒否メッセージはロケール切替へ追随する」を追加
(実アセット ja/en・dirty 配置タグの削除拒否 → SetLocale("en") → ja↔en 往復)。
是正前= **赤**(en 切替後も ja 文言のまま=症状の実測固定)。既存 792 本は緑を起点固定。

### 7.2 是正内容(§4 案A を採択 — ECO-104 1.2 と同型・真因構造の除去)

- `StatusMessage`(Resolve 済み文字列の ObservableProperty)→ `StatusMessageKey`(i18n キー=権威値)へ
  置換。ErrorCode 由来は `ErrorMessages.KeyOf` でキー化(:304)、直接キーはそのまま(:290)。
- 公開名 `StatusMessage` は**表示時に現在ロケールで解決する算出プロパティ**として維持
  (XAML バインド・既存テストとも不変)。OnStatusMessageKeyChanged+CultureChanged で再通知。
- 横断規約適合(ECO-080): 文言は Loc 経由の表示時解決に一本化。

### 7.3 機械受入

build 0 error / **Tests 793/793**(プローブ緑転・既存 792 不変)/ Oracle 109+2skip(R6 不変)/
validate_bom 0/0。

### 7.4 セルフゴールデン(R7)= 対象外

VM のみの是正(XAML 不変・メッセージの視覚様式不変=解決タイミングのみ変更)。ECO-038/104 前例。
検証はプローブ(7.1)が担う。

### 7.5 M4 要否

E-BOM/M-BOM 宣言変更なし。CP-UI-G6 への検査ケース刻印は accept 時。
