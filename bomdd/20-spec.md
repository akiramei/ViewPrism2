# 仕様書 — ViewPrism2 (loop-v1-core)

> 製造パッケージに含まれる(製造装置が読む)。REQ への双方向トレースを保つ。
> 原典: view-prism(TypeScript/Electron)のリバース仕様。原典ソースは工場非開示のため、本書が機能記述の唯一の典拠。

## 1. 概要と用語

ViewPrism2 は Windows 向けデスクトップ画像管理アプリケーション(.NET 10 / Avalonia UI 12 / SQLite)。
中核コンセプトは「タグ × 仮想ビュー」: 物理フォルダ階層に依存せず、タグ付けした画像を
ユーザー定義の仮想階層(ビュー)でフォルダのように辿る。

| 用語 | 定義 |
|---|---|
| 同期フォルダ | ユーザーが登録する物理フォルダ。スキャン対象の根。原典では「コレクション」とも呼ぶ |
| 画像(image) | 同期フォルダ配下で発見された画像ファイルの DB レコード。実体はファイルシステム上に残る(取り込みコピーはしない) |
| ステータス | 画像の状態: normal(正常)/missing(物理消失)/deleted(論理削除)/pending(再リンク候補確定待ち) |
| タグ | 画像に付与する分類子。simple(値なし)/textual(文字列値)/numeric(数値)の 3 種 |
| ビュー | 絞り込み条件・ソート・表示列・タグ階層を束ねた保存可能な仮想フォルダ定義 |
| タグ階層(hierarchy) | ビュー内に定義する木構造。各ノードがタグ(+値条件)を参照する |
| NodeGraph | タグ階層と実際のタグ付け状態から構築される、ユーザーが辿る展開済みノード木 |
| 再リンク | missing 画像と新発見ファイル(pending)を同一物として結合し、タグ等の関連を保持する操作 |

## 2. 機能仕様

### 2.0 論理データモデル (REQ-001, REQ-002, REQ-005)

エンティティと関係(物理スキーマは E-BOM/M-BOM で確定。カラム名は原典準拠):

- **sync_folders** 1—n **images**(フォルダ削除で画像も削除)
- **images** n—n **tags**(中間 **image_tags**、PK=(image_id, tag_id)、value 列に値)
- **tags** は自己参照 parent_id(単一親階層)。type 別設定として textual_tag_settings(predefined_values JSON 配列)、numeric_tag_settings(min/max/step/unit)
- **views** 1—n **view_conditions**(絞り込み条件)、1—n **view_tag_hierarchies**(階層ノード、自己参照 parent_id)
- ID は全エンティティ UUIDv4 小文字(REQ-001)。日時は ISO 8601 UTC 文字列 `YYYY-MM-DDTHH:mm:ss.fffZ`(REQ-002)
- 参照整合性は FK で固定: images.sync_folder_id → CASCADE / image_tags 両 FK → CASCADE /
  view_conditions.view_id → CASCADE, .tag_id → SET NULL / view_tag_hierarchies.view_id, .tag_id → CASCADE,
  .parent_id(自己参照)→ SET NULL / tags.parent_id → SET NULL
- 核/表面: core
- 受入観点: スキーマ検査(L2: PRAGMA/information クエリで FK・既定値・索引を照合)

### 2.1 同期フォルダとスキャン (REQ-010〜REQ-017)

**登録・管理 (REQ-010)**
- フィールド: name(表示名)、path(絶対パス)、is_active(既定 true)、include_subfolders(既定 true)、
  exclude_patterns(文字列配列、ファイル名の完全一致・大文字小文字無視で除外。例 `Thumbs.db`。glob/regex は対象外)、
  last_scan(NULL 初期)
- path はシステム内で一意(大文字小文字無視)。重複登録は明示エラーで拒否。name の重複は許可
- is_active=false のフォルダはスキャン・表示の対象外
- 削除時は配下 images が連鎖削除される(確認ダイアログ必須 — タグ関連も消えるため)

**スキャン (REQ-011〜015)**
1. path 配下を列挙(include_subfolders=true なら再帰)。拡張子(小文字化)∈ {.jpg,.jpeg,.png,.gif,.bmp,.webp} のみ対象 (REQ-011)
2. 各ファイルの相対パス(スラッシュ統一、先頭末尾スラッシュなし、比較は case-insensitive)を算出 (REQ-014)
3. 各ファイルを以下の優先順で判定する(最初に成立した規則のみ適用) (REQ-012):
   - (1) DB に同一相対パスの行があり、file_size と modified_date がともに一致 → 変更なし(スキップ)
   - (2) DB に同一相対パスの行があり、file_size または modified_date が異なる → SHA-256 再計算し
     hash/file_size/modified_date を更新(status は変更しない)
   - (3) DB に同一相対パスの行がない(新規) → SHA-256 計算:
     - (3a) 初回スキャン(last_scan が NULL)でない、かつ同一フォルダ内に同ハッシュ・status=missing の行が
       存在する → status=pending、candidate_link_id=その missing 行の id(複数一致時は id 昇順の先頭)で登録
     - (3b) それ以外 → status=normal で登録
     (created_date/modified_date はファイルのタイムスタンプの ISO 8601 UTC 変換)
   - 読み取り不能ファイル(ロック・アクセス拒否等)は DB を変更せずスキップし警告ログ。次回スキャンで再試行
4. DB 上 status=normal で物理ファイルが存在しない行 → status=missing へ更新
5. 物理ファイルが存在しない status=pending の行 → 行を削除(候補が消えたため)
6. 完了時(例外時も)last_scan を現在時刻に更新し、サマリ {added, missing, pending, updated, skipped} 件数を返す (REQ-015)

**ステータス遷移の全列挙 (REQ-016)** — 以下以外の遷移は存在しない:
| 遷移 | 契機 |
|---|---|
| (新規) → normal | スキャン規則 (3b) |
| (新規) → pending | スキャン規則 (3a) |
| normal → missing | スキャン規則 4(物理消失) |
| missing → normal | 再リンク確定(REQ-017。id 不変、パス情報を上書き) |
| pending → (行削除) | スキャン規則 5(物理消失)、または再リンク確定時の pending 側 |
| deleted | V1 では予約値。deleted へ遷移させる操作・deleted からの遷移は実装しない |

missing → pending の直接遷移は存在しない(pending は新規発見ファイル側にのみ付く)。
スキャンの二重起動は禁止(実行中は同一フォルダの再スキャン要求を拒否)。

**ハッシュ (REQ-013)**: SHA-256、ファイル全体、小文字 hex 64 文字。

**再リンク (REQ-017)**
- missing 画像を選択 → 同一 sync_folder 内・同ハッシュの pending 画像を候補として提示。
  複数候補は relative_path 昇順で列挙し、各候補に相対パス・ファイルサイズ・更新日時を表示する
- 確定前に確認(タグ・ノートが missing 側の画像として引き継がれる旨)を表示する
- 確定: missing 行に pending 行の relative_path/file_name/file_size/modified_date/created_date/hash を上書きし
  status=normal、candidate_link_id=NULL へ。pending 行は削除。**missing 側の image_id は不変**(タグ・関連保持が目的)
- 核/表面: core
- 受入観点: 遷移 4 ケース+再リンクを固定フィクスチャ(実ファイル+DB)で unit〜L2 exact 検査

### 2.2 タグシステム (REQ-020〜REQ-029)

- 3 種類の意味論 (REQ-020): simple は付いている/いないのみ(value=NULL)。textual は value に文字列。
  numeric は value に数値の不変文字列表現(InvariantCulture、比較は数値として行う)
- タグ名はシステム全体で一意・case-sensitive・空白のみは拒否 (REQ-021)。重複は DuplicateTagName エラー
- 階層 (REQ-022): 単一親。親削除→子の parent_id=NULL。循環(自己・子孫を親に指定)は拒否
- color は `^#[0-9A-Fa-f]{6}$` のみ受理、NULL 可 (REQ-023)。description は自由記述、NULL 可
- textual の predefined_values は順序保持の文字列配列。付与値はリスト外も許可(入力補助のみ) (REQ-024)
- numeric の min/max/step/unit は NULL 可。min/max 設定時は範囲外の付与を拒否(両端は含む) (REQ-025)
- 付与は UPSERT(再付与=値上書き、行は増えない) (REQ-026)。解除は行削除(冪等: 無い行の解除はエラーにしない)
- 一括付与・解除は単一トランザクション、失敗時全ロールバック (REQ-027)
- 削除カスケード (REQ-028): §2.0 の FK 規則どおり
- 一覧は name 昇順(序数比較)、使用数 = COUNT(DISTINCT image_id) (REQ-029)
- 核/表面: core(タグ編集 UI・色見本の描画のみ surface)
- 受入観点: 全規則 unit(exact、境界値ベクタ付き)。UI は golden+承認者

### 2.3 仮想ビュー (REQ-030〜REQ-033, REQ-038)

**CRUD (REQ-030)**: §2.0 のフィールド。name 必須(空白のみ拒否)。削除は条件・階層を連鎖削除。

**条件評価 (REQ-031)**: ビューの全条件を AND 結合し、status=normal の画像集合を絞り込む。
| operator | 対象 | 意味 | value / value2 |
|---|---|---|---|
| exists | 全 type | 当該タグが付与されている | 不使用 |
| equals | textual/numeric | value が完全一致(numeric は数値比較) | value=比較値 |
| between | numeric | value ≦ タグ値 ≦ value2(両端含む、数値比較) | value=下限, value2=上限 |
| regexp | textual | タグ値が正規表現にマッチ(.NET Regex、部分一致) | value=パターン |
| in | textual | タグ値が JSON 配列のいずれかと一致 | value=JSON 配列文字列 |

エッジケースの確定規則(いずれも例外でクラッシュしない):
- 対象タグが付与されていない画像 → いずれの演算子でも条件不成立(exists は false)。AND 結合のため当該画像は結果に含まれない
- simple タグ(value=NULL)への equals/between/regexp/in → 不成立。UI は simple タグに exists のみ提示する
- 数値変換できないタグ値(空文字含む) → equals/between の数値比較で不成立
- operator≠exists で value が NULL の条件 → その条件を評価から除外(無視)し警告ログ
- 不正な正規表現パターン・マッチタイムアウト(1 秒) → 条件不成立+警告(ログ+非モーダル通知)
- 実装方式(SQL/インメモリ)は自由。観測契約は §2.8 OC-1

**整列 (REQ-038)**: sort_field ∈ {name, created_date, modified_date, file_size}(既定 name)、
sort_direction ∈ {asc, desc}(既定 asc)。name は大文字小文字を無視した序数比較(OrdinalIgnoreCase 相当)。
二次キーは id 昇順(同値時も実行ごとに並びが変わらない安定ソート)。

**modified_at 規則 (REQ-032)**: ビュー本体・条件・階層のいずれの変更でも更新。閲覧では更新しない。

**一覧 (REQ-033)**: お気に入り=is_favorite=true を name 昇順。最近=modified_at 降順 limit 件(既定 10)。
いずれも同値時は id 昇順。
- 核/表面: core
- 受入観点: 演算子×型×境界値のテストベクタで unit(exact)。

### 2.4 タグ階層と NodeGraph (REQ-034〜REQ-037)

**階層定義 (REQ-034)**: ノード = {id, view_id, tag_id 必須, parent_id(NULL=ルート直下), position(同一親内 0 起点昇順),
alias(NULL なら tag.name を表示), condition_type ∈ {equals, range, pattern, values, NULL}, condition_value(JSON)}。
condition_value スキーマ: equals=`{"value":…}` / range=`{"valueFrom":…,"valueTo":…}` / pattern=`{"pattern":…}` /
values=`{"values":[…]}`。ノード移動で自己・子孫を親に指定→拒否。

**NodeGraph 構築 (REQ-035)**: 階層ノードを position 順に走査し、タグ type 別に展開する。
値の抽出対象は常に **status=normal の画像**のみ(REQ-016、INV-010):
- simple/numeric タグ → 1 ノード(配下に階層上の子ノードを接続)
- textual タグ → タグ名ノードの配下に「値ノード」を生成。値 = 当該タグが status=normal の画像に付与された
  distinct 値(predefined_values 外の自由入力値も含む。ノードに condition_type があればその条件で値を制限)、
  序数昇順
  - distinct 値が 0 件 → タグ名ノードのみ(値ノードなし)。ノードは表示・選択可能で、選択時は exists 条件と
    して評価される(結果 0 件なら空状態表示)
  - distinct 値が 1 件 → タグ名ノードと値ノードを統合した一体型ノード(表示名「タグ名: 値」)とし、
    階層上の子ノードは一体型ノードの配下に接続する
  - 2 件以上 → 値ノード群を生成し、階層上の子ノードは**各値ノード**の配下に接続する

NodeGraph は表示のたびにタグ付け状態から再構築する(キャッシュする場合はタグ付け・階層変更で無効化)。
タグ付けにより distinct 値数が 0↔1↔2 と変化した場合、次回構築で構造が変わる(選択状態は解決できなければルートへ)。

**絞り込み (REQ-036)**: 選択ノードまでのパス上の全ノードから条件列を生成し AND 評価(§2.3 の評価器を使用):
ルート=無条件 / simple・textual タグ名ノード=exists / 値ノード・一体型ノード=equals(その値) /
numeric ノード=condition_type に応じ equals または between(range の場合 valueFrom/valueTo)。

**ホームタグ (REQ-037)**: ビューを開いたとき home_tag_id(階層ノード id)が解決できれば該当ノードを初期選択。
解決不能(削除済み等)ならルート。エラーにしない。
- 核/表面: core(木構造の描画のみ surface)
- 受入観点: フィクスチャ(階層+タグ付け)→期待ノード木・期待画像集合の unit exact。値 0/1/2 件の境界必須

### 2.5 サムネイルと画像表示 (REQ-040, REQ-045)

- 生成 (REQ-040): 長辺 256px へ縮小(アスペクト比保持・拡大しない)。PNG 入力→PNG 出力、その他→JPEG 品質 80。
  保存先 `%APPDATA%/ViewPrism2/thumbnails/{MD5(画像絶対パスの小文字)}.{png|jpg}`。存在すれば再生成しない。
  生成失敗(壊れた画像)はキャッシュへ記録せずプレースホルダ表示とし(次回表示時に再試行)、スキャン・一覧表示を
  停止させない。読み取り不能なキャッシュファイルは削除して再生成する
- フルサイズ表示キャッシュ (REQ-045): メモリ LRU、上限 50 枚、有効期限 3 分
- 核/表面: 生成パラメータ=surface(画像ライブラリ API へトレース)、キャッシュ規則=core
- 受入観点: 寸法・形式・キャッシュ動作は L2(出力ファイルのメタデータ検査)。LRU は unit(読込回数カウント)

### 2.6 シェルと画面構成 (REQ-041〜REQ-044, REQ-046) — v1.2 改訂(loop-v1 ずる裁定反映)

**シェル**: 上部タブナビゲーション「タグ」「画像」+右端に「設定」。原典の「作業」タブは V3 スコープ
のため V1 では設けない。

**タグタブ(3 ペイン)** — タグ・ビュー・階層の管理:
- 左「ビュー管理」: 新規ビュー作成ボタン、ビュー一覧(各行: 名前+編集/削除アイコン)。選択中のビューが
  中央の階層編集対象になる。ビュー作成/編集ダイアログ = 名前(必須)+説明
- 中央「階層構造」: 選択ビューのタグ階層ツリー。タグパレットから D&D(またはボタン)でノード追加。
  ノード操作: 展開/折畳、ホームタグ設定/解除(設定中ノードは強調表示)、別名のインライン編集、
  条件設定(textual/numeric のみ。condition_type/condition_value のダイアログ)、削除。
  **編集はメモリ内で行い「保存」で一括コミット**(REQ-032 の modified_at 更新は保存時に 1 回)、
  「キャンセル」は確認後に破棄。未保存変更がある間のみ保存/キャンセルを活性化
- 右「タグパレット」: 検索ボックス(名前の部分一致・大文字小文字無視)、「追加」ボタン→タグ作成ダイアログ、
  タグ一覧(色スウォッチ+名前+種類チップ+編集/削除)。
  タグ作成/編集ダイアログ: タグ名(必須)・種類(シンプル/テキスト/数値、作成後の種類変更は不可)・
  カラー(プリセット+hex 表示)・説明。テキスト型: 候補値の追加・削除・D&D 並べ替え。
  数値型: min/max/step/unit の入力

**画像タブ** — 閲覧とタグ付け:
- 左: 同期フォルダ(コレクション)一覧+スキャン実行、ビュー選択(お気に入り/最近)、選択ビューの
  NodeGraph ツリー、「全画像」固定入口(条件なし・全 normal 画像)。
  ※原典はフォルダ風表示+パンくずだが、本実装は左ペインのツリーで同等の AND パス意味論を提供する(意図的差分。
  golden で承認判定)
- ツールバー: グリッド/リスト切替、列数 3/4/5/6、ソート(sort_field×方向)、「タグ編集」モードトグル
- 中央: グリッド/リスト(下記)
- 右パネル: 通常時=詳細パネル(REQ-043)、タグ編集モード時=タグ付与パネル(REQ-046)に切替

各部品の規則:
- グリッド (REQ-041): 列数 3/4/5/6(既定 4)。正方形セル+ファイル名。クリック=単一選択、Ctrl+クリック=トグル
  (選択順の番号バッジ表示)、**ダブルクリック=ビューア起動(タグ編集モード中は無効)**。
  100 件以上で仮想化必須(目標: 1 万件でスクロール操作可能)
- リスト (REQ-042): display_columns(JSON 配列)に従う。列 = {type: basic|tag, key, label, width}。
  basic.key ∈ {name, size, modified_date}。tag.key=タグ id(削除済みタグの列は無視して描画し、残り列で全幅を埋める)。
  既定 3 列
- 詳細 (REQ-043): ファイル名/サイズ(1024 進、小数 1 桁、単位 B・KB・MB・GB)/解像度(px)/作成・更新日時
  (ロケール書式)/ノート(編集→images.notes へ保存)/タグ一覧(名前+値、numeric は unit 併記)
- タグ付与パネル (REQ-046): 「現在のタグ」(選択画像に付与済み。複数選択時は共通タグ。各行に解除×)と
  「タグを追加」(全タグ一覧+検索)の 2 区画。追加候補を選んで「適用」→選択画像全件へ一括付与(REQ-027 の
  原子バッチ)。textual は候補値ドロップダウン+自由入力、numeric は値入力ダイアログ
  (「全画像に同じ値」=固定値 / 「選択順に連番」=開始値+選択順 i)。範囲外値はダイアログ内で検証して拒否
- ビューア (REQ-044): 単一画像をウィンドウフィット表示(縮小のみ、拡大なし)。→/PageDown=次、←/PageUp=前、
  Esc/閉じるボタン=閉じる。並びは呼び出し元一覧の整列結果。端で停止(ループ・例外なし)。表示中はタイトル等に
  「現在位置/総数」を表示

空状態の規則(全画面共通):
- グリッド/リスト: 画像 0 件 → 中央に「画像がありません」相当のメッセージ(i18n キー)。何もない場所の
  ダブルクリックは無視
- 詳細パネル/タグ付与パネル: 選択 0 件 → 「画像を選択してください」相当のプレースホルダ
- NodeGraph: 選択ノードの結果 0 件 → グリッドの空状態と同じ
- タグタブ: ビュー未選択 → 階層ペインに「ビューを選択してください」/ 階層ノード 0 件 → 「まだタグが
  追加されていません」相当の案内

golden 判定基準(承認者 maintainer 向けチェックリスト — 合否は固定オラクルでなく承認で記録):
- G-1 シェル+グリッド: タブ構成(タグ/画像+設定)。指定列数で正方形セルが整列し、ファイル名が 1 行省略(…)表示。
  選択セルに強調枠+選択順バッジ(1 起点昇順)
- G-2 リスト: display_columns どおりの列構成・ヘッダ表示。行選択が機能
- G-3 詳細: §の項目が全て表示され、ノート編集が保存される
- G-4 ビューア: グリッドのダブルクリックで起動。画像がウィンドウに収まり(縮小のみ)、端で停止。「現在位置/総数」表示
- G-5 i18n: ja/en 切替で主要画面の文言が即時切替、生キー露出なし
- G-6 タグタブ: 3 ペイン構成。タグ作成ダイアログ(型別フォーム・候補値並べ替え・カラー hex)、ビュー作成、
  階層への追加・別名・条件・ホームタグ・削除、保存/キャンセルのバッチ動作
- G-7 タグ付与: タグ編集モードで右パネルが切替わり、現在タグ/追加/適用が機能。numeric の固定値/連番、
  textual の候補値選択が機能
- 核/表面: 選択・ナビゲーション・整形・適用対象計算ロジック=core(ViewModel 単体で観測可能にする)。描画=surface
- 受入観点: ロジックは unit exact(テストベクタ: 1023B→`1023 B`、1024B→`1.0 KB`、端での停止、連番 startValue+i)。
  画面は golden(G-1〜G-7)+承認者(ダークテーマ不要)

### 2.7 i18n と設定 (REQ-050〜REQ-052)

- 2 言語(ja 既定/en)。リソースはキー→文言の辞書。欠落キーは ja へフォールバック、それも無ければキー文字列を
  返す(例外を投げない) (REQ-050)。文言資産は原典 messages/ja.json・en.json から該当分を変換(K-BOM 資産)
- 言語切替は即時反映(再起動不要)+ settings.json へ永続化 (REQ-051)
- settings.json(%APPDATA%/ViewPrism2/): locale、ウィンドウ位置・サイズ・最大化状態、グリッド列数、
  最後に開いたビュー id。破損時は既定値で起動し、ファイルを再生成 (REQ-052)
- 核/表面: フォールバック・永続化=core。文言・書式=surface(翻訳資産へトレース)
- 受入観点: フォールバックと破損耐性は unit exact。表示言語は golden+承認者

### 2.8 観測契約(in-process 受入の入出力定義)

UI を介さず検査する部品の契約。シグネチャは論理形(言語上の型は E-BOM/M-BOM で確定)。

| ID | 部品 | 入力 | 出力 | 検査 |
|---|---|---|---|---|
| OC-1 | 条件評価器 (REQ-031) | 画像集合(各画像のタグ付け状態付き)+条件列 | 条件を満たす画像 id 集合 | 固定フィクスチャで exact(unit) |
| OC-2 | NodeGraph 構築器 (REQ-035) | 階層ノード列+タグ定義+タグ付け状態(status 込み) | ノード木(型・表示名・親子・順序・各ノードの条件) | 期待木と exact(unit) |
| OC-3 | パス→条件変換 (REQ-036) | 選択ノードまでのノード列 | 条件列(OC-1 の入力形式) | 期待条件列と exact(unit) |
| OC-4 | 整列器 (REQ-038) | 画像集合+sort_field+sort_direction | 整列済み id 列 | テストベクタ exact(unit) |
| OC-5 | スキャン判定器 (REQ-012) | ファイル情報(相対パス・サイズ・更新日時・ハッシュ遅延計算)+DB 状態 | 判定結果(skip/update/add-normal/add-pending+candidate)と適用後 DB 状態 | 遷移 4 ケース+境界を exact(unit〜L2) |
| OC-6 | 画像メモリキャッシュ (REQ-045) | 取得要求列 | 実ロード回数・保持集合 | LRU/期限/上限を exact(unit) |
| OC-7 | サイズ整形器 (REQ-043) | バイト数 | 表示文字列 | ベクタ exact(unit): 0→`0 B`、1023→`1023 B`、1024→`1.0 KB`、1048576→`1.0 MB` |
| OC-8 | i18n 解決器 (REQ-050) | キー+ロケール+リソース状態 | 文言(フォールバック適用後) | 欠落ケース exact(unit) |

## 3. 不変条件(M-BOM へ前倒し)

| ID | 不変条件 | 関係する REQ |
|---|---|---|
| INV-001 | エンティティ ID は生成後不変。再リンクでも missing 側 image_id を保持する(関連の保全)。削除時の参照破損は FK(CASCADE/SET NULL)で管理し、SET NULL 化された参照は読み取り側がフォールバックする | REQ-001, REQ-017, REQ-028 |
| INV-002 | 日時は常に UTC・ISO 8601(ミリ秒 3 桁・literal Z)。文字列ソート=時系列ソートが成立する。同値時の順序は id 昇順で安定させる | REQ-002, REQ-032, REQ-033 |
| INV-003 | (image_id, tag_id) は image_tags 内で高々 1 行(UPSERT) | REQ-026 |
| INV-004 | タグ階層・ビュー階層に循環が存在しない(自己・子孫への親付け替えは常に拒否) | REQ-022, REQ-034 |
| INV-005 | relative_path は正規形(スラッシュ区切り・先頭末尾スラッシュなし)でのみ格納。比較は case-insensitive | REQ-014 |
| INV-006 | 一括操作(バッチタグ付け等)は全適用か無適用(原子性) | REQ-027 |
| INV-007 | numeric タグ値の格納は InvariantCulture の不変文字列、比較は常に数値比較(辞書順比較を使わない) | REQ-020, REQ-031 |
| INV-008 | 参照切れ(削除済みタグ・ノード・ビュー)に遭遇した読み取り処理は例外で停止せず、無視またはフォールバックする | REQ-028, REQ-037, REQ-042 |
| INV-009 | ユーザーの画像ファイル本体に対して書き込み・移動・削除を一切行わない(読み取り専用) | REQ-010〜017 |
| INV-010 | 既定の画像一覧・ビュー評価・NodeGraph の値抽出は status=normal のみを対象とする。missing/deleted/pending のタグ付け状態は表示系に反映されない | REQ-016, REQ-031, REQ-035 |

## 4. 沈黙次元の第1回掃討(silence-checklist)

| 次元 | 宣言 | 内容/参照 |
|---|---|---|
| 日時表現 | specified | ISO 8601 UTC、ミリ秒 3 桁、literal Z のみ (REQ-002) |
| ID 形式 | specified | UUIDv4 小文字 36 文字 (REQ-001) |
| 文字列照合(タグ名一意性) | specified | case-sensitive・序数 (REQ-021) |
| 文字列照合(パス) | specified | case-insensitive (REQ-014) |
| 文字列ソート(名前順) | specified | 大文字小文字を無視した序数比較(OrdinalIgnoreCase)、同値時 id 昇順 (REQ-029, REQ-038) |
| 正規表現の安全性 | specified | マッチタイムアウト 1 秒。不正・タイムアウトは条件不成立+警告 (REQ-031) |
| 長いパス(MAX_PATH 超) | specified | .NET の長パスサポートを有効化。それでも読めないファイルはスキップ+警告ログ |
| シンボリックリンク/ジャンクション | specified | ディレクトリのリパースポイントは辿らない(無限ループ防止)。ファイルのリンクは通常ファイルとして扱う |
| Unicode パス正規化 | specified | OS が返したパス文字列をそのまま使用(NFC/NFD の変換は行わない) |
| ファイルサイズ表示上限 | specified | 単位は GB まで。1024GB 以上も GB 表示 |
| 数値の文化圏依存書式 | specified | 格納・比較は InvariantCulture (INV-007)。表示のみロケール書式 |
| 正規表現方言 | specified | .NET Regex。不正パターンは条件不成立+通知 (REQ-031) |
| エラー語彙(エラーコード体系) | deferred-to-phase3 | M-BOM で全列挙(DuplicateTagName 等は REQ に既出) |
| 並行実行(スキャン中の UI 操作) | deferred-to-phase3 | M-BOM で排他方針を確定(最低限: スキャン二重起動の禁止) |
| ファイルサイズ表示丸め | specified | 1024 進・小数 1 桁 (REQ-043) |
| サムネイルのディスクキャッシュ上限 | specified | 上限なし・自動削除なし(原典準拠)。手動クリアは V1 外 |
| ファイル更新時のサムネイル陳腐化 | specified | 許容する(キーはパスのみ依存、原典準拠の既知制約) (REQ-040) |
| 画像の EXIF 回転(Orientation) | specified | V1 では適用しない(原典も未適用)。後続ループ候補 |
| テーマ(ダークモード) | out-of-scope | 原典でも実装未確認。V1 はライトのみ |
| アクセシビリティ | exploratory | 探索プローブで観測のみ(合否に入れない) |
| 性能(大規模ライブラリ) | exploratory | 1 万画像での操作可能性を探索プローブで観測。固定オラクルに入れない |
| ログ・診断 | deferred-to-phase3 | M-BOM でログ層を定義 |
| マルチウィンドウ/多重起動 | specified | 多重起動は禁止(2 つ目は既存をアクティブ化)。単一ウィンドウ |
| DB 同時アクセス(プロセス内) | deferred-to-phase3 | M-BOM で接続戦略(単一接続/プール)を確定 |
| インストール/配布形態 | out-of-scope | V1 はビルド成果物の直接実行。インストーラは後続 |

## 5. トレース表

| REQ | 実現節 | 受入観点(深さ) | 原版証拠(設計者用・工場非開示) |
|---|---|---|---|
| REQ-001 | §2.0 | unit: ID pattern | image-repository.ts:742, tag-repository.ts:113 |
| REQ-002 | §2.0 | unit: 形式 exact | manager.ts:65,458 |
| REQ-003 | §2.0 | L2: PRAGMA 照会 | manager.ts:126-129 |
| REQ-004 | §2.0 | L2: スキーマ同値 | manager.ts:52-87,137-144 |
| REQ-005 | §2.0 | L1: ファイル存在 | main.ts:63-64 |
| REQ-010 | §2.1 | unit: CRUD+CASCADE | manager.ts:293-302,189 |
| REQ-011 | §2.1 | unit: 拡張子ベクタ | scanner.ts:10,522 |
| REQ-012 | §2.1 | unit〜L2: 遷移 4 ケース | scanner.ts:114-472, file-verifier.ts:201-217 |
| REQ-013 | §2.1 | unit: 既知ベクタ | file-verifier.ts:125-141 |
| REQ-014 | §2.1 | unit: 正規化ベクタ | file-verifier.ts:77-94 |
| REQ-015 | §2.1 | unit: last_scan+サマリ | scanner.ts:430-436,464-471 |
| REQ-016 | §2.1 | unit: 既定表示の絞り | shared/types/images.ts:7, image-repository.ts:50,107,162 |
| REQ-017 | §2.1 | unit: id 保持・関連保全 | file-verifier.ts:387-439, image-repository.ts:581-736 |
| REQ-020 | §2.2 | unit: 3 種ラウンドトリップ | shared/types/tags.ts:20-38 |
| REQ-021 | §2.2 | unit: 重複拒否 | migration009, tag-repository.ts:188-192 |
| REQ-022 | §2.2 | unit: SET NULL+循環拒否 | manager.ts:204(循環拒否は意図的差分) |
| REQ-023 | §2.2 | unit: 形式ベクタ | manager.ts:199-201, ColorPicker.tsx:17-71 |
| REQ-024 | §2.2 | unit: 順序保持 | manager.ts:207-212 |
| REQ-025 | §2.2 | unit: 境界ベクタ | manager.ts:214-222(範囲拒否は意図的差分) |
| REQ-026 | §2.2 | unit: UPSERT | tag-repository.ts:477-485 |
| REQ-027 | §2.2 | unit: 原子性 | tag-management.service.ts:160-200 |
| REQ-028 | §2.2 | unit: 4 テーブル状態 | manager.ts:204,231-232,261,277 |
| REQ-029 | §2.2 | unit: 並び+件数 | tag-repository.ts:252 |
| REQ-030 | §2.3 | unit: CRUD+CASCADE | manager.ts:236-262 |
| REQ-031 | §2.3 | unit: 演算子×境界ベクタ | view-search.service.ts:104-201 |
| REQ-032 | §2.3 | unit: 更新トリガ | view-repository.ts:307 |
| REQ-033 | §2.3 | unit: 並び+limit | view-repository.ts:40,492, ipc/views/index.ts:61 |
| REQ-034 | §2.4 | unit: ノード CRUD+循環拒否 | manager.ts:265-279, view-tag-hierarchy-repository.ts:526-563 |
| REQ-035 | §2.4 | unit: 期待ノード木(0/1/2 件境界) | nodeGraph/types.ts:8-15, utils.ts:20-219 |
| REQ-036 | §2.4 | unit〜L2: パス→条件→画像集合 | utils.ts:228-308 |
| REQ-037 | §2.4 | unit: 3 ケース(あり/なし/参照切れ) | useNodeGraph.ts:86-110, utils.ts:316-354 |
| REQ-038 | §2.3 | unit: フィールド×方向ベクタ | (候補値確定は意図的差分) |
| REQ-040 | §2.5 | L2: 寸法・形式・キャッシュ | ipc/images/thumbnails/index.ts:11-45 |
| REQ-041 | §2.6 | unit+golden+探索プローブ | ImageGrid.tsx:23,43-50, VirtualizedGrid.tsx |
| REQ-042 | §2.6 | unit+golden | ImageList.tsx:33-35,102-119 |
| REQ-043 | §2.6 | unit: 整形ベクタ+golden | ImageDetailModal.tsx:177-289 |
| REQ-044 | §2.6 | unit: ナビゲーション+golden | useViewerNavigation.ts:163-170(Esc・フィットは意図的差分) |
| REQ-046 | §2.6 | unit: 適用/連番ロジック+golden G-7 | TaggingPanel.tsx, NumericTagDialog.tsx:64-128(v1.2 追加・ずる裁定) |
| REQ-045 | §2.5 | unit: LRU 動作 | ProxyImage.tsx:8-34 |
| REQ-050 | §2.7 | unit: フォールバック+golden | i18n.config.ts:2-3, i18n/request.ts:39-45 |
| REQ-051 | §2.7 | unit+golden | language.ts:36-48(即時反映は意図的差分) |
| REQ-052 | §2.7 | unit: ラウンドトリップ+破損耐性 | language.ts:7, main.ts:109-110(拡張は意図的差分) |

## 6. カバレッジ監査(リバース工程 — 未トレース要素の分類)

原典に存在するが本ループの REQ にトレースされない要素の全数分類。

**out-of-scope(後続ループ/ECO で対応、チャーター準拠)**
| 原典要素 | 行き先 |
|---|---|
| 類似画像検索・重複検出(image_features/image_similarity、pHash/ORB) | Loop V4 |
| ワークスペース(workspaces/workspace_images) | Loop V3 |
| バックアップ/復元(16 IPC)・DB 修復・view_revisions | Loop V5 |
| 見開き・スクロール・ストーリービュー・タグ制御モード・ページ送りモード | Loop V2 |
| Undo/Redo | Loop V3 |
| 既存 viewprism.db からのデータ移行 | ECO(必要時) |

**ignored-with-reason(原典スキーマ・文書に存在するが実装未到達 — 本実装でも採用しない)**
| 原典要素 | 理由 |
|---|---|
| view_conditions.logic_operator(OR)・group_id・field 条件 | 原典で評価ロジック未実装。V1 は AND のみ(REQ-031)。スキーマ列も設けない(必要時に ECO) |
| ビューの export/import | 原典で IPC 未実装(仕様書記載のみ) |
| 組み込みビュー(「全画像」等) | 原典に実装なし。V1 は「全画像」相当を UI 上の固定入口として提供するか E-BOM で判断 |
| USN Journal / リアルタイムファイル監視 | 原典に実装なし(文書記載のみ)。手動スキャンのみ |
| EXIF 抽出(exifreader) | 原典で利用コードなし。詳細パネルの解像度はデコード時取得で代替 |
| ビューアのズーム・パン・回転・スライドショー | 原典に実装なし |
| テーマ切替(ダークモード) | 原典で実装未確認 |
| app_version テーブル | マイグレーション基盤(REQ-004)に統合し、独立要求にしない |

**unknown(原典の格納先・挙動が特定できず — S-BOM 登録し版更新時に再調査)**
| 原典要素 | 内容 | V1 での確定 |
|---|---|---|
| 画像詳細のレーティング(5 つ星) | UI に存在するが格納先カラム不明 | V1 除外。再調査後に ECO 判断 |
| images.status='deleted' への遷移操作 | 論理削除を起こす UI/IPC が特定できず | deleted は予約値とし遷移を実装しない(§2.1 遷移表で確定済み) |
| exclude_patterns のパターン形式(glob/regex) | 原典で解釈コード未確認 | ファイル名完全一致として確定済み(REQ-010)。原典挙動の再調査は不要 |

---
## ゲート記録(G2/G2')
- 実施日: 2026-06-11
- マルチリーダー監査: リーダー数 N=1(チャーター: 実用適用)
  - G2(第 1 回): 所見 22 件(blocker 5 / major 16 / minor 1)→ **通過不可**。
    主要欠陥: NodeGraph 値抽出の status 未規定、条件評価のエッジケース未規定、ステータス遷移の列挙漏れ、
    観測契約未定義、golden 判定基準なし
  - 補正: blocker 5 件・major 全件を本書 v1.1 に反映(§2.1 遷移表・判定優先順、§2.3 エッジケース規則、
    §2.4 status=normal 限定、§2.6 空状態+golden 基準 G-1〜G-5、§2.8 観測契約 OC-1〜OC-8、§4 追加 6 次元)
  - G2'(再監査): blocker 0 / major 0 / minor 3(AUDIT-101: NodeGraph 構造変化時の選択リセット詳細、
    AUDIT-102: 削除済みタグ列の幅配分、AUDIT-103: DB 接続戦略)→ **通過**。minor 3 件は E-BOM/M-BOM へ申し送り
- MeasurementCapability: 全 REQ が adequate へ到達可能(unmeasurable ゼロ)= **yes**
  - human-approval-required の REQ と承認者: REQ-041〜044, REQ-050〜051(golden 基準 G-1〜G-5)— 承認者 maintainer
  - 観測契約が必要な REQ(in-process): §2.8 OC-1〜OC-8 として定義済み
