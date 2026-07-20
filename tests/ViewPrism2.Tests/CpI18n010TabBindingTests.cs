using System.Text.RegularExpressions;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Common;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-079(CP-I18N-010 拡張): 画像タブ・作業タブの多言語対応漏れの再発防止 lint(検査の谷間是正)。
/// 両 View の XAML に、文言バインディングでない直書き日本語(K-AVALONIA「XAML 直書き文字列禁止」違反=
/// REQ-051「言語変更は UI 全体へ反映」の破れ)が残っていないことを検査する。
/// これは ECO-079 のプローブ(是正前は ImageTabView 88 件 / WorkTabView 80 件で不合格)であり、
/// 同型逸脱の恒久ガードでもある。他サーフェスは全て Loc[] 配線済みのため対象外。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010XamlLintTests
{
    // ECO-080: Title を追加(全ウィンドウの Window.Title も検査軸に含める=実測で全 View 配線済み)
    private static readonly string[] TextAttrs =
        { "Text", "Content", "Watermark", "Header", "ToolTip.Tip", "PlaceholderText", "Title" };

    private static readonly Regex Japanese = new("[぀-ヿ一-鿿]");

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ViewPrism2.sln")))
            {
                return d.FullName;
            }
        }

        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }

    internal static List<string> HardcodedJapaneseInXaml(string xaml)
    {
        // コメントブロックを除去(<!-- ... -->・複数行対応)して本文だけを検査する
        var text = Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);

        var findings = new List<string>();
        foreach (var attr in TextAttrs)
        {
            foreach (Match m in Regex.Matches(text, attr + "=\"([^\"]*)\""))
            {
                var value = m.Groups[1].Value;
                if (value.StartsWith('{'))
                {
                    continue; // {Binding ...} は配線済み
                }

                if (Japanese.IsMatch(value))
                {
                    findings.Add($"{attr}=\"{value}\"");
                }
            }
        }

        return findings;
    }

    // ECO-080: ECO-079 の 2 ファイル固定 pin を src 配下の全 axaml へ一般化(横断関心事の機械ゲート化)。
    // 将来の新画面が文書参照に依存せず lint で強制される。bin/obj は列挙から除外。
    [Fact]
    public void 全Viewに直書き日本語文言が残っていない()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count >= 20, $"axaml 列挙が少なすぎる({files.Count} 件)— 列挙ロジックの空振りを疑う");

        var report = new List<string>();
        foreach (var file in files)
        {
            var findings = HardcodedJapaneseInXaml(File.ReadAllText(file));
            if (findings.Count > 0)
            {
                report.Add($"{Path.GetRelativePath(root, file)}: {findings.Count} 件 [{string.Join(" | ", findings.Take(6))}]");
            }
        }

        Assert.True(report.Count == 0,
            "i18n 未配線の直書き日本語が残存(K-AVALONIA 違反=言語切替に非追随):\n" + string.Join("\n", report));
    }

    // ECO-080(ECO-078 教訓=検査の暗黙前提は陽性対照として持つ): 検出器が空振りしていないことを固定。
    [Fact]
    public void 陽性対照_直書き日本語とバインドを検出器が正しく分別する()
    {
        const string sample = """
            <StackPanel>
              <!-- コメント内の日本語「無視される」は検出しない -->
              <TextBlock Text="直書きの日本語" />
              <TextBlock Text="{Binding Loc[some.key]}" />
              <Button Content="OK" ToolTip.Tip="ヒント文言" />
              <Window Title="タイトル直書き" />
            </StackPanel>
            """;

        var findings = HardcodedJapaneseInXaml(sample);

        Assert.Equal(3, findings.Count); // Text 直書き+ToolTip.Tip+Title(コメント/バインド/ASCII は非検出)
        Assert.Contains(findings, f => f.StartsWith("Text=", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.StartsWith("ToolTip.Tip=", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.StartsWith("Title=", StringComparison.Ordinal));
    }

    private static List<string> HardcodedJapaneseLiterals(string csPath)
    {
        var text = File.ReadAllText(csPath);
        text = Regex.Replace(text, "/\\*.*?\\*/", "", RegexOptions.Singleline); // ブロックコメント除去
        text = Regex.Replace(text, "//[^\n]*", "");                              // 行コメント除去
        var findings = new List<string>();
        foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\""))
        {
            var value = m.Groups[1].Value;
            if (Japanese.IsMatch(value))
            {
                findings.Add(value);
            }
        }

        return findings;
    }

    // GF-079-01(golden 所見 2026-07-13): ツールバーのモードボタン/軸/列見出し等が日本語固着 —
    // 直書き文言が XAML でなく VM 算出プロパティ側に残っていた。VM 層の直書き日本語も恒久ガード化する。
    [Theory]
    [InlineData("src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs")]
    [InlineData("src/ViewPrism2.App/ViewModels/WorkTabViewModel.cs")]
    [InlineData("src/ViewPrism2.App/ViewModels/ImageTabOrganizeViewModel.cs")]
    [InlineData("src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs")]
    public void タブVMに直書き日本語文言が残っていない(string relativePath)
    {
        var findings = HardcodedJapaneseLiterals(Path.Combine(RepoRoot(), relativePath));

        Assert.True(
            findings.Count == 0,
            $"{relativePath} に i18n 未配線の直書き日本語リテラルが {findings.Count} 件残存: "
            + string.Join(" | ", findings.Take(12)));
    }
}

/// <summary>
/// ECO-079: 画像タブ・作業タブが束ねた i18n キーが ja/en 両言語で解決し、言語切替で英語化することを固定する
/// (静的 lint と対で、キーの存在と切替の end-to-end を裏取りする)。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010TabKeysTests
{
    private static LocalizationService Load()
        => new(I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")), "ja");

    [Fact]
    public void 画像作業タブで束ねたキーがjaとenで解決し言語切替で英語化する()
    {
        var loc = Load();

        // ECO-079 で再利用/新設した代表キー(コレクション/ツールバー/モード/StringFormat 書式)
        (string Key, string Ja, string En)[] cases =
        {
            ("navigation.collections", "コレクション", "Collections"),
            ("navigation.workspaces", "作業スペース", "Workspaces"),
            ("toolbar.organize", "整理", "Organize"),
            ("toolbar.tagEdit", "タグ編集", "Tag Edit"),
            ("view.scanning", "スキャン中", "Scanning"),
            ("workspace.badgeDefault", "既定", "Default"),
            ("view.selectValueToApply", "値を選んで付与（{0}）", "Select a value to apply ({0})"),
            ("view.organizeTargetsBadge", "整理対象 {0}", "Organize targets {0}"),
            ("view.thresholdAtLeast", "{0} 以上", "{0} or more"),
        };

        foreach (var (key, ja, _) in cases)
        {
            Assert.Equal(ja, loc.T(key));
        }

        loc.SetLocale("en");
        foreach (var (key, _, en) in cases)
        {
            Assert.Equal(en, loc.T(key));
        }
    }
}

/// <summary>
/// GF-079-01: 画像/作業タブの VM 算出ラベル(ツールバーのモードボタン・表示軸・列見出し)が
/// 言語切替(ja→en)で英語化することを end-to-end で固定する。maintainer golden 所見の再発防止。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010TabVmLabelTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static LocalizationService RealLoc()
        => new(I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")), "ja");

    [Fact]
    public void 画像タブのモードボタンと表示軸ラベルが言語切替で英語化する()
    {
        var loc = RealLoc();
        var vm = TestImageTab.NewVm(_db, loc);

        // ja(既定)
        Assert.Equal("タグ編集", vm.EditButtonLabel);
        Assert.Equal("整理", vm.OrganizeButtonLabel);
        Assert.Equal("作業", vm.WorkButtonLabel);
        Assert.Equal("ファイルシステム", vm.AxisLabel);

        loc.SetLocale("en");
        Assert.Equal("Tag Edit", vm.EditButtonLabel);
        Assert.Equal("Organize", vm.OrganizeButtonLabel);
        Assert.Equal("Work", vm.WorkButtonLabel);
        Assert.Equal("File System", vm.AxisLabel);
    }

    [Fact]
    public void 作業タブのモードボタンと列見出しが言語切替で英語化する()
    {
        var loc = RealLoc();
        var vm = new WorkTabViewModel(
            new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new NullWindowService(), new ImageSorter(), new AppSettings(), loc);

        Assert.Equal("タグ編集", vm.EditButtonLabel);
        Assert.Equal("整理", vm.OrganizeButtonLabel);

        // en へ切替: CultureChanged が列見出しを再構築する
        loc.SetLocale("en");
        Assert.Equal("Tag Edit", vm.EditButtonLabel);
        Assert.Equal("Organize", vm.OrganizeButtonLabel);
        Assert.Contains(vm.ListColumns, c => c.Label == "Name");
        Assert.Contains(vm.ListColumns, c => c.Label == "Modified Date");

        // ja へ戻す: 列見出しも日本語へ再構築
        loc.SetLocale("ja");
        Assert.Equal("整理", vm.OrganizeButtonLabel);
        Assert.Contains(vm.ListColumns, c => c.Label == "名前");
        Assert.Contains(vm.ListColumns, c => c.Label == "更新日");
    }

    [Fact]
    public async Task 画像タブの列見出しとソート候補と件数が言語切替で英語化する()
    {
        // ECO-108(maintainer 実機所見 2026-07-17): WorkTab は GF-079-01 の Rebuild 対で追随するが
        // ImageTab に同等がなく、列見出し/ソート候補/種別チップ/件数が焼き込み言語のまま残る(対称化 probe)。
        // 種別チップは ListColumnBuilder.KindChipLabel の VM 直書き日本語(XAML lint の死角)も対象。
        var loc = RealLoc();
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C108", Path = @"C:\col-108" };
        await _db.Folders.AddAsync(col);
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = IdGenerator.NewId(), SyncFolderId = col.Id, RelativePath = "a.jpg", FileName = "a.jpg",
            FileSize = 1, Hash = "Ha108", Status = ImageStatus.Normal,
            CreatedDate = "2026-07-17T00:00:00.000Z", ModifiedDate = "2026-07-17T00:00:00.000Z",
        });

        var vm = TestImageTab.NewVm(_db, loc);
        await vm.InitializeAsync(col.Id); // FS 軸=既定 3 列

        Assert.Contains(vm.ListColumns, c => c.Label == "名前");
        Assert.Contains(vm.SortColumns, o => o.KindChip == "基本");
        Assert.Equal("1 項目", vm.CountLabel);

        loc.SetLocale("en");
        Assert.Contains(vm.ListColumns, c => c.Label == "Name");
        Assert.Contains(vm.ListColumns, c => c.Label == "Modified Date");
        Assert.DoesNotContain(vm.ListColumns, c => c.Label == "名前");
        Assert.Contains(vm.SortColumns, o => o.KindChip == "Basic");
        Assert.Equal("1 items", vm.CountLabel);

        loc.SetLocale("ja"); // 往復も追随
        Assert.Contains(vm.ListColumns, c => c.Label == "名前");
        Assert.Contains(vm.SortColumns, o => o.KindChip == "基本");
    }

    private sealed class NullWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;
        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }
}
