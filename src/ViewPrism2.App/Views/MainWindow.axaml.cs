using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// シェルの View(M-UI-013)。ロジックは MainWindowViewModel に置き、
/// ここはポインタイベント→VM 呼び出し・TreeView 選択同期・ビューポート幅供給のみ(K-MVVM)。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DoubleClickDetector _doubleClick = new();
    private MainWindowViewModel? _viewModel;
    private bool _syncingTreeSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainWindowViewModel);
    }

    private void Attach(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // VM 側の選択変更(ホームタグ初期選択・選択復元)を TreeView へ反映する
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTreeNode) &&
            _viewModel is not null &&
            !_syncingTreeSelection &&
            !Equals(NodeTree.SelectedItem, _viewModel.SelectedTreeNode))
        {
            NodeTree.SelectedItem = _viewModel.SelectedTreeNode;
        }
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _syncingTreeSelection = true;
        try
        {
            _viewModel.SelectedTreeNode = NodeTree.SelectedItem as GraphNodeViewModel;
        }
        finally
        {
            _syncingTreeSelection = false;
        }
    }

    private void OnViewItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not null &&
            sender is Control { DataContext: ViewListItemViewModel item } control &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _viewModel.SelectViewListItemCommand.Execute(item);
        }
    }

    /// <summary>
    /// グリッドセル/リスト行のポインタ操作(REQ-041 v1.3: クリック・Ctrl+クリック・
    /// SHIFT+クリック範囲 union・ダブルクリック)。
    /// ダブルクリック判定は ClickCount に加えて自前検出(DoubleClickDetector)で補完する
    /// (DF-4: ポインタ経路・活性化・微小移動で ClickCount が 2 に達しない事例への堅牢化)。
    /// </summary>
    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null ||
            sender is not Control { DataContext: ImageItemViewModel item } control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var detected = _doubleClick.ObserveClick(
            item, (long)e.Timestamp, SystemDoubleClickTimeMs, ctrl || shift);
        var isDouble = e.ClickCount >= 2 || detected;
        _viewModel.Browser.HandleItemPointer(item, ctrl, shift, isDouble);
    }

    /// <summary>OS のダブルクリック時間(ms)。本アプリは Windows 専用(仕様 §1)。</summary>
    private static double SystemDoubleClickTimeMs
    {
        get
        {
            try
            {
                return GetDoubleClickTime();
            }
            catch (EntryPointNotFoundException)
            {
                return 500; // 既定値(Windows 標準)
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    /// <summary>コレクション項目のクリック(REQ-053 v1.3/CR-2: 選択スコープの切替)。</summary>
    private void OnCollectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not null &&
            sender is Control { DataContext: FolderRowViewModel row } control &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            _viewModel.SelectCollectionCommand.Execute(row);
        }
    }

    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // セル辺・列幅の計算用にコンテンツ幅を VM へ供給(スクロールバー余裕 20px)
        _viewModel?.Browser.UpdateViewportWidth(Math.Max(0, e.NewSize.Width - 20));
    }
}
