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

- **gate①(裁定)**: 不要(CMP-011 Standard 登録+REG-C5 裁定済み)
- **gate②(golden)**: **必要 — §8 の基準で維持者確認待ち。**

## 7. 実施記録(2026-07-21 fix)

- **R5(赤採取)**: 視覚 probe 3 本(旧 ctor+同一アサーション)= **全赤**(中央揃え Left/テーマ既定
  グレー/はい・いいえ)+lint 検査C= **赤**(`ConfirmDialog.axaml=2(台帳 0)`)。是正で ctor が
  動詞ラベル必須へ変わったため(REG-C5 の型強制)、probe は新署名へ更新(アサーション本体不変)。
- **是正= 案A 全 4 部**:
  1. Components.axaml へ CMP-011 共有 Style(`dlgBtn`+secondary/primary/destructive・中央揃え内蔵・
     値は golden 承認済み outlineButton+registry 実測 #D6E0EE/#D83A3F/#C4282D の転写)。
     R8 所見1 で全 variant に `:pointerover`/`:pressed` の template 上書きを追加(Button.cta 様式=
     擬似クラス無しの setter は Fluent 既定に負ける)。
  2. ConfirmDialog: dlgBtn 委譲+ctor/ConfirmAsync 署名拡張(**confirmLabel 必須= REG-C5 違反を
     型で再発不能に**・destructive・cancelLabel 任意)。Enter/Escape 既定・初期フォーカス
     (キャンセル側)は旧実装から不変(R8 所見7 で git 遡り確認)。
  3. 10 サイト動詞化台帳: destructive 7(ビュー削除/タグ削除/コレクション削除=「削除する」・
     完全削除×2=「完全削除」・空にする×2=「ゴミ箱を空にする」= REG-C5 正典ラベル)+
     primary 2(再リンク×2=「再リンクする」)+終了確認= destructive「破棄して終了」+cancel
     「戻る」(**ECO-103 裁定文言の実現**= 従来ははい/いいえに退化していた)。
     i18n ja/en 4 キー追加+`common.yes/no` 死キー削除(全消費ゼロを grep 悉皆)。
  4. lint 検査C 新設(クラス無し Button のファイル別件数台帳= 13 ファイル・全根拠つき・
     陽性/陰性対照)。R8 所見2 で `<Button.Flyout>` 誤計数を是正(`(?![\w.])`)+幻 2 エントリ除去。
- **機械受入(4 点)**: フルビルド 0 error・0 警告 / Tests **865/865**(probe 5+lint 検査C 緑転・
  Stub 42 箇所署名追随)/ Oracle 109+2skip / validate 0/0。
- **R7**: ConfirmDialog は mock なし面(03 検査行に明記)= captures 並置の原器が無く、契約 probe
  (GfConfirmDialogVisualParityTests 5 本= 中央揃え/secondary/destructive/primary/動詞・戻る)が
  並置代替(ECO-116 実測 pin 型)。**L2 列 ○ 既存面(B-1/B-2/B-3/E-1)= 既存 Gf 視覚 parity
  3 クラス(Snapshot/Package/EntryE1)が全緑維持= 無変更の機械証明**(dlgBtn は新設クラス=
  既存面は未参照・base Button スタイルは CornerRadius のみで不変)。
- **R8(独立レビュー・fresh context)**: 所見 11 件全処置。
  - **スコープ内 3= 全て処置済み**: 所見1(primary の hover 退色= cta 様式の template 上書きを
    全 variant へ)・所見2(lint の `<Button.Flyout>` 誤計数= 正規表現是正+幻 2 エントリ除去+
    陰性対照)・所見3(primary variant+cancelLabel 経路の probe 追加)。処置後 再受入= 865/865。
  - **スコープ外 3= cheat-log 記帳(2026-07-21)**: Escape/Enter キーボード契約の不在(既存・
    CMP-011 改訂と対)・ECO-073 面の hover 同欠陥(golden 非実測次元= lazy 遡及時に解消)・
    hover/pressed 値の CAD 未規定(暫定選択の申し送り)。
  - 問題なし 5(機械裏取り済み): 署名追随の全数(必須引数= 型で閉包)・variant 選定の妥当性
    (REG-C5 正典ラベルとの一致)・i18n 整合(601 キー対称・yes/no 消費ゼロ・BOM/改行健全)・
    静的スタイル値の契約一致・ConfirmDialog 挙動不変(フォーカスはキャンセル側= 誤操作安全)。
- **diff 規模**: src 10 ファイル+styles+i18n 2 ファイル・tests probe 1 クラス+lint 拡張+Stub 42 箇所。

## 8. 停止点= golden 合格基準(gate②・実機。ja/en 両言語= ECO-079 規約)

1. **ビュー削除確認(症状面)**: タグタブ→ビュー行「削除」→ ダイアログが「キャンセル」(白 outline)+
   「削除する」(赤塗り・白文字)・**両ボタンともテキスト中央揃え**・キャンセル左/削除する右。
2. **他の destructive 面**: タグ削除・コレクション削除=「削除する」/ゴミ箱の完全削除=「完全削除」/
   ゴミ箱を空にする=「ゴミ箱を空にする」— いずれも赤 CTA+キャンセル。
3. **primary 面**: 修復の再リンク確定=「再リンクする」(青塗り)+キャンセル。
4. **終了確認(ECO-103 文言)**: 階層編集を未保存のままウィンドウを閉じる→「戻る」+「破棄して終了」(赤)。
5. **言語切替**: en で Delete/Cancel/Delete permanently/Empty Trash/Relink/Discard and exit/Go back が
   表示され、はみ出し・切れがない。ja 復帰で従来どおり。
6. **回帰(L2 列既存面)**: 書き出し(B-1)・取り込み(B-2/B-3)・設定 データとバックアップ(E-1)の
   ダイアログの見た目が従来どおり(共有 Style 新設の影響なし)。

合格なら `/eco-accept eco-126` を指示してください。
