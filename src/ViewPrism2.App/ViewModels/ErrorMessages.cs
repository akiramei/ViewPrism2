using ViewPrism2.Core.Common;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ErrorCode → 表示文言の解決(M-BOM silence_sweep: UI 表示文言は i18n キー error.&lt;code&gt;)。
/// </summary>
public static class ErrorMessages
{
    public static string Resolve(LocalizationService localization, ErrorCode? code)
    {
        ArgumentNullException.ThrowIfNull(localization);
        return localization.T(KeyOf(code));
    }

    public static string KeyOf(ErrorCode? code) => code switch
    {
        ErrorCode.DuplicateTagName => "error.duplicateTagName",
        ErrorCode.DuplicateFolderPath => "error.duplicateFolderPath",
        ErrorCode.ValidationError => "error.validationError",
        ErrorCode.NotFound => "error.notFound",
        ErrorCode.CircularReference => "error.circularReference",
        ErrorCode.ScanInProgress => "error.scanInProgress",
        ErrorCode.IoError => "error.ioError",
        ErrorCode.InvalidRegex => "error.invalidRegex",
        _ => "error.validationError",
    };
}
