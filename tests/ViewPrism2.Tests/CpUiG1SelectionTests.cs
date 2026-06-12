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

        browser.HandleItemPointer(items[0], ctrl: false, shift: false, isDoubleClick: false);
        browser.HandleItemPointer(items[1], ctrl: false, shift: false, isDoubleClick: false);

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

        browser.HandleItemPointer(items[0], ctrl: true, shift: false, isDoubleClick: false);
        browser.HandleItemPointer(items[2], ctrl: true, shift: false, isDoubleClick: false);
        browser.HandleItemPointer(items[1], ctrl: true, shift: false, isDoubleClick: false);

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
        browser.HandleItemPointer(items[0], true, false, false);
        browser.HandleItemPointer(items[1], true, false, false);
        browser.HandleItemPointer(items[2], true, false, false);

        browser.HandleItemPointer(items[1], true, false, false); // 中間を解除

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

        browser.HandleItemPointer(items[1], ctrl: false, shift: false, isDoubleClick: true);

        Assert.Same(items[1], opened);
        Assert.Empty(browser.Selection);
    }

    [Fact]
    public void タグ編集モード中はダブルクリックでビューアを開かない()
    {
        // REQ-041 v1.2: ダブルクリック=ビューア起動(タグ編集モード中は無効)
        var browser = WithImages("a.jpg", "b.jpg");
        var items = browser.SortedItems;
        var openedCount = 0;
        browser.OpenItemRequested += (_, _) => openedCount++;

        browser.SuppressOpenItem = true;
        browser.HandleItemPointer(items[0], ctrl: false, shift: false, isDoubleClick: true);
        Assert.Equal(0, openedCount);

        browser.SuppressOpenItem = false;
        browser.HandleItemPointer(items[0], ctrl: false, shift: false, isDoubleClick: true);
        Assert.Equal(1, openedCount);
    }

    [Fact]
    public void RestoreSelectionは選択順を保って復元し見つからないidは読み飛ばす()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg");
        var items = browser.SortedItems;

        browser.RestoreSelection([items[2].Record.Id, "missing-id", items[0].Record.Id]);

        Assert.Equal(1, items[2].SelectionOrder);
        Assert.Equal(2, items[0].SelectionOrder);
        Assert.False(items[1].IsSelected);
        Assert.Equal(2, browser.Selection.Count);
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
        browser.HandleItemPointer(browser.SortedItems[0], false, false, false);
        Assert.Single(browser.Selection);

        browser.SetImages([Entry("x", "x.jpg")]);

        Assert.Empty(browser.Selection);
        Assert.Null(browser.LastSelected);
    }

    // ---- レスポンシブ自動列(REQ-041 v1.3/CR-1: 列数 = clamp(⌊幅/220px⌋, 2, 8)) ----

    [Theory]
    [InlineData(0, 2)]      // 幅 0 → 下限クランプ
    [InlineData(219, 2)]    // ⌊219/220⌋=0 → 2(下限)
    [InlineData(439, 2)]    // ⌊439/220⌋=1 → 2(下限)
    [InlineData(440, 2)]    // ちょうど 2 列
    [InlineData(659, 2)]
    [InlineData(660, 3)]    // ちょうど 3 列
    [InlineData(1100, 5)]
    [InlineData(1759, 7)]
    [InlineData(1760, 8)]   // ちょうど 8 列
    [InlineData(2200, 8)]   // ⌊2200/220⌋=10 → 8(上限)
    [InlineData(99999, 8)]  // 上限クランプ
    public void レスポンシブ列算出はclamp境界を満たす(double width, int expected)
    {
        Assert.Equal(expected, ImageBrowserViewModel.ComputeColumns(width));
    }

    [Fact]
    public void ビューポート幅の供給で列数が自動算出され行が組み直される()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg");

        browser.UpdateViewportWidth(1100); // ⌊1100/220⌋=5 列
        Assert.Equal(5, browser.GridColumns);
        Assert.Single(browser.Rows);
        Assert.Equal(5, browser.Rows[0].Cells.Count);

        browser.UpdateViewportWidth(660); // 3 列
        Assert.Equal(3, browser.GridColumns);
        Assert.Equal(2, browser.Rows.Count);
        Assert.Equal(3, browser.Rows[0].Cells.Count);
        Assert.Equal(2, browser.Rows[1].Cells.Count);

        browser.UpdateViewportWidth(100); // 下限 2 列
        Assert.Equal(2, browser.GridColumns);
        Assert.Equal(3, browser.Rows.Count);
    }

    // ---- SHIFT+クリック範囲選択(REQ-041 v1.3/CR-3: union・置換しない・選択順は index 昇順で末尾へ追番) ----

    [Fact]
    public void Shiftクリックは最後の選択からクリック位置までを既存選択へ追加する()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg");
        var items = browser.SortedItems;

        browser.HandleItemPointer(items[1], ctrl: false, shift: false, isDoubleClick: false); // b を単一選択
        browser.HandleItemPointer(items[3], ctrl: false, shift: true, isDoubleClick: false);  // b..d を union

        Assert.Equal(1, items[1].SelectionOrder); // 既存選択は保持(置換しない)
        Assert.Equal(2, items[2].SelectionOrder); // index 昇順で末尾へ追番
        Assert.Equal(3, items[3].SelectionOrder);
        Assert.False(items[0].IsSelected);
        Assert.False(items[4].IsSelected);
        Assert.Equal(3, browser.Selection.Count);
    }

    [Fact]
    public void Shiftクリックは既存選択を置換せずunionする()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg");
        var items = browser.SortedItems;

        browser.HandleItemPointer(items[4], ctrl: true, shift: false, isDoubleClick: false); // e(順 1)
        browser.HandleItemPointer(items[0], ctrl: true, shift: false, isDoubleClick: false); // a(順 2)
        browser.HandleItemPointer(items[2], ctrl: false, shift: true, isDoubleClick: false); // a..c を union

        Assert.Equal(1, items[4].SelectionOrder); // 既存はそのまま(置換しない)
        Assert.Equal(2, items[0].SelectionOrder); // アンカー(最後の選択)もそのまま
        Assert.Equal(3, items[1].SelectionOrder); // 新規分のみ index 昇順で追番
        Assert.Equal(4, items[2].SelectionOrder);
        Assert.Equal(4, browser.Selection.Count);
    }

    [Fact]
    public void Shiftクリックは上方向の範囲も選択できる()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg", "d.jpg");
        var items = browser.SortedItems;

        browser.HandleItemPointer(items[3], ctrl: false, shift: false, isDoubleClick: false); // d を選択
        browser.HandleItemPointer(items[1], ctrl: false, shift: true, isDoubleClick: false);  // b..d

        Assert.Equal(1, items[3].SelectionOrder);
        Assert.Equal(2, items[1].SelectionOrder); // 新規分は index 昇順(b → c)で追番
        Assert.Equal(3, items[2].SelectionOrder);
        Assert.False(items[0].IsSelected);
    }

    [Fact]
    public void 選択なしでのShiftクリックはクリック項目のみ選択する()
    {
        var browser = WithImages("a.jpg", "b.jpg", "c.jpg");
        var items = browser.SortedItems;

        browser.HandleItemPointer(items[2], ctrl: false, shift: true, isDoubleClick: false);

        Assert.Single(browser.Selection);
        Assert.Equal(1, items[2].SelectionOrder);
    }

    // ---- ダブルクリック判定フォールバック(DF-4 堅牢化: ClickCount 非依存の自前検出) ----

    [Fact]
    public void 同一アイテムへの時間内連続クリックはダブルクリックと判定する()
    {
        var detector = new DoubleClickDetector();
        var item = new object();

        Assert.False(detector.ObserveClick(item, 1000, 500));
        Assert.True(detector.ObserveClick(item, 1300, 500));  // 300ms 後 → ダブル
        Assert.False(detector.ObserveClick(item, 1400, 500)); // 成立後はリセット(3 連打で再成立しない)
    }

    [Fact]
    public void 時間超過や別アイテムはダブルクリックにならない()
    {
        var detector = new DoubleClickDetector();
        var a = new object();
        var b = new object();

        Assert.False(detector.ObserveClick(a, 1000, 500));
        Assert.False(detector.ObserveClick(a, 1600, 500)); // 600ms 後 → 時間超過
        Assert.False(detector.ObserveClick(b, 1700, 500)); // 別アイテム
        Assert.True(detector.ObserveClick(b, 1800, 500));  // b の 2 回目
    }

    [Fact]
    public void 修飾キー付きクリックは判定対象外で状態をリセットする()
    {
        var detector = new DoubleClickDetector();
        var item = new object();

        Assert.False(detector.ObserveClick(item, 1000, 500));
        Assert.False(detector.ObserveClick(item, 1100, 500, hasModifiers: true)); // Ctrl/Shift は選択操作
        Assert.False(detector.ObserveClick(item, 1200, 500)); // リセット済みなので 1 回目扱い
        Assert.True(detector.ObserveClick(item, 1300, 500));
    }

    // ---- ソート選択肢(REQ-038 v1.3/CR-4: created_date を UI から除外・後方互換は温存) ----

    [Fact]
    public void ソート選択肢にcreated_dateが含まれない()
    {
        var browser = NewBrowser();
        Assert.DoesNotContain(browser.SortFieldOptions, o => o.Value == SortField.CreatedDate);
        Assert.Equal(
            [SortField.Name, SortField.ModifiedDate, SortField.FileSize],
            browser.SortFieldOptions.Select(o => o.Value));
    }

    [Fact]
    public void DB既存値created_dateは後方互換として従来どおり整列に使える()
    {
        var browser = NewBrowser();
        browser.SetImages(
        [
            new ImageEntry(new ImageRecord
            {
                Id = "1",
                SyncFolderId = "f",
                RelativePath = "b.jpg",
                FileName = "b.jpg",
                FileSize = 10,
                Hash = new string('0', 64),
                CreatedDate = "2026-06-12T00:00:00.000Z", // 新しい
                ModifiedDate = "2026-06-01T00:00:00.000Z",
            }, @"C:\img\b.jpg", []),
            new ImageEntry(new ImageRecord
            {
                Id = "2",
                SyncFolderId = "f",
                RelativePath = "a.jpg",
                FileName = "a.jpg",
                FileSize = 10,
                Hash = new string('0', 64),
                CreatedDate = "2026-06-10T00:00:00.000Z", // 古い
                ModifiedDate = "2026-06-02T00:00:00.000Z",
            }, @"C:\img\a.jpg", []),
        ]);

        browser.SetSort(SortField.CreatedDate, SortDirection.Asc); // 例外なく受理(REQ-038 後方互換)

        Assert.Equal(SortField.CreatedDate, browser.SortField);
        Assert.Equal(["a.jpg", "b.jpg"], browser.SortedItems.Select(i => i.FileName)); // created_date 昇順

        // 選択肢を選び直したら後方互換フィールドは解除される
        browser.SetSort(SortField.Name, SortDirection.Asc);
        Assert.Equal(SortField.Name, browser.SortField);
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
    public void ビューポート幅と自動算出列数から正方形セル辺を算出する()
    {
        var browser = WithImages("a.jpg");
        browser.UpdateViewportWidth(1000); // ⌊1000/220⌋=4 列
        Assert.Equal(4, browser.GridColumns);
        var size4 = browser.CellSize;

        browser.UpdateViewportWidth(1500); // 6 列(幅あたりのセル辺は縮む)
        Assert.Equal(6, browser.GridColumns);
        var size6 = browser.CellSize;

        Assert.True(size4 > 0);
        Assert.True(size6 > 0);
        Assert.True(size4 >= size6 - 30); // 同水準(220px 基準)に保たれる
    }
}
