using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-WORKSPACE-028(ECO-020 / WorkspaceService): 作業スペースの Core 規律。
/// デフォルト回転=常に厳密 1 つ(INV-W1)・所属は集合で件数/一覧は normal のみ(INV-W2)・
/// デフォルトはリネーム不可(INV-W3)・add/move は所属の論理操作のみで物理非破壊(INV-W4)・移動は原子(INV-W5)。
/// 受け渡し(AddImagesToDefault)= 画像タブ作業「追加」の行き先(DOM-0026)。
/// </summary>
[Trait("cp", "CP-WORKSPACE-028")]
public sealed class CpWorkspace028Tests
{
    private const string Folder = "folder-1";

    private static FakeClock Clock() => new(new DateTime(2026, 6, 29, 10, 27, 33, DateTimeKind.Utc));

    private static ImageRecord Image(string id, ImageStatus status = ImageStatus.Normal) => new()
    {
        Id = id,
        SyncFolderId = Folder,
        RelativePath = $"{id}.jpg",
        FileName = $"{id}.jpg",
        FileSize = 100,
        Hash = "h",
        Status = status,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };

    private static async Task SeedAsync(TempDb db, params (string id, ImageStatus status)[] images)
    {
        await db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = "C:/f" });
        foreach (var (id, status) in images)
        {
            await db.Images.AddAsync(Image(id, status));
        }
    }

    [Fact]
    public async Task EnsureDefaultExists_シードは1件_冪等()
    {
        using var db = new TempDb(Clock());
        var service = new WorkspaceService(db.Workspaces, db.Clock);

        var first = await service.EnsureDefaultExistsAsync();
        var second = await service.EnsureDefaultExistsAsync();

        Assert.Equal(first.Id, second.Id);              // 冪等(再シードしない)
        Assert.True(first.IsDefault);
        Assert.Equal(WorkspaceService.DefaultName, first.Name);
        var all = await service.ListAsync();
        Assert.Single(all);                              // 1 件のみ
    }

    [Fact]
    public async Task デフォルト回転_常にデフォルト1つ_旧は時刻名で降格_新はデフォルト()
    {
        using var db = new TempDb(Clock());
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        var old = await service.EnsureDefaultExistsAsync();

        var fresh = await service.CreateRotatingDefaultAsync();

        var all = await service.ListAsync();
        Assert.Equal(2, all.Count);
        Assert.Single(all, w => w.Workspace.IsDefault);  // デフォルトは厳密に 1 つ(INV-W1)
        var newDef = all.Single(w => w.Workspace.IsDefault).Workspace;
        Assert.Equal(fresh.Id, newDef.Id);
        Assert.Equal(WorkspaceService.DefaultName, newDef.Name);
        var demoted = all.Single(w => w.Workspace.Id == old.Id).Workspace;
        Assert.False(demoted.IsDefault);                 // 旧デフォルトは降格
        Assert.Equal("2026/06/29 10:27", demoted.Name);  // 時刻名(決定論)
    }

    [Fact]
    public async Task 一覧は新しいほど上_デフォルトが最上段_スタック順()
    {
        using var db = new TempDb(Clock());
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        await service.EnsureDefaultExistsAsync();    // ws1(デフォルト)
        await service.CreateRotatingDefaultAsync();  // ws2(デフォルト)・ws1 降格
        await service.CreateRotatingDefaultAsync();  // ws3(デフォルト)・ws2/ws1 降格

        var all = await service.ListAsync();
        Assert.Equal(3, all.Count);
        Assert.True(all[0].Workspace.IsDefault);                          // 最上段=デフォルト(最新・カレント)
        Assert.True(all[0].Workspace.Seq > all[1].Workspace.Seq);         // 新しいほど上(seq 降順)
        Assert.True(all[1].Workspace.Seq > all[2].Workspace.Seq);
    }

    [Fact]
    public async Task リネーム_非デフォルトは可_デフォルトは不可_空はスペースへ()
    {
        using var db = new TempDb(Clock());
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        var def = await service.EnsureDefaultExistsAsync();
        await service.CreateRotatingDefaultAsync();       // def は降格(非デフォルトになる)

        // デフォルトはリネーム不可(INV-W3): 回転後の新デフォルトを対象に
        var newDef = (await service.ListAsync()).Single(w => w.Workspace.IsDefault).Workspace;
        var denied = await service.RenameAsync(newDef.Id, "新名");
        Assert.False(denied.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, denied.Error);

        // 非デフォルト(降格した旧 def)はリネーム可
        var ok = await service.RenameAsync(def.Id, "  整理中  ");
        Assert.True(ok.IsSuccess);
        Assert.Equal("整理中", (await db.Workspaces.GetByIdAsync(def.Id))!.Name);  // trim

        // 空は「スペース」へフォールバック
        var blank = await service.RenameAsync(def.Id, "   ");
        Assert.True(blank.IsSuccess);
        Assert.Equal("スペース", (await db.Workspaces.GetByIdAsync(def.Id))!.Name);
    }

    [Fact]
    public async Task 削除_非デフォルトは可_所属は外れるが画像は物理非破壊()
    {
        using var db = new TempDb(Clock());
        await SeedAsync(db, ("a", ImageStatus.Normal), ("b", ImageStatus.Normal));
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        var def = await service.AddImagesToDefaultAsync(new[] { "a", "b" });
        var other = await service.CreateRotatingDefaultAsync();    // 新デフォルト・def は降格
        await service.MoveImagesAsync(def.Id, other.Id, new[] { "a" });  // a を other へ(b は def 残留)

        // 非デフォルト(降格した旧 def)は削除可
        var ok = await service.DeleteAsync(def.Id);
        Assert.True(ok.IsSuccess);

        var all = await service.ListAsync();
        Assert.DoesNotContain(all, w => w.Workspace.Id == def.Id);  // スペースは消える
        Assert.Null(await db.Workspaces.GetByIdAsync(def.Id));
        // 画像自体は物理非破壊(INV-W4): other の a は残る・b も images には残存
        Assert.Equal(new[] { "a" }, (await service.GetImagesAsync(other.Id)).Select(i => i.Id).ToArray());
        Assert.NotNull(await db.Images.GetByIdAsync("b"));
    }

    [Fact]
    public async Task 削除_デフォルトは不可_ValidationError()
    {
        using var db = new TempDb(Clock());
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        var def = await service.EnsureDefaultExistsAsync();

        var denied = await service.DeleteAsync(def.Id);

        Assert.False(denied.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, denied.Error);
        Assert.Single(await service.ListAsync());   // デフォルトは残る(INV-W1)
    }

    [Fact]
    public async Task 削除_存在しないスペースはNotFound()
    {
        using var db = new TempDb(Clock());
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        await service.EnsureDefaultExistsAsync();

        var result = await service.DeleteAsync("no-such-ws");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
    }

    [Fact]
    public async Task 受け渡し_デフォルトへ和集合_重複なし_件数とリストはnormalのみ()
    {
        using var db = new TempDb(Clock());
        await SeedAsync(db, ("a", ImageStatus.Normal), ("b", ImageStatus.Normal), ("c", ImageStatus.Deleted));
        var service = new WorkspaceService(db.Workspaces, db.Clock);

        var def = await service.AddImagesToDefaultAsync(new[] { "a", "b", "c" });
        await service.AddImagesToDefaultAsync(new[] { "a", "b" });  // 重複追加(集合=増えない)

        var listed = await service.GetImagesAsync(def.Id);
        Assert.Equal(new[] { "a", "b" }, listed.Select(i => i.Id).ToArray());  // normal のみ・安定順
        var count = (await service.ListAsync()).Single(w => w.Workspace.Id == def.Id).NormalImageCount;
        Assert.Equal(2, count);                          // deleted は件数から除外(INV-W2)
    }

    [Fact]
    public async Task 移動_元から除去し移動先へ_同一スペースは拒否()
    {
        using var db = new TempDb(Clock());
        await SeedAsync(db, ("a", ImageStatus.Normal), ("b", ImageStatus.Normal));
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        var def = await service.AddImagesToDefaultAsync(new[] { "a", "b" });
        var other = await service.CreateRotatingDefaultAsync();  // 新デフォルト=移動先

        // 同一スペースは拒否
        var same = await service.MoveImagesAsync(def.Id, def.Id, new[] { "a" });
        Assert.False(same.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, same.Error);

        // a を def → other へ移動(原子)
        var moved = await service.MoveImagesAsync(def.Id, other.Id, new[] { "a" });
        Assert.True(moved.IsSuccess);
        Assert.Equal(new[] { "b" }, (await service.GetImagesAsync(def.Id)).Select(i => i.Id).ToArray());
        Assert.Equal(new[] { "a" }, (await service.GetImagesAsync(other.Id)).Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task 移動_存在しないスペースはNotFound()
    {
        using var db = new TempDb(Clock());
        await SeedAsync(db, ("a", ImageStatus.Normal));
        var service = new WorkspaceService(db.Workspaces, db.Clock);
        var def = await service.AddImagesToDefaultAsync(new[] { "a" });

        var result = await service.MoveImagesAsync(def.Id, "no-such-ws", new[] { "a" });
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.Error);
    }
}
