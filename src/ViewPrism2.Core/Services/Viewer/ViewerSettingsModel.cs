using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// ビューア表示設定の型付きモデル(M-VIEWERCORE-017)。AppSettings の Viewer* 7 項目
/// (文字列・int で格納)との変換と、列挙の文字列表現(enum ⇔ JSON 文字列)を担う(K-I18N とは独立)。
/// 範囲外・型不正・列挙外文字列は項目単位で既定値へ落とす(仕様 §2.9 REQ-059・破損耐性 REQ-052。
/// CP-SET-009 v2.0)。純粋計算(I/O なし)。
/// </summary>
public sealed record ViewerSettingsModel
{
    public ViewerMode Mode { get; init; } = ViewerMode.Normal;
    public ResizeMode ResizeMode { get; init; } = ResizeMode.NoResize;
    public AlignMode AlignMode { get; init; } = AlignMode.Middle;
    public GapMode GapMode { get; init; } = GapMode.Tight;

    /// <summary>0〜100 の整数。範囲外・型不正は 0(仕様 §2.9 REQ-059)。</summary>
    public int CustomGapPx { get; init; }

    public PageTurnMode PageTurnMode { get; init; } = PageTurnMode.DoublePage;
    public bool StartWithEmptyPage { get; init; }

    // ---- モック改善で追加(単一フィット・背景・スクロール横揃え)----
    public FitMode FitMode { get; init; } = FitMode.Fit;
    public BackgroundMode BackgroundMode { get; init; } = BackgroundMode.Dark;
    public ScrollHAlign ScrollHAlign { get; init; } = ScrollHAlign.Center;

    // ---- 文字列表現(JSON 格納値。仕様 §2.9 の表記そのまま)----
    public const string ModeNormal = "normal";
    public const string ModeScroll = "scroll";
    public const string ModeSpreadRight = "spread-right";
    public const string ModeSpreadLeft = "spread-left";

    public const string ResizeMatchLarger = "matchLargerHeight";
    public const string ResizeMatchSmaller = "matchSmallerHeight";
    public const string ResizeNone = "noResize";

    public const string AlignTop = "top";
    public const string AlignMiddle = "middle";
    public const string AlignBottom = "bottom";

    public const string GapTight = "tight";
    public const string GapLoose = "loose";

    public const string TurnDouble = "doublePage";
    public const string TurnSingle = "singlePage";

    public const string FitFit = "fit";
    public const string FitWidth = "width";
    public const string FitOne = "one";

    public const string BgDark = "dark";
    public const string BgLight = "light";
    public const string BgChecker = "checker";

    public const string HAlignLeft = "left";
    public const string HAlignCenter = "center";
    public const string HAlignRight = "right";

    public const int MinGapPx = 0;
    public const int MaxGapPx = 100;

    /// <summary>AppSettings の Viewer* 項目から型付きモデルを組み立てる(項目単位の既定化込み)。</summary>
    public static ViewerSettingsModel FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ViewerSettingsModel
        {
            Mode = ParseMode(settings.ViewerMode),
            ResizeMode = ParseResize(settings.ViewerResizeMode),
            AlignMode = ParseAlign(settings.ViewerAlignMode),
            GapMode = ParseGap(settings.ViewerGapMode),
            CustomGapPx = NormalizeGapPx(settings.ViewerCustomGapPx),
            PageTurnMode = ParseTurn(settings.ViewerPageTurnMode),
            StartWithEmptyPage = settings.ViewerStartWithEmptyPage,
            FitMode = ParseFit(settings.ViewerFitMode),
            BackgroundMode = ParseBackground(settings.ViewerBackground),
            ScrollHAlign = ParseScrollHAlign(settings.ViewerScrollHAlign),
        };
    }

    /// <summary>型付きモデルを AppSettings の Viewer* 項目へ書き戻す(文字列表現で格納)。</summary>
    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ViewerMode = ToString(Mode);
        settings.ViewerResizeMode = ToString(ResizeMode);
        settings.ViewerAlignMode = ToString(AlignMode);
        settings.ViewerGapMode = ToString(GapMode);
        settings.ViewerCustomGapPx = NormalizeGapPx(CustomGapPx);
        settings.ViewerPageTurnMode = ToString(PageTurnMode);
        settings.ViewerStartWithEmptyPage = StartWithEmptyPage;
        settings.ViewerFitMode = ToString(FitMode);
        settings.ViewerBackground = ToString(BackgroundMode);
        settings.ViewerScrollHAlign = ToString(ScrollHAlign);
    }

    public static ViewerMode ParseMode(string? value) => value switch
    {
        ModeScroll => ViewerMode.Scroll,
        ModeSpreadRight => ViewerMode.SpreadRight,
        ModeSpreadLeft => ViewerMode.SpreadLeft,
        ModeNormal => ViewerMode.Normal,
        _ => ViewerMode.Normal, // 列挙外文字列は既定
    };

    public static ResizeMode ParseResize(string? value) => value switch
    {
        ResizeMatchLarger => ResizeMode.MatchLargerHeight,
        ResizeMatchSmaller => ResizeMode.MatchSmallerHeight,
        ResizeNone => ResizeMode.NoResize,
        _ => ResizeMode.NoResize,
    };

    public static AlignMode ParseAlign(string? value) => value switch
    {
        AlignTop => AlignMode.Top,
        AlignBottom => AlignMode.Bottom,
        AlignMiddle => AlignMode.Middle,
        _ => AlignMode.Middle,
    };

    public static GapMode ParseGap(string? value) => value switch
    {
        GapLoose => GapMode.Loose,
        GapTight => GapMode.Tight,
        _ => GapMode.Tight,
    };

    public static PageTurnMode ParseTurn(string? value) => value switch
    {
        TurnSingle => PageTurnMode.SinglePage,
        TurnDouble => PageTurnMode.DoublePage,
        _ => PageTurnMode.DoublePage,
    };

    public static FitMode ParseFit(string? value) => value switch
    {
        FitWidth => FitMode.Width,
        FitOne => FitMode.One,
        FitFit => FitMode.Fit,
        _ => FitMode.Fit,
    };

    public static BackgroundMode ParseBackground(string? value) => value switch
    {
        BgLight => BackgroundMode.Light,
        BgChecker => BackgroundMode.Checker,
        BgDark => BackgroundMode.Dark,
        _ => BackgroundMode.Dark,
    };

    public static ScrollHAlign ParseScrollHAlign(string? value) => value switch
    {
        HAlignLeft => ScrollHAlign.Left,
        HAlignRight => ScrollHAlign.Right,
        HAlignCenter => ScrollHAlign.Center,
        _ => ScrollHAlign.Center,
    };

    /// <summary>customGapPx の正規化: 0〜100 範囲外は既定 0(仕様 §2.9 REQ-059)。</summary>
    public static int NormalizeGapPx(int value) => value is >= MinGapPx and <= MaxGapPx ? value : 0;

    public static string ToString(ViewerMode mode) => mode switch
    {
        ViewerMode.Scroll => ModeScroll,
        ViewerMode.SpreadRight => ModeSpreadRight,
        ViewerMode.SpreadLeft => ModeSpreadLeft,
        _ => ModeNormal,
    };

    public static string ToString(ResizeMode mode) => mode switch
    {
        ResizeMode.MatchLargerHeight => ResizeMatchLarger,
        ResizeMode.MatchSmallerHeight => ResizeMatchSmaller,
        _ => ResizeNone,
    };

    public static string ToString(AlignMode mode) => mode switch
    {
        AlignMode.Top => AlignTop,
        AlignMode.Bottom => AlignBottom,
        _ => AlignMiddle,
    };

    public static string ToString(GapMode mode) => mode == GapMode.Loose ? GapLoose : GapTight;

    public static string ToString(PageTurnMode mode) => mode == PageTurnMode.SinglePage ? TurnSingle : TurnDouble;

    public static string ToString(FitMode mode) => mode switch
    {
        FitMode.Width => FitWidth,
        FitMode.One => FitOne,
        _ => FitFit,
    };

    public static string ToString(BackgroundMode mode) => mode switch
    {
        BackgroundMode.Light => BgLight,
        BackgroundMode.Checker => BgChecker,
        _ => BgDark,
    };

    public static string ToString(ScrollHAlign mode) => mode switch
    {
        ScrollHAlign.Left => HAlignLeft,
        ScrollHAlign.Right => HAlignRight,
        _ => HAlignCenter,
    };
}
