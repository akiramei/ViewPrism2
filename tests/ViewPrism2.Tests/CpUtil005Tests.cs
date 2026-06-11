using System.Text;
using ViewPrism2.Core.Common;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>CP-UTIL-005: 共通ユーティリティが正規形を厳守する(33-control-plan.yaml test_vectors)。</summary>
[Trait("cp", "CP-UTIL-005")]
public sealed class CpUtil005Tests
{
    [Fact]
    public void IdGenerator_1000回生成_全てUUIDv4小文字_重複なし()
    {
        const string pattern = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
        var ids = Enumerable.Range(0, 1000).Select(_ => IdGenerator.NewId()).ToList();

        Assert.All(ids, id => Assert.Matches(pattern, id));
        Assert.Equal(1000, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SystemClock_UtcNowIso_は正規形に一致する()
    {
        var iso = new SystemClock().UtcNowIso();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", iso);
    }

    [Fact]
    public void FakeClock_でも同一の正規形を返す()
    {
        var clock = new FakeClock(new DateTime(2026, 6, 11, 1, 2, 3, 456, DateTimeKind.Utc));
        Assert.Equal("2026-06-11T01:02:03.456Z", clock.UtcNowIso());
    }

    [Fact]
    public void Sha256_空ストリーム()
    {
        using var stream = new MemoryStream();
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            FileHasher.ComputeSha256(stream));
    }

    [Fact]
    public void Sha256_abc_UTF8()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            FileHasher.ComputeSha256(stream));
    }

    [Fact]
    public void PathNormalizer_ToRelative_正規形_大文字小文字保持()
    {
        Assert.Equal("Sub/a.JPG", PathNormalizer.ToRelative(@"C:\pics", @"C:\pics\Sub\a.JPG"));
    }

    [Fact]
    public void PathNormalizer_Equals_は大文字小文字を無視する()
    {
        Assert.True(PathNormalizer.Equals("sub/a.jpg", "SUB/A.JPG"));
        Assert.False(PathNormalizer.Equals("sub/a.jpg", "sub/b.jpg"));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    [InlineData(1099511627776L, "1024.0 GB")]
    public void ByteSizeFormatter_テストベクタ(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}
