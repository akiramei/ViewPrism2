namespace ViewPrism2.Core.Common;

/// <summary>エンティティ ID 生成(REQ-001)。UUIDv4 小文字 36 文字。生成後不変(INV-001)。</summary>
public static class IdGenerator
{
    public static string NewId() => Guid.NewGuid().ToString("D");
}
