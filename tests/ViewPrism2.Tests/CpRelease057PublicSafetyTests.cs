using ViewPrism2.App.ViewModels;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-057: golden 用 seed は公開可能な架空データだけで構成する。
/// 実端末プロファイルや第三者作品由来の識別子が再混入したら公開前に止める。
/// </summary>
[Trait("cp", "CP-RELEASE-018")]
public sealed class CpRelease057PublicSafetyTests
{
    [Fact]
    public void 画像タブseedは架空プロファイルと一般名だけを使う()
    {
        var vm = new ImageTabSeedViewModel();
        var privateUser = string.Concat('a', 'k', 'i', 'r', 'a');
        var thirdPartyTitle = string.Concat("END", "FIELD");

        Assert.NotEmpty(vm.Collections);
        Assert.All(vm.Collections, collection =>
        {
            Assert.StartsWith(@"C:\Demo\Media\", collection.Path, StringComparison.Ordinal);
            Assert.DoesNotContain(privateUser, collection.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OneDrive", collection.Path, StringComparison.OrdinalIgnoreCase);
        });

        Assert.All(vm.Items, item =>
        {
            Assert.DoesNotContain(thirdPartyTitle, item.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(privateUser, item.Name, StringComparison.OrdinalIgnoreCase);
        });
    }
}
