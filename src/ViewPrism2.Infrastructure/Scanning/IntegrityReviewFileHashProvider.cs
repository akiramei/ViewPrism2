using System.Security.Cryptography;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.Infrastructure.Scanning;

/// <summary>
/// REQ-103: 統合裁定面専用の on-demand SHA-256 再計算。
/// スキャン経路へは注入せず、物理ファイルを読み取り専用で開く。
/// </summary>
public sealed class IntegrityReviewFileHashProvider : IIntegrityReviewHashProvider
{
    public async Task<string> ComputeSha256Async(string absolutePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(absolutePath);
        await using var stream = new FileStream(
            absolutePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
