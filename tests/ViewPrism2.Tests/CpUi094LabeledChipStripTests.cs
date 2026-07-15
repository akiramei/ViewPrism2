using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-CHIPWRAP-088 部品面への継承(ECO-094): 固定クローム チップ行の共有部品化(LabeledChipStrip)。
/// ECO-090 の暫定統制(同一ベクトル検査・コードは 2 面のまま)を構造的 DRY へ置換する —
/// 両タブのチップ行が**同一の共有部品**で実現されていることを構造 probe で固定し、
/// 意味論の等価は既存 CpUi090(パリティ)/CpUi091(容量)の無改変緑で担保する。
/// テンプレート統一(画像タブの richer template へ寄せる)が作業タブへ視覚差を持ち込まないことも pin する
/// (未定義値バッジ/ナビ chevron は IsUndef/IsNav=false で不活性=作業タブのチップ工場は
/// Neutral/Colored(isNav:false) のみ)。
/// </summary>
[Trait("cp", "CP-CHIPWRAP-088")]
public sealed class CpUi094LabeledChipStripTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    // ---- 構造 probe(是正前赤): チップ行は共有部品 LabeledChipStrip で実現される ----

    [Fact]
    public async Task 画像タブ_チップ行は共有部品LabeledChipStripで実現される()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new ImageTabView(), Width = 1366, Height = 900 };
            window.Show();
            RunJobs();
            AssertChipRowRealizedBySharedControl(window, "画像タブ");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 作業タブ_チップ行は共有部品LabeledChipStripで実現される()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new WorkTabView(), Width = 1366, Height = 900 };
            window.Show();
            RunJobs();
            AssertChipRowRealizedBySharedControl(window, "作業タブ");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// 共有部品の存在は型名で検査する(是正前は型自体が存在しない=赤)。
    /// DRY 後の後退(部品を剥がして再び 2 面コピペへ戻す)もこの probe が防ぐ。
    /// </summary>
    private static void AssertChipRowRealizedBySharedControl(Window window, string face)
    {
        var strip = window.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.GetType().Name == "LabeledChipStrip");
        Assert.True(strip is not null,
            $"{face}: チップ行が共有部品 LabeledChipStrip で実現されていない(ECO-094 DRY 契約)");
        var display = strip!.GetVisualDescendants().OfType<ItemsControl>()
            .FirstOrDefault(i => i.Name == "ChipDisplay");
        Assert.True(display is not null,
            $"{face}: LabeledChipStrip 内に ChipDisplay(チップ ItemsControl)が見つからない");
    }

    // ---- テンプレート統一の不活性 pin: 作業タブへ未定義値/ナビ意匠が漏れない(視覚不変) ----

    [Fact]
    public async Task 作業タブ_統一テンプレートでも未定義値バッジとナビ矢印は現れない()
    {
        await HeadlessApp.Session.Dispatch(() =>
        {
            var vm = NewWorkVm();
            vm.ShowChips = true;
            vm.ShowChipHint = true;
            vm.ChipHintLabel = "タグで絞り込み";
            vm.Chips.Add(ChipVM.Neutral("クリア", active: true));
            foreach (var name in new[] { "屋外", "室内", "夜景" })
            {
                vm.Chips.Add(ChipVM.Colored(name, name, "#2459cf", 3, active: false, isNav: false));
            }
            vm.WsEmpty = false;

            var window = new Window { Content = new WorkTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();

            var chips = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("tagChip") && b.IsVisible).ToList();
            Assert.Equal(4, chips.Count); // クリア+3(前提の健全性)

            // 画像タブ専用意匠は IsUndef/IsNav=false で不活性(可視要素として現れない)
            foreach (var chip in chips)
            {
                Assert.DoesNotContain(chip.GetVisualDescendants().OfType<Avalonia.Controls.PathIcon>(),
                    p => p.Classes.Contains("chipNav") && p.IsVisible);
                Assert.False(chip.Classes.Contains("undef"), "作業タブのチップに undef クラスが付与されている");
            }
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ(CpUi090 と同系) ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private WorkTabViewModel NewWorkVm() =>
        new(new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new AlwaysPresentProbe()),
            new NullWindows(), new ImageSorter(), new AppSettings(), TestLoc.Ja());

    private sealed class AlwaysPresentProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => true;
    }

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
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
    }
}
