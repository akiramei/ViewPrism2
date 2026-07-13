# ECO-083 案F — Avalonia 上流報告(Issue 1 投稿済み)

- 作成: 2026-07-14(ECO-083 クローズ後の残課題処理)
- **処置(2026-07-14 更新)**: 当初「投稿しない」裁定 → maintainer が方針変更し **Issue 1(故障増幅)のみ投稿**。
  - **投稿済み**: [AvaloniaUI/Avalonia#21770](https://github.com/AvaloniaUI/Avalonia/issues/21770)(2026-07-13 UTC・open・相互参照プレースホルダは投稿時に除去済み=1 本立て)
  - **Issue 2(PerTest 再初期化レース)は保留**(未投稿)。メンテナからトリガー詳細を求められた場合、`issue-2-pertest-reinit-race.md` をコメント貼付するか、その時点で 2 本目として投稿する(その際は #21770 を相互参照)。
- 関連: [60-change-order-eco-083.md](../../60-change-order-eco-083.md)(真因診断・自リポの防御=PerAssembly+FailFast 監視+HangDump)

## 内容

| ファイル | 内容 |
|---|---|
| `issue-1-failure-amplification.md` | **主(故障増幅)**: work item の cleanup 段から漏れた例外が消費者ループを静黙死させ、当該+以後の全 Dispatch が永久未完になる。**12.1.0 で決定論的に再現**(本 README 下記の実測)。二層修正提案つき。 |
| `issue-2-pertest-reinit-race.md` | **関連(発火レース)**: PerTest 既定の毎 Dispatch 再初期化で `DefaultRenderLoop.Add`→`VerifyAccess` が間欠失敗(12.0.4 実発火スタック・~2/15)。12.1.0 では構築時保護(#21688)によりハング→間欠テスト失敗へ緩和されるが、レース自体は残る。 |
| `repro/` | 最小再現(コンソールアプリ・Avalonia.Headless 12.1.0・`WaitAsync` 期限方式・isolation を引数で切替)。ソリューション非参加。 |

## 12.1.0 実測記録(2026-07-14・本ドラフトの根拠)

- 再現実行(PerAssembly/PerTest とも同一結果): sanity 完了 → poison(cleanup の RunJobs へ例外を残す)の TCS 未遷移 → **後続 Dispatch も 10 秒未完=永久ハング再現**。例外は GC 後の `UnobservedTaskException` のみ=呼び出し側に不可視。
- 12.1.0 バイナリのデコンパイル確認: ①`EnsureXxxApplication` は保護済み(`tcs.TrySetException`=#21688)②`finally { disposable.Dispose(); }` は TCS 完了ブロック前のまま保護外(テスト本体の例外も cleanup 例外で失われる)③消費者ループの catch は `OperationCanceledException` のみ・終端故障処理なし。

## Issue 2 を投稿する場合の手順(将来の参考)

1. 上流の最新版で `repro/` を再実測し、#21770 が未修正であることを確認する(本記録は 12.1.0=2026-07-09 リリース時点)。
2. `issue-2-pertest-reinit-race.md` 内のプレースホルダ `#ISSUE1` を **#21770** へ置換して投稿する。
3. 投稿後、#21770 側へ関連コメント(または本文編集)で相互リンクする。
4. 経過観測: #21770 が上流で修正されたら、自リポの防御のうちリフレクション監視(`HeadlessApp` の `_dispatchTask` 依存=保守負債)を撤去できる可能性がある(その際は ECO 起票=CpHarnessEco083Tests の pin も同時改訂)。
