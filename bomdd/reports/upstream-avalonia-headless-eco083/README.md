# ECO-083 案F — Avalonia 上流報告ドラフト(未投稿・記録のみ)

- 作成: 2026-07-14(ECO-083 クローズ後の残課題処理)
- **処置裁定: 投稿しない**(maintainer 2026-07-14)。上流欠陥の診断・再現・修正提案を記録として保存する。
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

## 投稿する場合の手順(将来の参考)

1. Issue 1 → Issue 2 の順に投稿し、本文中のプレースホルダ `#ISSUE1` / `#ISSUE2` を採番後の実番号へ相互置換する。
2. `#21688` / `#21467` は番号のまま GitHub が自動リンクする。
3. 投稿前に上流の最新版で `repro/` を再実測し、修正済みでないことを確認する(本記録は 12.1.0=2026-07-09 リリース時点)。
