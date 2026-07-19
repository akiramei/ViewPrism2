# ECO-112: 画像タブ ファイル操作モード(参照系・⋯入口・パスをコピー/場所を開く)の新設

- 起票日: 2026-07-19
- 報告者: maintainer
- baseline: main `c49286a`
- 種別: 新機能(CAD 2026-07-19 改版の取り込み・net-new surface 拡張)

## §1 要求

画像タブに**ファイル操作モード**を実装する。CAD 正典(乖離時は常に CAD が正):

- `../ViewPrismUI/docs/screens/image_tab.md`「ファイル操作モード(2026-07-19)」節(行 409-455)
  +視覚契約 VC-IMG-11〜13(行 231-233)
- 視覚原器: `../ViewPrismUI/docs/screens/captures/image_tab/` の 5 面
  (MENU-fileops / TB-fileops-{none,single,multi} / full-fileops — 実在確認済み。実装後は R7 並置突合)
- 一次資料: `../ViewPrismUI/資料/画像タブ/ViewPrism2 画像タブ.dc.html`
  (2026-07-19 納品版・SHA-256 `981D2071…82E112B0`・fileOpsMode 実装済み)

契約の要点(CAD 本文より):

1. 入口=ツールバー「⋯」メニューの「ファイル操作」。開始で他モードを解除・選択クリア。
2. モード中のツールバーは既存のモード出し分け契約どおり(他モード入口+「⋯」非表示・
   表示軸/ソート/グリッド・リスト+「ファイル操作を終了」のみ)。**右ペインは開かない**。
3. 選択系モード共通の画像選択(クリック/Ctrl/Shift・チェック+青リング・**番号バッジなし**・
   フォルダ選択不可・IMG-025 交差規則適用)。
4. ボタン出し分け(VC-IMG-12): 0 件=「終了」のみ/1 件=「パスをコピー」(バッジ 1)+
   「ファイルの場所を開く」/2 件以上=「パスをコピー」(バッジ=N)のみ。
5. パスをコピー=選択画像の実ファイル絶対パスをクリップボードへ、1 行 1 ファイル。
6. ファイルの場所を開く=1 件時のみ。親フォルダを OS ファイルマネージャで開き可能なら
   ファイルを選択状態に(Windows=`explorer /select` 相当・macOS=Finder reveal・
   Linux=同等、不可なら親フォルダを開くへフォールバック)。
7. 終了で選択解除して通常閲覧へ。離脱・実行ボタンのラベルは狭幅でも畳まない(ECO-027 契約③)。

**mock を鵜呑みにしない箇所**(CAD の MOCK 差分注記・VP-UI-005/006):
モード中の「タグ編集」「⋯」残存=未配線の簡略(出し分け契約が正)/ソート導線は旧固定式が
残存(file_list v2 モデルを退行させない)/「パスをコピー」「場所を開く」の挙動は mock 未配線
= CAD 本文が契約。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **健全** | 2026-07-19 改版で節+VC-IMG-11〜13+captures 5 面+MOCK 差分注記まで完備。骨格(入口・選択・コピー書式・reveal の OS 別挙動)は CAD 確定 |
| CAD 未確定(IMG-026) | **裁定要(gate①)** | `../ViewPrismUI/docs/review_points.md` IMG-026 の挙動残余 4 点(§4 に案を提示)。実装判断で固定してはならない旨が起票指示に明記 |
| BOM(30-ebom/20-spec) | **未宣言(net-new)** | REQ 該当なし(最新=REQ-098)・E-UI-MODE-041 のモード列挙は タグ編集/整理/作業/削除 の 4 つで停止・E-UI-BROWSE-022 の選択視覚 invariant は「チェック/選択順バッジ一貫適用」=番号バッジなしの本モードと未整合 → fix 時に BOM 拡張(§5) |
| 実装 | **net-new(欠陥ではない)** | `FileOps` 文字列は src 全体で 0 件(起票時 grep)。既存資産: 排他 4 モード構造(`ImageTabViewModel.cs:84-130` `_editMode/_organizeMode/_workMode/_deleteMode`)+選択機構(`InSelectMode:914`)+「⋯」メニュー(`ImageTabView.axaml:898-957`)。クリップボード/OS reveal の既存実装なし(`Clipboard|Process.Start` grep=WindowService の再起動用 1 件のみ) |

結論: **新機能の正規取り込み**。CAD は健全・IMG-026 の 4 点のみ gate① 裁定を要する。
上流(CAD)是正は不要。

## §3 切り分け済みの事実

### 3.1 確定(起票時実測)

- 既存の排他文脈モードは 4 つ(タグ編集・整理・作業・削除)。ファイル操作は**第 5 の排他
  選択系モード**で、構造上の最近傍同型は削除モード(⋯メニュー入口・右ペインなし・
  ツールバーに終了+実行系・ECO-018)。
- 選択機構は `InSelectMode`(edit/work/delete)へ fileops を加えて再利用可能。ただし現行の
  グリッド選択視覚は**チェック+選択順番号バッジ**を一貫適用(E-UI-BROWSE-022/ECO-017)
  しており、VC-IMG-13「番号バッジは出さない」とはモード別出し分けが必要(net-new フラグ)。
- 「⋯」メニュー現状(`ImageTabView.axaml:905` 幅 200)=(狭幅時 整理+区切り)→修復→削除→
  ゴミ箱→区切り→設定でバックアップ・転送。VC-IMG-11 の項目順は(狭幅時 整理+区切り)→
  **ファイル操作**→修復→削除→ゴミ箱→区切り→設定で…、幅 208px。挿入位置は先頭ブロック・
  幅は転写時に原器へ合わせる。
- IMG-025(交差規則)は ECO-097 で実装済み(`selection := selection ∩ visibleImages`)。
  fileops が `InSelectMode` に乗れば追加実装なしで適用される見込み。
- 画像の絶対パスは Core の collection root+relative_path から合成可能(既存 records 保有)。
  Core 変更は不要見込み。
- クリップボードは Avalonia TopLevel Clipboard API(View 側取得)、reveal は
  `System.Diagnostics.Process`(App 層)で実装可能=いずれも K-AVALONIA/.NET 既存 K-BOM の範囲。

### 3.2 疑い(未検証)

- ECO-027 レスポンシブ収納としきい値の相互作用(fileops ボタン群は他モードの終了/実行と
  同幅級のため既存契約①②③で吸収できる見込み — fix 時に狭幅プローブで確認)。
- リスト表示時の選択視覚(mock 実測=行ハイライトのみ)— IMG-026④ の裁定対象。

## §4 是正方針(実装方針+gate① 裁定対象)

### 4.1 実装骨格(CAD 確定分・裁定不要)

- `_fileOpsMode` を第 5 排他モードとして追加(突入=他モード解除+選択クリア+右ペイン
  閉・離脱=選択解除)。`InAnyMode`/`InSelectMode`/`ShowXxxEntry` 系へ合流。
- 選択視覚: チェック+青リングは既存選択視覚を再利用、**番号バッジのみモード別に抑止**
  (fileops 時 false のフラグを ImageItemVM へ供給)。フォルダ選択不可=既存どおり。
- ツールバー: 「ファイル操作を終了」(✕+ラベル・白地)+「パスをコピー」(青・件数バッジ)+
  「ファイルの場所を開く」(1 件時のみ)を VC-IMG-12 転写で追加。ラベルは狭幅でも畳まない。
- 「⋯」メニューへ「ファイル操作」行を VC-IMG-11 転写で追加(フォルダグリフ・先頭ブロック)。
- i18n: ja/en 両 JSON へ新キー追加(toolbar.fileOps 系・CP-I18N-010 3 層運用に従う)。
- パス合成・出し分け判定・コピー文字列生成は描画から独立した決定論ロジックとして unit 検査
  可能にする(既存モードの規律踏襲)。OS reveal は App 層サービスへ隔離(プラットフォーム分岐)。

### 4.2 gate① 裁定対象(IMG-026 ①〜④)— **裁定済み(2026-07-19・maintainer)**

**4 点とも推奨案どおり確定: 1-a / 2-b / 3-a / 4-a。**
CAD 先行反映済み= ViewPrismUI `1401c9c`(review_points.md IMG-026 決定化+image_tab.md
「ファイル操作モード」節へ ①コピー書式 ②ボタン内一時表示 ③Linux 検出詳細 ④リスト選択視覚 を刻印)。
以下は裁定時の選択肢記録。

**① コピー順・改行コード・末尾改行**

| 案 | 内容 | 根拠 |
| --- | --- | --- |
| **1-a(推奨)** | **表示順**(現在のソート適用後の一覧順)・改行=**OS ネイティブ**(`Environment.NewLine`=Windows CRLF/他 LF)・**末尾改行なし** | 表示順=ユーザーが見ている順で決定論的。本モードは選択順の番号バッジを出さない設計(VC-IMG-13)=選択順は可視化されず、IMG-025 交差で選択順は壊れうる。OS ネイティブ改行は貼り付け先(メモ帳/シェル)との親和性が最大。末尾改行なしは 1 件コピーを純粋なパス文字列にする |
| 1-b | 選択順・LF 固定・末尾改行あり | タグ編集の連番思想と揃うが、番号バッジ非表示の本モードでは順序が不可視=説明不能な順になる |

**② コピー/オープンの完了フィードバック**

| 案 | 内容 | 根拠 |
| --- | --- | --- |
| 2-a | 無表現(mock 準拠・最小) | mock は挙動未配線で無表現。ただしコピーは成否が見えない操作 |
| **2-b(推奨)** | 「パスをコピー」ボタン内の**一時表示**(ラベル/グリフが約 2 秒「コピーしました ✓」へ→自動復帰)。「場所を開く」は OS 窓が開くこと自体がフィードバック=無表現 | 新部品(トースト基盤)を作らず既存ボタン内で完結。ECO-106 の脆弱クラス(常駐メッセージ)に該当しない一過性表示 |
| 2-c | トースト新設 | 新規部品= CAD 未設計。本 ECO で作るのは過剰(要 CAD 先行) |

**③ Linux フォールバックの検出詳細**

| 案 | 内容 | 根拠 |
| --- | --- | --- |
| **3-a(推奨)** | **実装裁量で確定**: D-Bus `org.freedesktop.FileManager1.ShowItems` を試行→失敗(未提供/エラー)時に `xdg-open <親フォルダ>` へフォールバック。事前検出はしない(実行時失敗フォールバック) | CAD は「親フォルダを開くへフォールバック」まで確定済み。検出方式は環境依存の実装詳細で、失敗時挙動が CAD 契約を満たせば足りる。本製品の主戦場は Windows(explorer /select は確定的) |
| 3-b | 検出方式も CAD へ昇格(裁定文面を CAD 追記) | 移植性契約を厳密化したい場合 |

**④ リスト表示のファイル操作モード選択視覚**

| 案 | 内容 | 根拠 |
| --- | --- | --- |
| **4-a(推奨)** | **タグ編集モードと同型**(既存リスト行の選択視覚=行ハイライトを再利用・新規視覚なし) | mock 実測が行ハイライトのみ=既存と同型。差分を作る理由がない |
| 4-b | リスト行にもチェック列を新設 | mock に存在しない=CAD 先行が必要 |

### 4.3 プローブ(R5・fix 時先行)

- モード遷移: 突入で他モード解除+選択クリア+右ペイン閉/離脱で選択解除(是正前=モード不在で赤)。
- 出し分け: 選択 0/1/2 件でボタン可視状態が VC-IMG-12 の表どおり転移。
- コピー文字列: 裁定①の確定書式(順序・改行・末尾)を決定論 unit で固定。
- 番号バッジ: fileops 選択中はバッジ非表示・タグ編集では従来どおり表示(退行ガード)。
- R7: CaptureHarness へ fileops 撮影面(MENU/TB×3/full)を追加し原器 5 面と並置突合。

## §5 影響 BOM(見込み)

- **20-spec**: REQ-099 新設(ファイル操作モード=参照系・入口/選択/コピー/reveal/出し分け)。
- **30-ebom**: E-UI-MODE-041 invariant 追加(第 5 排他モード・⋯入口・出し分け・右ペインなし・
  ECO-027 ラベル維持則の適用)/E-UI-BROWSE-022 invariant 追記(選択順バッジのモード別抑止=
  fileops は チェック+青リングのみ)。OS reveal/クリップボードは App 層実装詳細として
  E-UI-MODE-041 配下に記す(新 E part 化は fix 時判断・K-BOM 追加なし見込み)。
- **src**: ImageTabViewModel(+モード状態/出し分け/コピー文字列生成)・ImageTabView.axaml
  (メニュー行+モード中ツールバー+選択視覚フラグ)・App 層 reveal サービス新設・
  Assets/i18n/{ja,en}.json 新キー。Core 変更なし見込み。
- **tests**: §4.3 プローブ+既存モード排他/IMG-025 の退行ガード。
- **CP**: CP-UI-G1 系へ観点刻印(accept 時)。R7= captures 並置(撮影ハーネス拡張)。

## §6 残ゲート

- ~~gate①(裁定)~~: **済(2026-07-19)**= 1-a/2-b/3-a/4-a 確定・CAD 反映済み(ViewPrismUI `1401c9c`)。
- **gate②(golden)**: 実装後、maintainer 実機で VC-IMG-11〜13+原器 5 面並置の視覚検収+
  コピー/場所を開くの実挙動確認。

## §7 実施記録(2026-07-19・/eco-fix)

### 7.1 プローブ先行(R5)と赤の実測

- 様式注記: ECO-084 の R5 様式(リフレクション解決)でなく **API 殻先行+挙動赤**を採用。理由=
  コピー/場所を開くは副作用系でフェイク(IFileOperationsService)の ctor 注入がプローブに必須のため。
  殻(状態フィールド+公開プロパティ+無配線コマンド・挙動ゼロ)のみ先行し、プローブ 11 本
  (挙動 8= CpUiG1FileOpsModeTests・視覚 3= GfFileOpsVisualParityTests・VC-IMG-11〜13 から先行生成
  =GF-073 様式)を実行して **11/11 不合格を実測**(既存 806 は全緑=殻の無影響も同時実証)→配線後 全緑転。
- 追加プローブ(R8 由来・7.4): フィードバック中 CanExecute(是正前赤=1/818)+タイマ満了自動復帰。

### 7.2 是正の構造

- 第 5 排他モード `_fileOpsMode`: 既存 4 モードの全入口に排他解除を追加(全数 grep で漏れ 0 を R8 確認)。
  InAnyMode/InSelectMode/ShowXxxEntry へ合流。右ペイン(ShowRightPane=edit/organize)は不変。
- 選択視覚: inSelect 再利用+**番号バッジのモード別抑止**(CreateImageItem/RefreshSelectionMarkers の
  両経路で order=null+isPlainCheck)。ImageItemVM に IsPlainCheck/ShowPlainCheck(白✓)を追加。
  リスト行は既存 imageListRow.selected がそのまま適用(裁定④=追加実装なしで成立)。
- コピー: 表示順(AllLoadedImagesInContext)+Environment.NewLine+末尾改行なし(裁定①)。
  フィードバック(裁定②)は StartCopyFeedback= fire-and-forget タイマ+**解除遷移の全列挙**
  (ECO-104 教訓)= タイマ/モード離脱(ExitFileOps)/選択・マーカー変化(RefreshSelectionMarkers)/
  母集合・文脈再計算(Recompute 先頭)。ラベルは表示時解決(ECO-106 様式・CopyPathsLabel)。
- OS 連携: IFileOperationsService 新設(App 層)。Windows=explorer /select・macOS=open -R・
  Linux=D-Bus ShowItems 試行→xdg-open 親フォルダ(裁定③)、Linux 経路は Task.Run で UI 非ブロック。
  クリップボードは Avalonia 12 ClipboardExtensions.SetTextAsync(IClipboard 拡張=K-AVALONIA 型解決規律で確認)。
- i18n: 新キー 5(toolbar.fileOps/fileOpsExit/copyPaths/copyPathsDone/openFileLocation)を ja/en 同時追加。
  lint 3 次元(重複/未使用/解決タイミング)全緑=CopyPathsLabel は算出プロパティで脆弱クラス非該当。

### 7.3 機械受入(2026-07-19・全緑)

- dotnet build: 0 error。`--no-incremental` フルビルドで警告 0(K-AVALONIA/ECO-111 規律)。
- dotnet test Tests: **819/819**(既存 806+新プローブ 13)。Oracle: 109+2skip(R6=既存固定行変更なし)。
- validate_bom: 0 error / 0 warning。

### 7.4 セルフレビュー(R8)+処置

fresh context の独立レビュー(diff 1210 行・全観点)を実施。所見全数と処置:

| 所見 | 分類 | 処置 |
| --- | --- | --- |
| 2-1 タイマがコマンド実行を占有→表示中 2 秒ボタン disabled+文言グレー化 | スコープ内欠陥 | プローブ先行(CanExecute 赤)→タイマを fire-and-forget へ分離。撮影面 impl-fileops-tb-copied.png で青塗り白文字維持を視覚実測 |
| 2-3 コピー側のみ例外無防備(reveal と非対称) | スコープ内(軽微) | try/catch(失敗時はフィードバックも出さない=成功偽装回避) |
| 4-3 Linux reveal の UI スレッド 3 秒ブロック | スコープ内(軽微) | Task.Run へ分離 |
| 4-2 dbus array:string のカンマ分割 | スコープ内(軽微) | URI の "," を %2C エスケープ(fail-safe は元々あり) |
| 7-1 タイマ満了経路が未検査 | スコープ内(検査) | CopyFeedbackDuration プロパティ化(SavedToastDuration 様式)+満了テスト追加 |
| 2-4 満了経路で CTS 残置 | 記録のみ | 実害なし(次回 Copy/Clear で解放)。コメントで根拠残置 |
| 6-2 メニュー行高の面内差(fileops=42 / 既存行≒37) | golden 送り | VC-IMG-11③ は fileops 行のみ 42 を契約。既存行は ECO-018/077 golden 済み面=本 ECO で触らない。並置所見として §7.5 に列挙 |
| 3-4 WorkTab ⋯メニュー幅 200 の面間非対称 | R3(スコープ外) | 51-cheat-log 記帳(2026-07-19)。WorkTab CAD に幅改版なし=現状適法 |
| 排他遷移/バッジ抑止/出し分け/ctor・シグネチャ退行/i18n/XAML 実在 | 問題なし確認 | 全数確認済み(レビュー記録) |

未処置のスコープ内所見= **0**。

### 7.5 セルフゴールデン(R7・原器 5 面並置)

撮影= tools/ViewPrism2.CaptureHarness へ CaptureFileOpsAsync 追加(ECO-109 FL 撮影と同型・
シードはファイル実体なし=クローム比較目的・CP-UI-G6 許容)。出力 6 面(impl-fileops-{menu,tb-none,
tb-single,tb-copied,tb-multi,full}.png)を原器 5 面と並置突合。

**転写完了(一致)**: メニュー項目順(ファイル操作→修復→削除→ゴミ箱→区切り→設定行)・幅 208・
フォルダグリフ+13.5/500・行高 42 / TB 出し分け 0/1/2 件(終了=✕+白地/コピー=青 #2F6BED 塗り+
コピーグリフ+白地バッジ N/場所を開く=白地+フォルダグリフ)/ 選択視覚(青塗りチェック+白✓・
番号なし・青リング+ハロー)/ 右ペインなし。

**差分の全列挙と分類**:
1. mock 残存「タグ編集」「⋯」(TB 3 面+full)= CAD MOCK 差分注記の未配線簡略→**実装が正(裁定済み)**。
2. mock 旧固定ソート(名前/↑)= VP-UI-006・file_list v2 が正→**裁定済み**。
3. 設定でバックアップ・転送行の淡紫ハイライト+1 行省略(mock=プレーン 2 行折返し)=
   **ECO-077 実機 golden 済みの既存様式**(本 ECO 非対象)。
4. メニューアイコンの塗り/輪郭(Material filled vs mock outline)= **K-DESIGN filled 規約
   (ECO-009)の歴代 golden 済み同型**(修復/削除/ゴミ箱行と同輩)。
5. コピー件数バッジの角丸(mock≈円形 / 実装= radius6 = 削除の delMoveBadge と同型・アプリ内一貫)=
   **golden 判断へ送付**(VC-IMG-12 は「白地バッジに青文字」までを契約・形状は未規定)。
6. メニュー行高の面内差(fileops=42・既存行≒37)= **golden 判断へ送付**(R8 所見 6-2)。
7. full 面の背景差(実サムネなし・シード差・ウィンドウクローム)= **CP-UI-G6 許容(ECO-109 前例)**。

転写漏れ= **0**。フィードバック面(原器なし=mock 挙動未配線)は impl-fileops-tb-copied.png を
golden 材料として添付。

## §8 クローズ(2026-07-19 golden 合格)

- **golden**: approved(2026-07-19 maintainer 実機・基準 7 点)。①⋯入口(メニュー閉+選択クリア+
  右ペインなし)②出し分け 0/1/2 件=原器並置一致 ③選択視覚(白✓+青リング・番号なし・フォルダ潜り・
  リスト行同型・タグ編集の連番退行なし)④コピー=表示順/1 行 1 パス/末尾なし+ボタン内フィードバック
  (グレー化なし)⑤場所を開く= explorer /select ⑥終了/排他/狭幅ラベル維持 ⑦**裁定送り 2 件
  (コピー件数バッジ角丸 radius6 / ⋯メニュー行高の面内差)も許容承認**。
- **再発防止**: CP-UI-G1 へ ECO-112 観点を刻印(モード契約+R8 先取りの潜伏実績=タイマのコマンド占有
  による :disabled 化+許容差分 2 件=以後の golden で差分扱いしない)。機械側= CpUiG1FileOpsModeTests
  (10)+GfFileOpsVisualParityTests(VC-IMG-11〜13)で pin。
- **M4 同期**: 20-spec §2.6= REQ-099 新設(ファイル操作モード逐条+⋯メニュー 4 項目化+右パネル/
  モード列挙へ合流)/30-ebom= E-UI-MODE-041(第 5 モード invariant+REQ-099)+E-UI-BROWSE-022
  (選択視覚のモード別出し分け invariant)/32-mbom= M-UI-IMAGETAB-035 mode_dispatch へ合流/
  33-control-plan= CP-UI-G1 刻印。
- **残課題(R3 送付済み)**: WorkTab ⋯メニュー幅 200 の面間非対称(51-cheat-log 2026-07-19・
  work_tab CAD 改版時に read-across 突合)。

### 教訓

1. **R8(独立レビュー)は R7(自己並置)の谷間を埋める**: フィードバック中の :disabled 化は、実装者
   コンテキストでは「コピーしました ✓ が出る」ことだけを見て撮影面にも含めておらず(R7 の谷間)、
   fresh context のレビューが CommunityToolkit の並行実行禁止仕様から先取りした。UI の一時状態
   (トースト/フィードバック/attention)は**撮影面の列挙対象**に含めること — ECO-103 の
   SavedToastDuration 固定表示様式(撮影用タイマ差し替え)が横展開できた。既存教訓
   「検査の谷間は返却値にもある」(ECO-109)の隣接形=**検査の谷間は一時状態にもある**。
2. **解除遷移の全列挙(ECO-104 教訓)は新設フィードバックにも最初から適用できる**: タイマ以外の
   解除遷移(モード離脱/選択変化/母集合再計算)を設計時に列挙したため、言語切替・スキャン更新との
   干渉を後追い所見なしで通過。教訓の read-across が予防として機能した実例(N=1→適用実績)。
3. **net-new の R5 は「API 殻先行+挙動赤」が副作用系で有効**: リフレクション様式(ECO-056/084)は
   フェイク注入が要る副作用系(クリップボード/OS 起動)には適さない。殻(挙動ゼロ)先行→ 11/11 赤
   →配線→緑転で、赤の実測と型安全な恒久プローブを両立した。様式の選択基準=**プローブが外部効果の
   観測を要するなら殻先行、状態遷移のみならリフレクション**。
