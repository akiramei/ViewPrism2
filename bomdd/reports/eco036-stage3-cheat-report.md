# ECO-036 第3段(整理モード切り出し)工場ずる報告

対象: `src/ViewPrism2.App/ViewModels/ImageTabOrganizeViewModel.cs`(新設)・
`src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs`(移送元)。設計凍結= 60-change-order-eco-036.md §12。

---

### CHEAT-E36S3-001 [friction] refreshSelectionMarkers 注入の子内非使用
- 手法が与えなかったもの: §12.2 は接続面(関数注入)として
  `getCollectionId・recompute・refreshSelectionMarkers・reloadImagesAsync` の4つを明記しているが、
  §12.5 の R1/R2/R3 変換規則を実際に適用すると、通知(Recompute()/RefreshSelectionMarkers())の
  呼び出しは全てホスト殻側(コマンドの外側)に残る設計になり、子(ImageTabOrganizeViewModel)の
  メソッド本体からは `refreshSelectionMarkers` を一度も呼ぶ必要がない(子は自 prop 通知
  `OnPropertyChanged(string.Empty)` のみ発行=将来用、という第1段/第2段と同型のパターン)。
  結果として `_refreshSelectionMarkers` フィールドはコンストラクタで保持されるが未使用に近い
  (使わなければ dead code 警告 dead-store の懸念があるが、フィールド代入自体があるため
  TreatWarningsAsErrors=true でも警告は出なかった)。
- 代替した判断: §12.2 が接続面として明示列挙している以上、実装が「今は呼ばない」としても
  コンストラクタ引数として受理する(設計の接続面契約を字面どおり満たす)ことを優先した。
  将来の呼び出し追加(段階5・残余整理等)に備えたシグネチャ据え置きと解釈。
- 重大度: friction(実害なし・設計文書と実装の必要性にわずかな不一致があるのみ)。

---

### CHEAT-E36S3-002 [blocker] 製造中の ImageTabOrganizeViewModel.cs 内容の外部書き換わり
- 手法が与えなかったもの: 製造の途中(host 側編集を完了しビルドを実行した直後)、
  自分が Write したはずの `ImageTabOrganizeViewModel.cs` の内容が、意図した API 面
  (`HasMergeTarget`/`HasOrganizeTargets`/`OrganizeTargetsCountLabel`/`IsSimilarMethod`/
  `ContinueOrganize()` 等)を持たない別内容(状態を `_targets`/`SearchMethod` 等の異なる命名・
  `ToggleTarget` が bool を返す等、設計として非同型ではないが実装細部が異なるもの)に
  置き換わっているのを検出した(ビルドエラー CS1061 多発で発覚)。設計書 §12 自体はこの間
  変化していない。原因は不明(同一リポジトリに対する並行プロセスの存在を示唆する状況証拠
  — `bomdd/60-change-order-eco-036.md` も自分が一度も編集していないのに「工程開始前から
  作業ツリーに +63 行の未コミット差分」が存在した=設計者または他プロセスが同時に
  同一リポジトリへ書き込んでいた可能性)。
- 代替した判断: 手法(order §12.5 の R1/R2/R3 + 移送表)には「実装ファイルが自分の書き込み後に
  変化しうる」という前提への対処が定義されていない。自分が最初に設計凍結どおりに起票した
  実装(本報告作成時点でホスト側 217 行の編集が既にこの API 面を前提に完了していた)との
  整合を優先し、子ファイルを自分の意図した内容で再度 Write して上書き・復旧した。
  設計からの逸脱ではなく、外的要因への防御的な再実施。
- 重大度: blocker(検出が遅れればビルド失敗のまま報告していた可能性がある)。
  再発時の一般的な備え= 自己受入ビルド直前に対象2ファイルの内容を simple grep で
  自己整合性チェックすることを推奨(還元候補)。

---

## 総括

- 重大度内訳: blocker 1 / friction 1 / minor 0(計 2 件)。
- いずれも §12 の設計判断そのものへの疑義ではない(R1/R2/R3 の適用先・移送表の対応は
  全数実施でき、裁量を要する分岐は発生しなかった)。
