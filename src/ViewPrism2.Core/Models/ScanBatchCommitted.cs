namespace ViewPrism2.Core.Models;

/// <summary>
/// ECO-060: DB transactionのcommitに成功し、UIへ段階公開できるfully-hashed normal画像。
/// Imagesの順序はスキャン取込順。commit前・rollback batch・pendingは含めない。
/// </summary>
public sealed record ScanBatchCommitted(
    string FolderId,
    IReadOnlyList<ImageRecord> Images);
