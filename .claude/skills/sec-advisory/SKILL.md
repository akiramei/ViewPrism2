---
name: sec-advisory
description: OSS セキュリティアドバイザリ(CVE・NuGet 監査警告等)の受理。K-BOM/パッケージグラフの実測逆引き→影響判定→処置選択肢の提示(human gate=処置裁定)で停止する。裁定後の実行は経路が合流する(/eco-file または設計者適用)。
---

# /sec-advisory — OSS アドバイザリの逆引きと処置裁定の準備

典拠: [bomdd/change-management.md](../../../bomdd/change-management.md) §3.4、
BomDD playbook §8(外部劣化= DEG イベント・forward-03)。
引数: アドバイザリ(CVE 番号・NU 警告・GitHub Security Advisory URL 等)。

## 絶対規律

- **BOM トレースだけで影響判定しない**。一次資料は
  ①アドバイザリ本文(影響バージョン範囲・攻撃条件)と
  ②**パッケージグラフの実測**(`dotnet list package --include-transitive --vulnerable` 等)。
- **このスキルの中でパッケージ更新やコード変更を行わない**。処置は裁定(gate①)の後。

## 手順

1. **受理**: アドバイザリの内容(対象パッケージ・影響バージョン・深刻度・攻撃条件)を記録。
2. **実測逆引き**:
   - `dotnet list package --include-transitive` で全プロジェクトの解決バージョンを実測
     (直接参照か推移参照か・どの csproj 経由か)。
   - `bomdd/31-kbom.yaml` の調達宣言と突合(宣言と実態の乖離があればそれ自体を所見として記録)。
   - 型解決の検証規律: パッケージ分割(例: Avalonia 系)に注意し、**参照の有無は csproj/実測で
     判断する**(DLL 文字列 grep で存在判断しない)。
3. **影響判定**: 攻撃条件が本製品の使用形態(ローカルデスクトップ・ネットワーク面の有無)で
   成立するかを評価。「該当コードパス不使用」判定には根拠(呼び出しサイト grep)を付ける。
4. **scratch 交換検証**: 別作業ツリーまたは scratch で更新版へ差し替え、build+Tests+Oracle が
   緑かを確認(破壊的変更の有無)。**本ツリーには適用しない。**

## 停止点(human gate① = 処置裁定)

選択肢を以下の形で提示して停止する:

- **(a) 調達交換のみ**(バージョン更新だけ・コード変更なし):
  fresh 工場を使わず**設計者適用+全再認証**(build/Tests/Oracle/validate_bom)で閉じる。
  ECO は register に記録(発生源= DEG イベント・doc+csproj のみ)。
- **(b) コード変更を伴う**(API 破壊・回避策実装・根本解消):
  /eco-file で通常の ECO 経路へ(実例: NU1903 を ECO-026 内で根本解消)。
- **(c) 影響なし・対応不要**: 根拠(バージョン範囲外・攻撃条件不成立)つきで記録のみ。
  根拠は register か 51 に残す(「対応しなかった」を追跡可能にする)。

各選択肢に diff 規模・再認証範囲・リスク(先送りした場合の露出)を添える。
裁定を受けたら該当経路を実行する。
