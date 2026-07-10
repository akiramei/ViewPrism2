using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Settings;

namespace ViewPrism2.GoldenHarness;

internal static class Program
{
    private const int ImageCount = 10_000;
    private const int TagCount = 5;
    private const string FolderId = "eco058-folder";
    private const string WorkspaceId = "eco058-workspace";
    private const string Stamp = "2026-07-10T00:00:00.000Z";
    private const string AppMutexName = @"Global\ViewPrism2";
    private const string OwnershipMarker = ".viewprism2-eco058-owned";

    private static readonly string[] TagColors =
        ["#E5484D", "#30A46C", "#3E63DD", "#F5A524", "#8E4EC6"];

    public static async Task<int> Main(string[] args)
    {
        var command = args.Length == 0 ? "golden" : args[0].ToLowerInvariant();
        if (command is not ("golden" or "verify-fixture"))
        {
            Console.Error.WriteLine("usage: ViewPrism2.GoldenHarness [golden|verify-fixture]");
            return 2;
        }

        var launch = command == "golden";
        if (launch && IsAppRunning())
        {
            Console.Error.WriteLine(
                "ViewPrism2.App is already running. Close it before ECO-058 golden so the isolated profile is unambiguous.");
            return 3;
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var runRoot = Path.Combine(
            Path.GetTempPath(), $"ViewPrism2-ECO058-Golden-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var dataDir = await GenerateAndVerifyAsync(runRoot, cancellation.Token);
            if (!launch)
            {
                Console.WriteLine("ECO-058 fixture verification completed; no UI was launched.");
                return 0;
            }

            // Generation takes several seconds. Recheck the product mutex immediately before launch;
            // a process-name-only check would miss dotnet-hosted instances and races.
            if (IsAppRunning())
            {
                Console.Error.WriteLine(
                    "ViewPrism2 became active while the fixture was generated. Close it and rerun golden.");
                return 3;
            }

            var appPath = Path.Combine(
                repoRoot, "src", "ViewPrism2.App", "bin", "Release", "net10.0", "ViewPrism2.App.exe");
            if (!File.Exists(appPath))
            {
                Console.Error.WriteLine($"Release app not found: {appPath}");
                Console.Error.WriteLine("Run 'dotnet build -c Release' first.");
                return 4;
            }

            return await LaunchAndWaitAsync(appPath, dataDir, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("ECO-058 golden was cancelled; performing best-effort fixture cleanup.");
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (Directory.Exists(runRoot))
            {
                Directory.Delete(runRoot, recursive: true);
                Console.WriteLine($"Removed isolated ECO-058 fixture: {runRoot}");
            }
        }
    }

    private static async Task<string> GenerateAndVerifyAsync(
        string runRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runRoot);
        File.WriteAllText(
            Path.Combine(runRoot, OwnershipMarker),
            "Owned by M-GOLDEN-HARNESS-039; safe to remove only after ViewPrism2.App exits.");
        var imagesDir = Path.Combine(runRoot, "images");
        var dataDir = Path.Combine(runRoot, "data");
        var thumbnailsDir = Path.Combine(dataDir, "thumbnails");
        Directory.CreateDirectory(imagesDir);
        Directory.CreateDirectory(thumbnailsDir);

        Console.WriteLine("Generating isolated ECO-058 golden fixture (10,000 images)...");
        var jpeg = CreateJpeg();
        var thumbnails = new ThumbnailService(thumbnailsDir);
        var sourcePaths = new string[ImageCount];
        for (var i = 0; i < ImageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(imagesDir, $"image-{i:D5}.jpg");
            sourcePaths[i] = sourcePath;
            await File.WriteAllBytesAsync(sourcePath, jpeg, cancellationToken);
            await File.WriteAllBytesAsync(
                thumbnails.GetCachePath(sourcePath), jpeg, cancellationToken);
        }

        var dbPath = Path.Combine(dataDir, "viewprism2.db");
        using (var db = DatabaseManager.Open(dbPath, new SystemClock()))
        {
            await db.RunAsync(conn =>
            {
                using var transaction = conn.BeginTransaction();
                Execute(conn, transaction,
                    "INSERT INTO sync_folders(id,name,path,is_active,include_subfolders,exclude_patterns,last_scan) VALUES($id,$name,$path,1,1,'[]',$at)",
                    ("$id", FolderId), ("$name", "ECO-058 Golden 10k"),
                    ("$path", imagesDir), ("$at", Stamp));
                Execute(conn, transaction,
                    "INSERT INTO workspaces(id,name,is_default,seq,created_at) VALUES($id,$name,1,0,$at)",
                    ("$id", WorkspaceId), ("$name", "10,000件"), ("$at", Stamp));

                for (var tag = 0; tag < TagCount; tag++)
                {
                    Execute(conn, transaction,
                        "INSERT INTO tags(id,name,type,color) VALUES($id,$name,'simple',$color)",
                        ("$id", $"eco058-tag-{tag}"), ("$name", $"計測タグ{tag + 1}"),
                        ("$color", TagColors[tag]));
                }

                using var image = conn.CreateCommand();
                image.Transaction = transaction;
                image.CommandText = """
                    INSERT INTO images(id,sync_folder_id,relative_path,file_name,file_size,hash,status,created_date,modified_date)
                    VALUES($id,$folder,$relative,$name,$size,$hash,'normal',$created,$modified)
                    """;
                var imageId = image.Parameters.Add("$id", SqliteType.Text);
                image.Parameters.AddWithValue("$folder", FolderId);
                var relative = image.Parameters.Add("$relative", SqliteType.Text);
                var name = image.Parameters.Add("$name", SqliteType.Text);
                image.Parameters.AddWithValue("$size", jpeg.Length);
                image.Parameters.AddWithValue("$hash", "eco058-identical-content");
                var created = image.Parameters.Add("$created", SqliteType.Text);
                var modified = image.Parameters.Add("$modified", SqliteType.Text);
                image.Prepare();

                using var membership = conn.CreateCommand();
                membership.Transaction = transaction;
                membership.CommandText =
                    "INSERT INTO workspace_images(workspace_id,image_id,added_at) VALUES($workspace,$image,$at)";
                membership.Parameters.AddWithValue("$workspace", WorkspaceId);
                var memberImage = membership.Parameters.Add("$image", SqliteType.Text);
                var addedAt = membership.Parameters.Add("$at", SqliteType.Text);
                membership.Prepare();

                using var imageTag = conn.CreateCommand();
                imageTag.Transaction = transaction;
                imageTag.CommandText = "INSERT INTO image_tags(image_id,tag_id) VALUES($image,$tag)";
                var taggedImage = imageTag.Parameters.Add("$image", SqliteType.Text);
                var taggedTag = imageTag.Parameters.Add("$tag", SqliteType.Text);
                imageTag.Prepare();

                for (var i = 0; i < ImageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = $"eco058-image-{i:D5}";
                    var fileName = $"image-{i:D5}.jpg";
                    var timestamp = $"2026-07-{(i % 10) + 1:D2}T00:00:00.000Z";

                    imageId.Value = id;
                    relative.Value = fileName;
                    name.Value = fileName;
                    created.Value = timestamp;
                    modified.Value = timestamp;
                    image.ExecuteNonQuery();

                    memberImage.Value = id;
                    addedAt.Value = timestamp;
                    membership.ExecuteNonQuery();

                    taggedImage.Value = id;
                    for (var tag = 0; tag < TagCount; tag++)
                    {
                        taggedTag.Value = $"eco058-tag-{tag}";
                        imageTag.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
                return Task.CompletedTask;
            }, cancellationToken);
        }

        new SettingsStore(dataDir).Save(new AppSettings
        {
            Locale = "ja",
            WindowWidth = 1366,
            WindowHeight = 900,
            DisplayMode = "grid",
            WorkTabDisplayMode = "grid",
            LastCollectionId = FolderId,
        });

        await VerifyAsync(dataDir, sourcePaths, thumbnails, cancellationToken);
        Console.WriteLine($"VIEWPRISM2_DATA_DIR={dataDir}");
        return dataDir;
    }

    private static async Task VerifyAsync(
        string dataDir, IReadOnlyList<string> sourcePaths, ThumbnailService thumbnails,
        CancellationToken cancellationToken)
    {
        var dbPath = Path.Combine(dataDir, "viewprism2.db");
        using (var db = DatabaseManager.Open(dbPath, new SystemClock()))
        {
            var counts = await db.RunAsync(conn => Task.FromResult((
                Images: Scalar(conn, "SELECT COUNT(*) FROM images"),
                Tags: Scalar(conn, "SELECT COUNT(*) FROM tags"),
                ImageTags: Scalar(conn, "SELECT COUNT(*) FROM image_tags"),
                Workspaces: Scalar(conn, "SELECT COUNT(*) FROM workspaces"),
                WorkspaceImages: Scalar(conn, "SELECT COUNT(*) FROM workspace_images"))));

            var expected = (
                Images: (long)ImageCount,
                Tags: (long)TagCount,
                ImageTags: (long)ImageCount * TagCount,
                Workspaces: 1L,
                WorkspaceImages: (long)ImageCount);
            if (counts != expected)
                throw new InvalidDataException($"Golden DB count mismatch: actual={counts}, expected={expected}");
        }

        if (sourcePaths.Count != ImageCount)
            throw new InvalidDataException($"Source count mismatch: {sourcePaths.Count}");

        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VerifyDimensions(sourcePath);
            VerifyDimensions(thumbnails.GetCachePath(sourcePath));
        }

        var settings = new SettingsStore(dataDir).Load();
        if (settings.LastCollectionId != FolderId || settings.WorkTabDisplayMode != "grid")
            throw new InvalidDataException("Golden settings do not select the isolated grid fixture.");

        Console.WriteLine(
            "Verified fixture: images=10000, workspace_images=10000, image_tags=50000, " +
            "source/cache dimensions=256x192(all), WorkTab=grid.");
    }

    private static async Task<int> LaunchAndWaitAsync(
        string appPath, string dataDir, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
        };
        start.Environment["VIEWPRISM2_DATA_DIR"] = dataDir;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to launch ViewPrism2.App.");

        var windowReady = false;
        try
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    break;

                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    windowReady = true;
                    break;
                }

                await Task.Delay(250, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process);
            throw;
        }

        if (!windowReady)
        {
            var detail = process.HasExited
                ? $"exit code {process.ExitCode} before creating a window"
                : "no main window within 20 seconds";
            await StopProcessAsync(process);
            throw new InvalidOperationException(
                $"The isolated Release app did not become ready ({detail}). Golden was not executed.");
        }

        Console.WriteLine($"Launched isolated Release app with a ready window (PID={process.Id}).");
        Console.WriteLine("Close the app after the ECO-058 golden checks; the fixture will then be removed automatically.");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process);
            throw;
        }
        return process.ExitCode;
    }

    private static bool IsAppRunning()
    {
        var processes = Process.GetProcessesByName("ViewPrism2.App");
        try
        {
            if (processes.Length > 0)
                return true;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        try
        {
            if (!Mutex.TryOpenExisting(AppMutexName, out var mutex))
                return false;

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return; // Process exited between HasExited and Kill.
        }

        await process.WaitForExitAsync();
    }

    private static byte[] CreateJpeg()
    {
        using var bitmap = new SKBitmap(256, 192, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(35, 46, 64));
        using var paint = new SKPaint { IsAntialias = false };
        for (var y = 0; y < 192; y += 16)
        {
            paint.Color = new SKColor((byte)(55 + y), (byte)(95 + y / 2), (byte)(180 - y / 3));
            canvas.DrawRect(0, y, 256, 16, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80)
            ?? throw new InvalidOperationException("Failed to encode the ECO-058 JPEG fixture.");
        return data.ToArray();
    }

    private static void VerifyDimensions(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width != 256 || codec.Info.Height != 192)
            throw new InvalidDataException($"Golden image dimension mismatch: {path}");
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? throw new InvalidDataException(sql));
    }

    private static void Execute(
        SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values)
            command.Parameters.AddWithValue(value.Name, value.Value);
        command.ExecuteNonQuery();
    }

    private static string FindRepoRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ViewPrism2.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("ViewPrism2.sln was not found from the harness output path.");
    }
}
