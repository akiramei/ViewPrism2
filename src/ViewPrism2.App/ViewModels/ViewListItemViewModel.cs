using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.ViewModels;

/// <summary>左ペインのビュー一覧項目(お気に入り/最近/全画像、REQ-033)。</summary>
public sealed partial class ViewListItemViewModel : ObservableObject
{
    public ViewListItemViewModel(View? view, string displayName)
    {
        View = view;
        _displayName = displayName;
    }

    /// <summary>null = 固定入口「全画像」(仕様 §6: V1 は UI 上の固定入口として提供)。</summary>
    public View? View { get; }

    public bool IsAllImages => View is null;

    public bool IsUserView => View is not null;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private bool _isSelected;
}
