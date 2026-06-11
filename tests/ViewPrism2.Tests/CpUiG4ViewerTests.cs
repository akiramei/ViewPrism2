using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G4(unit 部分): ViewerViewModel のナビゲーション(M-UI-014、REQ-044)。
/// Next/Prev は端で停止(ループ・例外なし、空一覧含む — FMEA-002)。CurrentPositionText="n / total"。
/// 描画(フィット表示)は golden(承認者 maintainer)。
/// </summary>
[Trait("cp", "CP-UI-G4")]
public sealed class CpUiG4ViewerTests
{
    private static ImageEntry Entry(string id, string name)
    {
        var record = new ImageRecord
        {
            Id = id,
            SyncFolderId = "f",
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = new string('0', 64),
            CreatedDate = "2026-06-11T00:00:00.000Z",
            ModifiedDate = "2026-06-11T00:00:00.000Z",
        };
        return new ImageEntry(record, @"C:\img\" + name, []);
    }

    private static IReadOnlyList<ImageEntry> Three() =>
        [Entry("a", "a.jpg"), Entry("b", "b.jpg"), Entry("c", "c.jpg")];

    [Fact]
    public void 初期位置と現在位置表示()
    {
        var vm = new ViewerViewModel(Three(), startIndex: 0);

        Assert.Equal("1 / 3", vm.CurrentPositionText);
        Assert.Equal("a.jpg", vm.Current!.Record.FileName);
        Assert.Contains("1 / 3", vm.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Nextで進み末尾で停止する()
    {
        var vm = new ViewerViewModel(Three(), 0);

        vm.NextCommand.Execute(null);
        Assert.Equal("2 / 3", vm.CurrentPositionText);

        vm.NextCommand.Execute(null);
        Assert.Equal("3 / 3", vm.CurrentPositionText);

        vm.NextCommand.Execute(null); // 端で停止(ループ・例外なし)
        Assert.Equal("3 / 3", vm.CurrentPositionText);
        Assert.Equal("c.jpg", vm.Current!.Record.FileName);
    }

    [Fact]
    public void Prevで戻り先頭で停止する()
    {
        var vm = new ViewerViewModel(Three(), 2);

        vm.PrevCommand.Execute(null);
        vm.PrevCommand.Execute(null);
        Assert.Equal("1 / 3", vm.CurrentPositionText);

        vm.PrevCommand.Execute(null); // 端で停止
        Assert.Equal("1 / 3", vm.CurrentPositionText);
        Assert.Equal("a.jpg", vm.Current!.Record.FileName);
    }

    [Fact]
    public void 空一覧でもクラッシュしない_FMEA002()
    {
        var vm = new ViewerViewModel([], 0);

        Assert.Equal("0 / 0", vm.CurrentPositionText);
        Assert.Null(vm.Current);
        Assert.Null(vm.CurrentImagePath);

        vm.NextCommand.Execute(null);
        vm.PrevCommand.Execute(null);
        Assert.Equal("0 / 0", vm.CurrentPositionText);
    }

    [Fact]
    public void 範囲外の開始位置はクランプされる()
    {
        Assert.Equal("3 / 3", new ViewerViewModel(Three(), 99).CurrentPositionText);
        Assert.Equal("1 / 3", new ViewerViewModel(Three(), -5).CurrentPositionText);
    }

    [Fact]
    public void 現在画像パスはナビゲーションに追随する()
    {
        var vm = new ViewerViewModel(Three(), 0);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CurrentImagePath))
            {
                changed.Add(vm.CurrentImagePath);
            }
        };

        vm.NextCommand.Execute(null);

        Assert.Equal(@"C:\img\b.jpg", vm.CurrentImagePath);
        Assert.Single(changed);
    }

    [Fact]
    public void CloseはCloseRequestedを発火する()
    {
        var vm = new ViewerViewModel(Three(), 0);
        var raised = 0;
        vm.CloseRequested += (_, _) => raised++;

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, raised);
    }
}
