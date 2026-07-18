# ECO-111: 廃止予定 API TextBox.Watermark の使用(AVLN5001 ビルド警告)

- 起票日: 2026-07-18
- 報告者: maintainer(ビルド警告の指摘)
- 種別: 実装規約是正(廃止予定 API → 後継 API へのリネーム追随。視覚・動作不変)
- status: staged

## §1 症状

ビルドのたびに Avalonia コンパイラ警告が 1 件出る:

```
src\ViewPrism2.App\Views/LabeledChipStrip.axaml(67,16): Avalonia warning AVLN5001:
'TextBox.Watermark' is obsolete: Use PlaceholderText instead.
```

Avalonia 12 で `TextBox.Watermark` が `[Obsolete]` となり、後継は `PlaceholderText`
(機能等価のリネーム)。将来の Avalonia メジャーで削除されればビルド不能になる負債であり、
恒常警告は「警告 0 が正常」の基準線を汚して新規警告の検出をマスクする。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **無関係** | プレースホルダ文言・視覚は CAD 定義通りで不変。API 名の選択は実装層の関心。 |
| BOM(30-ebom/32-mbom/31-kbom) | **健全(K-AVALONIA の範囲内)** | K-AVALONIA は Avalonia 実装規約の置き場だが、廃止 API 追随は個別宣言不要の一般保守。SC-CHIPSTRIP-001(LabeledChipStrip)の宣言・受入観点に変更なし。 |
| 実装 | **是正対象(廃止 API 使用)** | [LabeledChipStrip.axaml:68](../src/ViewPrism2.App/Views/LabeledChipStrip.axaml) の `Watermark="{Binding ...Loc[chip.searchPlaceholder]}"` が唯一の使用箇所(起票時 grep 全数調査= src 内 1 件のみ)。 |

**診断確定: 実装層(廃止 API の後継リネーム追随)。gate①(裁定)不要。**

## §3 切り分け済みの事実

### 3.1 確定(起票時実測)

- 使用箇所は **src 全体で 1 箇所のみ**: LabeledChipStrip.axaml:68(チップ overflow
  ポップオーバーの検索欄プレースホルダ・Loc `chip.searchPlaceholder` バインド)。
- 混入経緯: `bf96bf0`(ECO-091・2026-07-15)で ImageTabView 側に導入 →
  `8e866a9`(ECO-094 共有部品化)で LabeledChipStrip へ移設。潜伏 3 日
  (警告としては毎ビルド可視だったが、機械受入基準が「0 error」で警告を数えないため滞留)。
- `PlaceherText` ではなく `PlaceholderText` が後継(警告文言・Avalonia 12 API)。機能等価の
  リネームであり、視覚(文言・薄色表示)・動作(入力で消える)は不変。
- テスト側: CpI18n010TabBindingTests の i18n 検査属性リストは `Watermark` と
  `PlaceholderText` を**両方とも既収載**(:27)→ テスト変更不要(旧名残置は防御的に無害)。
- スタイル側: `Watermark` プロパティを参照するスタイルセレクタは存在しない(grep 0 件)。

### 3.2 未検証

- なし(全数 1 件・機械的リネーム)。

## §4 是正方針

`Watermark=` → `PlaceholderText=` の属性リネーム 1 箇所。バインド式・Loc キーは不変。

- プローブ相当(R5 の適用形): 動作欠陥でなくビルド警告のため、実測裏取りは
  **是正前ビルドログの AVLN5001 1 件 → 是正後 0 件**の機械証拠で行う(ECO-105 前例=
  機械証拠検収)。プレースホルダ文言の i18n 配線は既存 lint(CpI18n010TabBindingTests が
  PlaceholderText を検査対象に既収載)が継続監視する。
- golden: **n/a 見込み**(視覚・動作不変の API リネーム。ECO-090/105 前例)。

## §5 影響 BOM

- src: LabeledChipStrip.axaml 1 行(SC-CHIPSTRIP-001 の surface・宣言変更なし)。
- tests/BOM/CP: 変更なし見込み(既存 lint が後継 API を既にカバー)。
- 既存固定 Oracle 行は変更しない(R6)。

## §6 残ゲート

1. gate①: 不要(実装層確定)。
2. ~~/eco-fix eco-111~~ **実施済み(2026-07-18・§7)**。
3. gate②: n/a(機械証拠検収=§7。maintainer の accept 指示でクローズ)。

## §7 実施記録(2026-07-18・/eco-fix)

- **是正前実測(プローブ相当・R5 の機械証拠形)**: `dotnet build --no-incremental` で
  AVLN5001 を検出(LabeledChipStrip.axaml(67,16))。**追加確定事実=インクリメンタルビルドでは
  XAML 再コンパイルが走らず警告が非表示** — 「出たり出なかったりする」ことが滞留の追加要因
  (§3.1 の「0 error 基準が警告を数えない」に加える)。
- **是正**: `Watermark=` → `PlaceholderText=` の属性リネーム 1 箇所(バインド式・Loc キー不変)。
- **是正後実測**: `--no-incremental` フルビルドで **AVLN5001 = 0 件・警告 0 個**。
- **機械受入(全緑)**: build 0 error・0 警告 / Tests 806/806 / Oracle 109+2skip / validate 0/0。
- R7 セルフゴールデン=対象外(機能等価リネーム・視覚不変。プレースホルダの i18n 配線は
  既存 lint CpI18n010TabBindingTests が PlaceholderText を検査対象に既収載で継続監視)。
