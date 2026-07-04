# ECO-036 第1段(god-VM 解体系列・ゴミ箱切り出し)cheat-report

対象: `bomdd/60-change-order-eco-036.md` §8(第1段 設計凍結)の移送表に基づく機械的製造。
以下は移送表・§8 から導けなかった判断の全件記録(義務)。

---

### CHEAT-E36S1-001 [接続面の不足] OpenTrash の MoreMenuOpen クローズが注入インターフェースに無い
- 手法が与えなかったもの: §8.2 の接続面はコンストラクタ関数注入を
  `getCollectionId / reloadImagesAsync / recompute / fmtSize` の4つと明記。しかし移送対象の
  `OpenTrash`(旧 1195 行)は `MoreMenuOpen = false`(ホストの ⋯ メニュー開閉状態)を副作用として
  持っており、これは上記4関数のどれでも表現できない(`recompute` は `Recompute()` を呼ぶだけで
  `MoreMenuOpen` には触れない)。他の「ホスト残置」コマンド(OpenRepair/EnterDelete)は同様の
  `MoreMenuOpen = false` を保持したままホストに残るため対称性がない。
- 代替した判断: 注入関数を1つ追加(`closeMoreMenu: Action`)し、子の `OpenTrash` から
  `_closeMoreMenu()` を呼ぶことで挙動(⋯メニューが即座に閉じる)を完全保存した。
  `ImageTabTrashViewModel` のコンストラクタは 9 引数(images/trash/windows +
  getCollectionId/reloadImagesAsync/recompute/fmtSize/closeMoreMenu/resolveAbsolutePath)になっている
  (§8.2 の想定より2つ多い。理由は本項と次項)。
- 重大度: friction

### CHEAT-E36S1-002 [接続面の不足] 絶対パス解決(_collectionPath)への依存が注入インターフェースに無い
- 手法が与えなかったもの: `LoadTrashItemsAsync`(旧 1214 行)は `_collectionPath`
  (コレクションID→ルートパス辞書・ホストの private フィールド)と `Path.Combine` で
  `TrashPopupItemVM` の絶対パスを組み立てている。§8.2 の注入契約 4 点にはこの依存の解決手段がない。
- 代替した判断: `resolveAbsolutePath: Func<string,string>` を追加注入し、ホスト側に
  `ResolveAbsolutePath(string relativePath)`(`BuildEntry` と同型のロジック)を新設してそれを渡した。
  子は `_collectionPath` を直接参照しない(接続面の禁止=子はホスト型を参照しない、は維持)。
- 重大度: friction

### CHEAT-E36S1-003 [絶対契約との構造的衝突] 既存テストがホストの旧公開契約(trash系メンバー)を直接参照
- 手法が与えなかったもの: 移送表は「子へ移送」対象を ImageTabViewModel から除去する前提だが、
  既存テスト `CpUiG1TrashPopupTests.cs`・`CpUiG1MaintenanceMenuTests.cs`・`CpUiG1DeleteModeTests.cs`
  (計 84 箇所)が `vm.OpenTrashCommand` `vm.TrashOpen` `vm.HasTrash` 等、ホスト VM の
  trash 系メンバーを**直接**呼んでいる。§8.3 は「tests/」を影響なし予測(diff ゼロ)に含めており、
  移送表どおりに実装するとテスト無改訂(絶対契約)と両立しない矛盾があった。
  §5 停止条件(「移送表で分類できないメンバーが出る」)に近い事態だが、stop/report ではなく
  実装で解消可能と判断し継続した。
- 代替した判断: ホスト(ImageTabViewModel)に **後方互換の委譲プロパティ・コマンド**
  (`HasTrash`/`TrashCount`/`TrashOpen`/`TrashPopupItems`/`TrashPopupCount`/`HasTrashItems`/
  `TrashPopupEmpty`/`HasTrashSel`/`TrashSelCount`/`TrashSelCountLabel`/`TrashSelectAllLabel`/
  `CanRestoreTrash`/`CanPurgeTrash`/`OpenTrashCommand`/`CloseTrashCommand`/`ToggleTrashItemCommand`/
  `ToggleTrashSelectAllCommand`/`RestoreSelectedTrashCommand`/`PurgeSelectedTrashCommand`/
  `EmptyTrashCommand`)を全て `Trash.*` への単純転送として追加した。状態・ロジックは完全に
  `ImageTabTrashViewModel` が所有し、ホスト側は薄い転送のみ(§8.2 の所有権移転の精神は維持)。
  XAML のバインディングパスは移送表どおり `Trash.*` へ書き換え済みで、この委譲プロパティは
  **テスト後方互換専用**(XAML からは使われない)。
- 重大度: blocker(これが無いと自己受入が build エラーで止まり、絶対契約(テスト無改訂)と
  境界裁定(子への完全移送)のどちらかを破らざるを得なかった)

### CHEAT-E36S1-004 [影響集合の後始末] ホスト private フィールド `_trash`(TrashService)の除去
- 手法が与えなかったもの: `DeleteToTrash` の実行ループを `Trash.MoveToTrashAsync(ids)` へ委譲した結果、
  ホストの `private readonly TrashService _trash` フィールドが未使用になった(CS0169 相当の警告要因)。
  §8.3 の影響なし予測は「Core/Infrastructure 全域 diff ゼロ」等を述べるのみで、ホストの未使用
  private フィールドの扱いには触れていない。受入基準「workspace lint error/warn 0」を満たすため
  対応が必要だった。
- 代替した判断: `_trash` フィールドとその代入行を削除(コンストラクタの `trash` パラメータ自体は
  `Trash` 子 VM の構築に使うため残置=シグネチャ変更なし・DI 影響なし)。
- 重大度: minor

### CHEAT-E36S1-005 [影響集合の判断] bomdd/32-mbom.yaml への M-UI-TRASH-032 宣言は本製造の範囲外とした
- 手法が与えなかったもの: §8.3 の影響あり表は `bomdd/32-mbom.yaml`(unit 宣言+lineage)を含むが、
  本タスクの指示(「変更対象の現物3ファイル」)は C# 2 ファイル+XAML 1 ファイルのみを明示し、
  bomdd/ 配下の編集を工場の作業範囲に含めていなかった。
- 代替した判断: `32-mbom.yaml` への `M-UI-TRASH-032` 宣言・`M-UI-016` の partial-split 註は実施せず、
  本 cheat-report に記録するに留めた(設計者側のフォローアップ想定)。
- 重大度: friction

### CHEAT-E36S1-006 [観測] bomdd/60-change-order-eco-036.md と 60-change-register.yaml が着手前から変更済みだった
- 手法が与えなかったもの: 製造着手時点の `git status` で、上記2ファイルは既にワーキングツリーで
  変更されていた(register の `status: staged` → `in-progress` 等)。本工場はこれらを一切編集して
  いない(diff は製造開始前から存在)。絶対契約の「3ファイル外への diff 禁止」との整合を確認する
  必要があったため記録する。
- 代替した判断: 自分の作業に起因しないことを diff 内容の確認で検証し、そのまま維持(何もしない)。
- 重大度: minor
