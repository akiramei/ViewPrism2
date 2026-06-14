namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// criteria 条件検索の条件集合(M-CRITERIA-024 / REQ-068、仕様 §2.11.1)。
/// 各プロパティ null = その条件は未指定(無視する)。指定された条件のみ AND 結合する。
/// 1 つも指定されない(全 null)場合、検索は非実行で空列を返す(誤操作防止)。
/// </summary>
/// <remarks>
/// mtime 範囲は modified_date(ISO 8601 文字列・INV-002)に対する序数文字列比較で評価する
/// (文字列ソート=時系列ソートが成立する正規形なので範囲比較も序数で正しい)。
/// </remarks>
public sealed record SearchCriteria
{
    /// <summary>SHA-256 完全一致(Ordinal)。null=未指定。</summary>
    public string? Hash { get; init; }

    /// <summary>ファイル名の部分一致(OrdinalIgnoreCase)。null=未指定。</summary>
    public string? NameContains { get; init; }

    /// <summary>拡張子の完全一致(case-insensitive)。先頭ドット有無は正規化する。null=未指定。</summary>
    public string? Extension { get; init; }

    /// <summary>modified_date の下限(以上・ISO 8601 序数比較)。null=未指定。</summary>
    public string? MtimeFrom { get; init; }

    /// <summary>modified_date の上限(以下・ISO 8601 序数比較)。null=未指定。</summary>
    public string? MtimeTo { get; init; }

    /// <summary>file_size の下限(以上)。null=未指定。</summary>
    public long? SizeMin { get; init; }

    /// <summary>file_size の上限(以下)。null=未指定。</summary>
    public long? SizeMax { get; init; }
}
