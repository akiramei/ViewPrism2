using System.Security.Cryptography;

namespace ViewPrism2.Core.Common;

/// <summary>ファイルハッシュ(REQ-013)。SHA-256・ファイル全体・小文字 hex 64 文字。</summary>
public static class FileHasher
{
    public static string ComputeSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
