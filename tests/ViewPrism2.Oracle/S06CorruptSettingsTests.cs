using System.Text.Json;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-06: 設定破損起動(spec §2.7 REQ-052、EQ-001)。
/// settings.json に不正 JSON を書き込んで Load すると、既定値(ja/1200x800/列 4)で返り例外なし。
/// .bak 退避+正常ファイル再生成。
/// </summary>
[Trait("oracle", "S-06")]
public sealed class S06CorruptSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s06-" + Guid.NewGuid().ToString("D"));

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
    public void 不正JSONのLoadは既定値で返り退避と再生成を行う()
    {
        const string corrupt = "{ \"locale\": \"en\", THIS IS NOT JSON";
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, corrupt);

        // 例外なし(Load が投げればテスト失敗として記録される)
        var settings = store.Load();

        // 既定値: ja / 1200x800 / グリッド列 4
        Assert.Equal("ja", settings.Locale);
        Assert.Equal(1200, settings.WindowWidth);
        Assert.Equal(800, settings.WindowHeight);
        Assert.Equal(4, settings.GridColumns);

        // .bak 退避(破損内容が保全される)
        Assert.True(File.Exists(store.BackupFilePath), ".bak が作成されていません。");
        Assert.Equal(corrupt, File.ReadAllText(store.BackupFilePath));

        // 正常ファイル再生成(妥当な JSON としてパースでき、既定値と同値)
        Assert.True(File.Exists(store.SettingsFilePath), "settings.json が再生成されていません。");
        var regenerated = JsonSerializer.Deserialize<AppSettings>(
            File.ReadAllText(store.SettingsFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(regenerated);
        Assert.Equal("ja", regenerated.Locale);
        Assert.Equal(1200, regenerated.WindowWidth);
        Assert.Equal(800, regenerated.WindowHeight);
        Assert.Equal(4, regenerated.GridColumns);
    }
}
