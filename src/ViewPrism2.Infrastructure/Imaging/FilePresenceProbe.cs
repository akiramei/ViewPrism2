using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>
/// 物理ファイルの存在確認(M-TRASH-026 / 仕様 §2.11.3、INV-009 / INV-013)。
/// File.Exists のみを呼ぶ — 読み取りの存在確認だけで open/move/delete はしない。
/// SkiaSharp/FS アクセスを Infrastructure に閉じる層分離(ADR-0002。PHashImageReader と同型)。
/// </summary>
public sealed class FilePresenceProbe : IFilePresenceProbe
{
    /// <summary>絶対パスに物理ファイルが存在するか(読み取りの存在確認のみ・INV-009)。</summary>
    public bool Exists(string absoluteImagePath)
        => !string.IsNullOrEmpty(absoluteImagePath) && File.Exists(absoluteImagePath);
}
