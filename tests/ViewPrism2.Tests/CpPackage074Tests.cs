using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-074(gate①裁定=案A+ユーザー文書配下・B-2 未選択は案イ): バックアップ置き場所の管理。
/// ①書き出し既定出力先=管理フォルダ(<Documents>/ViewPrism2/collections。settings null=既定)
/// ②取り込みウィザードは表示直後に picker 自動起動(CAD の 2 状態定義=未選択は定常状態でない)
/// ③キャンセル残留時はプレースホルダ文言(未選択の空白カードを見せない)。
/// </summary>
[Trait("cp", "CP-PACKAGE-032")]
public sealed class CpPackage074Tests : IDisposable
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
    public void 書き出しの既定出力先は管理フォルダ配下になる()
    {
        var folder = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var exporter = new CollectionPackageExporter(_db.Manager, _db.Clock, "9.9.9");
        var vm = new CollectionExportViewModel(exporter, [folder], CreateLoc(), (_, _) => Task.FromResult<string?>(null));

        var managed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ViewPrism2", "collections");
        Assert.Equal(Path.Combine(managed, "旅行" + CollectionPackageFormat.FileExtension), vm.OutputPath);
    }

    [Fact]
    public async Task 取り込みウィザードは表示直後にファイル選択を開きキャンセル残留時はプレースホルダを示す()
    {
        var loc = CreateLoc();
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var pickCount = 0;
        var vm = new CollectionImportViewModel(importer, [collection], loc,
            _ => { pickCount++; return Task.FromResult<string?>(null); },
            () => Task.FromResult<IReadOnlyList<Tag>>([]));
        await Session.Dispatch(() =>
        {
            var window = new CollectionImportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                // ② 表示直後に picker が 1 回だけ自動起動される(案イ)
                Assert.Equal(1, pickCount);

                // ③ キャンセル残留(未選択)ではプレースホルダ文言が可視
                var placeholder = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Name == "PackagePlaceholder");
                Assert.True(placeholder is not null, "ECO-074③: PackagePlaceholder が無い");
                Assert.Equal(loc.T("package.selectFilePrompt"), placeholder!.Text);
                Assert.True(placeholder.IsVisible, "ECO-074③: 未選択なのにプレースホルダが不可視");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
