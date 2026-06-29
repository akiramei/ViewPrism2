using ViewPrism2.Core.Common;
using ViewPrism2.Infrastructure.Database;

namespace ViewPrism2.Tests;

/// <summary>
/// 受入用の一時ファイル SQLite DB(M-HARNESS-015)。
/// 各テストクラスインスタンスごとに独立した DB ファイルとリポジトリ群を提供する。
/// </summary>
internal sealed class TempDb : IDisposable
{
    public TempDb(IClock? clock = null)
    {
        Clock = clock ?? new SystemClock();
        Directory = Path.Combine(Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));
        DbPath = Path.Combine(Directory, "viewprism2.db");
        Manager = DatabaseManager.Open(DbPath, Clock);
        Folders = new SyncFolderRepository(Manager);
        Images = new ImageRepository(Manager);
        Tags = new TagRepository(Manager);
        Views = new ViewRepository(Manager);
        Features = new ImageFeatureRepository(Manager);
        Similarities = new ImageSimilarityRepository(Manager);
        Merges = new MergeRepository(Manager);
        Workspaces = new WorkspaceRepository(Manager);
    }

    public IClock Clock { get; }

    public string Directory { get; }

    public string DbPath { get; }

    public DatabaseManager Manager { get; }

    public SyncFolderRepository Folders { get; }

    public ImageRepository Images { get; }

    public TagRepository Tags { get; }

    public ViewRepository Views { get; }

    public ImageFeatureRepository Features { get; }

    public ImageSimilarityRepository Similarities { get; }

    public MergeRepository Merges { get; }

    public WorkspaceRepository Workspaces { get; }

    public void Dispose()
    {
        Manager.Dispose();
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末失敗はテスト結果に影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
