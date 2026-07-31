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
| `repro/` | 最小再現+**修正検証**(コンソールアプリ・`WaitAsync` 期限方式・isolation を `args[0]` で切替・**版を `-p:AvaloniaHeadlessVersion=` で切替**〔既定 12.1.1〕)。ECO-141 で検証ケース 4/5(work item 本体の throw→ループ生存)を追加。ソリューション非参加。 |

## 12.1.0 実測記録(2026-07-14・本ドラフトの根拠)

- 再現実行(PerAssembly/PerTest とも同一結果): sanity 完了 → poison(cleanup の RunJobs へ例外を残す)の TCS 未遷移 → **後続 Dispatch も 10 秒未完=永久ハング再現**。例外は GC 後の `UnobservedTaskException` のみ=呼び出し側に不可視。
- 12.1.0 バイナリのデコンパイル確認: ①`EnsureXxxApplication` は保護済み(`tcs.TrySetException`=#21688)②`finally { disposable.Dispose(); }` は TCS 完了ブロック前のまま保護外(テスト本体の例外も cleanup 例外で失われる)③消費者ループの catch は `OperationCanceledException` のみ・終端故障処理なし。

## 12.1.1 修正検証記録(2026-07-24・ViewPrism2 ECO-141)

上流 **PR #21781** が **12.1.1** に含まれてリリースされたため、同じ再現ハーネスで**陽性対照つき**に再実測した
(`repro/` の版は MSBuild プロパティで切替可能=本記録は committed artifact からそのまま再現できる)。

```
dotnet run -p:AvaloniaHeadlessVersion=12.1.0   # 陽性対照
dotnet run                                      # 既定=12.1.1(検証)
```

| ケース | 12.1.0(陽性対照) | 12.1.1(検証) |
|---|---|---|
| 1) sanity | 完了 | 完了 |
| 2) poison(cleanup 段の RunJobs で例外) | **未完了**(TCS 未遷移) | **faulted**=呼び出し元へ surface |
| 3) 後続 Dispatch | **未完了=永久ハング再現** | **完了**=defect NOT reproduced |
| 4) work item 本体の throw | 未完了(※ 2 で既にループ死亡のため独立対照にはならない) | **faulted**=自身の TCS へ |
| 5) 4 の後の Dispatch | 未完了=ループ死亡 | **完了=ループ生存** |

**限定**: IL 実測では消費者ループの catch は 12.1.0/12.1.1 とも `OperationCanceledException` のみで**不変**であり、
12.1.1 の追加保護は work item 側の 1 箇所。したがって封じ込めが確認できたのは
**外部から到達できる 2 経路(cleanup 段/work item 本体)**であり、queued action の prologue/epilogue・
`finally` 本体・`ExecutionContext.Run` は依然無保護(残余経路は Avalonia 内部のみ)。

**自リポへの反映(ECO-141)**: 上記を根拠に `HeadlessApp` のリフレクション監視(`_dispatchTask` 依存)を撤去し、
`CpHarnessEco083Tests` を**内部構造の pin から挙動契約の pin へ**置換した。撤去による診断能力の後退
(原因例外全文の即時顕在化の喪失)は M-HARNESS-015 の契約へ明記。PerAssembly・SessionInitFixture・HangDump は維持。

## Issue 2 を投稿する場合の手順(将来の参考)

1. 上流の最新版で `repro/` を再実測し、#21770 が未修正であることを確認する(本記録は 12.1.0=2026-07-09 リリース時点)。
2. `issue-2-pertest-reinit-race.md` 内のプレースホルダ `#ISSUE1` を **#21770** へ置換して投稿する。
3. 投稿後、#21770 側へ関連コメント(または本文編集)で相互リンクする。
4. 経過観測: **実施済み(2026-07-24)** — #21770 は #21781 で修正され 12.1.1 に含まれた。自リポの監視撤去+pin 改訂は **ECO-141** で完了(上記「12.1.1 修正検証記録」)。
