namespace ViewPrism2.Core.Common;

/// <summary>値を返さない操作の結果(M-CORE-001)。</summary>
public sealed class Result
{
    private Result(bool isSuccess, ErrorCode? error, string? message)
    {
        IsSuccess = isSuccess;
        Error = error;
        Message = message;
    }

    public bool IsSuccess { get; }

    /// <summary>失敗時のエラーコード。成功時は null。</summary>
    public ErrorCode? Error { get; }

    public string? Message { get; }

    public static Result Ok() => new(true, null, null);

    public static Result Fail(ErrorCode error, string? message = null) => new(false, error, message);
}

/// <summary>値を返す操作の結果(M-CORE-001)。</summary>
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, ErrorCode? error, string? message)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Message = message;
    }

    public bool IsSuccess { get; }

    /// <summary>成功時の値。失敗時は既定値。</summary>
    public T? Value { get; }

    /// <summary>失敗時のエラーコード。成功時は null。</summary>
    public ErrorCode? Error { get; }

    public string? Message { get; }

    public static Result<T> Ok(T value) => new(true, value, null, null);

    public static Result<T> Fail(ErrorCode error, string? message = null) => new(false, default, error, message);
}
