namespace ViewPrism2.Core.Models;

/// <summary>画像ステータス(仕様 §2.1 REQ-016)。Deleted は V1 では予約値。</summary>
public enum ImageStatus
{
    Normal,
    Missing,
    Deleted,
    Pending,
}

/// <summary>タグの 3 種意味論(仕様 §2.2 REQ-020)。</summary>
public enum TagType
{
    Simple,
    Textual,
    Numeric,
}

/// <summary>ビュー条件の演算子(仕様 §2.3 REQ-031)。</summary>
public enum ConditionOperator
{
    Exists,
    Equals,
    Between,
    Regexp,
    In,
}

/// <summary>整列フィールド(仕様 §2.3 REQ-038)。既定 Name。</summary>
public enum SortField
{
    Name,
    CreatedDate,
    ModifiedDate,
    FileSize,
}

/// <summary>整列方向(仕様 §2.3 REQ-038)。既定 Asc。</summary>
public enum SortDirection
{
    Asc,
    Desc,
}

/// <summary>階層ノードの値条件種別(仕様 §2.4 REQ-034)。null=条件なし。</summary>
public enum HierarchyConditionType
{
    Equals,
    Range,
    Pattern,
    Values,
}

/// <summary>NodeGraph ノードの種別(仕様 §2.4 REQ-035)。</summary>
public enum NodeKind
{
    /// <summary>ルート(無条件)。</summary>
    Root,
    /// <summary>タグ名ノード(simple/numeric タグ、または値 0 件の textual タグ)。</summary>
    TagName,
    /// <summary>textual タグの値ノード(distinct 値 2 件以上)。</summary>
    Value,
    /// <summary>一体型ノード「タグ名: 値」(distinct 値 1 件)。</summary>
    Combined,
}
