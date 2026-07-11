# ECO-062 (applied) — 類似画像検索を閲覧コンテキストへ限定 — FS 同一フォルダ／タグビュー現ノード／作業スペース

> ECO-060 golden 後の maintainer 性能所見と 2026-07-11 の要求を受けて起票・工程診断した機能拡張。
> 起票段階では `src/tests` を変更しない(R1)。

## §1 症状・要求(観測 2026-07-11・報告者 maintainer)

### 観測済みの症状

- 約 262,046 件のコレクションで、スキャン完了後の初回類似検索が、画面上の表示フォルダ約 701 件ではなく
  **コレクション全体**を候補として直列に pHash 生成・比較するため、極めて長時間になる。
- ECO-060 golden でも同じ所見を観測し、同 ECO は「スキャン中の検索 gate」が対象だったため、既存性能課題として
  分離候補に記録していた(`60-change-order-eco-060.md` §8 / register notes)。

### maintainer 要求

- ファイルシステム表示では、候補スコープを**対象画像と同一フォルダ**にする。
- サブフォルダを含める案も検討する。
- タグビュー(階層ビュー)では、対象画像と同一ノードまたはリーフに相当する範囲を候補にする。

再現手順:

1. 多数のサブフォルダを含む大規模コレクションを選択する。
2. ファイルシステム軸で一つのフォルダへ潜り、画像を整理トレイのマージ先に指定する。
3. 類似画像検索を実行する。
4. 表示フォルダの画像数ではなく、選択コレクションの全 normal 画像数に比例して初回処理が長時間化する。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **意味論未定義** | `docs/screens/image_tab.md` は FS 軸/タグビュー軸の閲覧と整理トレイの類似検索を定義するが、画像タブの類似候補を「現在フォルダ」「サブツリー」「現在ノード」「リーフ」のどれに限定するかを定義していない。対照的に `work_tab.md` は「検索スコープは現スペース内」と明記する |
| 要求・仕様 | **現要求はコレクション全体を明示** | REQ-064・仕様 §2.10.4・OC-16 は候補を同一コレクション内の normal 画像とする。したがって現挙動は仕様適合であり、単純な実装逸脱ではない |
| E-BOM / M-BOM | **現仕様を正しく転写** | E-SIMSEARCH-032 / E-UI-SIMILARITY-035 と M-SIMSEARCH-021 は同一コレクション境界を契約化。CP-SIM-017/FMEA-022 も別コレクション混入防止を検査するが、閲覧コンテキストによる上限を持たない |
| 実装(Core) | **現仕様どおり・性能問題を顕在化** | `SimilaritySearchService.FindSimilarAsync` は base image から `SyncFolderId` を解決し、`GetByFolderAsync` の全 normal 画像を候補化して逐次処理する。候補 ID/述語を受け取る API がない |
| 実装(ImageTab) | **閲覧コンテキストは保持するが検索へ未伝達** | `ImageTabViewModel` は FS の `_fsPath`、タグビューの `_viewPath`、現在の表示画像 `_matchedFiles` を保持する一方、`ImageTabOrganizeViewModel` へ渡すのは collection id のみ。類似検索は候補集合を渡さず Core を呼ぶ |
| 実装(WorkTab read-across) | **CAD/BOM 契約に対する性能上の不完全配線** | WorkTab は `FindSimilarAsync` でコレクション全体を計算した**後**に workspace ID で結果を絞る。結果集合は現スペース内だが、計算量は現スペース内に限定されない |

帰属: **要求拡張 + CAD のスコープ意味論追加**。現実装は既存の「同一コレクション全体」契約に適合しているため、
欠陥修正としてコードだけを変更できない。まず候補スコープを CAD/要求/BOM で裁定し、その後 `/eco-fix ECO-062` で
プローブ先行の製造を行う。

未確定事項との関係:

- IMG-016/017(ECO-060 のスキャン中表示詳細)とは無関係。検索開始可否でなく、開始後の候補集合を扱う。
- タグビューの NodeGraph は画像の排他的な「所属先」を保存しない。画像は複数の子ノード条件に同時一致し得るため、
  「対象画像から一意のリーフを逆算」は現モデルでは曖昧であり、CAD 裁定なしに実装できない。

## §3 切り分け済みの事実

確定(コード・台帳・golden 記録で確認):

- FS 軸の現在表示は `_fsPath` 直下の画像だけであり、サブフォルダ内画像はフォルダカードへ集約される
  (`ResolveFs`: remainder に `/` があれば folder count、なければ file)。したがって「現在表示画像集合」と
  「同一フォルダ直下」は一致する(タグチップによる表示フィルタは別途除外して考える必要がある)。
- タグビュー軸の現在表示集合は、選択ビューの root + `_viewPath` の条件を満たす画像集合である。
- pHash 特徴量/ペア類似度キャッシュは永続化済み。初回だけでなく、未計算候補を大量に含むほど画像 decode/DCT と
  DB upsert が候補数に比例する。キャッシュ済みでも全候補の DB 照会ループは残る。
- 条件検索は本所見の対象外。類似検索だけを変更し、CriteriaSearchService の collection スコープは維持する。
- WorkTab の正規スコープは現 workspace 内で既に CAD/E-BOM に確定済みであり、候補先行絞り込みは新裁定でなく
  同一 API 変更の read-across である。

疑い(未検証 — `/eco-fix` のプローブで測る):

- 候補を明示 ID 集合で先行限定すれば、初回 pHash 計算件数とペア照会件数はその集合の件数に抑えられ、
  262k コレクションでも小フォルダ/小ノードの待ち時間を大幅に削減できる。
- repository で候補 ID を一括取得するか、呼び出し側の `ImageRecord` snapshot を Core へ渡すかで、DB round-trip と
  API 責務が変わる。最小かつ Core の status/自己除外防御を維持する形は製造時にプローブで確定する。

## §4 是正方針(gate① 裁定済み — 2026-07-11)

maintainer が 2026-07-11 に推奨案 **A + V1 + 検索時点 snapshot** を承認した。CAD は ViewPrismUI
`eef89bb` (`decide(IMG-018): 類似検索を閲覧コンテキストへ限定`)で先行改訂済み。

### 共通の不変条件(全案)

- Core は、呼び出し側が渡した候補スコープに対しても `status=Normal`、基準自身除外、同一コレクション境界を
  防御層として維持する。候補指定で別コレクション/Deleted を混入させない。
- フィルタは特徴量/類似度キャッシュ参照**より前**に適用する。スコープ外画像の pHash 生成・ペア DB 照会を行わない。
- 閾値、Score 順、同値 id 順、回転/鏡像変種、キャッシュ無効化の意味論は不変。
- WorkTab は現 workspace の画像 ID を候補として先行限定する(現行の結果後段フィルタを性能境界へ昇格)。
- 条件検索は変更しない。

### gate①-1: FS 軸のサブフォルダ — 案 A 採用

- **案 A(推奨): 同一フォルダ直下のみ**。対象画像の `relative_path` の親ディレクトリが一致する normal 画像だけ。
  現在表示集合と一致し、予測可能で最も強い性能上限を得る。サブフォルダは検索しない。
- **案 B: 同一フォルダのサブツリー**。親ディレクトリ自身 + 全子孫フォルダを含める。アルバム配下をまとめて探せるが、
  コレクション root の画像を起点にすると従来同様に全件となり、性能問題が再発し得る。
- **案 C: UI で「サブフォルダを含む」を選択**。既定 A、明示 opt-in で B。柔軟だが CAD/UI/golden の変更が増え、
  スコープ状態・ラベル・再検索時保持の追加設計が必要。

### gate①-2: タグビュー軸 — 案 V1 採用

- **案 V1(推奨): 現在選択中ノード(path)の表示母集合**。検索開始時の root + `_viewPath` 条件に一致する画像を候補とする。
  ユーザーが見ている文脈と一致し、複数リーフ一致の曖昧さがない。root で実行すればビュー全体となる。
- **案 V2: 対象画像が一致する全リーフの和集合**。対象画像から子孫を探索し、一致する leaf 群の画像を集める。
  一画像が複数 leaf に一致する場合は和集合が広がり、ユーザーが見ていない枝も含み得る。
- **案 V3: 一致 leaf をユーザーに選ばせる**。最も明示的だが、新しい選択 UI と複数一致/ゼロ一致の状態設計が必要。

裁定は **A + V1**。検索候補を「その画像が置かれた唯一の所属先」ではなく、**検索を開始した閲覧コンテキスト**
として定義する。FS では親フォルダが一意、タグビューでは現在ノード path が一意となり、両軸で同じ操作モデルになる。
ただし、マージ先がナビ変更後もトレイに保持される現挙動を踏まえ、検索時点の文脈を使うか、マージ先選択時点の文脈を
snapshot するかは **検索時点**に確定した。画面に見えている閲覧文脈と一致させる。ただし FS のタグチップは
一時表示フィルタのため候補を狭めず、親 relative path の完全一致を使う。ナビ変更によりマージ先が現在 FS フォルダ外なら
候補なしとし、マージ先の旧フォルダを暗黙に検索しない。

## §5 影響 BOM / 受入計画

- ViewPrismUI `docs/screens/image_tab.md` + mock/review points
  - 類似検索の候補スコープ、サブフォルダ規則、タグビューの current node 規則、検索結果ヘッダ等でのスコープ表示要否。
- `10-requirements.yaml` / `20-spec.md` §2.10.4 / OC-16
  - REQ-064 の「同一コレクション全体」を上限防御へ変更し、呼び出し surface の閲覧コンテキストを候補集合とする。
- `E-SIMSEARCH-032` / `M-SIMSEARCH-021`
  - 明示候補集合を受ける API、フィルタ先行、別 collection/status 防御。
- `E-UI-SIMILARITY-035` / `M-UI-ORGANIZE-034` / `M-UI-IMAGETAB-035`
  - FS path / view path から候補集合を作り、Organize VM を経由して Core へ渡す。
- `E-UI-WORKSPACE-043` / `M-UI-WORKSPACE-029`(read-across)
  - workspace 候補を Core 前段で限定する。結果意味論は不変。
- `CP-SIM-017` / `CP-UI-G9` / `CP-UI-G1` / `FMEA-022` + 性能 probe
  - scope 外 candidate の reader/cache 非接触、同一フォルダ境界(`a/x.jpg` vs `a2/y.jpg` を含む)、
    view current-node、workspace、閾値/安定順回帰を exact で固定。
  - 大規模 collection + 小 scope の fake reader 呼出数が scope 件数に比例し、collection 件数に比例しないことを検査。
- 既存固定 Oracle 行は変更しない(R6)。OC-16 の変更分は新規行または製品 unit CP へ追加する。
- DB schema / migration / pHash adapter 世代: **不変予測**(候補選択だけで特徴量形式を変えない)。

golden 再検査範囲:

- 画像タブ: FS 軸の同一フォルダ、タグビューの現在ノード、root 境界、ナビ変更後の検索、結果件数/一致率。
- WorkTab: 現スペース内だけが候補となり、別 workspace/collection の画像を計算・表示しない。
- 既存: 整理トレイ 3 ゾーン、条件検索、候補追加、マージ/Undo、スキャン中 gate。

## §6 残ゲート

- **gate①(CAD/意味論裁定)**: 完了。A + V1 + 検索時点を採用、ViewPrismUI `eef89bb` へ反映済み。
- `/eco-fix ECO-062`: 要求/BOM/新規受入行をオラクル・ファーストで同期 → プローブ先行 → 最小製造 → 機械受入。
- **gate②(golden)**: §5 の FS/view/workspace 境界と既存整理操作の実機確認後に `/eco-accept ECO-062`。

## §7 実施記録(2026-07-11・fix)

### 7.1 プローブ先行(R5) — 是正前の赤

`CpSim017Tests` に CP-SIM-017 の新規 3 観点を先行追加した。

1. 明示 scope 候補外・非 normal・別 collection が reader/feature/similarity cache に触れず、reader 呼出数が
   `base + scope内候補` だけになること。
2. FS は親 path 完全一致(`a/` と `a2/` を区別)・subfolder 除外・検索時点の current folder 外 target は 0 件。
3. view は target から leaf を逆算せず、検索時点 current node の母集合を使うこと。

是正前実行は **CS1061** (`SimilaritySearchService.FindSimilarInScopeAsync` 不在) + **CS0103**
(`SimilarityScopeResolver` 不在)で不合格。診断どおり「Core が明示候補を受けない」「surface の FS/view scope 決定がない」
ことを実測で裏取りした。初回 sandbox 実行は Avalonia telemetry log の権限拒否だったため通常環境で再実行し、上記の
製品由来コンパイル不合格を確認した。

### 7.2 製造

- `SimilaritySearchService`:
  - `FindSimilarInScopeAsync(baseId, threshold, IReadOnlyCollection<ImageRecord>, ...)` を追加。
  - 明示候補へ normal・自身除外・同一 collection・重複 id 除外を feature/cache より前に再適用。
  - 既存 `FindSimilarAsync` は同一 collection 全体を渡す後方互換 facade として維持し、既存固定 Oracle を無改変で保存。
- `SimilarityScopeResolver`(新規純粋ロジック):
  - FS は slash 正規化後の親 relative path 完全一致。base が current folder 外なら空。
  - view は検索時 current node が渡す画像集合を母集合とし、同一 collection/normal を整える。
- `ImageTabViewModel` → `ImageTabOrganizeViewModel`:
  - 検索ボタン押下時に候補を解決する関数を注入。FS は `_entries` から再解決して tag chip の `_matchedFiles`
    フィルタを波及させず、view は root + `_viewPath` をその時点で `ViewMatched` 評価する。
- `WorkTabViewModel`:
  - collection 全体を計算後に workspace ID で落とす後段 filter を撤去し、`_sourceImages` を Core の明示候補へ渡す。
- 要求/BOM:
  - REQ-087 新設、仕様 §2.10.4、E/M-BOM、CP-SIM-017、FMEA-022、沈黙次元を IMG-018/CAD `eef89bb` と同期。
  - DB schema / migration / pHash adapter / 条件検索は不変。

### 7.3 機械受入

- `dotnet build --no-restore`: **0 warning / 0 error**。
- `dotnet test tests/ViewPrism2.Tests --no-build`: **583/583 pass**(既存 580 + ECO-062 新規 3)。
- `dotnet test tests/ViewPrism2.Oracle --no-build`: **109 pass + 既知 2 skip**(既存行無改変)。
- `python bomdd/validate_bom.py`: fix 記録・status 更新後に **0 error / 0 warning**。
- `git diff --check`: error なし。

テスト運用所見: 最初の全 Tests 実行は直前 test host が TestResults log を保持して失敗したため、当該
`ViewPrism2.Tests` process だけを終了して再実行した。再実行で 1 件失敗したのは新規プローブが `outside` 画像を
明示 scope に含めながら scope 外を期待した入力矛盾で、surface/Core 責務に合わせ候補から外して是正。その後 583/583。

## §8 残ゲート(fix 後)

- **gate②(golden)**: 完了(maintainer 実機、2026-07-11)。

## §9 クローズ(2026-07-11)

### 9.1 golden 結果

maintainer が `/eco-accept ECO-062` で §5 の golden を合格承認した。

- FS: 同一フォルダ直下だけを候補とし、subfolder/兄弟 folder を除外。tag chip の一時絞り込みは候補へ影響しない。
- FS navigation: マージ先選択後に別 folder へ移動した場合は候補 0 件。
- view: 検索実行時の current node 母集合だけを候補とし、別枝へ広がらない。
- WorkTab: 現 workspace 内だけを Core 前段候補とし、大規模 collection 全体の計算待ちを再発させない。
- 裏面回帰: 閾値・結果順・候補追加・マージ/Undo・スキャン中 gate は正常。

### 9.2 再発防止

- **CP-SIM-017**: scope 外/非 normal/別 collection の reader/feature/similarity cache 非接触と、reader 呼出数が
  `base + scope内候補` に一致することを unit exact で固定。
- **CP-UI-G9**: 画像タブ FS/view の検索時文脈境界と整理トレイの裏面回帰を、26万件全走査の潜伏実績つきで明記。
- **CP-UI-G1**: WorkTab が結果後段 filter へ退行せず、現 workspace を Core 前段候補にする read-across を明記。
- As-Built `golden_2026_07_11_eco062` に承認内容を記録。

### 9.3 教訓

性能問題の境界は「結果に何を表示したか」ではなく、**高価な処理へ入る前に何を候補から除いたか**で定義する。
WorkTab は従来も結果だけは workspace 内だったが、pHash 計算後の filter だったため性能契約を満たさなかった。
ECO-058 の read-across 漏れと同様、共有機能を別 surface へ再利用するときは結果意味論だけでなく、計算境界・仮想化・
キャッシュ接触を含む非機能契約も同時に移植する。また、NodeGraph のような非排他的分類では対象から「所属 leaf」を
逆算せず、ユーザーが選択した閲覧コンテキストを明示入力にすることで曖昧さと全域探索を同時に避けられる。

### 9.4 残課題

- 本 ECO に起因する残ゲートなし。
- IMG-016/017(スキャン中表示詳細・再スキャン異常系)は既存の別課題で、本 ECO の候補スコープとは独立。
