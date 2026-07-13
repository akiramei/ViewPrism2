# ECO-079 — 画像タブ・作業タブの多言語対応漏れ(直書き文言が言語切替に非追随)

- status: applied(2026-07-13 gate② golden 合格・GF-079-01 を経てクローズ)
- type: 不具合(i18n 未配線。実装逸脱=REQ-051「UI 全体へ即時反映」/K-AVALONIA「XAML 直書き禁止」違反)
- baseline: main 318eeac
- 報告者: maintainer(2026-07-13・スクリーンショット3枚添付=en モードで画像タブ/整理モード/作業タブが日本語固着)

## §1 症状

言語を英語(en)に切り替えても、**画像タブ**(コレクションペイン・ツールバー・タグ編集/整理/作業/削除の各モード・並び替え/表示メニュー・ゴミ箱・検索/類似パネル)と**作業タブ**(作業スペースペイン・同ツールバー・各モード・ゴミ箱・削除確認)の文言が日本語のまま切り替わらない。

添付スクショの決定的所見: **トップタブ(Tags / Images / Work / Settings)と設定ボタンは英語化しているのに、その直下の画像タブ・作業タブの中身だけ日本語で固着**している。同一ウィンドウ内で切り替わる面と切り替わらない面が併存 → 「一部サーフェスが i18n に配線されていない」ことを示す。

要求: 画像タブ・作業タブも他サーフェス同様、言語切替(ja⇔en)に追随させる。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全** | mock は日本語だが視覚レイアウトの原器として正。i18n はキー写像で担保する設計(他 17 サーフェスで成立)。CAD 欠陥なし・視覚は不変。 |
| 要求/規約(REQ/K/OC) | **健全** | REQ-050(ja 既定+en の 2 言語・欠落は ja フォールバック)・**REQ-051(言語変更は即時=再起動不要で UI 全体へ反映)**・OC-8・E-I18N-014(解決器)・**K-AVALONIA(文言は LocalizationService 経由バインディング・XAML 直書き文字列禁止)**。規約は明文で存在し、他全サーフェスで適用済み。 |
| BOM(30-ebom/32-mbom/33-cp) | **谷間あり(健全に近い)** | E-I18N-014 に image/work タブ surface が depends する宣言はある。しかし **画像タブ/作業タブの文言 Loc バインドを検証する CP/Oracle/validator が不在**=逸脱がすり抜けた。CP-UI-G1 の smoke oracle に「言語切替でラベル・ボタンが切り替わり」の文言はあるが面別網羅でなく、**golden は全 ECO で ja 実施のため言語切替が実測されなかった(golden 谷間)**。 |
| 実装 | **逸脱と確定** | ImageTabView.axaml=直書き JP 88 箇所・`Loc[` バインド 0。WorkTabView.axaml=直書き JP 80 箇所・`Loc[` 0。両 VM が `Loc` プロキシ未公開(WorkTabViewModel は LocalizationService の DI すら未受領)。**参照実装 TagsTabViewModel/TagsTabView は Loc プロキシ+26 バインドで正しく配線済み**。 |

**結論**: CAD・要求・規約は健全。**実装逸脱と確定**(gate① 裁定は不要)。BOM 側に検査の谷間(この種の逸脱を捕捉する検査が無い)が併存し、golden も ja 実施のため見逃されていた。

## §3 切り分け済みの事実(確定と未検証を分離)

### 確定(実測)

- サーフェス別 `Loc[` バインド数 / 非コメント直書き JP 数(2026-07-13 実測):
  - **ImageTabView: Loc=0 / 直書き=88** ← 欠陥
  - **WorkTabView: Loc=0 / 直書き=80** ← 欠陥
  - TagsTabView: Loc=26 / 直書き=0 / ViewerWindow: 72/0 / CollectionImportWindow: 48/0 / TagEditorWindow: 24/0 / SettingsWindow: 20/0 / RepairWindow: 19/0 / ViewEditDialog: 17/0 / SnapshotWindow: 16/0 / FolderManagementWindow: 12/0 / CollectionExportWindow: 12/0 / NodeConditionDialog: 11/0 / NumericValueDialog: 8/0 / RelinkWindow: 6/0 — **他 17 サーフェスは全て Loc>0・直書き 0**。MainWindow も非コメント直書き=0。
  - → 欠陥は**ユーザー報告の 2 サーフェスに正確に限局**。
- i18n 語彙は **ja/en とも 1076 キー既存**。`collection`(82)/`toolbar`(56)/`sort`(6)/`merge`(11)/`similar`(7)/`trash`(11)/`view`(141)/`filelist`(4)/`tagging`(10)/`common`(35)/`navigation` 群は**英訳込みで populated**=直書き文言の**大半は既存キーで再利用可能**(例: コレクション→`navigation.collections`、その他→`toolbar.more`、整理→`toolbar.organize`、タグ編集→`toolbar.tagEdit`、作業スペース→`navigation.workspaces`、昇順/降順→`view.ascending`/`descending`、並び替え/表示/ゴミ箱→`toolbar.sort`/`view`/`trash`、探す→`modals.advancedRepair.find`)。
- ImageTabViewModel は `LocalizationService _localization` を **DI 受領済み**(ECO-025 β-2 で列ピッカー生成に使用・ImageTabViewModel.cs:147/162)だが `Loc` プロキシは未公開。
- WorkTabViewModel は LocalizationService の DI **未受領**(コンストラクタに引数なし)。
- 参照実装 TagsTabViewModel.cs:76/94-101 = `Loc = new LocalizationProxy(localization)` を公開し、`CultureChanged` で `Loc` を差し替えて `OnPropertyChanged(nameof(Loc))`(コメント「DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)」)。
- git 履歴: ImageTabView.axaml 初出=`bcd861d`「画像タブ製造 M1+M2: 部品層 + golden ハーネス surface(**モック準拠**・実機検証済)」。`git log -S 'Loc['` で ImageTabView に Loc バインドを足したコミットは**皆無**=初出時点から一度も i18n 配線されていない。

**混入原因(確定)**: 画像タブ/作業タブ製造時(bcd861d 系)に、日本語モックを直接転写して文言を XAML 直書きし、TagsTabView が持つ i18n 配線(Loc プロキシ+バインド)を適用しなかった。
**マスキング要因(確定)**: 既定ロケールが ja(REQ-050)のため、通常運用では直書き JP と i18n 化文言が区別不能。**en 切替時に初めて顕在化**(=ユーザー提供スクショ)。golden が全 ECO で ja 実施だったことも見逃しを助長。

### 未検証(疑い)

- 一部直書き文言に対応キーが未存在(実測サンプル: 「スキャン中」「再試行」「追加済」「既定」に完全一致キーなし)=**新規キー数点の ja/en 追加が必要**。全直書きの棚卸しは /eco-fix 時に実施。
- StringFormat バインド(例 `整理対象 {0}`・`値を選んで付与（{0}）`・`{0} 以上`)は `Loc[key]` インデクサだけでは素直に扱えず、`T(key, args)` 変換 or 既存コンバータ/多重バインドが必要。TagsTab/他サーフェスの `{count}` 系キー処理を前例に方式を fix 時決定。
- VM コードビハインドで組み立てる文言(成功トースト等)は既に `T()` 経由の可能性が高い(merge.* / trash.* 等が json に存在)が、直書き残があれば fix 時に追補。

## §4 是正方針(案・着手時確定)

**案A(推奨)**: TagsTabView と同一パターンで両タブを i18n 配線する。

1. WorkTabViewModel に `LocalizationService` を DI 追加(ImageTabViewModel は受領済み)。
2. 両 VM に `Loc` プロキシ(LocalizationProxy)を公開し、`CultureChanged` で `Loc` を差し替え+`OnPropertyChanged(nameof(Loc))`(TagsTabViewModel の DF-3 パターンを踏襲=K-AVALONIA の罠対策)。
3. ImageTabView.axaml / WorkTabView.axaml の直書き文言(計 168)を `{Binding Loc[key]}` へ置換。**語彙は既存キー再利用を優先**、不足分のみ ja/en に追加。StringFormat は `T(args)` か既存コンバータ前例へ寄せる。ToolTip.Tip も対象。
4. 視覚は不変(文言のみ・ja 表示は現状と同一)。

**プローブ先行**(/eco-fix の入口): 是正前に赤で用意 —
- 単体: en へ切替後、両タブの代表キー(navigation.collections / toolbar.organize / navigation.workspaces 等)が英語解決される(現状=XAML 直書きのため VM/T では検知できない → **ヘッドレス視覚 probe** が本命: en 適用後に ImageTab/WorkTab のツールバー文言が英字化する)。
- **谷間対策の候補**(スコープ内で軽量なら同梱): 「ImageTabView/WorkTabView の非コメント XAML に直書き JP 文字列が無い(=全文言 Loc バインド)」を検査する lint(validator or test)。再発防止=この検査が無ければ同型逸脱が再混入し得る。

## §5 影響 BOM

- 実装: `ImageTabView.axaml` / `WorkTabView.axaml` / `ImageTabViewModel.cs`(Loc 公開)/ `WorkTabViewModel.cs`(DI+Loc 公開)。DI 登録(WorkTabViewModel が LocalizationService を受ける)。
- i18n: `Assets/i18n/ja.json` / `en.json`(不足キーのみ追加=数点見込み)。
- CP/Oracle: 言語切替網羅 or 直書き JP 不在の検査追加(谷間是正・**再発防止として推奨**)。既存固定 Oracle 行は変更しない(R6)。
- CAD: **変更なし**(mock 準拠・視覚不変)。
- REQ/spec: REQ-050/051・OC-8・E-I18N-014 の適用確認のみ(新規宣言不要の見込み)。

## §6 残ゲート

- **gate① 裁定: 不要**(実装逸脱と確定・CAD 健全・視覚不変)。→ 是正完了(§7)。
- **gate② golden**: 言語切替(ja⇔en)で画像タブ・作業タブの全文言が切り替わることの maintainer 実機確認。R7 セルフゴールデン=日本語 mock(CAD captures)との並置に加え、**en 表示時のはみ出し/切れ**を確認(英字は日本語より長くなり得るためレイアウト観点も含める)。

## §7 実施記録(2026-07-13 /eco-fix)

### プローブ先行(R5)= 赤の実測裏取り

- **静的 lint プローブ**を新設(`CpI18n010XamlLintTests`・谷間是正を兼ねる恒久ガード): ImageTabView/WorkTabView の非コメント XAML に文言バインディングでない直書き日本語が無いことを検査。是正**前**(fixed axaml を stash して HEAD へ戻した状態)で実行 → **2 件不合格**(ImageTabView・WorkTabView=診断どおり・件数は起票時実測 88/80)。是正後に緑転。
- 機能プローブ `CpI18n010TabKeysTests`= 代表キー(navigation.collections/toolbar.organize/view.scanning/StringFormat 書式等)が ja/en で解決し en 切替で英語化することを固定。

### 是正(案A採用=真因構造を消す)

TagsTabView と同一の DF-3 パターンで両タブを i18n 配線:
- **VM**: ImageTabViewModel(`_localization` 受領済)・WorkTabViewModel(`LocalizationService` を DI 追加)に `Loc` プロキシを公開し、`CultureChanged` で差し替え+`OnPropertyChanged(nameof(Loc))`。MainWindowViewModel の WorkTab 構築へ `localization` を伝搬。
- **XAML**: 両 axaml の直書き文言 168 サイトを `{Binding $parent[UserControl].((vm:XxxTabViewModel)DataContext).Loc[key]}`(祖先形=全 DataContext スコープで安全)へ置換。StringFormat 6 本(NumRange/OrganizeTargets.Count/SimilarThresholdLabel)は新設 `LocalizedFormatConverter`(IMultiValueConverter・`{x:Static}` 単一実体)+ MultiBinding で「Loc[書式] + 値」に統一(テンプレートも言語切替へ追随)。
- **i18n**: 既存キー再利用 94 マッピング中、不足分 58 キー(3 書式含む)を ja/en へ追加。**転写ドリフト 0 を実測**=束ねた 94 マッピング全ての ja 値が元の直書き文字列と完全一致(視覚不変=R7 セルフゴールデンの核を機械保証)。CAD/mock は不変。
- **テスト影響(必要な追随)**: 直書き→Loc バインド化で描画テキストが loc 依存になったため、View を描画して文言を検査するテストの loc を空辞書→実アセット(`TestLoc.Ja()`)へ統一(Empty 19+inline 5 箇所)。両 VM は `T(` 呼び出し 0 のため VM 計算値は不変=挙動テストへの影響なし。VC8(GfEntryE1・⋯メニュー構造 probe)は DataContext 無し描画を前提にしていたため、共有ビルダー `TestImageTab.NewVm` で実 loc を持つ ImageTabViewModel を DataContext に与え、論理祖先で Loc を解決(未表示 Popup でも `$parent[UserControl]` は論理探索のため解決を実測確認)。

### 機械受入(4 点・全緑)

- `dotnet build ViewPrism2.sln`: **0 warning / 0 error**。
- Tests: **658/658**(旧 655 + 新規プローブ 3)。`dotnet test`(全並列)で無関係の既存フレーク(CpWorkspace028=Dapper Int64→Int32・並列 SQLite 共有)が断続再現したため、memory の実行規律どおり **exe 直接実行で 658/658 を確定**(当該フレークは isolation 10/10 緑・本変更は WorkspaceService 不変=51-cheat-log 記録・R3)。
- Oracle: **109 pass / 2 known skip**(R6 不変)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。

### R7 セルフゴールデン(UI fix)

視覚不変(ja 表示は現状と同一)を機械保証: ①転写ドリフト 0 の実測(上記)②描画テスト(Img014 等)が ja レンダリングで従来の日本語ラベルを検出=現状維持③機能プローブが en 解決を裏取り。CAD captures は ja のため ja 不変=並置一致は構成上自明。en 表示は新規挙動(CAD に en capture 無し)=maintainer 実機の golden 観点(はみ出し/切れ)へ委ねる。

**次 gate=② golden**(下記 golden 基準)。

## §8 GF-079-01(golden 所見 2026-07-13・VM 層の多言語対応漏れ)

### 所見(maintainer 実機・en 切替)

第1回 fix(§7)後の golden で、**ツールバーのモードボタン(タグ編集/整理/作業/○○を終了)・表示軸セレクタの「ファイルシステム」・リスト列見出し(名前/サイズ/更新日)**が en 表示でも日本語固着。「ファイルシステムだけはシステム定義なので翻訳が必要」との指摘。

### 追加診断

§7 の是正は **XAML 直書き**を Loc バインドへ移したが、これらの文言は XAML でなく **VM 算出プロパティ側の直書き日本語**だった(§3 の疑い「VM 文言は T() 経由の可能性が高い」が誤り)。実測: 画像/作業タブの live VM 4 ファイル(ImageTabViewModel/WorkTabViewModel/ImageTabOrganizeViewModel/ImageTabTrashViewModel)+ 共有 POCO に **111 の日本語文字列リテラル**(モードトグルの三項・件数/確認の補間・軸名・列名・タグ種別ラベル・エラー/ステータス)。`ImageTabSeedViewModel` クラス本体はテスト専用(本番未使用)のため対象外。同一 ECO の多言語対応漏れ=GF として是正を延長(R3 分離起票不要)。

### 是正(GF-079-01)

- **サブ VM への localization 注入**: ImageTabOrganizeViewModel・ImageTabTrashViewModel に `LocalizationService` を DI(ImageTabViewModel から伝搬)+ `CultureChanged` 購読で `OnPropertyChanged(string.Empty)`(算出ラベルの一斉再評価)。
- **主要 VM の言語切替追随**: ImageTabViewModel・WorkTabViewModel の CultureChanged に `OnPropertyChanged(string.Empty)` を追加。WorkTabViewModel は静的 `BasicSortColumns`(列見出しの baked ラベル源)をインスタンス化し、`RebuildBasicSortColumns()`+`BuildSortModels()` を CultureChanged で再構築(静的 baked のままだと言語切替に非追随)。
- **文言の T() 化**: 111 リテラルを `_localization.T(key[, args])` へ。既存キー再利用 19(navigation.work/toolbar.tagEditExit/collection.fileSystem/common.name…)+ 新規 40(非パラメータ 28+書式テンプレート 12=`{count} 項目`等の補間は T の `{name}` 置換で)。`static BasicColLabel` はインスタンス化して T 化。`OrganizeResultVM`(POCO)の「条件一致」は criteriaLabel 引数化して VM から localized 値を注入。
- **転写ドリフト 0**: 束ねた全キーの ja 値=元の直書き文字列と完全一致(視覚不変)。

### プローブ(GF-079-01)

- **VM 層 lint 追加**(`CpI18n010XamlLintTests.タブVMに直書き日本語文言が残っていない`): live VM 4 ファイルにコメント除去後の直書き日本語リテラルが無いことを検査(是正前 111 件で赤=`scan_vm.py` 実測、是正後 0)。XAML lint と対で恒久ガード。
- **VM ラベル切替テスト**(`CpI18n010TabVmLabelTests`): 実 loc で両 VM を構築→ja「タグ編集/整理/作業/ファイルシステム」→en「Tag Edit/Organize/Work/File System」、作業タブ列見出し ja「名前/更新日」⇔en「Name/Modified Date」を end-to-end 固定。

### 機械受入(GF-079-01・全緑)

- build 0/0・Tests **664/664**(§7 の 658+新規プローブ/ラベル 6)・Oracle 109+2skip(R6 不変)・validate_bom 0/0。
- テスト追随=OrganizeResultVM/ImageTabOrganizeViewModel の新引数へ既存テスト2ファイルを更新。並列フル run の既存フレーク(CpUiRepairViewModelTests/CpWorkspace028=Dapper Int64→Int32)は isolation 緑・exe フル run で 664/664 確定(51-cheat-log 記録・R3・本変更と無関係)。

**gate② golden 再提示**(§7 基準 + 本 GF の観点=ツールバーのモードボタン・軸「ファイルシステム」・リスト列見出しが ja⇔en で切替)。

## §9 クローズ(2026-07-13 gate② golden 合格)

### 実機確認内容(maintainer・2026-07-13)

言語切替(ja⇔en)で画像タブ・作業タブの**全文言が追随**することを承認: コレクション/作業スペース見出し・ツールバー・タグ編集/整理/作業/削除の各モード入口と「○○を終了」・表示軸「ファイルシステム」(システム定義=翻訳対象、ユーザー定義ビュー名は非翻訳)・リスト列見出し(名前/サイズ/更新日)・整理トレイ(マージ実行/停止/一致度/件数)・ゴミ箱・タグ追加(種別ラベル/候補ヒント)・件数「N 項目/N 枚」。ja 復帰時の視覚不変、en 表示のはみ出し/切れも確認。第1回 fix(§7=XAML のみ)で残った VM 算出プロパティの直書き(§8=GF-079-01)を是正して再 golden 合格。

### 再発防止の所在

- **CP-UI-G1**(画像/作業タブの golden オラクル)characteristic へ ECO-079 節を追記=「言語切替 ja⇔en で全文言が追随」+**golden は ja/en 双方で実施**(単一言語 golden は i18n 未配線をマスキングする)。tolerance/oracle にも言語切替を明記。
- **機械 pin**: `CpI18n010XamlLintTests`(両 axaml の非コメント直書き JP=0)+タブ VM lint(live VM 4 ファイルの直書き JP リテラル=0)+`CpI18n010TabVmLabelTests`(実 loc で ja⇔en 切替=ラベル英語化)+`CpI18n010TabKeysTests`(束ねキーの ja/en 解決)。XAML と VM の両層を恒久ガード。

### 教訓

1. **i18n 未配線は「文言の生成層」を横断して棚卸しする**。UI 文言は XAML 直書き・VM 算出プロパティ・共有 POCO・システム定義名(軸/列)の 4 層に散在し得る。XAML だけを検査/是正すると VM 層が残る(本 ECO=第1回 fix が XAML のみで GF-079-01 を招いた実績)。再発防止の lint も**層ごとに**用意する(XAML lint + VM lint)。これは ECO-024 の「原典撤去は依存を全工程で棚卸し」の i18n 版。
2. **既定ロケール+単一言語 golden は i18n 欠陥をマスキングする**。既定 ja で通常運用は日本語=直書きと i18n 化文言が区別不能、歴代 golden が全て ja 実施で言語切替を実測しなかった(golden の谷間)。**golden は両言語で実施する**を CP へ命題化した。これは「検査の暗黙前提は selftest の陽性対照として持つ」(ECO-078 教訓)の golden 版=**golden の暗黙前提(単一言語)を明示条件へ昇格**。
3. **sibling の健全さがマスキング要因になる**。TagsTabView が i18n 化済みだったため「i18n 基盤は機能している」に見え、ImageTabView/WorkTabView の未配線が見逃された。同型 surface 群では**1 面の健全は他面の健全を含意しない**=read-across 監査の対象(ECO-058/062 の WorkTab read-across 漏れと同族)。

### M4 同期の要否

**不要**。CAD/mock は不変(視覚不変)・surface 新設や挙動仕様変更なし・DB スキーマ不変。文言配線(実装層)+ i18n 資産(K-BOM 相当だが V1 由来語彙の再利用が主)+ CP 観点追記のみ。REQ-050/051・OC-8・E-I18N-014 は既存契約の適用確認で新規宣言不要。
