# Change Order — ECO-057(applied): 公開前プライバシー・著作権・評判リスクの無害化

> 2026-07-10、maintainer から「誤って公開ボタンを押しても被害が出ない状態」を要求として受理。
> 対象は現行ツリーだけでなく、全 Git 履歴、Git 管理外の配布混入候補、将来の再混入経路を含む。

## 1. 症状・要求

事実(2026-07-10、公開前監査で実測):

- Git 管理中の現行ツリーと全 230 コミットに、画像・動画・PDF・Office 文書・アーカイブの
  パスは存在しない。秘密鍵、主要トークン形式、JWT、パスワード代入、接続文字列、Base64
  埋め込み画像も検出されなかった。
- `ImageTabSeedViewModel` のデモデータに実ユーザー名を含む OneDrive/Pictures/Downloads/
  Camera/Desktop の絶対パスと実環境由来と見える件数がある。
- 製造台帳 17 箇所に、私有の実画像 `orientation_fixture_06` のファイル名、スマートフォン撮影由来、EXIF、
  実寸法、変換複製名、類似度実測が記録されている。
- そのほか製造台帳・治具・UI-IR の 13 ファイルに実ユーザー名を含む絶対パスがある。
- `.gitignore` 対象の `work/` にスクリーンショット 21 枚がある。目視で、実ユーザーパス、
  OneDrive 配下のフォルダ名・件数、私有のキャラクター画像とファイル名、Snipping Tool 通知を確認した。
  通常の Git push には含まれないが、リポジトリディレクトリの ZIP/手動アップロードでは混入する。
- Git remote URL は GitHub アカウント名を含む。これは公開主体として不可避かつ意図された識別子で、
  私有端末のユーザー名・画像情報とは分離して扱う。コミットメールは GitHub noreply のみ。

要求:

- GitHub の可視性を誤操作で public にしても、私有画像、著作権・趣味嗜好・職業上の評判に関わる
  画像、私有端末パス、実画像由来の識別情報を第三者が取得できないこと。
- 現行ツリーの削除だけでなく、全 refs/履歴からの復元、ZIP 配布、将来の再混入を封止すること。

疑い(未検証):

- `sample-photo.jpg` など、デモ名と実画像名の境界が台帳化されていない文字列に、ほかの実画像由来情報が
  含まれる可能性。fix 時に画像様ファイル名と固有名詞を全数レビューする。
- 一般的な正規表現に一致しない資格情報、自然文に埋め込まれた個人・勤務先情報が残る可能性。
  専用 scanner と語彙レビューを追加して確定する。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 非該当 | UI の機能・外観裁定ではなく、公開成果物のデータ衛生問題。 |
| 要求/仕様 | 欠落 | 公開時に私有データを含めない要求、デモデータ匿名化規則、公開前受入が未定義。 |
| 実装 | 不適合 | デモ seed が実ユーザー名・私有フォルダ構成・件数を直接保持する。 |
| 製造台帳 | 不適合 | 再現性に不要な実画像識別子・実端末絶対パスを永続記録している。 |
| リポジトリ/配布工程 | 欠落 | `work/` は ignore のみでリポジトリ直下に残り、ZIP/手動公開を防げない。履歴・画像・PII・secret の公開前 gate がない。 |

混入:

- 絶対パスは初期製造 `a68a04b` 以降、UI-IR、PLM intake、治具、デモ seed へ複数回転写。
- `orientation_fixture_06` 記録は ECO-048〜055 系の実機 golden 記録で混入。
- `work/tag-tab` のスクリーンショットは Git 未追跡のため、Git 履歴への混入はない。

潜伏・マスキング:

- `.gitignore` が「Git には入らない」ことだけを保証し、「リポジトリフォルダを配布しても安全」と
  誤同一視できる構造だった。
- golden は製品挙動の受入であり、入力画像・スクリーンショット・台帳記述の公開適合性は検査面外。
- secret 検査、PII/ローカルパス検査、Git 全履歴の media magic/path 検査が公開手順にない。

診断結論: CAD 裁定不要。実装・台帳・リポジトリ工程の欠陥是正として `/eco-fix eco-057` に進める。

## 3. 切り分け済みの事実

確定:

- Git 管理された画像バイナリは現行・履歴ともゼロ。したがって私有画像バイナリを消すための
  履歴 purge は不要だが、絶対パスと実画像識別文字列は過去コミットから復元できる。
- `work/` の 21 枚はすべて未追跡・ignore 済み。GitHub の通常 push では公開されないが、目標は
  誤操作耐性なので、リポジトリ外へ隔離しなければ要件未達。
- 現 remote は GitHub 上の private repository。履歴無害化後は remote の全公開可能 refs を
  洗浄済み履歴へ置換しなければ、可視性変更時に旧履歴が公開される。

未検証(fix 時に確定):

- remote に pull request refs、release assets、Actions artifacts、Wiki、issue 添付など、通常の
  `git fetch` で見えない公開候補があるか。
- GitHub 側の branch protection と force push 可否。
- 専用 secret scanner の追加導入可否。利用不能時は複数パターンのローカル監査で代替し限界を記録する。

## 4. 是正方針

1. 是正前プローブ: 公開禁止パターン(実ユーザー絶対パス、実画像識別子)と、リポジトリ直下の
   media/office/archive を現行ツリー・全 Git refs・配布候補から検出し、不合格を実測する。
2. デモ seed を明白な架空ユーザー・架空パス・架空件数・架空画像名へ置換する。
3. 台帳・治具・UI-IR の絶対パスを repo-relative または環境変数基準へ置換する。
4. `orientation_fixture_06` と実画像由来の識別情報を、再現性を保存した合成 fixture 名・一般化した測定記録へ置換する。
5. `work/` のスクリーンショットをリポジトリ外の私有隔離先へ移し、repo 内には非画像の説明だけを残す。
6. 公開安全監査スクリプトを追加し、現行ツリー・tracked paths・全 refs の禁止パターンを恒久検査する。
7. 通常コミットで挙動を検証後、バックアップ bundle を私有隔離先に作成して Git 履歴を書き換える。
   洗浄 clone で再監査し、remote refs を洗浄済み履歴へ置換する。
8. GitHub 側の非 Git 成果物を確認し、公開前チェックリストと最終 go/no-go を記録する。

## 5. 影響 BOM

- 実装: `M-UI-IMAGETAB-035`(デモ seed 表示データのみ、製品機能不変)。
- 製造台帳: ECO-048〜055 関連記録、PLM intake、UI-IR、as-built、charter。
- 工程/検査: `34-routing`、`33-control-plan` または専用公開安全監査手順・プローブ。
- Git: 全 refs/履歴、remote refs。既存固定オラクルと DB は不変。

## 6. 残ゲート

1. ~~工程診断~~ → 完了。裁定不要。
2. ~~/eco-fix eco-057: 匿名化、隔離、公開安全プローブ、機械受入、履歴洗浄、remote 同期。~~ → 完了。
3. ~~golden: 洗浄済み fresh clone の機械受入+使い捨て `VIEWPRISM2_DATA_DIR` による実機の空状態/私有データ非露出。~~ → 2026-07-10 maintainer 合格。
4. ~~/eco-accept eco-057: 公開前 gate を CP/routing に固定しクローズ。~~ → 本節で完了。

## 7. 実施記録(2026-07-10 — worktree 是正・機械受入完了、履歴洗浄前 checkpoint)

- **プローブ先行(R5)**:
  - `CpRelease057PublicSafetyTests` を追加。golden seed のコレクションパスが架空プロファイルで、
    アイテム名が一般名だけであることを固定した。
  - `audit_public_release.py` を追加し、現行 workspace に対して是正前 **200 findings** を実測した。
    内訳は private user/profile path、実画像 fixture 識別子、第三者作品由来デモ名、私有画像名、
    `work/` 内の未追跡 media 21 枚+magic bytes。値そのものは検査出力で再掲しない設計とした。
- **是正裁定**: 単語単位の隠蔽ではなく、転写元となる seed/台帳を一般化し、workspace media を
  リポジトリ外へ隔離し、全コミットで fail-closed 検査する案を採用。ignore 追加だけの案は
  ZIP/手動公開を防げないため不採用。
- **現行ツリー是正**:
  - 画像タブ seed を `C:\Demo\Media\...` の架空コレクション、一般的なアルバム/作例/カテゴリ/
    スタイル/季節タグ、`sample-photo.jpg`/`sample_NNN.png` へ全面置換。第三者作品・趣味嗜好を
    推測させる作品名/用途語彙を撤去した。
  - 製造台帳・治具・UI-IR の実ユーザー識別子を `maintainer`、絶対パスを `<repo-root>` または
    repo-relative path、実画像 fixture を合成 fixture の一般名へ置換。`scale01-jig.py` は
    `__file__` 基準で repo root を解決するよう変更し、可搬性も保存した。
  - `work/tag-tab` 一式(media 21+HTML/JS 等)をリポジトリ外の私有バックアップ領域へ移動。
    Git 管理外だったため tracked diff/履歴削除は発生しない。
- **恒久封止**:
  - pre-commit は変更ファイル種別に関係なく worktree 公開安全監査を実行し、Python 不在も含め
    fail-closed。`CP-RELEASE-018` と `ROUTING-PUBLIC-001` に worktree→全履歴→fresh clone→
    GitHub 非Git資産→公開許可の順序を固定した。
- **是正後実測(worktree)**: public-release audit **PASS 0 findings**。build **0 warning/0 error**、
  Tests **572/572**(既存571+新規1)、Oracle **109 pass+2 skip**(既存行無改変)、
  validate_bom **0 error/0 warning**、validator selftest **OK**。
- **履歴洗浄**:
  - repo 外に全 11 refs/完全履歴を含む私有 bundle を作成し、`git bundle verify` で complete history を確認。
  - 232 commits の blob/commit message を置換し、main+7 tags を匿名化履歴へ移行。書換え不能だった
    Codex 内部 tree ref は bundle 保全後に削除。全refs履歴プローブは **164 findings→0 findings** に転化した。
  - rewrite 後に old objects を repack/cleanup。生成された Python bytecode は削除し、`.gitignore` に
    `__pycache__/`/`*.pyc` を追加した。
- **GitHub 非Git資産監査(同期前・repo は PRIVATE のまま)**:
  - Releases=0、Actions runs=0、Actions artifacts=0、Issues=0、Pull Requests=0、pull refs=0。
  - Wiki disabled/リポジトリなし、Pages なし。公開時に別経路から露出する資産は検出されなかった。
- **fresh clone/remote 同期**:
  - 短い一時パスの local fresh clone で audit 0、build 0/0、Tests 572/572、Oracle 109+2skip、
    validate 0-0、selftest OK。長い私有保管パスでは MTP がログ競合/停止したため、その結果は
    受入に使わず専用プロセスを終了し、短い独立 clone の直列再走結果だけを採用した。
  - remote refs が監査開始時の main+7 tags と完全一致し第三者更新がないことを確認後、main は
    `--force-with-lease`、7 tags は同名の洗浄済みrefsへ force push。可視性は PRIVATE のまま維持。
  - GitHub から remote fresh clone し、local HEAD と commit 完全一致、7 tags 完備、全履歴 audit 0。
    さらに build 0/0、Tests 572/572、Oracle 109+2skip、validate 0-0、selftest OK を再確認した。
  - 履歴書換えで commit ID は変わったため、旧IDの監査証跡は非公開 bundle のみに保存する。
- **GitHub server 残存の除去(2026-07-10)**:
  - force push 後も代表旧 SHA 3 件が GitHub commits API で取得可能(`exit 0`)と実測。現行台帳には
    履歴説明用の旧短縮IDが残るため、ref非到達だけでは「誤ってpublic化しても安全」を満たさないと判定した。
  - 削除直前に complete bundle、洗浄済みlocal audit 0、HEAD、7 tags、GitHub 非Git資産0、repo設定を
    再確認。maintainer の明示承認後、旧private repoを削除し、API/ls-remote双方で不存在を確認した。
  - 同名repoを**新規private**で作成し、空repo確認後に洗浄済みmain+7 tagsだけをpush。default branch=main、
    Issues enabled、Wiki disabledを復元。代表旧 SHA 3 件はすべて取得不能(`exit 1`)、新HEADだけ取得可能
    (`exit 0`)へ転化した。remote refsは HEAD+main+7 tagsのみ。
  - 再作成repoからfresh cloneし、local HEAD完全一致・7 tags・全履歴audit 0・build 0/0・
    Tests 572/572・Oracle 109+2skip・validate 0-0・selftest OKを再確認。repoはPRIVATEのまま。

### golden 合格基準(gate② — maintainer 実機)

1. 起動中の ViewPrism2 を終了する。既存 `%APPDATA%\ViewPrism2` は削除・移動・改変しない。
2. PowerShell で次を実行し、使い捨ての空プロファイルへ分離して起動する。
   ```powershell
   $env:VIEWPRISM2_DATA_DIR = Join-Path $env:TEMP ("ViewPrism2-ECO057-" + [guid]::NewGuid())
   dotnet run --project src/ViewPrism2.App
   ```
3. 画像タブに既存コレクション/画像/実端末パスが現れず、空状態であること。タグ/作業タブも既存
   ユーザープロファイル由来データを表示しないこと。各タブを一巡し、レイアウト崩れ・生ID露出・
   操作回帰がないこと。
4. アプリ終了後に `Remove-Item Env:VIEWPRISM2_DATA_DIR` で環境変数を解除する。必要なら手順2で表示した
   一時ディレクトリだけを削除してよいが、`%APPDATA%\ViewPrism2` には触れないこと。
5. `python bomdd/audit_public_release.py --history` を実行し、`PASS (0 findings)` を確認すること。
   この合格後も `ROUTING-PUBLIC-001` の gate を通さず新しいcommitをpublic化しないこと。

#### GF-057-01(2026-07-10 — golden手順の是正)

- 初回提示したgolden手順は、通常起動で `C:\Demo\Media\...` のseed表示を求めていた。しかし実際の
  composition rootは `MainWindowViewModel.ImageTab=ImageTabViewModel` を構築し、通常起動は
  `%APPDATA%\ViewPrism2\viewprism2.db` を読む。`ImageTabSeedViewModel` は通常起動へ未接続で、
  `CpRelease057PublicSafetyTests` が直接構築する機械fixtureである。
- maintainer の通常起動実測で既存ユーザープロファイル由来のパス/画像が表示され、手順の誤りが顕在化。
  製品は仕様どおりであり、Git公開対象への混入ではない(実DB/画像はrepo外・tracked/history audit 0)。
- 是正: 既存プロファイルを破壊せず、既設の `VIEWPRISM2_DATA_DIR` 隔離注入口(CP-L1-SMOKEと同経路)を
  golden手順へ採用。合格期待は「架空seed表示」でなく「空の隔離プロファイルから実データが出ない」。

## 8. クローズ(2026-07-10 — golden 合格/applied)

- **実機確認**: maintainer が `VIEWPRISM2_DATA_DIR` の使い捨て空プロファイルで起動し、画像タブの
  左コレクション欄が空、項目数が 0、既存ユーザープロファイル由来の実端末パス・私有画像が表示
  されないことを添付画面で確認し、`/eco-accept eco-057` で承認した。既存プロファイルは無改変。
- **再発防止**: `CP-RELEASE-018` に、worktree/全履歴監査だけでなく、実機goldenを使い捨て
  `VIEWPRISM2_DATA_DIR` へ隔離する観点と GF-057-01 の潜伏実績を追記した。公開操作は引き続き
  `ROUTING-PUBLIC-001` の worktree→全履歴→fresh clone→GitHub非Git資産→公開許可の順序に従う。
  通常ユーザープロファイルでの起動結果は公開受入証跡として扱わない。
- **M4 判定**: 製品挙動・surface・要求仕様の変更はなく、公開工程と検査入力の隔離に関する是正である。
  CP/routing は fix 時点で同期済み、acceptでCPのgolden観点を補強したため、spec §2.6、E-BOM、M-BOM、
  35-dsbom への追加同期は不要。
- **教訓/read-across**: 公開安全性は「現行worktree」「Git全refs/サーバー残存object」「Git管理外資産」
  「実行時ユーザープロファイル」を別々の信頼境界として検査する。ignore、force push、通常起動は、
  それぞれ別境界の安全性を証明しない。特にgolden入力を実ユーザー状態から隔離しないと、検査そのものが
  保護対象を画面へ露出する。これは ECO-041/050/053 の「検査面にない次元は正常に見えたまま潜伏する」
  教訓の公開工程へのread-acrossであり、ViewPrism2固有でない方法論レベルの昇格候補とする。
- **残課題**: 私有complete bundleと隔離済みスクリーンショットはrepo外の私有バックアップとして維持する。
  将来の公開可視性変更は、新しいcommitを含め必ず `ROUTING-PUBLIC-001` を再実施する。未確定裁定なし。
