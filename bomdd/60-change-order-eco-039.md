# Change Order — ECO-039(applied): FL-002/FL-004 裁定の取り込み — 一覧表示状態の永続範囲

> 種別: 設計確定の取り込み(欠陥是正でない)。CAD 裁定= ViewPrismUI `docs/decisions/FL-002-004-persistence.md`
> (maintainer 2026-07-04・CAD コミット `d1f94ac`)。発端= ECO-038 診断で検出した作業タブの片方向依存
> (表示形式を読むが書かない)を R3 で本裁定へ送付したもの。

## 1. 取り込む裁定

- **FL-002 = S-a**: ソート状態(列・方向)は**画面ローカル(揮発・再起動で解除・永続しない)**。
  現実装どおり= **コード変更なし(明文化のみ)**。ビュー切替・表示列編集による自動解除と整合。
- **FL-004 = D-b**: 表示形式(グリッド/リスト)は**タブごとに独立永続**。
  作業タブ専用キーを新設し、初回(専用キー未保存)は画像タブの共通設定 `DisplayMode` を
  初期値に読む。以後は作業タブの切替を専用キーへ保存し、**画像タブとは連動しない**。
  既存 `DisplayMode` は画像タブ用として不変(互換・移行不要)。

## 2. 工程診断

CAD 裁定済み(gate① 通過・`d1f94ac`)・BOM 改訂は M4 で同期・実装対象は D-b のみ。
62(移行オラクル)不要: DB スキーマ不変。settings.json への nullable キー追加は
欠落=既定値フォールバック(CP-SET-009 の項目単位破損耐性と同型)で移行が発生しない。

## 3. 実装方針(D-b)

- `AppSettings` に `WorkTabDisplayMode`(string?・null=未保存)を追加(Entities.cs)。
- `WorkTabViewModel.InitializeAsync`: `_layout = WorkTabDisplayMode ?? DisplayMode` で初期化
  (専用キー優先・未保存時は共通キー= 初回挙動不変)。
- `SetGrid`/`SetList`: `_settings.WorkTabDisplayMode` へ書き込み(画像タブ CR-6 と同型の即時書込。
  保存自体はアプリ終了時の SettingsStore.Save 一括 — 既存フロー不変)。
- 受入テスト先行(拡張のオラクル・ファースト): 独立性(作業タブ切替が共通キーを汚さない)・
  復元(同一 settings で再構築した VM が専用キーを復元)・初回フォールバック(未保存時は共通キー)・
  専用キー優先、を CpUiG1WorkTabTests に追加し、**実装前に不合格を確認**してから実装する。

## 4. 影響 BOM

- E-UI-WORKSPACE-043(作業タブ surface・挙動仕様の追加)/ M-UI-WORKSPACE-029(WorkTabViewModel)
- M-SET-010(AppSettings キー追加。SettingsStore のロジック変更なし)
- 画像タブ(E-UI-BROWSE-022)は**不変**(S-a=現状追認・DisplayMode 意味論不変)
- オラクル影響なし(UI 設定・DB 不変)。既存固定オラクル行の改訂なし(R6)

## 5. 実施記録(2026-07-04 — 機械受入完了・golden 待ち)

- 受入テスト先行: CpUiG1WorkTabTests へ 2 件追加(独立保存+復元 / 初回フォールバック+専用キー優先)。
  **実装前に 2 件のみ不合格(529 中)を確認**してから実装(拡張のオラクル・ファースト)。
- 実装: AppSettings.WorkTabDisplayMode(null=未保存)+WorkTabViewModel の初期化
  (`WorkTabDisplayMode ?? DisplayMode`)と SetGrid/SetList の専用キー書込。diff= 3 ファイル小変更。
- 機械受入: build 0 警告 0 エラー・**Tests 529/529**・Oracle 100+2skip・validate_bom 0/0。
- 記録(環境): dotnet test の並行実行で MSB4166(テストホスト残骸のファイルロック)が発生。
  製品欠陥ではなく治具運用の注意(単独実行で解消・前提不成立の区別= playbook §4.4 の趣旨)。

## 6. 残ゲート

1. 受入テスト先行(赤確認)→ D-b 実装 → 機械受入(build 0 / Tests / Oracle / validate_bom 0-0)
2. golden(maintainer 実機・**再起動跨ぎ**):
   - 作業タブを list へ切替 → アプリ再起動 → 作業タブは list のまま復元・**画像タブは grid のまま**(独立)
   - 画像タブを list へ切替 → 再起動 → 画像タブ list・**作業タブは直前の作業タブ設定のまま**(連動しない)
3. クローズ時: M4 同期(spec §2.6 の作業タブ節へ永続仕様追記・M-UI-WORKSPACE-029 as-built・
   S-a の明文化= spec ソート節へ「画面ローカル(FL-002=S-a)」注記)+register applied 化

## 7. クローズ(2026-07-04 golden 合格)

- maintainer 実機(再起動跨ぎ): 作業タブ list→再起動→list 復元+画像タブ grid のまま(独立)OK・
  画像タブ切替が作業タブへ波及しないこと OK。
- M4 同期: spec ソート節へ S-a 明文化+settings 節へ WorkTabDisplayMode 追記・
  M-SET-010 schema/M-UI-WORKSPACE-029 as-built 注記。register= applied・golden approved。
- ECO-038 の R3 送付事項(片方向依存)は本 ECO で解消 — 検出(038)→裁定(CAD d1f94ac)→取り込み(039)の
  3 段が R3 分離起票の完結形。
