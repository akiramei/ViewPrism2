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

    /// <summary>データベース操作の失敗(v1.3/ECO-002 DF-2: DB 例外の Result 変換用に追加)。</summary>
    Database,

    /// <summary>使用中タグ定義の削除拒否(TAG-008 裁定/ECO-045: 付与・配置・条件参照があるタグは削除不可)。</summary>
    TagInUse,

    /// <summary>
    /// スキャンルートがディスク上に実在しない(フォルダの移動/改名でパスが失効・ドライブ未接続等)。
    /// 汎用 IoError と区別し、actionable な文言(改名/移動の示唆)を出すための専用コード(ECO-135)。
    /// </summary>
    ScanRootMissing,
}
