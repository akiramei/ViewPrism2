# ViewPrism2

BomDD(BOM 駆動開発)で製造する画像管理デスクトップアプリ(.NET10/Avalonia12/SQLite)。
`bomdd/` が設計・受入・変更管理の台帳(git が正本)。UI/UX の設計原器(CAD)は別リポ
`../ViewPrismUI`(乖離時は常に CAD が正)。方法論は `../BomDD`、PLM 工具は `../BomDD-Plm`。

## 変更管理 — 作業を始める前に必ず読む

手順の正本: [bomdd/change-management.md](bomdd/change-management.md)。要点:

- **起票なき src/tests 変更は禁止**。すべての変更は ECO(登録: `bomdd/60-change-register.yaml`)から。
- **コードから入らない**: 所見はまず工程診断(CAD/BOM/実装のどこの欠陥か)。上流欠陥は上流から直す。
- **ついで修正禁止**: スコープ外所見は分離起票か 51-cheat-log 記録の二択。
- **是正はプローブ先行**: 是正前に不合格となる回帰テストで真因を実測裏取りしてから触る。
- **既存固定オラクル行(tests/ViewPrism2.Oracle)は変更しない**。受入は新規行を追加。
- **human gate は「裁定」と「golden(maintainer 実機承認)」の 2 つだけ**。それ以外は AI が進め、
  gate 到達時に「人間がやること」を明示して停止する。

### 入口スキル(自由文プロンプトの代わりにこれを使う)

| コマンド | 用途 |
|---|---|
| `/eco-file <症状・要求>` | ECO 起票+工程診断(不具合・新機能・拡張すべての入口) |
| `/eco-fix <eco-NNN>` | プローブ先行の是正+機械受入 → golden 基準提示で停止 |
| `/eco-accept <eco-NNN>` | golden 合格後のクローズ(CP 明記・register applied・教訓) |
| `/sec-advisory <CVE等>` | OSS 脆弱性の実測逆引き → 処置選択肢提示で停止 |

## 機械受入(全 ECO 共通・全て緑が納品条件)

```
dotnet build                                 # 0 error
dotnet test tests/ViewPrism2.Tests           # 全緑
dotnet test tests/ViewPrism2.Oracle          # 全緑(skip は既知 2 件)
python bomdd/validate_bom.py                 # 0 error / 0 warning(pre-commit でも走る)
```

テスト実行の時間上限はハーネス自身が宣言する(ECO-081): 5 分間テストイベントが無ければ
ハングとみなし、残存スレッドのミニダンプ+ハング中テスト名を `TestResults/*_hang.{dmp,log}`
へ吐いて強制終了する(csproj の `TestingPlatformCommandLineArguments` 既定・呼び出し側の
私的タイムアウトは不要)。ハング失敗時はまず `*_hang.log` のテスト名とダンプを見る。
同等の代替経路として xUnit v3 の exe 直接実行(`tests/*/bin/Debug/net10.0/*.exe`)も可。

## コミット規約

`起票(eco-NNN):` → `decide(eco-NNN):`(裁定のみ) → `fix(eco-NNN):` → `accept(eco-NNN):`。
コミットは指示があってから。pre-commit の validate_bom を通らない変更は直してから。
