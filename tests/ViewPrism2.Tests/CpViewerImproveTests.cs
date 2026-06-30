using Avalonia.Layout;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ビューア改善(モック準拠フェーズ1): 単一フィット / 背景 / スクロール横揃え / 設定ドロワー /
/// 下部バー・シーク の VM 規律。描画自体は golden(maintainer)。設定は即時永続化(REQ-059 と同型)。
/// </summary>
[Trait("cp", "CP-VIEWER-IMPROVE")]
public sealed class CpViewerImproveTests
{
    private static ImageEntry Entry(string name)
    {
        var record = new ImageRecord
        {
            Id = name,
            SyncFolderId = "f",
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = new string('0', 64),
            CreatedDate = "2026-06-30T00:00:00.000Z",
            ModifiedDate = "2026-06-30T00:00:00.000Z",
        };
        return new ImageEntry(record, @"C:\img\" + name, []);
    }

    private static IReadOnlyList<ImageEntry> Three() => [Entry("a.jpg"), Entry("b.jpg"), Entry("c.jpg")];

    [Fact]
    public void 単一フィット切替_host可視とフラグが連動()
    {
        var vm = new ViewerViewModel(Three(), 0); // 既定 Fit
        Assert.True(vm.IsFitFit);
        Assert.True(vm.ShowNormalFit);
        Assert.False(vm.ShowNormalScroll);

        vm.SetFitCommand.Execute("width");
        Assert.True(vm.IsFitWidth);
        Assert.False(vm.ShowNormalFit);   // フィット host 非表示
        Assert.True(vm.ShowNormalScroll); // スクロール host 表示

        vm.SetFitCommand.Execute("one");
        Assert.True(vm.IsFitOne);
        Assert.True(vm.ShowNormalScroll);
    }

    [Fact]
    public void 背景切替_排他フラグ()
    {
        var vm = new ViewerViewModel(Three(), 0); // 既定 Dark
        Assert.True(vm.IsBgDark);
        Assert.False(vm.IsBgLight);

        vm.SetBackgroundCommand.Execute("checker");
        Assert.True(vm.IsBgChecker);
        Assert.False(vm.IsBgDark);
    }

    [Fact]
    public void スクロール横揃え_Avaloniaへ写像()
    {
        var vm = new ViewerViewModel(Three(), 0);
        Assert.Equal(HorizontalAlignment.Center, vm.ScrollItemAlignment); // 既定 Center

        vm.SetHAlignCommand.Execute("left");
        Assert.Equal(HorizontalAlignment.Left, vm.ScrollItemAlignment);

        vm.SetHAlignCommand.Execute("right");
        Assert.Equal(HorizontalAlignment.Right, vm.ScrollItemAlignment);
    }

    [Fact]
    public void 設定変更は即時永続化される()
    {
        ViewerSettingsModel? saved = null;
        var vm = new ViewerViewModel(Three(), 0, new ViewerSettingsModel(), m => saved = m);

        vm.SetFitCommand.Execute("one");
        Assert.Equal(FitMode.One, saved!.FitMode);

        vm.SetBackgroundCommand.Execute("light");
        Assert.Equal(BackgroundMode.Light, saved!.BackgroundMode);

        vm.SetHAlignCommand.Execute("right");
        Assert.Equal(ScrollHAlign.Right, saved!.ScrollHAlign);
    }

    [Fact]
    public void 下部バーは単一と見開きのみ_スクロールは非表示()
    {
        var vm = new ViewerViewModel(Three(), 0);
        Assert.True(vm.ShowBottomBar);   // normal
        Assert.True(vm.ShowSeek);        // 2 枚以上

        vm.SetScrollModeCommand.Execute(null);
        Assert.False(vm.ShowBottomBar);  // scroll は非表示

        vm.SetSpreadRightModeCommand.Execute(null);
        Assert.True(vm.ShowBottomBar);   // spread
        Assert.False(vm.ShowSeek);       // シークは単一のみ
    }

    [Fact]
    public void シークは単一で現在indexに連動し設定で移動する()
    {
        var vm = new ViewerViewModel(Three(), 0);
        Assert.Equal(2, vm.SeekMax);     // 3 枚 → 0..2
        Assert.Equal(0, vm.SeekValue);

        vm.SeekValue = 2;
        Assert.Equal("3 / 3", vm.CurrentPositionText);
        Assert.Equal(2, vm.SeekValue);
    }

    [Fact]
    public void 設定ドロワーのトグル()
    {
        var vm = new ViewerViewModel(Three(), 0);
        Assert.False(vm.SettingsOpen);

        vm.ToggleSettingsCommand.Execute(null);
        Assert.True(vm.SettingsOpen);

        vm.ToggleSettingsCommand.Execute(null);
        Assert.False(vm.SettingsOpen);
    }
}
