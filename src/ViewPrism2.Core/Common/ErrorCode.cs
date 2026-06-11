namespace ViewPrism2.Core.Common;

/// <summary>エラー語彙の全列挙(M-CORE-001 interface_contract.errors / M-BOM silence_sweep)。</summary>
public enum ErrorCode
{
    DuplicateTagName,
    DuplicateFolderPath,
    ValidationError,
    NotFound,
    CircularReference,
    ScanInProgress,
    IoError,
    InvalidRegex,
}
