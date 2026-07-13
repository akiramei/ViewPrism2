# ECO-079 — 画像タブ・作業タブの多言語対応漏れ(直書き文言が言語切替に非追随)

- status: staged
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

- **gate① 裁定: 不要**(実装逸脱と確定・CAD 健全・視覚不変)。`/eco-fix eco-079` で是正着手可。
- **gate② golden**: 言語切替(ja⇔en)で画像タブ・作業タブの全文言が切り替わることの maintainer 実機確認。R7 セルフゴールデン=日本語 mock(CAD captures)との並置に加え、**en 表示時のはみ出し/切れ**を確認(英字は日本語より長くなり得るためレイアウト観点も含める)。
