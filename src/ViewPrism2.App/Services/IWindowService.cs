using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.Services;

/// <summary>ノード条件ダイアログの結果(OK 時のみ非 null。ConditionType=null は「条件なし」)。</summary>
public sealed record NodeConditionResult(HierarchyConditionType? ConditionType, string? ConditionValueJson);

/// <summary>
/// ダイアログ・子ウィンドウ表示の抽象(K-MVVM: ViewModel から View への参照禁止。
/// ダイアログ表示は IDialogService 抽象を介す)。実装は App の View 層。
/// </summary>
public interface IWindowService
{
    /// <summary>確認ダイアログ(はい/いいえ)。</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>フォルダ選択(StorageProvider)。キャンセルは null。</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>同期フォルダ管理ウィンドウ(モーダル。詳細編集: 除外パターン・サブフォルダ等)。</summary>
    Task ShowFolderManagementAsync();

    /// <summary>設定(言語)ウィンドウ(モーダル)。</summary>
    Task ShowSettingsAsync();

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

    /// <summary>再リンクウィンドウ(対象フォルダ)。</summary>
    Task ShowRelinkAsync(string folderId);

    /// <summary>ビューアウィンドウ(REQ-044)。ordered は呼び出し元一覧の整列結果。</summary>
    void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex);

    /// <summary>
    /// 類似検索ウィンドウ(REQ-065、仕様 §2.10.4)。基準画像 1 枚に対し閾値で類似画像を検索する。
    /// collectionEntries は選択中コレクションの normal 画像(結果のサムネイル表示用)。
    /// </summary>
    Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries);

    /// <summary>
    /// マージダイアログ(REQ-067、仕様 §2.10.5)。マージ先 1 枚・マージ元 1 枚以上。
    /// 統合後タグプレビュー+非破壊明示。マージされたら true。
    /// </summary>
    Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources);

    /// <summary>
    /// トラッシュ表示(REQ-067/REQ-070/REQ-071、仕様 §2.10.5/§2.11.3-4)。選択中コレクションの
    /// deleted 一覧。V4 で復元・完全削除(非破壊明示文言+確認)を含む。
    /// </summary>
    Task ShowTrashAsync(string collectionId);

    /// <summary>
    /// 修復ライフサイクル UI(REQ-072、仕様 §2.11.5)。criteria 条件検索フォーム+結果と
    /// relink フロー(missing への候補提示・選択・確定)を表示する。
    /// 既定実装は no-op(UI を持たないテストスタブ互換)。View 層 WindowService が上書きする。
    /// </summary>
    Task ShowRepairAsync(string collectionId) => Task.CompletedTask;
}
