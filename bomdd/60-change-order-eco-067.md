# ECO-067 (staged) — pHash 距離0の非同一画像が100%表示される重複品質契約の見直し

> 利用者フィードバックと maintainer 実機観測(2026-07-11)を受け、`/eco-file` で工程診断した既存機能の品質改善要求。
> 起票段階では `src/tests` を変更しない(R1)。観測に用いた商用ゲーム画像・スクリーンショット・ファイル名は
> 著作権/プライバシー上の入力証拠としてリポジトリへ取り込まず、本書では匿名化した事実だけを記録する。

## §1 症状・要求(観測 2026-07-11・報告者 利用者/maintainer)

### 観測済みの症状

- 利用者から「類似画像の判定が甘く、100%になりやすい」とのフィードバックがあった。
- maintainer が同一人物を描いた複数画像で再現したところ、顔の表情が明確に異なる別画像が複数 100% と表示された。
- 画面上では候補に 100% が並ぶため、利用者には「画像内容が完全一致」または「差を検出できないほど同一」に見えるが、目視では口・目などの局所差を識別できる。
- 観測画像は商用ゲーム由来であり、製品リポジトリ・fixture・ECO 添付物へ保存しない。再現用の合成/自作 fixture は `/eco-fix` 着手時に別途作る。

再現手順(匿名化):

1. 同一人物・ほぼ同じ構図と背景で、顔の表情だけが異なる複数画像を同じ検索 scope に置く。
2. 1 枚をマージ先として類似画像検索を既定しきい値 70 以上で実行する。
3. 表情差のある候補が複数 100% と表示されることを確認する。

要求は「100% 表示を機械的に減らす」ことだけではなく、利用者が知覚する局所差と検索順位/表示値の意味を整合させ、
重複整理の判断材料として誤解しにくい類似品質契約へする必要があるかを判断すること。今回の判断は **改善が必要**。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **品質意味論が未定義** | `docs/screens/image_tab.md` は類似度しきい値、pHash等による候補、類似度順を定義するが、100%が「同一画像」を意味するか、知覚特徴の衝突を許す近似値か、局所差をどこまで順位へ反映するかを定義しない。結果面の「一致率」は利用者に完全一致率として読めるが、表示ラベルと保証水準の対応もない |
| 要求・仕様 | **現挙動を明示的に要求しており、利用者品質契約が不足** | REQ-061/062・仕様§2.10.1-2は32×32グレースケール、DCT低周波8×8、中央値二値化の64bit pHashと、距離0→100の固定変換を規定する。REQ-064/065はpHash-only・結果を類似度%付きで表示するとするが、非同一画像のpHash衝突、局所差、100%の意味、誤陽性許容を受入条件に持たない。ORB/detailedは明示的に後続ループへ除外されている |
| E-BOM / M-BOM | **実装契約は健全、品質特性が欠測** | E-PHASH-031/M-PHASH-020は決定性と距離変換を、E-SIMSEARCH-032/M-SIMSEARCH-021はpHash-only検索を宣言し、現実装と一致する。一方、異なる局所内容が同じ64bitへ縮退するcollision、非同一画像の100%禁止、detail-aware再順位付け、score calibrationを扱わない |
| Control Plan / Oracle | **仕様再現は検査するが利用者所見を捕捉できない** | CP-PHASH-016/S-19/S-20は合成近傍・非近傍、決定性、距離→% exactを検査する。CP-SIM-017は閾値/順序/cacheを検査し、CP-UI-G9は重複・回転・異フォーマット複製を中心に見る。ほぼ同じ構図で局所表情だけ異なる非同一ペア、pHash距離0のcollision率、100%ラベルの誤解を拒否する検査がない |
| 実装 | **仕様どおり。表示丸めバグではない** | `PerceptualHash`は32×32グレースケールの低周波64bitだけを保持する。`SimilaritySearchService.PairDistance`は片側identityと相手8変種の最小ハミング距離を採り、`SimilarityScore.FromDistance`は距離0だけを100へ写像する。UIは整数`Score`をそのまま`$\"{Score}%\"`で表示する。したがって100%は小数丸めでなく、identityまたは変種間のpHash距離0を表す |

帰属: **CAD/要求/品質設計の欠測**。現行実装は固定仕様・BOM・Oracleに適合しており、実装逸脱ではない。
pHash は低周波の大局構造を高速に比較する候補抽出としては機能するが、目・口など小領域の変化を必ず区別する特徴ではない。
64bitへの圧縮で異なる画像が同じ値になること自体も避けられず、その距離0を無条件に利用者向け100%へ翻訳する契約が違和感の真因である。

未確定事項との関係:

- 仕様§2.10の ORB/detailed モードは out-of-scope として明示 defer されている。本件を改善するなら、その裁定を再度開くか、別の局所差指標を選ぶ必要がある。
- ECO-048の8オリエンテーション変種は回転・鏡像複製のrecall改善であり、最小距離採用により100%候補を増やし得るが、今回の個別ペアがidentity衝突か変種衝突かは未計測。
- ECO-054の異フォーマット経路一貫性、ECO-062の候補scope、ECO-066のsession停止/進捗は健全性・性能・操作契約であり、本件の類似品質意味論とは独立。

## §3 切り分け済みの事実

### 確定(コード・CAD・BOM・履歴・画面観測で確認)

- 仕様§2.10.2と`SimilarityScore`のブレークポイントはともに `(0,100) (5,90) ...`。距離1は98となるため、整数化で99.xが100へ丸め上がる経路はなく、表示100は距離0に限る。
- UIの`ScoreText`は検索結果の整数scoreへ`%`を付加するだけで、別の丸め・補間・上限処理を行わない。
- pHashは32×32グレースケールから低周波8×8係数を中央値で二値化した64bitで、色相差と高周波/局所差の情報を直接保持しない。
- 8変種対応(ECO-048)後のペア距離は、小id側identityと大id側identity/回転/鏡像8変種の最小値である。どれか1変種が一致すれば距離0→100となる。
- pHash-only、距離0→100、ORB/detailed deferはV3設計`af494cb`、実装`3ef3547`からの意図した契約であり、今回まで実装driftはない。
- CADは結果を「一致率」と見せる一方、100%の保証水準や近似値である旨を定義していない。
- 商用画像・スクリーンショットはgit管理対象へ追加していない。本ECOは画像固有名を記録せず、匿名化した観測だけを保持する。

### 疑い(未検証 — gate①後の `/eco-fix` で合成fixtureにより測る)

- 観測ペアがidentity同士のpHash衝突か、片側の回転/鏡像変種との距離0かは、商用画像を検査素材として保存しない方針のため未測定。
- 同じ人物/構図の表情差で100%がどの程度発生するか、現在の代表的利用画像分布に対するcollision率・順位品質は未測定。
- 第2段指標として局所特徴、位置合わせ後の画素差、領域別構造差のどの組合せが必要精度と配布/性能制約を満たすかは比較未実施。
- 正規化表示画素の具体的な正規化レシピ(EXIF方向・色空間・alpha・decode規則)と完全比較/hashの製造方式はfix時に一意化が必要。

## §4 是正方針(gate①裁定済み — 2026-07-11)

maintainerが2026-07-11に共有した用途定義と検討内容に基づき、**案Aを「重複関係検証」として修正採用**した。
CADはViewPrismUI `bb9623b` (`decide(IMG-021): 重複関係検証として案Aを修正採用`)で先行改訂済み。
本機能の主目的を一般的な見た目の類似検索ではなく、**同一原画像に由来する電子的変種の検出・整理**とする。

### 4.1 利用者向け関係と削除安全性

| 判定 | 契約 | 整理操作 |
|---|---|---|
| 同一ファイル | SHA-256一致・バイト列まで同じ | 強い重複削除候補 |
| 画像内容一致 | 規定どおり正規化した表示画素が一致 | 強い重複削除候補。100%を表示できる唯一の決定的関係 |
| 実質同一 | 同一原画像の再圧縮・解像度変更・回転鏡像・軽微な色/余白差 | 同一グループ候補。保持候補を品質属性から提示可能 |
| 部分重複 | trim・余白除去・一部切出し等、同一原画像の一部を共有 | 要確認。自動削除対象にしない |
| 類似 | 題材/構図は近いが表情・ポーズ・文字・物体等の局所内容が異なる | 重複扱いせず、自動で重複グループへ投入しない |
| 非類似 | 整理上の関係なし | 候補外 |

- `一致率`は廃止または用途を限定する。pHash等の未校正な近似値を`一致率`、`重複確率`、`100%`として表示しない。
- 100%はEXIF方向・色空間・alpha等を規定どおり正規化した**表示画素一致**だけに限定する。主表示は数値100%より`画像内容一致`を優先する。
- SHA-256一致は`同一ファイル`という別の強い関係として表示する。

### 4.2 三段構造

1. **第0段・決定的同一性**: ファイルhashと正規化表示画素のhash/完全比較で`同一ファイル`/`画像内容一致`だけを確定する。
2. **第1段・高速候補抽出**: 現行pHashと回転鏡像変種等をrecall重視で維持する。距離を最終重複判定/表示百分率へ直結しない。
3. **第2段・重複関係検証**: 幾何対応、対応領域面積、位置合わせ後の局所差(全体平均だけでなく面積・強度・最大値)から`実質同一 / 部分重複 / 類似`を区別する。

具体指標はCAD段階で固定しない。ORB、位置合わせ後の画素差、領域別構造差等をfixtureで比較する。
一般的な意味埋め込みの単独採用は同じ人物/キャラクターの表情違いを高評価しやすく本目的に不適。
全体平均SSIM等の単独採用も局所編集を希釈し得るため不可とする。

### 4.3 代替案の位置づけと受入不変条件

- 案Bは必要なら暫定表示緩和として先行可能だが、それだけで本ECOを完了としない。
- 案Cは候補抽出器の比較対象に残すが、単一特徴量/距離曲線の変更だけで本ECOを完了としない。
- 最低順位契約: 形式変換、JPEG再圧縮、解像度変更等の同一原画像由来候補を、表情差・局所編集・同一題材の別画像より上位に並べる。
- 不変条件: **pHash距離0だけを根拠に、画像内容一致、実質同一、100%のいずれも確定しない**。
- `/eco-fix`では商用画像を使わず、同一file、同一画素、lossless変換、再圧縮、解像度変更、回転鏡像、trim、表情差、局所編集、同一題材別画像、無関係、pHash距離0の非同一pairを自作fixtureとして先行不合格化する。

diff規模は**大**。ViewPrismUI、REQ/spec、E/M/K-BOM、score/cache schema/version、検索性能、CP/Oracle/goldenへ波及し、指標により新依存/モデル配布の調達評価が必要。

## §5 影響BOM / 受入計画

- ViewPrismUI `docs/screens/image_tab.md` / `work_tab.md` / `review_points.md` / mock
  - 「一致率/類似度」の語彙、100%の意味、候補抽出と最終順位、detail-aware評価、同一性表示を裁定。
- `10-requirements.yaml` / `20-spec.md` §2.10 / unresolved decisions
  - REQ-061/062/064/065/084とORB deferを再評価し、品質階層・score calibration・100%境界・互換/移行を要求化。
- E-PHASH-031 / E-SIMSEARCH-032 / E-UI-SIMILARITY-035
  - coarse recall、detail precision、最終score/表示、回転鏡像、同一性の責務境界。
- M-PHASH-020 / M-SIMSEARCH-021 / M-UI-ORGANIZE-034 / K-BOM / Service BOM
  - 第2段指標、依存/モデル、adapter/cache version、計算・保存・無効化・配布サイズ/ライセンス。
- CP-PHASH-016 / CP-SIM-017 / CP-UI-G9/G1 + 新規品質CP / Oracle
  - 自作fixtureの階層ペアで順位/境界/誤陽性を固定。既存回転鏡像・異フォーマット・70閾値を移行後期待値として新規行で追加し、既存固定Oracle行は変更しない(R6)。
- P-07 / 性能検査
  - coarse→rerankの対象数、cold/warm、decode/指標/cache、低速媒体/大scopeを区分し、固定msでなく予算/上限を裁定後に定める。

## §6 残ゲート

1. ~~**gate① ViewPrismUI裁定**: 案A/B/C、100%を保証する同一性境界、表示語彙、局所差の受入fixture階層、精度/性能/配布制約を確定する。~~
   → **案Aを重複関係検証として修正採用**(2026-07-11・maintainer)。関係分類、100%境界、削除安全性、fixture/順位/不変条件を確定。
2. ~~CAD commitを製品側ECOへ取り込む。~~ → ViewPrismUI `bb9623b`で完了。
3. ~~明示の `/eco-fix ECO-067` 指示後、商用画像を使わない合成/自作fixtureで現行collisionを先行不合格化し、候補指標を比較してから最小製造する。~~
   → 2026-07-11 CP-DUPQUALITY-030を先行不合格化し、決定的同一性+D4/RGBA局所差+crop重複で製造完了(§7)。
4. ~~機械受入: build 0 / Tests / Oracle新規行 / validate_bom 0-0 / lifecycle。既存固定Oracle行は不変(R6)。~~ → §7.3のとおり完了。
5. gate② golden: 画像/作業タブで同一、再エンコード、回転鏡像、同構図の局所差、無関係画像の順位/表示と検索時間を実機確認する。

**残作業はhuman gate② goldenだけ**。合格後、明示の `/eco-accept ECO-067` でクローズする。

## §7 実施記録(2026-07-11 / gate② golden待ち)

### 7.1 R5 先行不合格と方式比較

商用画像を使わず、テスト内で生成・終了時削除する`CP-DUPQUALITY-030`を先に追加した。是正前は既存596件の
製品コードに`DuplicateRelationshipVerifier`/`DuplicateRelationship`が存在せず、CS0246/CS0103で新規probeだけが
コンパイル不合格となった。これにより真因をpHash曲線の単純調整でなく、候補抽出後の重複関係検証層と表示関係語彙の不在と裏取りした。

fixtureは同一file、byte差+同一表示画素、JPEG再encode、小解像度、90度回転、鏡像、center trim、局所内容置換、
無関係、および8bit grayscaleが同値の赤/緑局所置換(pHash距離0の非同一pair)を自作する。

比較の結果、一般意味embedding単独/全体平均SSIM単独/案CのpHash曲線変更はCADのprecision契約を直接満たさないため不採用。
新規依存なしのSkia経路で、次を組み合わせる案を採用した。

1. byte SHA-256 exact→`同一ファイル`、EXIF正立+sRGB BGRAの寸法/画素exact→`画像内容一致`。
2. pHashは従来どおり高速な粗候補抽出だけに使用する。
3. 64x64 sRGB RGBAでD4全8変種を位置合わせし、channel差の平均、変更画素率、強差画素率、8x8局所block最大平均を併用。
4. 寸法差がありfull-frame precisionを満たさない候補だけ、55%以上のcrop領域を両方向に探索して`部分重複`を判定。
5. 大局は近いが局所precisionを満たさない候補は`類似（重複ではありません）`、無関係は結果非公開。

crop探索は寸法差、full-frame平均差、局所block差のいずれかがcrop兆候を示す候補だけに限定する。SHA-256はDBの既存`ImageRecord.Hash`一致ならI/Oなしで
`同一ファイル`を確定し、非一致と既知のpairはverifier側のbyte再hashを省略する。

### 7.2 製造内容

- Core: `DuplicateRelationship` 6分類、表示語彙、`IDuplicateRelationshipVerifier`抽象、`SimilarResult`の関係/内部候補scoreを追加。
- Infrastructure: `DuplicateRelationshipVerifier`を追加。読取専用decodeで元画像/一時fileを変更しない(INV-009)。
- 検索: pHash閾値通過後だけ重複関係を検証し、関係強度→内部candidate score→旧pHash score→idで安定順。
  `NonSimilar`は非公開。pHash scoreは後方互換/診断用に保持するがUIへ渡して百分率表示しない。
- DB: migration 007で`image_similarity`へ`duplicate_relationship/candidate_score/verifier_adapter`を追加。
  旧NULL/adapter不一致は再検証し、feature再計算の既存pair連鎖削除で同時無効化。fresh/migration schema同値をCP-DB-006で確認。
- UI: 画像/作業タブとも`類似度のしきい値 50%〜100%`を`候補の広さ 広め/標準/絞る`へ変更。
  結果badgeは`同一ファイル / 画像内容一致 / 実質同一 / 部分重複 / 類似（重複ではありません）`とし、数値%を廃止。
- BOM: REQ-090、E/M-BOM、CP-DUPQUALITY-030、FMEA-042、design-system/service BOM、仕様§2.10.4を同期。
  新規NuGet/モデル依存なし。既存固定Oracle行は無変更(R6)。商用画像/スクリーンショット/ファイル名は非収載。

最終diffはECO記録を含む24ファイル規模。第2段方式を`SimilaritySearchService`へ直接埋めずCore抽象/Infrastructure実装へ分け、
画像/作業タブは同じservice結果と関係語彙を消費するためsurface間driftを増やさない。

### 7.3 機械受入

- `dotnet build ViewPrism2.sln --no-restore`: 成功、0 warning / 0 error。
- `dotnet test tests/ViewPrism2.Tests/ViewPrism2.Tests.csproj --no-restore --no-build`: 601 passed / 0 failed / 0 skipped。
  新規5本でfixture分類、pHash距離0非同一、production検索→pair cache→UI関係語彙、schema同値を固定。
- `dotnet test tests/ViewPrism2.Oracle/ViewPrism2.Oracle.csproj --no-restore --no-build`: 109 passed / 0 failed / 2 skipped。
  固定pHash/score/search期待値は変更していない(R6)。
- `python bomdd/validate_bom.py`: 0 errors / 0 warnings。
- `git diff --check`: whitespace errorなし。

### 7.4 gate② golden基準

1. 自作/権利上利用可能な同一画像のfile copy、lossless形式変換、JPEG再圧縮、縮小、回転/鏡像、trim、局所編集/表情差を同一scopeへ用意する。
2. 画像タブで整理→マージ先→類似画像検索を開き、設定が`候補しきい値 N%`で70%/80%をexact再指定できる。
3. copy=`同一ファイル 100%`、同一表示画素=`画像内容一致 100%`、再圧縮/縮小/回転鏡像=`実質同一 90〜99%`、trim=`部分重複 70〜79%`となる。
4. 表情差・目口/文字/物体等の局所編集は`類似（重複ではありません） 40〜49%`となり、100%/画像内容一致/実質同一にならない。
5. 同一原画像由来候補が表情差/局所編集/同題材別画像より上位。無関係画像は候補外。
6. `部分重複`/`類似`を見て自動削除されたり、自動で整理対象へ入ったりしない。追加は利用者操作だけ。
7. 作業タブでも同じ分類/語彙/順序。候補scope、停止/進捗/cancel、条件検索、候補追加、merge/Undo、scan gateに回帰がない。

## §8 GF-067-01 是正(2026-07-11 / golden再確認待ち)

### 8.1 不合格所見と診断

maintainer実機で、同じ`実質同一`でも画像ごとの類似加減が異なるのに同じ語彙だけとなる違和感、および
検索設定が`広め/標準/絞る`では70%/80%の条件を再現できないことを観測した。添付スクリーンショットは商用画像を
含むためrepoへ収載せず、匿名化した所見だけを記録する。

CADはViewPrismUI `b686b37`で補正済み。関係分類/100%境界/pHash粗候補分離は維持し、検索条件と結果を分ける:

- 検索設定=`候補しきい値 N%`。第1段pHashの粗候補条件であり、70%/80%をexactに再指定可能。
- 結果=`関係ラベル + 検証器candidate score%`。例`実質同一 94%`。重複確率/旧pHash scoreではない。
- 一致度帯=exact 100、実質同一90〜99、部分重複70〜79、類似40〜49。関係境界を逆転させない。

### 8.2 R5先行不合格と是正

既存599件合格のまま新規/追補probe 2件だけが不合格:

- 画像タブ既定候補しきい値: expected `70%` / actual `標準`。
- pHash距離0非同一pair: expected `類似（重複ではありません） 48%` / actual 関係語彙のみ。

計算/cacheには既に`CandidateScore`と数値しきい値が存在し、表示経路だけが前者をpHash旧scoreへ置換、後者を3語へ丸めていた。
画像/作業タブとも検索結果tupleへ`SimilarResult.CandidateScore`を渡し、`ScoreText`を関係+一致度%へ変更。
設定は`候補しきい値`+`N% 以上`+50%/100%目盛へ戻した。分類器、閾値帯、DB schema、100%境界は無変更。

### 8.3 機械受入と再golden基準

- build 0 warning / 0 error、Tests 601/601、Oracle 109 pass+既知2skip、validator 0-0(最終値)。
- 再golden: 画像/作業タブで70%→80%→70%を再指定でき、同じscope/cache状態で70%条件が再現する。
- 結果は`実質同一 9x%`等で同関係内の差が分かる。`類似（重複ではありません）`にも40〜49%を併記する。
- pHash距離0の非同一画像は100%にならず、画像内容一致/実質同一にも昇格しない。
- 条件検索は従来どおり`条件一致`で、候補しきい値/一致度%の対象外。

## §9 GF-067-02 是正(2026-07-11 / golden再確認待ち)

### 9.1 不合格所見と診断

maintainer実機で`実質同一 99%`は意味が重複し、親切のために関係語彙と数値を併記した結果、どちらを
判断軸にすべきか分からないと観測した。さらにGF-067-01は検索設定をpHash候補score、結果を検証器candidate
scoreとする二軸設計だったため、`70% 以上`の検索結果に`48%`が現れ得た。添付スクリーンショットは商用画像を
含むためrepoへ収載せず、匿名化した所見だけを記録する。

CADはViewPrismUI `df560cf`でGF-067-01を置換した。利用者向けの判断軸を検証器一致度%ひとつに統一する:

- 結果badge=`N%`のみ。`実質同一 / 類似`等の関係語彙とpHash scoreは表示しない。
- 検索設定=`一致度 N% 以上`。結果と同じcandidate scoreへ適用し、70%検索には70%以上だけを出す。
- pHashは内部固定50の粗候補gate、関係分類は100%境界/削除安全性/cacheの内部契約として維持する。
- 100%は正規化表示画素exactだけ、部分重複/類似の自動削除・自動投入禁止も維持する。

### 9.2 R5先行不合格と是正

既存600件合格のまま、新probeだけが不合格となった:

- verifier candidate scoreが48のpairをthreshold 70で検索: expected 0件 / actual 1件。

`SimilaritySearchService`のproduction経路を、pHash内部固定50→検証→candidate score利用者thresholdの順へ変更し、
candidate score降順/id昇順へ統一した。verifier未注入の固定Oracle経路は従来pHash契約を維持する。両タブのbadgeを
`N%`だけ、方式表示を`一致度 · N% 以上`、設定見出しを`一致度のしきい値`へ変更した。DB schema、verifier、
一致度帯、100%境界は無変更。

### 9.3 機械受入と再golden基準

- Tests 601/601。新probeはthreshold70で48を除外し、テスト専用threshold40で返すこと、およびbadgeが`48%`/`94%`
  の数値だけであることを固定した。
- build 0 warning / 0 error、Oracle 109 pass+既知2skip、validator 0 error / 0 warning、diff-check clean。
- 再golden: 画像/作業タブとも結果badgeは`99%`等の数値だけで、関係語彙を併記しない。
- 70%検索の全結果が70%以上、80%検索の全結果が80%以上であり、70%へ戻すと同じscope/cache条件を再現する。
- pHash距離0の非同一画像は100%にならず、UI最小50%未満なら通常検索結果へ現れない。
- 条件検索は従来どおり`条件一致`。候補scope、停止/進捗/cancel、候補追加、merge/Undo、scan gateに回帰がない。

## §10 GF-067-03 是正(2026-07-11 / golden再確認待ち)

### 10.1 実機所見の再診断

GF-067-01画面で表情違いが`実質同一 98〜99%`だった事実から、旧pHash値ではなく検証器v1の
`CandidateScore`が高一致を返していたと確定した。旧pHash-only cacheは関係NULL/adapter不一致なら再検証されるため、
「旧ビルドまたは旧cacheの可能性が高い」という仮説は棄却した。既存fixtureの局所編集は160x120上の34x22シアン矩形で、
実際の瞼/口線より面積・色差が大きく、64x64正規化後の小面積差分を代表していなかった。

同時に、部分重複の許容mean上限40に対し旧score式のrangeが16で、宣言帯域70〜79を構造的に保証しないこと、
`CandidateScore`のコードコメントがGF-067-02後も「表示しない」のまま残る契約追随漏れを確認した。

### 10.2 R5先行不合格

商用画像を使わず、既存合成画像へ小面積・中程度色差の瞼/口線3か所を描くfixtureを追加した。是正前は
expected `Similar` / actual `SubstantiallySame`で、全602件中この新規1件だけが不合格となった。

帯域は画像差分の偶然の組合せでprivate式へ到達させず、関係別CandidateScore写像をCore決定契約として切り出すprobeを
追加した。是正前は`DuplicateCandidateScore`欠落のCS0103で不合格。境界は部分重複mean 0→79、16→75、40→70を固定する。

### 10.3 是正

- 64x64/8x8 blockの差分総量を降順化し、上位6/64 block(約9.4%)の集中率を追加。80%以上かつ
  changed fraction 0.1%以上、max block mean 1以上なら局所編集として`実質同一`から除外する。
- 既存mean/changed/severe/max block/D4/crop判定は維持し、集中率は補助指標としてのみ追加した。
- `DuplicateCandidateScore.FromMean`をCoreの単一正本とし、exact=100、実質同一90〜99、部分重複70〜79、
  類似40〜49から逸脱しない写像へ変更。コメントもUI表示/検索しきい値との共通軸へ同期した。
- verifier adapterを`skia-duplicate-relationship-v2`へ更新。v1 pair cacheを実検索で再検証してv2保存するprobeを追加した。
  DB schema/migration/pHash adapterは変更なし。

対象CPは15/15、全製品テストは611/611へ転じた。商用画像・スクリーンショット・固定binary fixtureは収載していない。

機械受入は`dotnet build` 0 warning / 0 error、Tests 611/611、固定Oracle 109 pass / 既知2 skip、
`validate_bom.py` 0 error / 0 warning。既存固定Oracle行、DB schema、migration、pHash adapterは無変更。

### 10.4 残ゲート

fix commit後、maintainerが実機で次を確認する:

1. 同一scopeの表情違い画像が既定70%検索へ出ない(または70%未満)こと。
2. 同一file/同一画素は100%、再圧縮・小resize・回転鏡像は90〜99%、trimは70〜79%で残ること。
3. 旧検索済みprofileでも再検索によりv1 cacheが使い回されず、旧98〜99%が残らないこと。
4. 画像/作業タブで表示としきい値が単一のN%軸、70→80→70が再現し、条件検索・停止・merge/Undoに回帰がないこと。

## §11 GF-067-04 是正(2026-07-11 / golden再確認待ち)

### 11.1 実機不合格と真因

GF-067-03後の実機70%検索で、同じ人物・同じ構図の表情差候補が従来複数件から1件へ激減した。利用者の
「見た目が近い画像を70%未満とは考えにくい」という判断と不一致。添付商用画像はrepoへ収載せず、件数と
匿名化所見だけを記録した。

真因は、内部の削除安全分類をそのまま表示数値帯へ写像したGF-067-02/03契約。局所集中判定の境界を挟むと、
見た目が近い表情差が`SubstantiallySame=99%`または`Similar=49%以下`へ不連続に分断された。分類精度の追加だけでは
この崖を解消できないため、関係分類と利用者向け連続類似度を分離する。

CAD IMG-021はViewPrismUI `e6a8078`で補正済み。結果badgeは従来どおりN%一つで、関係ラベルを復活させない。

### 11.2 R5先行不合格

既存の商用画像不使用fixtureの期待を次へ変更した:

- 小面積・中差分の瞼/口線3か所: 内部`Similar`を維持し、表示詳細scoreは70〜95。
- production pHash距離0局所色差: 内部`Similar`を維持し、表示scoreは70〜94。threshold70で返り95で除外。
- verifier v2の誤った99% cacheをv3で再検証し、関係`Similar`と連続scoreへ更新。

是正前は前2件がactual 49/48で、対象15件中2件だけ不合格。関係分類と固定表示帯の結合を実測で裏取りした。

### 11.3 是正

- `DetailSimilarityScore`をCore純粋関数として追加。meanの大局減点と、changed/severe/maxBlock/上位6 block集中率の
  局所減点を連続合成し、近似結果は1〜99に限定する。測定差が増えるほど単調に下げる。
- `DuplicateRelationshipVerifier`は関係と独立したdetail scoreを返す。`Similar`を40〜49へ、`PartialOverlap`を
  70〜79へ強制しない。verifier adapterをv3へ更新し、v2 cacheを自動再検証する。
- production表示/threshold/順位は、決定的exactなら100、それ以外は`min(pHash大局score, detail score)`。
  pHashは候補抽出だけでなく大局的な見た目の上限、detailは局所差の上限として働く。
- 内部関係は非表示の削除安全契約として維持。`PartialOverlap/Similar`の自動削除・重複投入禁止は不変。
- UI形状、DB schema/migration、pHash adapter、固定Oracle行、新規依存は変更しない。

対象CP 7/7、全製品テスト603/603へ転じた。機械受入はbuild 0 warning / 0 error、固定Oracle
109 pass / 既知2 skip、validator 0 error / 0 warning。既存固定Oracle行は変更していない。

### 11.4 残ゲート

fix commit後、maintainerが同じ実機画像で次を確認する:

1. 70%検索で表情差候補が1件へ激減せず、見た目に応じた複数の連続値で残る。
2. 表情差は100%にならず、95%等へ上げると段階的に絞られる。
3. 同一file/正規化画素一致だけ100%。再圧縮/resize/回転鏡像/trimの候補性を失わない。
4. 内部`類似/部分重複`は自動削除・自動投入されず、画像/作業タブの単一N%、70→80→70、条件検索、停止、merge/Undoに回帰がない。
