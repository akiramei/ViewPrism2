using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(unit 部分): グリッド選択ロジック・空状態判定(M-UI-013、REQ-041)。
/// クリック=単一選択 / Ctrl+クリック=トグル(選択順バッジ 1 起点昇順)/ ダブルクリック=表示要求。
/// 描画(セル整列・省略表示)は golden(承認者 maintainer)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1SelectionTests
{
    private static LocalizationService NewLoc() => new(
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string> { ["image.gridView.noImages"] = "画像がありません" },
            ["en"] = new Dictionary<string, string> { ["image.gridView.noImages"] = "No images" },
        });

    private static ImageBrowserViewModel NewBrowser() => new(NewLoc(), new ImageSorter());

    private static ImageEntry Entry(string id, string name, IReadOnlyList<EvalTagValue>? tags = null)
    {
        var record = new ImageRecord
        {
            Id = id,
            SyncFolderId = "f",
            RelativePath = name,
            FileName = name,
            FileSize = 10,
            Hash = new string('0', 64),
            CreatedDate = "2026-06-11T00:00:00.000Z",
            ModifiedDate = "2026-06-11T00:00:00.000Z",
        };
        return new ImageEntry(record, @"C:\img\" + name, tags ?? []);
    }

    private static ImageBrowserViewModel WithImages(params string[] names)
    {
        var browser = NewBrowser();
        browser.SetImages(names.Select((n, i) => Entry($"id{i:D2}", n)).ToList());
        return browser;
    }

    [Fact]
    public void クリックは単一選択で置き換える()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg");
        var items = browser.SortedItems;

        browser.HandleItemPointer(items[0], ctrl: false, isDoubleClick: false);
        browser.HandleItemPointer(items[1], ctrl: false, isDoubleClick: false);

        Assert.False(items[0].IsSelected);
        Assert.Null(items[0].SelectionOrder);
        Assert.True(items[1].IsSelected);
        Assert.Equal(1, items[1].SelectionOrder);
        Assert.Single(browser.Selection);
    }

    [Fact]
    public void Ctrlクリックはトグルで選択順バッジは1起点昇順()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg");
        var items = browser.SortedItems;

        browser.HandleItemPointer(items[0], ctrl: true, isDoubleClick: false);
        browser.HandleItemPointer(items[2], ctrl: true, isDoubleClick: false);
        browser.HandleItemPointer(items[1], ctrl: true, isDoubleClick: false);

        Assert.Equal(1, items[0].SelectionOrder);
        Assert.Equal(2, items[2].SelectionOrder);
        Assert.Equal(3, items[1].SelectionOrder);
        Assert.Equal("1", items[0].SelectionOrderText);
    }

    [Fact]
    public void Ctrlクリックで解除すると残りが採番し直される()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg");
        var items = browser.SortedItems;
        browser.HandleItemPointer(items[0], true, false);
        browser.HandleItemPointer(items[1], true, false);
        browser.HandleItemPointer(items[2], true, false);

        browser.HandleItemPointer(items[1], true, false); // 中間を解除

        Assert.False(items[1].IsSelected);
        Assert.Null(items[1].SelectionOrder);
        Assert.Equal(1, items[0].SelectionOrder); // 1 起点昇順を維持
        Assert.Equal(2, items[2].SelectionOrder);
        Assert.Equal(2, browser.Selection.Count);
    }

    [Fact]
    public void ダブルクリックは表示要求を発火し選択は変えない()
    {
        var browser = WithImages("a.jpg", "b.jpg");
        var items = browser.SortedItems;
        ImageItemViewModel? opened = null;
        browser.OpenItemRequested += (_, item) => opened = item;

        browser.HandleItemPointer(items[1], ctrl: false, isDoubleClick: true);

        Assert.Same(items[1], opened);
        Assert.Empty(browser.Selection);
    }

    [Fact]
    public void 画像0件で空状態になる()
    {
        var browser = NewBrowser();
        Assert.True(browser.IsEmpty); // 初期状態
        Assert.Equal("画像がありません", browser.EmptyMessage);

        browser.SetImages([Entry("a", "a.jpg")]);
        Assert.False(browser.IsEmpty);

        browser.SetImages([]);
        Assert.True(browser.IsEmpty); // NodeGraph 0 件もグリッドと同じ空状態(仕様 §2.6)
        Assert.Empty(browser.Rows);
    }

    [Fact]
    public void 画像差し替えで選択はクリアされる()
    {
        var browser = WithImages("a.jpg");
        browser.HandleItemPointer(browser.SortedItems[0], false, false);
        Assert.Single(browser.Selection);

        browser.SetImages([Entry("x", "x.jpg")]);

        Assert.Empty(browser.Selection);
        Assert.Null(browser.LastSelected);
    }

    [Fact]
    public void 行リストは列数で分割され列数変更で組み直される()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg");
        Assert.Equal(4, browser.GridColumns); // 既定 4(REQ-041)
        Assert.Equal(2, browser.Rows.Count);
        Assert.Equal(4, browser.Rows[0].Cells.Count);
        Assert.Single(browser.Rows[1].Cells);

        browser.GridColumns = 5;
        Assert.Single(browser.Rows);
        Assert.Equal(5, browser.Rows[0].Cells.Count);

        browser.GridColumns = 3;
        Assert.Equal(2, browser.Rows.Count);
        Assert.Equal(3, browser.Rows[0].Cells.Count);
        Assert.Equal(2, browser.Rows[1].Cells.Count);
    }

    [Fact]
    public void 整列は現在のソート設定に従い切替で並べ直す()
    {
        var browser = NewBrowser();
        browser.SetImages([Entry("2", "b.jpg"), Entry("1", "A.jpg"), Entry("3", "c.jpg")]);

        // 既定: name asc(OrdinalIgnoreCase)
        Assert.Equal(["A.jpg", "b.jpg", "c.jpg"], browser.SortedItems.Select(i => i.FileName));

        browser.SetSort(SortField.Name, SortDirection.Desc);
        Assert.Equal(["c.jpg", "b.jpg", "A.jpg"], browser.SortedItems.Select(i => i.FileName));
    }

    [Fact]
    public void ビューポート幅と列数から正方形セル辺を算出する()
    {
        var browser = WithImages("a.jpg");
        browser.UpdateViewportWidth(1000);
        var size4 = browser.CellSize;

        browser.GridColumns = 6;
        var size6 = browser.CellSize;

        Assert.True(size4 > size6);
        Assert.True(size6 > 0);
    }
}
