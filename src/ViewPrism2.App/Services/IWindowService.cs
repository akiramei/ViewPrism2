using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.Services;

/// <summary>
/// 配置タグの設定ダイアログの結果(OK 時のみ非 null。ConditionType=null は「条件なし」)。
/// ExpansionMode/HideEmptyValues は ECO-086(REQ-096)の展開モード(既定=Observed で従来互換)。
/// </summary>
public sealed record NodeConditionResult(
    HierarchyConditionType? ConditionType,
    string? ConditionValueJson,
    HierarchyExpansionMode ExpansionMode = HierarchyExpansionMode.Observed,
    bool HideEmptyValues = false);

/// <summary>
/// 配置タグの設定ダイアログへの入力(ECO-086: 展開モード+条件の 2 セクション)。
/// DefinedValuesAvailable=false のとき、定義値/定義+観測の選択で警告(観測値フォールバック)を表示する(裁定 e)。
/// </summary>
public sealed record NodeSettingsRequest(
    HierarchyConditionType? ConditionType,
    string? ConditionValueJson,
    HierarchyExpansionMode ExpansionMode,
    bool HideEmptyValues,
    bool DefinedValuesAvailable);

/// <summary>設定ウィンドウの節(ECO-077/E-1: 誘導導線が「データとバックアップ」節を直接開くための指定)。</summary>
public enum SettingsSection
{
    General,
    DataBackup,
}

/// <summary>
/// ダイアログ・子ウィンドウ表示の抽象(K-MVVM: ViewModel から View への参照禁止。
/// ダイアログ表示は IDialogService 抽象を介す)。実装は App の View 層。
/// </summary>
public interface IWindowService
{
    /// <summary>
    /// 確認ダイアログ(ECO-126/CMP-011)。confirmLabel は応答が行為を名指す動詞ラベル
    /// (REG-C5「はい/いいえ」禁止=必須引数で型強制)。destructive=true で赤塗り CTA。
    /// cancelLabel 省略時は common.cancel。
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
        bool destructive = false, string? cancelLabel = null);

    /// <summary>フォルダ選択(StorageProvider)。キャンセルは null。</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>同期フォルダ管理ウィンドウ(モーダル。詳細編集: 除外パターン・サブフォルダ等)。</summary>
    Task ShowFolderManagementAsync();

    /// <summary>
    /// 二段階スキャン(ECO-130/REQ-100): 再スキャンの差分計算→サマリー確認→適用/破棄をモーダルで実施。
    /// 戻り値= 結末(適用/破棄/失敗)。破棄・キャンセル・失敗は DB 完全無変更。
    /// 既定実装=破棄(テスト Stub 追随の負担を避ける。実体は WindowService)。
    /// </summary>
    Task<ScanStagingOutcome> ShowScanStagingAsync(SyncFolder folder)
        => Task.FromResult(ScanStagingOutcome.Discarded);

    /// <summary>設定ウィンドウ(モーダル)。</summary>
    Task ShowSettingsAsync();

    /// <summary>
    /// 設定ウィンドウを指定節で開く(ECO-077/M5: ⋯ メニューの誘導が「データとバックアップ」節を開く)。
    /// 既定実装は節指定なしへ委譲(テストスタブ互換)。View 層 WindowService が上書きする。
    /// </summary>
    Task ShowSettingsAsync(SettingsSection section) => ShowSettingsAsync();

    /// <summary>スナップショット管理(ECO-072 A-1。入口=設定 ▸ データとバックアップ・SS-001 再裁定=ECO-077)。</summary>
    Task ShowSnapshotsAsync();

    /// <summary>
    /// コレクションを書き出す(ECO-073 B-1)。入口=設定 ▸ データとバックアップ(SS-001 再裁定=ECO-077/M5)。
    /// コレクション文脈を持たないため対象は B-1 内で選択する。既定実装は no-op(テストスタブ互換)。
    /// </summary>
    Task ShowCollectionExportAsync() => Task.CompletedTask;

    /// <summary>
    /// コレクションを取り込む(ECO-073 B-2〜B-4 ウィザード)。入口=設定 ▸ データとバックアップ
    /// (SS-001 再裁定=ECO-077/M5)。取り込み先は B-2 内で選択する(gate①裁定=案A・B-1 対称)。
    /// 既定実装は no-op(テストスタブ互換)。
    /// </summary>
    Task ShowCollectionImportAsync() => Task.CompletedTask;

    /// <summary>タグ作成/編集ダイアログ(タグパレットの「追加」「編集」)。保存されたら true。</summary>
    Task<bool> ShowTagEditorAsync(Tag? existing);

    /// <summary>ビュー作成/編集ダイアログ(v1.2: 名前(必須)+説明)。保存されたら true。</summary>
    Task<bool> ShowViewEditDialogAsync(View? existing);

    /// <summary>
    /// numeric タグの値入力ダイアログ(REQ-046)。「全画像に同じ値」(固定値)または
    /// 「選択順に連番」(開始値+選択順 i)。min/max/step はダイアログ内で検証する。
    /// 戻り値=選択順に並んだ適用値列。キャンセル・検証不能なら null(適用 0 件)。
    /// </summary>
    Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
        Tag tag, NumericTagSettings? settings, int selectionCount);

    /// <summary>階層ノードの条件設定ダイアログ(textual/numeric のみ)。キャンセルなら null。</summary>
    Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
        Tag tag, HierarchyConditionType? currentType, string? currentValueJson);

    /// <summary>
    /// 配置タグの設定ダイアログ(ECO-086: 展開モード+条件)。キャンセルなら null。
    /// 既定実装は条件のみの旧ダイアログへ委譲(テストスタブ互換)。実装(WindowService)が上書きする。
    /// </summary>
    Task<NodeConditionResult?> ShowNodeSettingsDialogAsync(Tag tag, NodeSettingsRequest request)
        => ShowNodeConditionDialogAsync(tag, request.ConditionType, request.ConditionValueJson);

    /// <summary>再リンクウィンドウ(対象フォルダ)。</summary>
    Task ShowRelinkAsync(string folderId);

    /// <summary>ビューアウィンドウ(REQ-044)。ordered は呼び出し元一覧の整列結果。</summary>
    void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex);

    // ECO-051: ShowSimilarSearchAsync / ShowMergeAsync / ShowTrashAsync は撤去。
    // V3 旧 UI(独立モーダル)は ECO-014 で整理トレイへ置換され、ECO-024 の legacy 撤去で
    // 呼び出し元が消滅した残骸だった(類似検索/マージ=整理トレイ・トラッシュ=インペイン ポップアップが実体)。

    /// <summary>
    /// 修復ライフサイクル UI(REQ-072、仕様 §2.11.5)。criteria 条件検索フォーム+結果と
    /// relink フロー(missing への候補提示・選択・確定)を表示する。
    /// 既定実装は no-op(UI を持たないテストスタブ互換)。View 層 WindowService が上書きする。
    /// </summary>
    Task ShowRepairAsync(string collectionId) => Task.CompletedTask;
}
