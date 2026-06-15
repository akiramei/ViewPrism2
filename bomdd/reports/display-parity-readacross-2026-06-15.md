# 表示パリティ Read-Across(CAPA・ECO-003 横展開) 2026-06-15

## 目的
ECO-003 で BomDD 方法論に追加した**表示パリティ・ゲート**(原典あり UI surface は表示要素を全数トレース)を、既に出荷済みの全移植画面へ**遡及適用**(CAPA の read-across / horizontal deployment)。「ゲートを足したら、そのゲートで過去の出荷物も一度測る」。同時に**新ゲートが過去の同型 omission を実際に炙り出すか**の検証を兼ねる。

## 方法
ViewPrism2 の移植画面を原典 view-prism(NewUI)へ対応付け、9 画面群を並列で**設計者側リバース監査**(原典の提示要素を全列挙→移植実装で present / missing / out-of-scope を判定、file:line 証拠付き)。工場隔離の対象外(リバース入力)。

## 検証結果(最重要)
**新ゲートは有効。** read-across は **GF-V4-04 と同一クラスの表示 omission を兄弟画面 `RelinkWindow`(フォルダ管理→再リンク経路)に発見**した。これは ECO-003 是正時に `RelinkViewModel` へ `AbsolutePath` を供給せず(既定 null)、`RelinkWindow.axaml` を据え置いた箇所。**個体修正(RepairWindow)は1経路に閉じ、同じ `RelinkCandidateViewModel` を使う兄弟経路に波及していなかった**。read-across が無ければこの omission は次に誰かが RelinkWindow を開くまで(=最終工程)流出し続けた。CAPA read-across の価値が実証された。

---

## A. 確定: 表示 omission(GF-V4-04 同型・データは存在し表示のみ欠落)→ ECO 候補
| # | 画面 | 欠落要素 | 重大度 | 証拠 | 修正規模 |
|---|---|---|---|---|---|
| A-1 | **RelinkWindow** 候補カード | **サムネイル + ファイル名** | **高** | RelinkWindow.axaml:44-51 / RelinkViewModel.cs:146-149(AbsolutePath 未供給・ISyncFolderRepository 未注入) | RepairWindow と同パターン(VM に folder 注入+AXAML 5要素化) |
| A-2 | 画像グリッド セル | ファイルサイズ | 中 | MainWindow.axaml:282-300(FileName のみ)。原典 ImageGridView は名前+`formatFileSize` 2段 | `ByteSizeFormatter.Format(Record.FileSize)` の TextBlock 1行追加 |
| A-3 | トラッシュ 項目 | ファイルサイズ | 中 | TrashView.axaml:53-58 / TrashItemViewModel(FileSize 未公開)。原典 TrashModal.tsx:271-273 | VM に FileSize 公開+テンプレ1行 |
| A-4 | タグタブ ビュー一覧 | お気に入り★ + ビュー説明 | 中 | TagsTabView.axaml:64-78(Name のみ)。View.IsFavorite/Description は存在(Entities.cs:105,107)。原典 ViewManagementPanel.tsx:219-223,248-250 | ViewRowViewModel にプロパティ公開+DataTemplate |
| A-5 | タグパレット 行 | タグ説明(description) | 低〜中 | TagsTabView パレット行(説明非表示)。Tag.Description 存在(Entities.cs:66)。原典 TagItem.tsx:84-88 | テンプレ+tooltip |
| A-6 | RelinkWindow missing 行 | ファイル名(現状パスのみ) | 低 | RelinkWindow.axaml:29 | テンプレ修正(A-1 と同時) |

いずれも**データ・整形器は既存**で、表示層のみの欠落=GF-V4-04 と完全に同型。帰属は **spec_omission**(各画面の表示契約が要件化されていない)。

## B. 要トリアージ(スコープ判断が要る — ユーザー裁定)
| # | 画面 | 内容 | 論点 |
|---|---|---|---|
| B-1 | 詳細パネル | **rating(評価 ★1-5)が UI もデータモデルも皆無** | 原典 ImageDetailModal は rating セクションを持つが、ViewPrism2 は `ImageRecord` に列すら無い。表示層でなく**データモデル gap**。意図的スコープ除外か移植漏れか要確認(DetailPanelVM のコメントは rating に言及せず=仕様段階で落ちた可能性) |
| B-2 | 修復(RepairWindow) | **「すべて自動修復」ボタン** + **「除外(選択を deleted 化)」** | 原典 RepairModal の主要動線。AutoRepairableCount は表示するが一括実行コマンドが無い。V4 スコープか後続か |
| B-3 | 修復(AdvancedRepair) | **類似画像検索モード**(外部画像+pHash しきい値+類似度%) | 既に **V5 deferred 既知**(「再エンコードの pHash 類似修復」)。再確認 |
| B-4 | 二次(新規画像) | **NewImagesModal 相当が無い** + 死蔵 i18n キー | ViewPrism2 は新規を即 normal 化(ScanJudge 規則3b)、pending=再リンク候補専用=**意図的設計差**。ただし `modals.newImages.*` の i18n キーが死蔵。設計差を明文化し死蔵キーを cleanup すべき |
| B-5 | ナビ | **作業(work)タブ未移植** | 既に **charter「含まない」既知**(作業スペース/バックアップ)。再確認 |

## C. out-of-scope / 許容(原典にも無い・意図的設計差・装飾)
- **ビューア**: omission なし。むしろ port が位置 N/M・ファイル名を追加表示=原典を上回る。
- **類似検索**: 精度モード非表示=仕様 §2.10.4 妥当。基準画像「マージ先」ラベル/初期プロンプト空状態/自己ヒット同一バッジは軽微(自己ヒット除外の有無のみ追加調査価値あり)。
- **マージ**: コア表示はパリティ達成。非破壊文言+統合後タグプレビューは port が上回る。宛先選択 UI 差は責務分割の設計差。タグ含めるトグル/キャンセルボタンは軽微。
- **詳細パネル**: 相対パス/hash/status は**原典でも非表示**=omission ではない。
- **トラッシュ**: パス/削除日時は原典の `DeletedImage` に存在しない=対象外。復元確認/選択件数/ローディングは軽微。
- **グリッド**: missing プレースホルダ/フォルダタイルは母集合が normal 限定の設計差。**読込失敗エラー表示**(原典は赤アイコン、port は '?' 据置)は REQ-040(一覧を止めない/次回再試行)由来の縮退の可能性=要明文化。

---

## 推奨アクション
1. **A 群(確定表示 omission)→ ECO-004 として一括是正**(spec→E-BOM→M-BOM→Control Plan→製造の連鎖。**直接修正しない**=ECO-003 で確立した規律)。A-1 RelinkWindow が最優先(同型 omission の実発見)。
2. **B 群はユーザー裁定**: in-scope omission(ECO 化)か intentional out-of-scope(charter/仕様に明記)かを分類。B-3/B-5 は既知 deferred の再確認、B-1/B-2/B-4 は新規判断。
3. **死蔵 i18n キー(B-4 newImages)** は別 cleanup タスク。
4. **C 群の「縮退の明文化」**(グリッド読込失敗表示・マージ tag トグル等)を charter/仕様に out-of-scope として記録し、将来の golden 再指摘を防ぐ。

## メタ結論
read-across は (1) 同型 omission を兄弟画面で1件確実に捕捉(A-1)、(2) データ存在の純表示 omission を4件(A-2〜A-5)、(3) スコープ未確定の gap を5件(B群)炙り出した。**新ゲートは過去の出荷物に対して機能した**。これが ECO-003 の lesson(表示契約を要件化+read-across で横展開)の実地検証。
