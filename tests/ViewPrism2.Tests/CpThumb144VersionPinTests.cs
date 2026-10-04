using System.Text.RegularExpressions;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-144/REQ-104: SkiaSharp の版が実装・調達台帳・Service BOM で一致し、
/// exact な版文字列であることを検査する。
/// </summary>
[Trait("cp", "CP-THUMB-007")]
public sealed class CpThumb144VersionPinTests
{
    private const string InfrastructureProject = "src/ViewPrism2.Infrastructure/ViewPrism2.Infrastructure.csproj";
    private const string ManufacturingBom = "bomdd/32-mbom.yaml";
    private const string ServiceBom = "bomdd/53-service-bom.yaml";

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ViewPrism2.sln"))) return d.FullName;
        }

        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }

    [Fact]
    [Trait("req", "REQ-104")]
    public void SkiaSharp版を三つの実ファイルから抽出できる()
    {
        var versions = ReadVersions();

        Assert.False(string.IsNullOrWhiteSpace(versions.Project));
        Assert.False(string.IsNullOrWhiteSpace(versions.ManufacturingBom));
        Assert.False(string.IsNullOrWhiteSpace(versions.ServiceBom));
    }

    [Fact]
    [Trait("req", "REQ-104")]
    public void SkiaSharp版は実装と二つの台帳で一致する()
    {
        var versions = ReadVersions();

        Assert.Equal(versions.Project, versions.ManufacturingBom);
        Assert.Equal(versions.Project, versions.ServiceBom);
    }

    [Fact]
    [Trait("req", "REQ-104")]
    public void SkiaSharp版は三箇所ともExactである()
    {
        var versions = ReadVersions();
        var exactVersion = new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);

        Assert.Matches(exactVersion, versions.Project);
        Assert.Matches(exactVersion, versions.ManufacturingBom);
        Assert.Matches(exactVersion, versions.ServiceBom);
    }

    private static Versions ReadVersions()
    {
        var projectText = ReadRequiredFile(InfrastructureProject);
        var manufacturingBomText = ReadRequiredFile(ManufacturingBom);
        var serviceBomText = ReadRequiredFile(ServiceBom);

        var projectVersion = ExtractRequired(
            InfrastructureProject,
            "PackageReference Include=\"SkiaSharp\" の Version",
            projectText,
            new Regex(
                @"<PackageReference\b(?=[^>]*\bInclude\s*=\s*[\""']SkiaSharp[\""'])[^>]*\bVersion\s*=\s*[\""']([^\""']+)[\""'][^>]*>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant));

        var manufacturingBomVersion = ExtractRequired(
            ManufacturingBom,
            "procurement 行の package: SkiaSharp に続く version",
            manufacturingBomText,
            new Regex(
                @"^[^\r\n]*\bpackage:\s*SkiaSharp\s*,[^\r\n]*\bversion:\s*[\""']?([^,\s}\""']+)",   // R8(ECO-144): 直後のカンマで固定(SkiaSharp.NativeAssets.* 行を拾わない)
                RegexOptions.Multiline | RegexOptions.CultureInvariant));

        var serviceBlock = ExtractRequired(
            ServiceBom,
            "id: SB-THUMB-020 のブロック",
            serviceBomText,
            new Regex(
                @"^(?<indent>[ \t]*)-\s+id:\s*SB-THUMB-020\s*$([\s\S]*?)(?=^\k<indent>-\s+id:|\z)",
                RegexOptions.Multiline | RegexOptions.CultureInvariant),
            group: 0);
        var serviceBomVersion = ExtractRequired(
            ServiceBom,
            "SB-THUMB-020 external_deps の kbom_ref: K-SKIA に続く version",
            serviceBlock,
            new Regex(
                @"external_deps:\s*\[\s*\{[^}]*\bkbom_ref:\s*K-SKIA\b[^}]*\bversion:\s*[\""']([^\""']+)[\""']",
                RegexOptions.Singleline | RegexOptions.CultureInvariant));

        return new Versions(projectVersion, manufacturingBomVersion, serviceBomVersion);
    }

    private static string ReadRequiredFile(string relativePath)
    {
        try
        {
            // R8(ECO-144): sln 不発見(DirectoryNotFoundException = IOException 派生)も「測定系復旧」の接頭辞で落とす
            var path = Path.Combine(RepoRoot(), relativePath);
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Fail($"測定系復旧: {relativePath} を読み取れなかった: {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string ExtractRequired(
        string relativePath,
        string target,
        string text,
        Regex pattern,
        int group = 1)
    {
        var match = pattern.Match(text);
        if (!match.Success || group >= match.Groups.Count || string.IsNullOrWhiteSpace(match.Groups[group].Value))
        {
            Assert.Fail($"測定系復旧: {relativePath} から {target} を抽出できなかった");
            return string.Empty;
        }

        return match.Groups[group].Value;
    }

    private sealed record Versions(string Project, string ManufacturingBom, string ServiceBom);
}
