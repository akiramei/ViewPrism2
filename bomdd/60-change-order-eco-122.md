# ECO-122 — UI 部品表(04_component_registry)適合性検査の配線 — lint 新設+視覚 probe の照合先移行

- 起票日: 2026-07-20
- 報告者: AI(maintainer 指示 2026-07-20「UI 部品表の新設と恒久運用」の依頼項)
- 種別: 検査器新設(部品適合 lint)+既存視覚 probe の照合先移行(src 挙動/視覚は不変)
- baseline: ViewPrism2 main `8e42f14` / ViewPrismUI main `78c95a7`

---

## 1. 要求

maintainer 裁定済み方針(2026-07-20・ViewPrismUI `78c95a7` で恒久運用規則を刻印)の柱5
「**lint・視覚 probe の照合先は今後この部品表(`../ViewPrismUI/docs/04_component_registry.md`)の契約とする**」
を VP2 側の検査基盤に配線する。依頼内訳は 2 点:

1. **lint**: 部品該当箇所での生コントロール+ローカル外観値の直指定検出
   (前例= CpI18n010XamlLintTests の直書き JP=0 と同型の走査)。
2. **視覚 probe**: Gf*VisualParityTests 系の期待値を面ごとのハードコードから部品表の契約値参照へ
   (候補= GfFileOpsVisualParityTests・GfSortMenuVisualParityTests)。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(部品表) | 健全(前提のみ) | CMP-001〜009 は既存 CAD/mock/golden 裁定からの転写で正典化済み。CMP-008 の契約欠落は **ECO-121 で別途起票済み**(本 ECO の照合先昇格の前提整備=先行) |
| BOM/CP | 拡張対象 | 現行 CP に「部品適合」の automated 観点なし。CP-AXAML-LINT-117(ScrollViewer.Padding)・CpI18n010 系(i18n)が同型 lint の前例 |
| tests | **配線欠落(本 ECO の対象)** | GfFileOpsVisualParityTests は `MockBlue #2F6BED`・幅208・行高42 等をテスト内定数で保持(:44,:122,:142)。GfSortMenuVisualParityTests は色8定数+寸法11箇所をテスト内保持(:50-57)。いずれも部品表と独立に二重定義=部品契約の改版が probe に自動波及しない |
| src | 健全(無変更) | 本 ECO は検査の配線のみ。実装の視覚・挙動は不変 |

**結論: 検査器/tests 層の配線 ECO。src 無変更・golden n/a 見込み(機械証拠検収= ECO-105/107/111 前例)。**

## 3. 切り分け済みの事実

### 確定

1. **二重定義の現状**: 部品表の契約値(CMP-003 実行系バッジ・CMP-006 メニュー寸法・CMP-007 選択視覚の
   色/寸法等)と、Gf*VisualParityTests のテスト内定数は**独立に二重管理**されている。
   部品表を改版してもテストは旧値のまま緑=改版が実装へ波及しない(逆に、テストだけ直せば
   部品表と乖離したまま緑)。照合先を一元化しない限り「部品表が正典」は検査上の実体を持たない。
2. **lint の前例様式**: CpI18n010XamlLintTests(直書き JP=0・全 .axaml 走査・層別 allowlist
   全エントリ根拠つき= ECO-107/109)・CpAxamlLintScrollViewerPaddingTests(禁止則ゼロ基線= ECO-117)。
   本 lint はこの走査系に「部品該当箇所の直指定検出」を加える拡張。
3. **陽性対照の要**: 「検査を追加する変更は陽性対照を同梱」(N=4: ECO-053/078/107/120)。
   lint・photo 双方に是正前赤(または注入赤)の実測が必要。
4. **部品表は VP2 側で編集しない**(恒久運用の柱4)。照合先の機械可読化が必要になっても、
   部品表本体の改稿は ViewPrismUI 側の作業として分離する。

### 未検証(着手時に確定)

- **A: 照合先の物理形**。候補:
  - **a. 越境読取**: テストが `../ViewPrismUI/docs/04_component_registry.md` を直接パース。
    正本一元だが、①Markdown 散文からの値抽出が脆い ②VP2 単独チェックアウトで
    テスト不能(CI 可搬性)。
  - **b. VP2 側に契約値写像(単一ファイル)**: `RegistryContract`(定数クラス)を 1 ファイル新設し、
    各定数に部品 ID+部品表の行への出典コメントを付す。lint と Gf* probe は全てここを参照。
    二重定義は残るが**1 箇所に集約**され、乖離検査(下記 B)で同期を機械化できる。
    前例= OrganizeCriteria 共有写像(ECO-055・両タブ pin の同型)。
  - **c. 部品表側に機械可読ブロック**: ViewPrismUI に YAML/JSON を添える。部品表改稿を要する
    ため ViewPrismUI 側 ECO の分離が必要=スコープ拡大。
  - **推奨= b**(着手時確定)。a の脆さと c のスコープ拡大を避け、写像ファイルの出典コメント+
    「部品表改版時は本ファイルへ転写」の運用で一元化する。将来 c へ移行する場合も b の写像が
    そのまま参照層として機能する。
- **B: lint の検出対象の第1トランシェ**。全 CMP の全契約を一度に lint 化するのは非現実的
  (色・寸法・構造の検出可能性が部品ごとに異なる)。機械検出可能なものから層別に:
  - 生コントロール検出: 部品該当のポップオーバー面で `Classes="popupMenu"` 系を使わない
    Border/Button の検出(構造走査)。
  - 直指定検出: 部品該当箇所での local な色/寸法リテラル(Standard 部品の契約値の重複直書き=
    ECO-119 型「画面ごと中央揃え」の検出)。allowlist は ECO-107 様式(全エントリ根拠つき)。
  - 検出限界は lint 側に明示宣言(ECO-107 教訓1=限界宣言には掃射手段紐づけ)。
- **C: REG-C1/C2/C3 の裁定影響** → **解消(2026-07-20・CAD 側で REG-C1〜C4 全裁定)**。
  C1= 部品表側の転写誤りを訂正(CMP-007: ordered が選択系既定・check はファイル操作限定=
  実装と一致・非対称不存在)。C2= CMP-010 表示系件数バッジ新設(現行実装 trashCountBadge を追認・
  実装が原器)。C3= CMP-006 Standard 昇格(幅=インスタンス契約値として固定・同型複製は同値必須)。
  C4= CMP-006 へ末尾到達性条項追加。**→ 写像は CMP-006/007/010 を含む全 Standard 部品で構成できる
  (Provisional 除外は不要になった)**。残る As-Built 乖離(作業タブ ⋯ 200→208 等)は **ECO-123(分離起票)**
  の是正対象= 本 ECO の写像は**契約値**を持ち、ECO-123 完了までは当該 probe の期待値だけが先行して
  契約値を pin する関係(順序= 123 → 122 なら差異は生じない)。

## 4. 是正方針(案・着手時確定)

1. **契約値写像の新設**(未検証 A 案b): `tests/ViewPrism2.Tests/RegistryContract.cs`(または
   src 側 Styles 参照が要るなら配置再考)。CMP-ID・部品表の節への出典・値。
2. **視覚 probe の移行**: GfFileOpsVisualParityTests・GfSortMenuVisualParityTests のテスト内定数を
   写像参照へ置換。**期待値の意味は不変**(値の変更ゼロ=probe は移行前後で同一判定)。
   移行の陽性対照=写像を一時的に壊して赤転を実測。
3. **lint 新設**(未検証 B): 第1トランシェを層別 allowlist つきで実装+陽性対照同梱。
   ゼロ基線が現状で成立しない場合、違反は是正せず**実態報告→裁定材料**へ回す
   (lint の first-run 違反は REG-C 系の面間差そのものの可能性が高い=勝手に統合しない・柱4)。
4. **CP 刻印**: CP-REGISTRY-LINT-122(仮称・automated)を 33-control-plan へ新設(accept 時)。

diff 規模: tests のみ(写像 1 ファイル+lint 1 クラス+既存 Gf* 2 ファイルの定数置換)。src 無変更。

## 5. 影響 BOM

- **tests**: RegistryContract 写像新設・Gf*VisualParityTests 2 本の参照移行・部品適合 lint 新設
  (いずれも既存固定 Oracle 行は変更しない= R6)
- **CP**: CP-REGISTRY-LINT-122 新設(automated・accept 時)
- **src**: 無変更
- **CAD**: 無変更(部品表の照合は読み取りのみ。改稿が必要になったら ViewPrismUI 側へ分離= 柱4)

## 6. 残ゲート

- **gate①(裁定)**: 不要(方針は maintainer 裁定済み)。
- **gate②(golden)**: n/a(視覚・挙動不変。機械証拠で検収= §7 実施記録)→ **残= accept 指示のみ**。

## 7. 実施記録(2026-07-20 fix)

- **着手時確定**: 物理形 A= **案b 採用**(RegistryContract.cs 単一ファイル・CMP-ID+部品表出典コメント)。
  トランシェ B= **CMP-006 限定**(検査A= Popup 面 Border の popupMenu クローム委譲/検査B= クローム
  属性の局所上書き禁止= ECO-119 型の機械化)。検出限界(Style 層・Flyout・自己閉じ Popup・色/字形)は
  lint docstring に掃射手段つきで宣言。
- **登載基準の明文化**(写像冒頭): ①値の一致でなくトークン所掌の一致(同値でも画面 CAD 所掌は
  登載しない= トークン誤写像の防止) ②登載範囲= Standard 部品(CMP-006/007/010)の機械検査可能な
  基幹値(参照者未着でも部品単位で登載= 次の probe が生値を再発明しない)。
- **R5(赤採取)**: lint を allowlist 空で実行 → **赤 3 件= first-run 悉皆が予測と完全一致・新規違反ゼロ**
  (A: LabeledChipStrip:chipPopCard / B: 両タブ並び替え Width=252:CornerRadius|Padding|BoxShadow)。
  全 3 件が裁定・記帳済み → 根拠つき allowlist 化(chipPopCard= 判定待ち・radius13= REG-C3 裁定)+
  死亡エントリ検出で緑転。
- **probe 移行(値不変= 移行前後で同一判定)**: 11 箇所(GfFileOps= accent/text.secondary/幅208/行高42・
  GfSortMenu= 幅252×2/行38/text-strong・GfPopoverMenuInstanceWidth= 240/208/240)。
  **移行の陽性対照= 実測済み**(MenuWidthSort を 999 へ一時破壊 → GfSortMenu 2 本赤転 → 復旧緑=
  写像→probe の配線が生きている証拠)。lint 自体の陽性対照 2 本(+R8 で NONE 分岐 2 種追加)は常設。
- **機械受入(4 点)**: フルビルド 0 error・0 警告 / Tests **851/851**(lint 4+移行 probe 同一判定維持)/
  Oracle 109+2skip / validate_bom 0/0。
- **R7**: 対象外宣言(src/XAML 無変更・視覚不変)。
- **R8(独立レビュー・fresh context)**: 所見 10 件全処置。
  - **スコープ内 3= 全て処置済み**: 所見1(decide 記録「CMP-006/007/010 全含め」と写像の不整合 →
    CMP-007 節追加+登載範囲の明文化)・所見6(陽性対照の NONE 分岐 2 種追加+タプル名 FileName へ
    訂正+自己閉じ Popup の限界宣言)・所見9(移行済みアサーションのメッセージ内 生ヘックス残存 →
    契約参照/中立表現へ)。処置後 再受入= Tests 851/851 全緑。
  - **スコープ外 3= 51-cheat-log 記帳(2026-07-20)**: 所見2(CMP-004 セグメント契約と並び替え実装の
    抵触= 三択報告対象・CAD 申し送り)・所見8(並び替え Padding/BoxShadow 実値乖離が As-Built
    乖離リスト不在・CAD 申し送り)・所見5(allowlist 件数非考慮= 改良候補)。
  - 問題なし 4(機械裏取り済み): 写像 13 定数の部品表突合 全一致・移行 11 箇所の値同一・lint の
    実サイト全数(Popup 9)検証・allowlist 根拠の実在確認。
- **diff 規模**: tests のみ(新設 2 ファイル+既存 3 ファイルの定数移行)。src/CAD 無変更。

## 8. 停止点

golden n/a(機械証拠検収= ①lint ゼロ基線+陽性対照 ②写像破壊→赤転の実測 ③移行前後の同一判定
= Tests 全緑維持 ④受入 4 点)。内容に問題なければ **accept 指示**(`/eco-accept eco-122`)をお願いします。
