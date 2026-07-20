# ECO-126 — 確認ダイアログ(ConfirmDialog)の CMP-011/L2 準拠是正 — 中央揃え漏れ+テーマ既定グレー+「はい/いいえ」の動詞化(REG-C5)

- 起票日: 2026-07-21
- 報告者: maintainer 指示(ビュー削除確認の L2 逸脱報告→ CAD 側正典化完了を受けた実装追随)
- 種別: 実装是正候補(共有ダイアログ部品の視覚+ラベル語彙。CAD 裁定済み)
- baseline: ViewPrism2 main `7501d61` / ViewPrismUI `c84e435`(REG-C5 裁定済み)

---

## 1. 症状/要求

ビュー削除の確認ダイアログ(タグタブ・ビュー行「削除」)が共通言語 L2/部品 CMP-011 に不適合:
①ボタンテキストが左寄り(Avalonia 既定= L2 が「取り漏れ禁止」と明記する既知の罠)②ボタンが
テーマ既定グレー(secondary= 白 outline `#D6E0EE`・destructive= 赤塗りの契約に不適合)
③ラベルが「はい/いいえ」(REG-C5 裁定 2026-07-21=「応答が行為を名指す動詞ラベル」標準・
「はい/いいえ」禁止)。

CAD 側は正典化済み(gate①相当は消化済み): CMP-011 DialogActionButton 登録(VPUI `8eb29ea`)+
REG-C5 裁定(`c84e435`)+03_dialog_language へ「ビュー削除確認(legacy)」検査行追加(L1/L2=○)。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD | 健全(正典化済み・2026-07-21) | CMP-011(バリアント 3 種+契約+制約「生 Button 直置き禁止・はい/いいえ禁止」)・REG-C5・03 マトリクス行 |
| BOM | 健全 | 挙動仕様不変(確認して bool を返す)。CP は accept 時刻印 |
| 実装 | **逸脱確定** | `ConfirmDialog.axaml`: 生 `Button` 2 個(クラス無し=テーマ既定グレー+左寄せ)+`common.yes/no`(axaml.cs:21-22)。混入= `1446321`(Phase 4 初期製造)= **最古参の legacy・全確認ダイアログの共有部品** |

## 3. 切り分け済みの事実(起票時掃射)

### 確定

1. **「はい/いいえ」の消費は ConfirmDialog 1 箇所に集約**(`common.yes/no` の grep 悉皆= 全 src で
   ConfirmDialog.axaml.cs のみ)。他の legacy ダイアログ(設定・タグ編集・修復ほか)に
   はい/いいえの独自実装は**存在しない**= 掃射クリーン。ダイアログ 1 箇所の是正で語彙違反は全数解消。
2. **呼び出し 10 サイトが共有**(ConfirmAsync grep 悉皆): ビュー削除(TagsTabViewModel:239)/
   タグ削除(TagPaletteViewModel:309)/コレクション削除(FolderManagementViewModel:242)/
   完全削除・ゴミ箱を空にする(ImageTabTrashViewModel:161,177+WorkTabViewModel:1421,1437)/
   再リンク確認(RelinkViewModel:125)/修復実行(RepairViewModel:192)/アプリ終了系(App.axaml.cs:90)。
   → ラベル動詞化は**サイトごとの CTA 指定**が必要(現署名 `ConfirmAsync(title, message)` には
   ラベル引数が無い= 構造的に「はい/いいえ」しか出せない)。
3. **L2 準拠様式は 3 面にローカル複製**: `footerBtn`/`outlineButton` スタイルが
   CollectionExportWindow/CollectionImportWindow/SnapshotWindow の各 axaml に**個別定義**
   (ECO-073 是正面・golden 承認済み)。共有 Style は未抽出= CMP-011 の実装写像が無い。
   ConfirmDialog に 4 つ目のローカル複製を作るのは権威規則 3(黙って面ごとの転写をしない)違反。
4. **中央揃えの基盤事実**: グローバル `Button` スタイル(Components.axaml:11)は CornerRadius のみ=
   HorizontalContentAlignment 未設定→ Avalonia 既定 Left。L2 の「取り漏れ禁止」明記事項どおりの逸脱。
5. **フッター構成は契約適合が既在**: 右寄せ・キャンセル左/CTA 右の順は現実装どおり。docked は
   SizeToContent 窓につき自明成立(可変兄弟なし)。

### 未検証(fix で確定)

- 10 サイトの CTA ラベル(動詞)+バリアント(destructive/primary)の台帳: 削除系 7 サイト=
  destructive 見込み・再リンク/修復/終了系= primary か destructive かをサイトごとに判定。
- `common.yes/no` キーの死キー化(全消費が消えたら削除= ECO-107 流儀)と ja/en 追加キーの設計。

## 4. 是正方針(案・着手時確定)

**案A(推奨)**: 4 部構成。

1. **CMP-011 共有 Style の新設**(Components.axaml): `dlgBtn`(中央揃え内蔵+共通寸法)+
   `secondary`(白 outline `#D6E0EE`)+`destructive`(塗り `#d83a3f`/枠 `#c4282d`/白文字)+
   `primary`(Accent 塗り+白文字)。既存 3 面のローカル style は**触らない**(視覚不変・
   共有クラスへの置換は lazy 遡及= cheat-log 記帳)。
2. **ConfirmDialog の是正**: ボタンへ `dlgBtn secondary`/`dlgBtn destructive|primary` 適用+
   ラベル注入化= `ConfirmAsync(title, message, confirmLabelKey, destructive)` へ署名拡張
   (**CTA ラベルを必須引数化= REG-C5 違反を型で再発不能に**。キャンセル側は共通
   `common.cancel` 固定)。
3. **10 サイトの動詞ラベル台帳**+i18n(ja/en)キー追加+`common.yes/no` 死キー削除。
   ビュー削除= 「キャンセル」+「削除する」(destructive)= 指示の②③どおり。
4. **lint(ECO-122 トランシェ2)**: CpRegistryLintTests へ検査C「ダイアログ Window の
   フッター生 Button 検出」(クラス無し `<Button` = CMP-011 制約の機械化・照合先= CMP-011)+
   RegistryContract へ CMP-011 色 3 値の写像追加+陽性対照。是正前赤= ConfirmDialog の 2 個。

R7(掃射義務): 共通言語 L2 に触れるため **03 マトリクス L2 列= ○ の全面**を並置検査
(ビュー削除確認+B-1/B-2/B-3/E-1 系。既存面は共有 Style 新設の影響を受けない設計だが
無変更確認まで並置で証明= GF-073 の 4 連鎖再発防止)。golden は ja/en 両言語(ECO-079 規約)。

## 5. 影響 BOM

- **src**: ConfirmDialog.axaml/.axaml.cs+IWindowService/WindowService(署名拡張)+呼び出し 10 サイト+
  Components.axaml(共有 Style)+i18n ja/en(キー追加・yes/no 削除)
- **tests**: lint 検査C+陽性対照・ConfirmDialog の視覚 probe(中央揃え= GF-109-01 型の実測・
  バリアント色)・既存 StubWindowService 群の署名追随
- **CAD**: 無変更(正典化済み)。既存 3 面のローカル style 置換予告= cheat-log
- **CP**: CP-UI-G1 系刻印+lint は CP-REGISTRY-LINT-122 の拡張(accept 時)

## 6. 残ゲート

- **gate①(裁定)**: 不要(CMP-011 Standard 登録+REG-C5 裁定済み。サイト別ラベルの文言は
  実装裁量+golden で確認)
- **gate②(golden)**: 必要(視覚変更。ビュー削除確認の CMP-011 準拠+L2 列全面の無変更+
  ja/en 両言語+全 10 サイトの確認動作)

## 7. 停止点

裁定は不要です。`/eco-fix eco-126` で是正に着手できます。
