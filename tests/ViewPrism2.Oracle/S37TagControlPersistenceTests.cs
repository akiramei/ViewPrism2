using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-37: タグ制御設定の永続化と破損耐性(REQ-077・spec §2.12.5・CP-SET-009 拡張、EQ-001)。
/// 設計者受入=工場非開示の独立導出。enableTagControl(既定 OFF)+ action→tag_id 6 個(既定 未割り当て)を
/// 保存→再読込で一致。未知値/型不正は項目単位で安全既定へ・例外なし。タグ制御キーなしの旧形式も既定で読める。
/// 現存しない tag_id を指すマッピングは永続値を保持する(解決時の未割り当て扱いは S-32)。
/// </summary>
[Trait("oracle", "S-37")]
public sealed class S37TagControlPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s37-" + Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void タグ制御トグルとマッピングがラウンドトリップする()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            EnableTagControl = true,
            TagActionForceLeftPage = "t-fl",
            TagActionForceRightPage = "t-fr",
            TagActionSpread = "t-spread",
            TagActionSkip = "t-skip",
            TagActionLeftPageEmpty = "t-le",
            TagActionRightPageEmpty = "t-re",
        });

        var loaded = new SettingsStore(_directory).Load();

        Assert.True(loaded.EnableTagControl);
        Assert.Equal("t-fl", loaded.TagActionForceLeftPage);
        Assert.Equal("t-fr", loaded.TagActionForceRightPage);
        Assert.Equal("t-spread", loaded.TagActionSpread);
        Assert.Equal("t-skip", loaded.TagActionSkip);
        Assert.Equal("t-le", loaded.TagActionLeftPageEmpty);
        Assert.Equal("t-re", loaded.TagActionRightPageEmpty);
    }

    [Fact]
    public void タグ制御キーなしの旧形式は既定OFF_全未割り当てで読める()
    {
        const string json = "{ \"locale\": \"ja\", \"viewerMode\": \"spread-right\" }";
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, json);

        var loaded = store.Load(); // 例外なし(前方互換)

        Assert.False(loaded.EnableTagControl);
        Assert.Null(loaded.TagActionForceLeftPage);
        Assert.Null(loaded.TagActionForceRightPage);
        Assert.Null(loaded.TagActionSpread);
        Assert.Null(loaded.TagActionSkip);
        Assert.Null(loaded.TagActionLeftPageEmpty);
        Assert.Null(loaded.TagActionRightPageEmpty);
        Assert.Equal("spread-right", loaded.ViewerMode); // 既存項目は従来どおり
    }

    [Fact]
    public void 型不正の値は項目単位で安全既定へ_他項目は保持_例外なし()
    {
        // enableTagControl が真偽でない・tagActionSkip が数値 → 当該のみ既定化。
        // tagActionForceLeftPage は妥当 → 保持。
        const string json =
            "{ \"enableTagControl\": \"notabool\", \"tagActionSkip\": 123, " +
            "\"tagActionForceLeftPage\": \"t-keep\" }";
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, json);

        var loaded = store.Load(); // 例外なし

        Assert.False(loaded.EnableTagControl);       // 型不正 → 既定 OFF
        Assert.Null(loaded.TagActionSkip);           // 型不正 → 未割り当て
        Assert.Equal("t-keep", loaded.TagActionForceLeftPage); // 妥当 → 保持
    }

    [Fact]
    public void 現存しないtag_idを指すマッピングも永続値を保持する()
    {
        // 永続層はタグ存在を検証しない(解決時に未割り当て扱い=S-32)。
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            EnableTagControl = true,
            TagActionSkip = "t-ghost-deleted", // 現存しない(かもしれない)tag_id
        });

        var loaded = new SettingsStore(_directory).Load();

        Assert.Equal("t-ghost-deleted", loaded.TagActionSkip); // 読込で消さない
    }
}
