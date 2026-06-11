using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.Services;

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

    /// <summary>同期フォルダ管理ウィンドウ(モーダル)。</summary>
    Task ShowFolderManagementAsync();

    /// <summary>タグ管理ウィンドウ(モーダル)。</summary>
    Task ShowTagManagementAsync();

    /// <summary>設定(言語)ウィンドウ(モーダル)。</summary>
    Task ShowSettingsAsync();

    /// <summary>タグ編集ダイアログ。保存されたら true。</summary>
    Task<bool> ShowTagEditorAsync(Tag? existing);

    /// <summary>ビュー編集ダイアログ。変更があったら true。</summary>
    Task<bool> ShowViewEditorAsync(View? existing);

    /// <summary>再リンクウィンドウ(対象フォルダ)。</summary>
    Task ShowRelinkAsync(string folderId);

    /// <summary>ビューアウィンドウ(REQ-044)。ordered は呼び出し元一覧の整列結果。</summary>
    void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex);
}
