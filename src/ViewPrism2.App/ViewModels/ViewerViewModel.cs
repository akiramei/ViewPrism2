using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ビューアのナビゲーション ViewModel(M-UI-014、REQ-044、G-4)。
/// Next/Prev は端で停止(ループ・例外なし。空一覧も安全 — FMEA-002)。
/// CurrentPositionText は「n / total」。並びは呼び出し元一覧の整列結果をそのまま受け取る。
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject
{
    private readonly IReadOnlyList<ImageEntry> _ordered;
    private int _index;

    public ViewerViewModel(IReadOnlyList<ImageEntry> ordered, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        _ordered = ordered;
        _index = ordered.Count == 0 ? -1 : Math.Clamp(startIndex, 0, ordered.Count - 1);
    }

    public ImageEntry? Current => _index >= 0 ? _ordered[_index] : null;

    public string? CurrentImagePath => Current?.AbsolutePath;

    /// <summary>「現在位置/総数」(REQ-044)。空一覧は「0 / 0」。</summary>
    public string CurrentPositionText => _ordered.Count == 0
        ? "0 / 0"
        : string.Create(CultureInfo.InvariantCulture, $"{_index + 1} / {_ordered.Count}");

    /// <summary>ウィンドウタイトル(現在位置/総数の表示先、REQ-044)。</summary>
    public string Title => Current is null
        ? $"ViewPrism2 — {CurrentPositionText}"
        : $"{Current.Record.FileName} — {CurrentPositionText}";

    public event EventHandler? CloseRequested;

    /// <summary>次へ(Right / PageDown)。端で停止。</summary>
    [RelayCommand]
    private void Next()
    {
        if (_index >= 0 && _index < _ordered.Count - 1)
        {
            _index++;
            RaisePositionChanged();
        }
    }

    /// <summary>前へ(Left / PageUp)。端で停止。</summary>
    [RelayCommand]
    private void Prev()
    {
        if (_index > 0)
        {
            _index--;
            RaisePositionChanged();
        }
    }

    /// <summary>閉じる(Escape / 閉じるボタン)。</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void RaisePositionChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentImagePath));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(Title));
    }
}
