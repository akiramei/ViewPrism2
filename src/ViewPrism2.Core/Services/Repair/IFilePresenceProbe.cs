namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// 物理ファイルの存在確認の抽象(M-TRASH-026 / 仕様 §2.11.3、INV-009 / INV-013)。
/// Core 抽象。実装(FilePresenceProbe)は Infrastructure 側で File.Exists のみを呼ぶ
/// (読み取りの存在確認だけ・open/move/delete しない)。SkiaSharp/FS アクセスは Infrastructure に閉じ、
/// TrashService(Core)は bool を受けて遷移判断する(IPHashImageReader と同型の層分離・ADR-0002)。
/// </summary>
public interface IFilePresenceProbe
{
    /// <summary>絶対パスに物理ファイルが存在するか。読み取りの存在確認のみ(INV-009)。</summary>
    bool Exists(string absoluteImagePath);
}
