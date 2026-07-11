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
    /// <summary>ECO-071先行probe: wheel providerと描画非依存のmode/境界判定を固定する。</summary>
    [Fact]
    [Trait("cp", "CP-UI-G8")]
    public void ホイールは単一見開きを論理送りし内部スクロールとoverlayを横取りしない()
    {
        var windowType = typeof(ViewPrism2.App.Views.ViewerWindow);
        Assert.NotNull(windowType.GetMethod("OnViewerWheelChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        var resolve = windowType.GetMethod("ResolveWheelAction",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(resolve);

        int Wheel(bool continuous, bool normalScroll, double offset, double viewport, double extent,
            double dx, double dy) => (int)resolve!.Invoke(null,
                [continuous, normalScroll, offset, viewport, extent, dx, dy])!;

        Assert.Equal(1, Wheel(false, false, 0, 0, 0, 0, -1));  // Fit/spread: 下=Next
        Assert.Equal(-1, Wheel(false, false, 0, 0, 0, 0, 1));  // Fit/spread: 上=Prev
        Assert.Equal(0, Wheel(true, false, 0, 0, 0, 0, -1));   // scroll modeはcontent scroll
        Assert.Equal(0, Wheel(false, true, 20, 100, 300, 0, -1)); // Width/Original途中はpan
        Assert.Equal(1, Wheel(false, true, 200, 100, 300, 0, -1)); // 既に下端ならNext
        Assert.Equal(-1, Wheel(false, true, 0, 100, 300, 0, 1));   // 既に上端ならPrev
        Assert.Equal(0, Wheel(false, false, 0, 0, 0, 1, 0));       // horizontalだけは無視
    }

    /// <summary>ECO-071 golden不合格probe: normal内部panからのpage turnは読み進める向きの端へ着地する。</summary>
    [Fact]
    [Trait("cp", "CP-UI-G8")]
    public void スクロール可能な単一画像のホイール送りは次の先頭と前の末尾へ着地する()
    {
        var windowType = typeof(ViewPrism2.App.Views.ViewerWindow);
        var resolve = windowType.GetMethod("ResolveWheelLandingOffset",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(resolve);

        double? Landing(bool normalScroll, int action, double viewport, double extent) =>
            (double?)resolve!.Invoke(null, [normalScroll, action, viewport, extent]);

        Assert.Equal(0d, Landing(true, 1, 100, 300));       // 下端→Next: 次画像の先頭
        Assert.Equal(200d, Landing(true, -1, 100, 300));    // 上端→Prev: 前画像の末尾
        Assert.Equal(0d, Landing(true, -1, 300, 100));      // pan不能なら先頭=末尾
        Assert.Null(Landing(false, -1, 100, 300));          // Fit/spreadには内部pan着地なし
        Assert.Null(Landing(true, 0, 100, 300));            // content scroll中は着地なし
    }

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

    // ---- 画像外余白クリックで閉じる(REQ-044 v1.3/ECO-002 CR-7)の判定ロジック ----
    // ホスト 1000×800 に 800×600 画像(縮小なし scale=1)→ 描画領域は (100,100)-(900,700)

    [Theory]
    [InlineData(50, 400, true)]    // 左余白
    [InlineData(950, 400, true)]   // 右余白
    [InlineData(500, 50, true)]    // 上余白
    [InlineData(500, 750, true)]   // 下余白
    [InlineData(500, 400, false)]  // 画像中央 → 閉じない
    [InlineData(100, 100, false)]  // 画像の左上隅(境界は画像側)
    [InlineData(900, 700, false)]  // 画像の右下隅(境界は画像側)
    [InlineData(99, 400, true)]    // 境界 1px 外
    [InlineData(-10, -10, true)]   // ホスト外(マージン側)
    public void 画像外余白の判定_等倍表示(double x, double y, bool expected)
    {
        Assert.Equal(expected, ViewerViewModel.IsBackgroundPoint(1000, 800, 800, 600, x, y));
    }

    [Fact]
    public void 画像外余白の判定_縮小フィットと拡大なし()
    {
        // 縮小のみ: 2000×1500 画像 → ホスト 1000×800 では scale=0.5 → 1000×750、中央 (0,25)-(1000,775)
        Assert.False(ViewerViewModel.IsBackgroundPoint(1000, 800, 2000, 1500, 500, 400)); // 画像上
        Assert.True(ViewerViewModel.IsBackgroundPoint(1000, 800, 2000, 1500, 500, 10));   // 上余白
        Assert.True(ViewerViewModel.IsBackgroundPoint(1000, 800, 2000, 1500, 500, 790));  // 下余白

        // 拡大なし(DownOnly): 100×100 画像はホスト中央に原寸 (450,350)-(550,450)
        Assert.False(ViewerViewModel.IsBackgroundPoint(1000, 800, 100, 100, 500, 400));
        Assert.True(ViewerViewModel.IsBackgroundPoint(1000, 800, 100, 100, 200, 400));
    }

    [Fact]
    public void 画像なしは全面が余白扱い()
    {
        Assert.True(ViewerViewModel.IsBackgroundPoint(1000, 800, 0, 0, 500, 400));
    }
}
