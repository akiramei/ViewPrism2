using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Package;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-077(SS-001 再裁定・gate①裁定=案A): 入口集約の挙動 probe。
/// ①B-1 はダイアログ内コレクション選択(CAD「対象コレクションは B-1 内で選択」)
/// ②B-2 は取り込み先コレクション選択(案A・B-1 対称。未選択では「次へ」不活性)
/// ③画像タブ ⋯ は誘導コマンド(設定 ▸ データとバックアップを開く)のみで実体コマンドを持たない。
/// </summary>
[Trait("cp", "CP-UI-G13")]
public sealed class CpUiEco077EntryTests : IDisposable
{
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static LocalizationService CreateLoc() => new(I18nResourceLoader.Load(
        Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));

    [Fact]
    public async Task B1書き出しはダイアログ内コレクション選択を持ち既定出力先が選択へ追随する()
    {
        var loc = CreateLoc();
        var folder = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var folder2 = new SyncFolder { Id = "col-2", Name = "家族", Path = @"C:\col2" };
        var exporter = new CollectionPackageExporter(_db.Manager, _db.Clock, "9.9.9");
        var vm = new CollectionExportViewModel(
            exporter, [folder, folder2], loc, (_, _) => Task.FromResult<string?>(null));
        await Session.Dispatch(() =>
        {
            var window = new CollectionExportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                var selector = window.GetVisualDescendants().OfType<ComboBox>()
                    .FirstOrDefault(c => c.Name == "ExportCollectionSelector");
                Assert.True(selector is not null,
                    "ECO-077①: B-1 のコレクション選択(ExportCollectionSelector)が無い(入口コレクション固定のまま)");
                Assert.Equal(2, selector!.ItemCount);

                // 既定=先頭(mock は常に選択済み状態)・選択変更で既定出力先の <名前> が追随
                Assert.Same(folder, vm.SelectedCollection);
                Assert.EndsWith("旅行" + CollectionPackageFormat.FileExtension, vm.OutputPath);
                vm.SelectedCollection = folder2;
                Assert.EndsWith("家族" + CollectionPackageFormat.FileExtension, vm.OutputPath);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task B2取り込みは取り込み先コレクション選択を持つ()
    {
        var loc = CreateLoc();
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var vm = new CollectionImportViewModel(importer, [collection], loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        await Session.Dispatch(() =>
        {
            var window = new CollectionImportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                var selector = window.GetVisualDescendants().OfType<ComboBox>()
                    .FirstOrDefault(c => c.Name == "ImportTargetSelector");
                Assert.True(selector is not null,
                    "ECO-077②: B-2 の取り込み先選択(ImportTargetSelector)が無い(入口コレクション固定のまま)");

                // 案A: 既定=未選択。互換 OK でも取り込み先を選ぶまで「次へ」不活性(互換 OK と AND)
                Assert.Null(vm.SelectedTarget);
                vm.Header = new PackageHeader(1, 1, [], null, null,
                    "2026-07-12T08:51:53.502Z", "1.0.0",
                    new PackageCollection("src-1", "旅行", null), [], 5);
                Assert.True(vm.VerifyOk);
                Assert.False(vm.CanProceed, "ECO-077②: 取り込み先未選択なのに次へが活性");
                vm.SelectedTarget = collection;
                Assert.True(vm.CanProceed);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void 画像タブは誘導コマンドのみで書き出し取り込みの実体コマンドを持たない()
    {
        // M5: ⋯ メニューは「設定でバックアップ・移送…」誘導 1 項目のみ。
        // VM 契約: OpenBackupSettingsCommand(設定 ▸ データとバックアップを開く)が実体 2 コマンドを置換する。
        var t = typeof(ImageTabViewModel);
        Assert.True(t.GetProperty("OpenBackupSettingsCommand") is not null,
            "ECO-077③: 誘導コマンド(OpenBackupSettingsCommand)が無い");
        Assert.True(t.GetProperty("ExportCollectionCommand") is null,
            "ECO-077③: 実体コマンド(ExportCollectionCommand)が残っている(M5 違反)");
        Assert.True(t.GetProperty("ImportCollectionCommand") is null,
            "ECO-077③: 実体コマンド(ImportCollectionCommand)が残っている(M5 違反)");
    }
}
