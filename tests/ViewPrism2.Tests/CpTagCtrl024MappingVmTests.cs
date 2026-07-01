using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TAGCTRL-024(VM 層): マッピング編集の即時適用/永続化と「既定に戻す」(ResetTagControlMapping)。
/// Core 純粋計算は CpTagCtrl024Tests、本ファイルは ViewerViewModel の設定反映・バッジ・reset を検査する。
/// ECO-022 golden G-11(GF-TAGCTRL-02: モーダル リワークで追加した ResetTagControlMappingCommand)の回帰。
/// </summary>
[Trait("cp", "CP-TAGCTRL-024")]
public sealed class CpTagCtrl024MappingVmTests
{
    private static ViewerViewModel NewViewer(out List<ViewerSettingsModel> saved)
    {
        var items = Enumerable.Range(0, 4).Select(i => Entry($"img{i}.jpg")).ToList();
        var persisted = new List<ViewerSettingsModel>();
        saved = persisted;
        return new ViewerViewModel(items, startIndex: 0, new ViewerSettingsModel(), model => persisted.Add(model));
    }

    [Fact]
    public void SetTagActionMapping_即時反映_バッジ更新_永続化()
    {
        var vm = NewViewer(out var saved);
        Assert.Equal(0, vm.MappedActionCount);
        Assert.Equal("0/6", vm.TagControlMappingBadge);

        vm.SetTagActionMapping(ViewerTagAction.ForceLeftPage, "tag-a");
        Assert.Equal(1, vm.MappedActionCount);
        Assert.Equal("1/6", vm.TagControlMappingBadge);

        vm.SetTagActionMapping(ViewerTagAction.Skip, "tag-b");
        Assert.Equal(2, vm.MappedActionCount);
        Assert.Equal("2/6", vm.TagControlMappingBadge);

        // 即時保存(REQ-077/078): 変更ごとに persist が呼ばれる
        Assert.NotEmpty(saved);
        Assert.Equal("tag-a", vm.Settings.TagActionMap.GetValueOrDefault(ViewerTagAction.ForceLeftPage));
        Assert.Equal("tag-b", vm.Settings.TagActionMap.GetValueOrDefault(ViewerTagAction.Skip));
    }

    [Fact]
    public void ResetTagControlMapping_全アクション未割り当てへ_即時保存()
    {
        var vm = NewViewer(out var saved);
        vm.SetTagActionMapping(ViewerTagAction.ForceLeftPage, "tag-a");
        vm.SetTagActionMapping(ViewerTagAction.ForceRightPage, "tag-b");
        vm.SetTagActionMapping(ViewerTagAction.Skip, "tag-c");
        Assert.Equal(3, vm.MappedActionCount);

        var savedCountBefore = saved.Count;
        vm.ResetTagControlMappingCommand.Execute(null);

        Assert.Equal(0, vm.MappedActionCount);
        Assert.Equal("0/6", vm.TagControlMappingBadge);
        Assert.All(vm.TagActionRows, r => Assert.False(r.HasAssignment));
        Assert.True(saved.Count > savedCountBefore); // reset も即時保存
    }

    [Fact]
    public void ResetTagControlMapping_既に空なら何もしない_冪等()
    {
        var vm = NewViewer(out var saved);
        Assert.Equal(0, vm.MappedActionCount);

        // 構築時の初期化で persist が走りうるため、reset 呼び出し直前の件数を基準にする。
        var savedCountBefore = saved.Count;
        vm.ResetTagControlMappingCommand.Execute(null);

        Assert.Equal(0, vm.MappedActionCount);
        Assert.Equal(savedCountBefore, saved.Count); // 空→空は新たに永続化しない(不要な書き込みを避ける)
    }

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
            CreatedDate = "2026-07-01T00:00:00.000Z",
            ModifiedDate = "2026-07-01T00:00:00.000Z",
        };
        return new ImageEntry(record, @"C:\img\" + name, []);
    }
}
