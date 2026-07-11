# ECO-066 (staged) — 類似画像検索の停止・進捗可視化 — 整理ライフサイクルと遅延結果の整合

> maintainer 実機報告・要求(2026-07-11)を受け、`/eco-file` で工程診断した既存機能拡張+実装逸脱是正。
> 起票段階では `src/tests` を変更しない(R1)。

## §1 症状・要求(観測 2026-07-11・報告者 maintainer)

### 観測済みの症状

- 類似画像検索を開始すると、利用者が途中で停止できない。
- 類似画像検索中に整理モードを終了しても、検索処理が継続しているように見える。
- 検索中であること以外に、現在の段階・処理済み件数・総候補件数が表示されず、停止しているのか処理中なのか判断できない。
- 初回検索は特に時間がかかる。現production adapter/実画像/scopeでの冷キャッシュ・温キャッシュ、画像decode、pHash計算、
  DB読書きの区分時間は未計測。
- 検索ボタン連打、整理モード終了直後の再入場、マージ先・閲覧範囲の変更に対する競合の有無も利用者からは判別できない。

### maintainer 要求

1. 類似検索中は同じ位置の「探す」を「停止」へ切り替え、明示キャンセル可能にする。
2. 整理モード終了では確認dialogを出さず、その整理セッションの類似検索を自動キャンセルする。
3. マージ先変更/解除、検索候補scope変更、別モード移行、新検索、window/app終了でも自動キャンセルする方向で検討する。
4. 有効な類似検索は1件だけとし、旧検索の遅延完了が再入場後または新しい整理状態へ結果を書き戻さない。
5. `基準画像を準備しています…` / `画像を比較中 128 / 842（15%）` / `停止しています…` を件数+barで表示する。
   ETAは本ECOの必須にしない。
6. cancelは失敗/0件ではない。初回cancelは検索前gridへ、完了結果からの再検索cancelは直前の完了結果を維持し、途中結果は公開しない。
7. cancelまでに正常生成したpHash/類似度cacheは保持し、次回検索で安全に再利用する。
8. 画像タブ/作業タブで停止・進捗・結果公開の意味論を統一し、重複実装によるdriftを防ぐ所有境界を検討する。
9. 実時間短縮は区分計測後に判断し、未計測の無制限並列化・大規模cache方式変更は行わない。必要なら性能最適化を別ECOへ分離する。

再現手順:

1. 未計算画像を複数含むcollectionを開き、画像タブまたは作業タブで整理モードへ入る。
2. マージ先を指定し、類似画像検索を開始する。
3. 検索中の右トレイを観察し、停止操作・段階・処理済み/総候補件数がないことを確認する。
4. 検索中に「整理を終了」し、直ちに整理モードへ再入場する。または検索ボタンを複数回操作する。
5. CPU/IO継続、検索状態、遅延結果の再表示、多重実行の有無を観測する。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **停止・進捗・cancel結果意味論が未定義** | `docs/screens/image_tab.md` は整理トレイ、「探す」→結果、結果0件、整理終了を定義するが、検索中表示、停止導線、自動cancel条件、cancel時のgrid/旧結果、`running/cancelling` 状態を定義しない。`work_tab.md` は整理トレイを画像タブと同一部品とするため同じ欠測が波及する。スキャン進捗popupの「close≠cancel」はcollection scan固有であり、本件の整理検索へ転用できない |
| 要求・仕様 | **進捗は既定義、cancel/lifecycleは欠測** | REQ-064は非同期実行・進捗通知が可能、REQ-065/仕様§2.10.4は「検索中は進捗を表示」と明記する。一方、停止、自動cancel、単一active search、遅延結果破棄、cancelと0件/失敗の区別、直前結果保持を規定しない。大規模応答性はP-07 exploratoryで合否外 |
| E-BOM | **進捗部品は宣言済みだが状態不変条件なし** | E-UI-SIMILARITY-035の名称は進捗を含むが、invariantsはトレイ導線/scope/scan gateのみ。検索セッションの所有、cancel trigger、原子的結果公開、画像/作業タブ共通意味論を持たない |
| M-BOM / Control Plan | **API字面とsurface受入の間が欠測** | M-SIMSEARCH-021は`progress, ct`付き検索APIを宣言するがreader抽象はtokenを受けない。M-UI-ORGANIZE-034は検索状態を所有するがsession/CTS/generationを宣言しない。CP-SIM-017は候補・cache正しさ、CP-UI-G9/G1は完了結果/整理操作を検査するだけでcancel・進捗単調性・遅延結果破棄をpinしない |
| 実装(Core) | **部分的なcancel/progress受口あり** | `SimilaritySearchService.FindSimilar(InScope)Async` は`IProgress<int>?`と`CancellationToken`を受け、候補ごとにtoken確認・百分率通知する。しかし基準画像準備phaseを通知せず、`IPHashImageReader.ComputePHash(Variants)Async`はtokenを受けないため1画像の同期Skia decode/D4計算中は停止不能。repository APIもtokenなし |
| 実装(ImageTab) | **進捗/cancel未配線+遅延公開競合** | `ImageTabOrganizeViewModel.RunSearchAsync` は`FindSimilarInScopeAsync`へprogress/tokenを渡さない。`ToggleOrganize`/`ResetState`は表示状態をfalse/空へ戻すだけで実行中Taskを停止しない。旧Task完了後は無条件に`_searchResults`/`_hasSearched=true`を書き戻すため、終了→再入場時に旧結果が新sessionへ混入し得る。`CanRunSearch`は`Searching`を除外せず再実行可能 |
| 実装(WorkTab) | **同じ欠陥を独立実装** | `WorkTabViewModel.RunSearch`もprogress/tokenなしでCoreを呼び、`ToggleOrganize`/`ResetOrganizeState`は実処理を停止しない。完了時の無条件公開と再実行可能性も同型。画像タブの子VMを再利用せず検索sessionを直接保持するためread-across drift面がある |

帰属: **既存機能拡張 + 実装逸脱の複合**。進捗表示はREQ-065/E-BOMに既に存在するため実装逸脱だが、停止導線・
自動cancel行列・cancel結果保持・遅延結果無効化はCAD/要求/BOMが未定義である。進捗だけを局所修正せず、ViewPrismUIで検索sessionの
状態・操作を先に裁定してから、REQ/BOMへ転写し `/eco-fix ECO-066` でプローブ先行製造する。

未確定事項との関係:

- ViewPrismUI `VP-UI-005` の全画面共通loading component一般化は継続中だが、本件は整理トレイ固有の実行中/停止状態であり、
  共通component化の決定を待たず検索surface内だけ確定できる。
- ECO-060/IMG-015〜017のcollection scan進捗・background継続とは別ライフサイクル。scan popup closeの意味論を検索停止へ流用しない。
- ECO-062/REQ-087は候補scope上限を閉じたが、scope snapshotの変更時に進行中検索をどう扱うかは本ECOで追加裁定する。
- 条件検索はin-memory/DB criteriaで通常短時間だが、ボタン部品と`_searching`状態を共有する。明示停止・段階進捗の主対象は
  pHash類似方式とし、条件検索へのread-across要否はCAD裁定で確定する。

## §3 切り分け済みの事実

### 確定(コード・CAD・BOM・履歴で確認)

- Coreの`progress`/`ct`はV3初版`3ef3547`(2026-06-13)から存在し、候補処理後に整数百分率を通知する。
- 画像タブ整理の検索/終了は`3176685`(2026-06-18)から、作業タブの複製実装は`f211fa9`(2026-06-29)から、
  画像タブ子VMへの移管は`6970990`(2026-07-04)から現在の「表示resetのみ・完了時無条件公開」を維持する。
- `Searching`公開プロパティは両VMにあるが、ImageTabView/WorkTabViewに`Searching` bindingは存在せず、現在のsurfaceでは描画されない。
- 両XAMLの「探す」は`CanRunSearch`系で活性化されるが、検索中を非活性条件に含めず、停止commandも存在しない。
- `ResetState`は`_searching=false`にするだけで旧Taskの実状態と乖離する。再入場後に新検索を開始すると旧/新Taskが同じ状態へ書き戻し得る。
- Coreは各candidate開始時にcancelを確認するが、production readerは`Task.Run`内でSkia decode→resize→D4 pHashを行いtokenを受けない。
  したがって現APIへtokenを配線するだけでも候補間停止は可能だが、現在処理中の1画像は完了まで走る。
- 特徴量/類似度cacheは候補単位で永続化される。特徴量再計算後に関与pairを削除し、pair score完成後にupsertするため、
  cancel済み検索の途中cacheを全検索transactionとしてrollbackする設計ではない。保持の正当性と中断点ごとの再利用は新規probeで固定が必要。
- P-07は2026-06-14の旧factory baselineで1,000枚cold平均1,633ms/warm 11.5msを記録するが、production adapter、
  8変種、ECO-054経路、ECO-062 scope、利用者の実画像を現在条件で区分した測定ではない。今回の支配区間の証拠にはできない。

### 疑い(未検証 — `/eco-fix` のプローブで測る)

- 整理終了→即再入場で旧Taskが完了すると、`_hasSearched=true`により旧結果が新sessionの結果として可視化される見込み。
- 連打で複数検索が並行し、最後に開始した検索ではなく最後に完了した検索が結果を上書きする見込み。
- 停止遅延の上限は候補間では短いが、大判/壊れかけ/低速媒体の単一画像decode時間に支配される可能性がある。
- 冷検索の支配要因がdecode/D4 pHash/単一SQLite接続のfeature+similarity upsertのどれかは未計測。無制限並列化は
  disk seek/CPU/共有SQLite semaphoreを競合させるため、計測なしでは採用できない。

## §4 是正方針(gate①の案)

### 案A(推奨・maintainer要求): 整理検索sessionを明示し、全triggerでcancel+世代無効化

1. 類似検索状態を`idle / preparing / comparing / cancelling / completed`として定義し、1 surfaceにつきactive sessionを1件に限定する。
2. 検索中は同じCTAを「停止」へ切り替える。停止押下は即`cancelling`、結果0件/失敗へ遷移させない。
3. 明示停止、整理終了、マージ先変更/解除、検索scope変更、別モード、新検索、window/app終了でactive sessionをcancelする。
4. session generationを進め、tokenを観測できない処理が遅延完了しても旧sessionからUI結果を公開しない。
5. 結果は検索単位で原子的に公開する。初回cancelはgrid、再検索cancelは直前completed snapshotを保持し、途中結果は非公開。
6. 進捗はphase+`completed/total`+整数%を単調通知し、preparing/comparing/cancellingを空状態・完了状態から分離する。
7. cacheは候補単位の有効な途中成果として保持し、次回検索で再利用する。結果publicationの原子性とcache transactionを混同しない。
8. ImageTab/WorkTabが同じ検索session component/状態機械を消費する境界を設け、scope解決とtray compositionだけをsurface側に残す。

- diff規模: **中〜大**。ViewPrismUI+REQ/spec+E/M-BOM/CP/FMEA、App共通session/両VM/XAML/i18n、Core進捗型、
  reader cancel粒度、unit/headless/goldenを変更。DB schema/pHash値の変更は不要予測。
- golden影響: CP-UI-G9/G1で検索中CTA・phase/count/bar・停止・整理終了・再入場・再検索cancel時の旧結果保持・通常完了を両タブ確認。
- 利点: 報告3症状と、同じ構造から生じる多重実行/遅延結果混入を一つの状態不変条件で閉じる。

### 案B(最小): 明示停止+整理終了だけcancel、その他は遅延結果破棄のみ

- 「停止」と整理終了ではcancelするが、マージ先/scope変更、新検索等はgenerationで旧結果を捨てるだけで計算継続を許す。
- diff規模: **中**。CTA/progress/最低限CTS+generationは必要。案Aよりtrigger配線とgolden行列が小さい。
- 欠点: 見えない旧検索がCPU/IO/DBを消費し続け、「整理文脈を失った検索は止める」という要求を部分的にしか満たさない。
  新検索との同時実行も許し得るため、単一active search要求とは相性が悪い。

### 案C(不採用候補): 進捗だけ追加し、検索はbackground継続

- 現在のCore百分率を表示するだけで停止/世代管理を追加しない。
- diff規模: **小**。ただし整理終了後の処理継続、多重実行、旧結果書戻しを温存し、maintainer要求1〜4/6を満たさない。

### 性能の扱い(全案共通)

- 本ECOでは冷/温、base準備、candidate decode+D4、feature/similarity repositoryを区分観測できるprobe/計測点を追加する。
- 固定msをunit合否にせず、進捗単調性、reader/repository呼出数、cancel後の追加処理上限、遅延結果非公開を決定論的に検査する。
- 区分計測で局在した低リスク是正が本session状態変更と不可分ならgate①後に範囲を確定する。bounded並列化、DB batch、
  事前index等の独立した性能構造変更は別ECOへ分離する。
- pHashアルゴリズム、距離→類似度、閾値、候補scope、adapter世代は不変とする。

## §5 影響BOM / 受入計画

- ViewPrismUI `docs/screens/image_tab.md` / `docs/screens/work_tab.md` / `docs/review_points.md`
  - 検索session状態、CTA切替、progress配置、cancel trigger行列、初回/再検索cancel結果、整理終了との所有関係を裁定。
- `10-requirements.yaml` / `20-spec.md` §2.10.4
  - REQ-065進捗をphase/countまで具体化。停止・単一active・自動cancel・遅延結果破棄・cancel≠0件/失敗・cache保持を新REQまたは追補。
- `E-UI-SIMILARITY-035` / `E-SIMSEARCH-032`
  - 検索session状態機械、結果publication境界、surface間同一意味論、Core進捗/cancel契約。
- `M-SIMSEARCH-021` / `M-UI-ORGANIZE-034` / `M-UI-IMAGETAB-035` / `M-UI-WORKSPACE-029`
  - richer progress、readerまでのcancel粒度、session owner/CTS/generation、両タブ共通部品、scope/target/lifetime trigger。
- `CP-SIM-017` + 新規session CP / `CP-UI-G9` / `CP-UI-G1` / `FMEA-022`追補
  - cancel前後のreader/cache呼出、進捗単調性、途中cache再利用、単一active、latest-wins/旧結果非公開、終了→再入場、
    first/re-search cancelの結果保持、通常完了回帰をexact+goldenで固定。
- P-07/探索probe
  - 現production adapterの冷/温とbase/decode+D4/DB区分を観測。過去値は履歴として維持し、固定Oracle合否へ昇格しない。

既存固定Oracle行は変更しない(R6)。新規unit/headless/probeを追加する。DB schema/migrationとpHash adapter世代は不変予測。

## §6 残ゲート

1. **gate① ViewPrismUI裁定**:
   - 案A(推奨・maintainer要求) / 案B / 案Cの検索session所有と自動cancel範囲。
   - 初回cancel=grid、再検索cancel=直前completed結果保持、途中結果非公開の確定。
   - phase/count/barの配置と`停止しています…`の表示、条件検索への適用範囲。
2. CAD commitを先行し、製品ECOへ取り込む。
3. `/eco-fix ECO-066`: 遅延結果/多重実行/progress未配線をプローブ先行で不合格化してから製造する。
4. 機械受入: build 0 / Tests / Oracle / validate_bom 0-0 / lifecycle。
5. gate② golden: 画像/作業タブで明示停止、自動停止、進捗、終了→再入場、再検索cancel、通常完了と整理回帰を実機確認する。

