# ECO-077(implemented): バックアップ・移送の入口を設定 ▸ データとバックアップへ集約(SS-001 再裁定=E-1 追随)

- 起票: 2026-07-13(maintainer 指示・ViewPrismUI SS-001 再裁定への追随)
- 種別: 既存機能の設計変更追随(CAD 上流裁定の転写。機能の実体=A層/B層エンジンは不変)
- 状態: implemented(2026-07-13 /eco-fix=§9。gate②=golden 待ち=§10)
- 関連: ECO-072(A層・設定バックアップ節入口=旧裁定(b))/ ECO-073(B層・⋯メニュー入口=旧裁定(b))/
  ECO-074(置き場所管理・B-2 picker 自動起動=E-1 でも維持)/
  **ECO-076(取り込みウィザード stepper B-3/B-4 可視化・別ブランチで accept 済み `367593b`・main 未マージ)**
  = 同じ CAD 画面(snapshot_export_import)の別契約(L7)。重複なし・相互参照のみ。
  register/台帳のマージ順に注意(本 ECO は ECO-076 の register エントリを持たないブランチ上で起票)。

## 1. 要求(maintainer 指示=CAD 再裁定の追随)

ViewPrismUI(CAD)が SS-001 を再裁定した(2026-07-13)。ViewPrism2 を追随させる。

- CAD 正: ViewPrismUI コミット `4787b03`「decide(ss-001): 入口を設定 ▸ データとバックアップへ集約 —
  mock 改版(E-1 新設)の CAD 化」。一次資料 mock 差し替え
  SHA-256 新 `1278cf0606b5…`(旧 `5fdf44645e31…`)。既存 6 サーフェス(A-1〜B-4)は画素不変。
- MOCK 裁定 M5(配置): 書き出す/取り込むの**本拠地=設定 ▸ データとバックアップ**(A 層スナップ
  ショットも同節)。右クリック新設は見送り。既存の整理「…」メニューは
  **「設定でバックアップ・移送…」の誘導 1 項目のみ**(実体を持たない・破壊系との混同と重複 UI を避ける)。
- **旧裁定 SS-001(b)分置(2026-07-12)=B 層をコレクション管理三点メニューに置く、は破棄。**
- CAD 文書: `docs/screens/snapshot_export_import.md`(E-1 のレイアウト/状態/インタラクション/
  視覚契約チェックリスト **VC-5〜VC-8**)・`docs/03_dialog_language.md`(マトリクスへ **E-1 行=L1/L3/L8**)・
  キャプチャ `docs/screens/captures/snapshot_export_import/E-1.png`(同梱済み・GF-072-01 恒久対策準拠)。

### E-1 の UI 契約(CAD 要点)

- 設定ウィンドウに「データとバックアップ」節(**左ナビの選択項目**=淡青背景+青文字。VC-6)。
  旧「バックアップ(スナップショット)」節は改称・拡張。E-1 は既存設定ウィンドウの節(新しい窓を作らない)。
- 節内:
  - 「スナップショット(この端末内)」**1 行カード**(副情報=`最終作成 yyyy/MM/dd HH:mm ・ N 件`=VC-7/L8、
    [開く]→A-1 管理ダイアログ)。
  - 「コレクションの移送(他端末・共有)」**2 行カード**(「コレクションを書き出す…」[選ぶ…]→B-1
    =**対象コレクションは B-1 内で選択**/「コレクションを取り込む…」[ファイルを選ぶ…]→B-2
    =ファイル選択自動起動・管理フォルダ起点=ECO-074 準拠)。
  - 末尾注記「既存データは削除されません(取り込みは追加のみ)」(淡色・VC-6)。
- 行カードは共通言語 **L3**(左端グリフ角丸スクエア+名前太字+副情報淡色。グリフ=スナップショット
  DB 円筒・濃紺/書き出す 上矢印トレイ・緑/取り込む 下矢印トレイ・青。行右端=白 outline+青文字ボタン。VC-5)。
  日時は **L8**。
- 整理「…」: 修復/削除(赤)/ゴミ箱+**区切り線の下に「設定でバックアップ・移送…」1 項目のみ**
  (淡紫ハイライト+太字。書き出す/取り込むの実体項目を置かない=M5。VC-8)。クリックで
  設定 ▸ データとバックアップ(E-1)へ誘導。

## 2. 工程診断(R2)

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全(上流で設計変更済み)** | `4787b03`=E-1 新設+M5+VC-5〜8+captures/E-1.png+03_dialog_language マトリクス E-1 行(L1/L3/L8)。snapshot_export_import.md「画面横断の依存」節に再裁定を明記・旧(b)分置の破棄を記録 |
| CAD(1 点の沈黙) | **軽微欠陥(未定義)=gate①候補** | E-1「ファイルを選ぶ…」→B-2 の**取り込み先コレクション決定が未定義**(§3 確定 4 項)。書き出し側は「対象コレクションは B-1 内で選択」と明記(interaction 表)されるが、取り込み側の対応規定がない。mock B-2〜B-4 に取り込み先コレクションの選択面もない |
| BOM(REQ/spec/E/M/CP) | **旧裁定への整合=追随対象** | REQ-092(入口=設定バックアップ節・裁定(b))/REQ-093(入口=⋯メニュー・裁定(b))/spec §2.13.5・§2.14.6/E-UI-SNAPSHOT-046(30-ebom:535)/E-UI-PACKAGE-048(30-ebom:556)/M-UI-SNAPSHOT-041 entry(32-mbom:660)/M-UI-PACKAGE-043 entry(32-mbom:688)/CP-UI-G12・CP-UI-G13 characteristic 冒頭の入口記述 — いずれも SS-001(b) を明記しており CAD 再裁定と乖離 |
| 実装 | **旧裁定の忠実な転写=追随対象** | ImageTabView.axaml:888-901(⋯メニューに実体 2 項目)・ImageTabViewModel.cs:1578-1598(ExportCollection/ImportCollection=`_collectionId` 入口固定)・SettingsWindow.axaml:28-35(「バックアップ」節・左ナビなしの 400px 単票)・WindowService.cs:155-180(Show 系が `collectionId` 必須) |

**結論: 上流(CAD)設計変更への追随であり、実装/BOM の欠陥ではない**(ECO-076 と同型)。
E-1 契約の大半は CAD 確定済みで裁定不要。ただし**取り込み先コレクション決定の 1 点だけ CAD が沈黙**
しており(§4 選択肢)、この点のみ gate① 裁定(ViewPrismUI 側の CAD 追補が先)を要する。

## 3. 切り分け済みの事実

確定:

1. **書き出し**: 現実装は入口コレクション固定で B-1 にコレクション選択 UI がない
   (WindowService.ShowCollectionExportAsync(collectionId)→CollectionExportViewModel は単一
   `SyncFolder` を受ける)。CAD mock B-1 は当初から「コレクション選択」を持ち(layout 表)、
   E-1 改版で「対象コレクションは **B-1 内で選択**」と interaction 表に明記された。
   → 書き出しは **CAD 確定・裁定不要**(B-1 内選択の実装が必要)。
2. **取り込み**: 現実装は入口コレクション=取り込み先
   (CollectionImportViewModel: `PreviewAsync(PackagePath, _collection.Id)`・
   `ImportAsync(path, _collection.Id, …)`)かつ**画像突き合わせ基準ルート=`_collection.Path`**
   (TargetRootPath)。E-1 にはコレクション文脈がなく、この 2 つの決定方法が CAD 未定義。
3. 旧 V1 差分裁定の前提が失効: 32-mbom 沈黙次元「取り込み先ルートの変更 UI」(32-mbom:823)は
   「誤ルートは過半ガード+**別コレクションの三点メニューから取り込み直し**で代替」としたが、
   M5 で三点メニューの実体が消えるため**この代替手順は成立しなくなる**(後継の決定方法とセットで裁定)。
4. GF-073-05 の golden 実績「同一ライブラリ内の**別コレクション**取込が成功する」は、
   取り込み先がパッケージ由来でなく**自由パラメータ**であることを前提にした承認=取り込み先を
   パッケージのコレクション UUID で自動決定する案は既承認挙動の再定義になる。
5. スナップショット行サマリのデータ源は既存: `SnapshotService.List(directory)` が
   `CreatedAtUtc` 降順の一覧を返す(SnapshotService.cs:121-148)= 件数+最終作成日時は
   新規インフラ不要。**0 件時の表記は未確定(SS-004 に併合済み・CAD も「未設計」と明記)**。
6. 現 SettingsWindow は幅 400・左ナビなしの単票(SettingsWindow.axaml:7,13)。E-1 は
   左ナビ(一般/データとバックアップ)+節構成・設定窓幅 812(**幅は許容差分 V2**=CAD 明記)。
   → 設定ウィンドウの構造変更(ナビ+節)が fix 範囲に入る(VC-6 が左ナビ選択状態を視覚契約に含むため)。
7. i18n: `settings.backup.*` は ECO-072 で配線済み(M2=自動バックアップ語彙は未配線温存)。
   「データとバックアップ」への改称・移送カード・注記の語彙追加が必要(ja/en)。
8. ECO-076(stepper B-3/B-4 可視化)は**別ブランチで accept 済み(`367593b`)・main 未マージ**。
   同じ CAD 画面の別契約(L7)で本件(E-1=L1/L3/L8)と衝突しない。本 ECO では L7 に触れない。

疑い(未検証):

- E-1 からの取り込みで ReloadImagesAsync/Recompute(現状 ImageTabViewModel.ImportCollection 内=
  ImageTabViewModel.cs:1596-1597)相当の再読込がどこに移るか — 設定入口からだと画像タブが取り込み先
  コレクションを表示中とは限らないため、再読込の要否・範囲は fix 時に実測して決める。

## 4. 是正方針(案・gate① は取り込み先決定の 1 点のみ)

### 4.1 CAD 確定分(裁定不要・fix で実施)

- ImageTabView ⋯メニュー: 実体 2 項目(書き出す/取り込む)を撤去し「設定でバックアップ・移送…」
  誘導 1 項目へ置換(VC-8=淡紫ハイライト+太字・区切り線下)。クリックで設定ウィンドウを
  「データとバックアップ」節選択状態で開く。
- SettingsWindow: 左ナビ(一般/データとバックアップ)+節構成へ再構成(幅は許容差分 V2)。
  「データとバックアップ」節=スナップショット 1 行カード(サマリ=最終作成+件数・[開く]→A-1)+
  移送 2 行カード([選ぶ…]→B-1・[ファイルを選ぶ…]→B-2 で picker 自動起動=ECO-074 準拠)+末尾注記。
  視覚は VC-5〜VC-7(L3/L8)。
- B-1: コレクション選択を追加(CAD mock 準拠=項目数・タグ数表示)。WindowService の Show 系は
  コレクション文脈なし起動に対応。
- 0 件時サマリ表記: **発明しない**。placeholder 最小(例: 件数のみ「0 件」・最終作成は省略)を
  設計者適用し golden で否認可、SS-004 正式裁定時に追随(ECO-074 §7 の案イ方式と同型)。
- 台帳同期: REQ-092/093・spec §2.13.5/§2.14.6・E-046/048・M-041/043 entry・CP-UI-G12/G13 の
  入口記述を「SS-001 再裁定(2026-07-13)=設定 ▸ データとバックアップ(E-1)」へ更新。
  32-mbom:823 の代替手順記述を後継へ書き換え。

### 4.2 gate① 裁定事項(取り込み先コレクションの決定方法)

- **案A(B-1 対称・推奨)**: B-2 に取り込み先コレクション選択を置く(B-1 の「コレクション選択」と
  同型のコントロール 1 つ)。エンジン(Preview/Import の collectionId 引数)不変・GF-073-05 の
  既承認挙動(別コレクション取込)保存。**CAD mock/文書の追補が先行条件**(ViewPrismUI 側)。
  diff 小〜中(B-2 面+CAD 改版)。golden 影響=B-2 初期状態の再定義。
- **案B(UUID 自動+不一致時選択)**: パッケージのコレクション UUID がライブラリに在ればそこへ自動、
  なければ B-2 で選択。自動側は誤選択リスク最小だが、GF-073-05 の「別コレクションへ意図的に
  取り込む」動線が 1 段深くなる+ライブラリ UUID 照合の新契約。diff 中。CAD 追補必要。
- **案C(暫定=⋯メニュー実体温存)**: 取り込みのみ三点メニュー実体を残す — **M5 と正面衝突するため
  推奨しない**(挙げるだけ)。

いずれも CAD が正: 採用案は ViewPrismUI(snapshot_export_import.md・必要なら mock/captures)へ
先に正典化してから ViewPrism2 の fix に入る。

## 5. 影響 BOM

- CAD: 追補 1 点のみ(§4.2 採用案の B-2 取り込み先規定)。E-1 本体は改版済み(`4787b03`)。
- REQ: REQ-092/REQ-093 の入口記述(10-requirements.yaml:1235/1256)。
- spec: §2.13.5(A層入口)・§2.14.6(B層入口と UI)。
- E-BOM: E-UI-SNAPSHOT-046(入口 invariant)・E-UI-PACKAGE-048(入口 invariant+B-1 コレクション選択)。
- M-BOM: M-UI-SNAPSHOT-041 entry・M-UI-PACKAGE-043 entry(⋯メニュー→E-1)・32-mbom:823 沈黙次元 note。
- CP: CP-UI-G12/CP-UI-G13 characteristic の入口記述+E-1 観点の追加。CP-SNAPSHOT-031/CP-PACKAGE-032
  (エンジン)は不変見込み。
- 実装: ImageTabView.axaml(⋯メニュー)・ImageTabViewModel(誘導コマンド化)・SettingsWindow.axaml/
  SettingsViewModel(ナビ+節+カード+サマリ)・WindowService(コレクション文脈なし起動+設定の節指定
  オープン)・CollectionExportViewModel(B-1 内選択)・CollectionImportViewModel(§4.2 採用案)・
  i18n ja/en(settings.dataBackup 系+誘導項目)。
- Oracle: 既存固定行は変更しない(R6)。

## 6. 検査計画(R7 セルフゴールデン・probe 先行)

- **E-1 視覚 probe は CAD の VC-5〜VC-8 から先行生成する**(GF 後追い禁止・fix 着手前に赤を実測):
  VC-5=行カード L3(グリフ配色 3 種+名前太字+副情報淡色+右端白 outline 青文字ボタン)/
  VC-6=左ナビ選択状態+節見出し+末尾注記/VC-7=サマリ書式 L8(`yyyy/MM/dd HH:mm ・ N 件`)/
  VC-8=⋯メニュー誘導 1 項目(実体 2 項目の不在+淡紫ハイライト+太字)。
- **共通言語マトリクスの並置検査範囲(影響分析宣言)**: 本 ECO は E-1 面で L1/L3/L8 に触れる。
  03_dialog_language マトリクスの該当列○面=
  **L3: E-1・A-1・A-2・B-1・B-2・B-3/L8: E-1・A-1・B-1・B-2・B-3・B-4/L1: 全 7 面**。
  E-1 以外の既存面は言語定義不変+画素不変(CAD 改版が明記)のため、既存 headless 視覚 pin
  (GfSnapshotVisualParityTests/GfPackageVisualParityTests)の**緑維持を回帰確認**とし、
  出荷前 R7 並置は E-1(+⋯メニュー)×captures/E-1.png を新規実施、既存面は captures 据え置きで並置。
  **L7(stepper)には触れない**(ECO-076 の契約・本 ECO のスコープ外)。
- 挙動 probe: 誘導項目→設定が「データとバックアップ」節選択で開く/[開く]→A-1/[選ぶ…]→B-1
  (コレクション選択可)/[ファイルを選ぶ…]→B-2 picker 自動起動(ECO-074 準拠・管理フォルダ起点)/
  スナップショットサマリが List() 実測値と一致/0 件時 placeholder。

## 7. 残ゲート

- gate①: ~~§4.2(取り込み先コレクションの決定方法)の裁定+0 件時 placeholder の設計者適用可否~~
  → **裁定済み(§8)**。
- gate②: golden(CP-UI-G12/G13 の E-1 観点)→ **操作手順=§10**(fix 済み・待機中)。

## 8. gate①裁定(2026-07-13)

- maintainer 裁定: **案A(B-2 内に取り込み先コレクション選択・B-1 対称)を採用**。
  エンジン(Preview/Import の collectionId 引数)不変・GF-073-05 の既承認挙動
  (別コレクションへの意図的取込)を保存する。
  **採用案の ViewPrismUI 正典化(snapshot_export_import.md の B-2 取り込み先規定+必要な mock/
  captures 追補)が fix の先行条件**(ECO-074 §8.1 と同型・CAD が正)。
- **0 件時サマリ表記=placeholder 最小を設計者適用**(golden で否認可): 発明せず、
  件数のみ「0 件」・最終作成は省略(§4.1)。SS-004 の正式裁定時に追随する。
- 32-mbom:823 の失効した代替手順(§3 確定 3)は、案A の「B-2 内選択」を後継として書き換える。

## 9. 実施記録(2026-07-13 /eco-fix)

### 9.1 CAD 正典化(先行条件)

- ViewPrismUI `05080e1`: snapshot_export_import.md へ B-2 状態表「取り込み先コレクション
  (実装補遺・ECO-077 裁定=案A)」行(B-1 対称・既定=未選択・選択まで「次へ」不活性=互換 OK と
  AND・突き合わせ基準ルート=選択コレクションのフォルダ)+interaction 表 B-2 行+
  「画面横断の依存」入口節へ B-1/B-2 内選択の一文+E-1 状態表の 0 件行へ placeholder 最小の暫定
  (SS-004 追随・golden で否認可)。

### 9.2 先行 probe(R5+R7: 視覚 probe は VC-5〜VC-8 から先行生成・GF 後追い禁止)

- `GfEntryE1VisualParityTests`(VC-5/6/7=CP-UI-G12・VC-8=CP-UI-G13)+
  `CpUiEco077EntryTests`(CP-UI-G13)を製品コード変更前に追加。
- **是正前実測: Tests 652 件中 6 fail(追加 probe 6 本すべて赤・既存 646 pass)**:
  VC5/VC6=左ナビ項目 0 個/VC7=SnapshotSummaryText 不在/VC8=実体項目
  「コレクションを書き出す…」残存(M5 違反)/B-1=ExportCollectionSelector 不在/
  B-2=ImportTargetSelector 不在/VM=OpenBackupSettingsCommand 不在。
- probe の 2 段強化(記録): 新 API(ctor 変更)に依存する検証は是正前はコンパイル可能な
  存在検査で赤を取り、是正と同一 diff 内で最終形(VC-7=実 SnapshotService fixture の書式検証+
  0 件 placeholder、B-1=既定先頭+出力先追随、B-2=未選択で CanProceed 不活性)へ強化した。

### 9.3 是正 diff

- SettingsWindow/VM: 左ナビ(一般/データとバックアップ)+節構成へ再構成(幅 720=許容差分 V2)。
  E-1 節=スナップショット行カード(DB 円筒グリフ濃紺 #2F4A75・サマリ=SnapshotService.List の
  先頭 CreatedAtUtc+件数・0 件=`settings.dataBackup.snapshotSummaryEmpty`)+移送 2 行カード
  (緑 #1F8A4C 上矢印トレイ/青 #2F6BED 下矢印トレイ・白 outline 青文字ボタン)+末尾注記。
  `SettingsSection` enum(General/DataBackup)+`ShowSettingsAsync(SettingsSection)`
  (IWindowService 既定実装=スタブ互換)。A-1 閉じ後サマリ再計算。
- ImageTabView/VM: ⋯ メニューの実体 2 項目を撤去し「設定でバックアップ・移送…」誘導 1 項目
  (淡紫 #F2EEFB ハイライト+太字+歯車グリフ・区切り線下・常時活性)へ。
  `OpenBackupSettingsCommand`=節選択オープン+閉じ後 ReloadImagesAsync+Recompute
  (旧 ImportCollection の再読込理由を継承)。旧 ExportCollection/ImportCollectionCommand 削除。
- CollectionExportViewModel/Window: ctor を全コレクション受けへ。B-1 内選択
  (カードに溶けた ComboBox=mock 同型の名前太字+シェブロン+件数淡色 2 行構成)。既定=先頭・
  選択変更で既定出力先(<名前>.viewprism2-collection.json)と件数表示が追随。
- CollectionImportViewModel/Window: ctor を全コレクション受けへ。B-2 に取り込み先コレクション
  カード(案A・既定未選択・placeholder)。`CanProceed`=VerifyOk∧選択済みで「次へ」を配線。
  Preview/Apply/突き合わせルート/結果文言は SelectedTarget 基準(エンジン引数は不変)。
- WindowService: ShowCollectionExport/ImportAsync(引数なし・コレクション 0 では開かない)+
  ShowSettingsAsync(section)。picker 起点=管理フォルダは不変(ECO-074 維持)。
- i18n: `settings.dataBackup.*` 15 キー+`package.importTarget`/`package.selectCollectionPrompt`(ja/en)。
- 既存 pin の契約反転(ECO-076 教訓=同一 diff 内): CpPackage073Tests の入口配線 pin を
  誘導(ShowSettingsAsync(DataBackup))へ改訂。Gf/CpPackage073/074 の VM ctor 呼び出しを追随。

### 9.4 機械受入

- `dotnet build`: 0 warning / 0 error。`ViewPrism2.Tests`: **652/652 pass**(probe 6 本緑転)。
- `ViewPrism2.Oracle`: 109 pass / 2 known skip(R6 不変)。`validate_bom`: 0/0。
- 台帳同期: REQ-092/093 入口・仕様 §2.13.5/§2.14.6(+§2.14 表面)・E-UI-SNAPSHOT-046/
  E-UI-PACKAGE-048 入口 invariant・M-UI-SNAPSHOT-041/M-UI-PACKAGE-043 entry+wizard・
  32-mbom:823 沈黙次元(失効代替手順→B-2 内選択)・CP-UI-G12/G13 characteristic。

### 9.5 R7 セルフゴールデン(captures 並置・転写漏れ 0)

検査面(headless Skia 実描画 PNG × CAD captures):

1. **E-1(設定 ▸ データとバックアップ)× captures/E-1.png**: 左ナビ選択状態・節見出し 2・
   行カード 3 枚(グリフ配色/名前太字/副情報淡色/白 outline 青文字ボタン)・サマリ L8・末尾注記
   =転写一致。
2. **整理 ⋯ メニュー × E-1.png 右パネル**: 修復(グレー)/削除(赤)/ゴミ箱(グレー)/区切り線/
   誘導 1 項目(淡紫+太字)。実体 2 項目なし=転写一致。
3. **B-1 × B-1.png**: コレクション選択をカードに溶けた 2 行構成(名前太字+右端シェブロン+
   件数淡色)へ転写(初回並置で既定枠 ComboBox の浮きを検出→溶かして再並置)。他要素回帰なし。
4. **B-2 × B-2.png**: 既存要素(stepper/ファイルカード/互換バッジ/概要)回帰なし。追加の
   取り込み先カードは CAD 実装補遺(9.1)が根拠=差分でない。
5. L3/L8 ○面の残り(A-1/A-2/B-3/B-4)は本 diff で不変+既存視覚 pin
   (GfSnapshot/GfPackageVisualParityTests)緑=回帰なし。L7(stepper)には触れていない(ECO-076)。

差分の全列挙と分類(転写漏れ 0):

| # | 差分 | 分類 |
|---|---|---|
| D1 | 擬似タイトルバー非再現 | 裁定済み意図的差分(CAD レイアウト節) |
| D2 | 設定窓幅 720(mock 812) | 許容差分 V2(CAD 明記) |
| D3 | E-1 に「閉じる」ボタン(テーマ既定)が residual(mock に無し) | 既存設定ウィンドウへの相乗り(E-1 は「既存の設定ウィンドウの節」=CAD 規定)の帰結。E-1 の L2=−(マトリクス)。golden で否認可 |
| D4 | 誘導グリフ=歯車(mock はスパークル様の紫グリフ) | VC-8 契約(淡紫ハイライト+太字+1 項目のみ)は充足・グリフ形状は契約外。golden で否認可 |

## 10. gate② golden 操作手順(CP-UI-G12/G13 の E-1 観点)

1. **E-1 並置**: 設定を開く→左ナビ「一般/データとバックアップ」。「データとバックアップ」選択で
   淡青背景+青文字。節の全体を captures/E-1.png と並置(行カード 3 枚のグリフ配色=濃紺 DB 円筒/
   緑上矢印/青下矢印・名前太字+副情報淡色・右端白 outline 青文字ボタン・節見出し 2・末尾注記
   「既存データは削除されません(取り込みは追加のみ)。」)。記録済み差分 D1〜D3 の許容可否も確認。
2. **サマリ(VC-7/L8)**: スナップショット行の副情報=「最終作成 yyyy/MM/dd HH:mm ・ N 件」が
   実件数と一致。空の保存先では「0 件」のみ(placeholder 最小=SS-004 暫定の否認可ポイント)。
   [開く]→A-1 で作成→閉じる→サマリが更新される。
3. **書き出し(B-1 内選択)**: [選ぶ…]→B-1。コレクション選択がカード内 2 行構成(mock 同型)・
   既定=先頭・選択変更で既定出力先の <名前> と件数表示が追随→書き出し成功(管理フォルダ既定=
   ECO-074 回帰なし)。
4. **取り込み(B-2 内選択=案A)**: [ファイルを選ぶ…]→B-2 が picker 自動起動(管理フォルダ起点)。
   **取り込み先コレクション未選択の間は互換 OK でも「次へ」不活性**→選択で活性→B-3→B-4 の
   既存フローに回帰なし。取り込み先を表示中のコレクションにした場合、完了後に画像タブへ反映。
5. **⋯ メニュー(VC-8/M5)**: 実体 2 項目(書き出す/取り込む)が無い。区切り線の下に
   「設定でバックアップ・移送…」1 項目のみ(淡紫ハイライト+太字・D4 の許容可否確認)→
   クリックで設定が「データとバックアップ」節選択で開く。
6. **ja/en**: E-1 文言(節名/カード/ボタン/注記/サマリ)が切替に追随。
7. **回帰**: A-1/A-2/B-3/B-4 と設定「一般」(言語切替)に視覚・挙動の回帰なし。
