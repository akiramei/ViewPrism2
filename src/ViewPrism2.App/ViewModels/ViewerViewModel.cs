using System.Globalization;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Viewer;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ビューアの ViewModel(M-UI-014 / M-UI-018、REQ-044・§2.9、G-4/G-8)。
/// V1: 単一画像(normal)のナビゲーション(Next/Prev は端で停止・ループや例外なし、空一覧も安全 — FMEA-002)。
/// V2: 表示モード(normal/scroll/spread-right/spread-left)・モード別位置記憶・見開きの送り/ペアリング・
/// スクロール位置追跡・共通表示設定の永続化。計算は M-VIEWERCORE-017(Core)のみ経由する
/// (コードビハインドでの判定禁止 — K-AVALONIA)。
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject
{
    private readonly IReadOnlyList<ImageEntry> _ordered;
    private readonly ViewerModeMemory _memory;

    /// <summary>設定変更の即時永続化(REQ-059)。null=永続化しない(unit テスト等)。</summary>
    private readonly Action<ViewerSettingsModel>? _persist;

    private ViewerSettingsModel _settings;

    // SHIFT 押下状態(見開きのページ送りステップ解決に使用。View 層の修飾キー検知から設定)
    private bool _shiftHeld;

    /// <summary>
    /// V1 互換コンストラクタ(M-UI-014)。normal モード・既定表示設定・永続化なし。
    /// 既存 CP-UI-G4 unit がこのシグネチャを使う(回帰ゼロ)。
    /// </summary>
    public ViewerViewModel(IReadOnlyList<ImageEntry> ordered, int startIndex)
        : this(ordered, startIndex, new ViewerSettingsModel(), persist: null)
    {
    }

    /// <summary>
    /// V2 コンストラクタ(M-UI-018)。保存済み表示設定で復元し、各モードの位置記憶を起動 index で初期化する
    /// (REQ-054: 初期値=起動時 index)。persist は設定変更の即時保存(REQ-059)。
    /// </summary>
    public ViewerViewModel(
        IReadOnlyList<ImageEntry> ordered,
        int startIndex,
        ViewerSettingsModel settings,
        Action<ViewerSettingsModel>? persist)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(settings);
        _ordered = ordered;
        _settings = settings;
        _persist = persist;

        var initial = ordered.Count == 0 ? 0 : Math.Clamp(startIndex, 0, ordered.Count - 1);
        _memory = new ViewerModeMemory(initial);
        _mode = ordered.Count == 0 ? ViewerMode.Normal : settings.Mode; // 復元したモード(REQ-059)
    }

    /// <summary>
    /// i18n プロキシ(コントロールバー・エラー文言の XAML バインディング用)。
    /// WindowService が設定する。unit テストでは null(文言不要)。
    /// </summary>
    public LocalizationProxy? Loc { get; set; }

    /// <summary>scroll/spread の画像ロード失敗文言テンプレート(ViewerImage.ErrorTemplate へ)。</summary>
    public string LoadErrorTemplate => Loc?.T("viewer.loadError") ?? "{fileName}";

    // ---- 共通(全モード) ----

    public int TotalCount => _ordered.Count;

    /// <summary>現在モードの記憶 index(空一覧は -1)。</summary>
    public int CurrentIndex => _ordered.Count == 0 ? -1 : _memory.Get(Mode);

    public ImageEntry? Current => CurrentIndex >= 0 ? _ordered[CurrentIndex] : null;

    public string? CurrentImagePath => Current?.AbsolutePath;

    /// <summary>呼び出し元一覧(整列結果)。各モードビューが描画に使う。</summary>
    public IReadOnlyList<ImageEntry> Items => _ordered;

    /// <summary>
    /// 「現在位置/総数」(REQ-044)。空一覧は「0 / 0」。
    /// 見開きはペアの表示番号「n-m / total」(単独ページ時は「n / total」)— K-DESIGN v2.0。
    /// </summary>
    public string CurrentPositionText
    {
        get
        {
            if (_ordered.Count == 0)
            {
                return "0 / 0";
            }

            // タグ制御 ON の見開き: 総数=非 skip 枚数・読み順の非 skip 位置で n-m / total(§2.12.4)
            if (IsTagControlActive)
            {
                var plan = EnsurePlan();
                if (plan.Spreads.Count == 0)
                {
                    return "0 / 0";
                }

                var sp = plan.Spreads[ClampSpread(_tagControlSpread, plan)];
                var total = plan.NonSkipCount;

                // 読み順: 右開き=右が先・左が後 / 左開き=左が先・右が後
                var firstIdx = Direction == SpreadDirection.Right ? sp.RightIndex : sp.LeftIndex;
                var secondIdx = Direction == SpreadDirection.Right ? sp.LeftIndex : sp.RightIndex;

                // 片側空白・spread 占有は単独表示(canonical の位置)
                if (sp.IsSpread || firstIdx is null || secondIdx is null)
                {
                    var pos = plan.NonSkipPosition.GetValueOrDefault(sp.CanonicalImage, sp.CanonicalImage + 1);
                    return string.Create(CultureInfo.InvariantCulture, $"{pos} / {total}");
                }

                var n = plan.NonSkipPosition.GetValueOrDefault(firstIdx.Value, firstIdx.Value + 1);
                var m = plan.NonSkipPosition.GetValueOrDefault(secondIdx.Value, secondIdx.Value + 1);
                return string.Create(CultureInfo.InvariantCulture, $"{n}-{m} / {total}");
            }

            if (IsSpread)
            {
                var pair = CurrentSpreadPair;
                // 表示は読み順(右開き=右が先・左が後 / 左開き=左が先・右が後)で小さい表示番号を先に出す
                var first = Direction == SpreadDirection.Right ? pair.RightIndex : pair.LeftIndex;
                var second = Direction == SpreadDirection.Right ? pair.LeftIndex : pair.RightIndex;
                if (first is { } f && second is { } s)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"{f + 1}-{s + 1} / {_ordered.Count}");
                }

                var single = first ?? second ?? CurrentIndex;
                return string.Create(CultureInfo.InvariantCulture, $"{single + 1} / {_ordered.Count}");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{CurrentIndex + 1} / {_ordered.Count}");
        }
    }

    public string Title => Current is null
        ? $"ViewPrism2 — {CurrentPositionText}"
        : $"{Current.Record.FileName} — {CurrentPositionText}";

    public event EventHandler? CloseRequested;

    /// <summary>現在位置が変わったとき(View 層がスクロール追従・再描画する)。</summary>
    public event EventHandler? CurrentIndexChanged;

    // ---- モード ----

    private ViewerMode _mode;

    /// <summary>表示モード(REQ-054)。切替時は当該モードの記憶位置へ復元し、設定を永続化する。</summary>
    public ViewerMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            _settings = _settings with { Mode = value };
            Persist();
            // 方向が変わるとプランのスロット並びが変わるため無効化し、記憶画像から見開き同期(復元)
            InvalidatePlan();
            SyncTagControlSpreadFromMemory();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTagControlActive));
            OnPropertyChanged(nameof(IsNormal));
            OnPropertyChanged(nameof(IsScroll));
            OnPropertyChanged(nameof(IsSpread));
            OnPropertyChanged(nameof(IsSpreadRight));
            OnPropertyChanged(nameof(IsSpreadLeft));
            OnPropertyChanged(nameof(Direction));
            OnPropertyChanged(nameof(ShowBottomBar));
            OnPropertyChanged(nameof(ShowSeek));
            OnPropertyChanged(nameof(SeekMax));
            OnPropertyChanged(nameof(ShowNormalFit));
            OnPropertyChanged(nameof(ShowNormalScroll));
            RaisePositionChanged();
            CurrentIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsNormal => Mode == ViewerMode.Normal;

    public bool IsScroll => Mode == ViewerMode.Scroll;

    public bool IsSpread => Mode is ViewerMode.SpreadRight or ViewerMode.SpreadLeft;

    /// <summary>右開きが選択中(GF-V2-01: 方向トグルの選択状態表示)。</summary>
    public bool IsSpreadRight => Mode == ViewerMode.SpreadRight;

    /// <summary>左開きが選択中(GF-V2-01)。</summary>
    public bool IsSpreadLeft => Mode == ViewerMode.SpreadLeft;

    /// <summary>見開きの開き方向(spread のときのみ意味を持つ)。</summary>
    public SpreadDirection Direction =>
        Mode == ViewerMode.SpreadLeft ? SpreadDirection.Left : SpreadDirection.Right;

    /// <summary>
    /// 現在の見開きペア(OC-9)。spread でないときも計算上は右開き基準で返す。
    /// タグ制御 ON の見開きでは plan(OC-24)由来の見開きを返す(M-UI-018 plan→描画結線)。
    /// </summary>
    public SpreadPair CurrentSpreadPair
    {
        get
        {
            if (IsTagControlActive)
            {
                var plan = EnsurePlan();
                if (plan.Spreads.Count == 0)
                {
                    return new SpreadPair { LeftIndex = null, RightIndex = null };
                }

                var sp = plan.Spreads[ClampSpread(_tagControlSpread, plan)];
                return new SpreadPair { LeftIndex = sp.LeftIndex, RightIndex = sp.RightIndex };
            }

            return SpreadPairCalculator.Calculate(
                CurrentIndex < 0 ? 0 : CurrentIndex, _ordered.Count, Direction, _settings.StartWithEmptyPage);
        }
    }

    // ---- タグ制御モード(ECO-022・§2.12。配置/送り/現在画像は E-VIEWER-TAGCTRL-044 経由)----

    /// <summary>タグ制御 ON が見開きで実効か(§5.5: 見開きのみフル。OFF/normal/scroll では従来経路)。</summary>
    public bool IsTagControlActive => _settings.EnableTagControl && IsSpread && _ordered.Count > 0;

    /// <summary>現在のプラン見開き index(タグ制御 ON の見開き専用座標系。OC-25)。</summary>
    private int _tagControlSpread;

    private TagControlPlan? _plan;

    /// <summary>現プラン見開きが span(spread 占有)か(View の描画分岐。span は単一画像を左右占有)。</summary>
    public bool CurrentIsSpreadOccupy
    {
        get
        {
            if (!IsTagControlActive)
            {
                return false;
            }

            var plan = EnsurePlan();
            return plan.Spreads.Count != 0 && plan.Spreads[ClampSpread(_tagControlSpread, plan)].IsSpread;
        }
    }

    /// <summary>spread 占有時に左右占有する画像 index(View 描画用。非占有時は -1)。</summary>
    public int CurrentSpreadOccupyImage
    {
        get
        {
            if (!CurrentIsSpreadOccupy)
            {
                return -1;
            }

            var plan = EnsurePlan();
            return plan.Spreads[ClampSpread(_tagControlSpread, plan)].CanonicalImage;
        }
    }

    /// <summary>解決済みマッピング(action→tag_id?)。Resolve への入力。</summary>
    private IReadOnlyDictionary<ViewerTagAction, string?> TagActionMap => _settings.TagActionMap;

    /// <summary>(画像 index, 解決アクション)列を構築しプランをキャッシュする(OC-23→OC-24)。</summary>
    private TagControlPlan EnsurePlan()
    {
        if (_plan is not null)
        {
            return _plan;
        }

        var items = new List<(int imageIndex, ViewerTagAction? action)>(_ordered.Count);
        for (var i = 0; i < _ordered.Count; i++)
        {
            var tagIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in _ordered[i].Tags)
            {
                tagIds.Add(tag.TagId);
            }

            var action = TagActionResolver.Resolve(tagIds, TagActionMap);
            items.Add((i, action));
        }

        _plan = TagControlLayoutCalculator.Build(items, Direction, _settings.StartWithEmptyPage);
        return _plan;
    }

    /// <summary>プラン無効化(マッピング/トグル/方向/空白開始の変更時)。次回参照で再計算。</summary>
    private void InvalidatePlan() => _plan = null;

    private static int ClampSpread(int spread, TagControlPlan plan) =>
        plan.Spreads.Count == 0 ? 0 : Math.Clamp(spread, 0, plan.Spreads.Count - 1);

    /// <summary>
    /// タグ制御 ON の見開きへ入る/設定変更したとき、現在の記憶画像を含むプラン見開きへ
    /// 同期する(モード復元の画像→見開き解決。canonical 現在画像 §2.12.4)。
    /// </summary>
    private void SyncTagControlSpreadFromMemory()
    {
        if (!IsTagControlActive)
        {
            return;
        }

        var plan = EnsurePlan();
        var image = CurrentIndex < 0 ? 0 : CurrentIndex;
        _tagControlSpread = ClampSpread(TagControlNavigator.SpreadOfImage(plan, image), plan);
    }

    /// <summary>プラン見開き送り後、canonical 現在画像を記憶へ書き戻す(normal/scroll への位置受け渡し)。</summary>
    private void WriteBackCanonicalImage()
    {
        var plan = EnsurePlan();
        var canonical = TagControlNavigator.CanonicalImage(plan, _tagControlSpread);
        if (canonical >= 0)
        {
            _memory.Set(Mode, Math.Clamp(canonical, 0, _ordered.Count - 1));
        }
    }

    /// <summary>タグ制御マッピングモーダルの開閉(ECO-019 in-tab popup 同型)。</summary>
    [ObservableProperty] private bool _tagControlMappingOpen;

    [RelayCommand]
    private void ToggleTagControlMapping() => TagControlMappingOpen = !TagControlMappingOpen;

    [RelayCommand]
    private void CloseTagControlMapping() => TagControlMappingOpen = false;

    /// <summary>マッピングを既定(全アクション未割り当て)へ戻す(モック「既定に戻す」)。即時保存・ライブ反映。</summary>
    [RelayCommand]
    private void ResetTagControlMapping()
    {
        // 割り当てが 1 つも無ければ no-op(既定 map は 6 キー値 null を持ちうるため Count ではなく値で判定)。
        if (!_settings.TagActionMap.Values.Any(v => !string.IsNullOrEmpty(v)))
        {
            return;
        }

        _settings = _settings with { TagActionMap = new Dictionary<ViewerTagAction, string?>() };
        InvalidatePlan();
        Persist();
        RebuildTagActionRows();
        SyncTagControlSpreadFromMemory();
        RaisePositionChanged();
        CurrentIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>タグ制御モード(REQ-077)。トグルで即時保存し plan を無効化(回帰: OFF で従来経路)。</summary>
    public bool EnableTagControl
    {
        get => _settings.EnableTagControl;
        set
        {
            if (_settings.EnableTagControl == value)
            {
                return;
            }

            _settings = _settings with { EnableTagControl = value };
            InvalidatePlan();
            Persist();
            SyncTagControlSpreadFromMemory();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTagControlActive));
            RaisePositionChanged();
            CurrentIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void ToggleTagControl() => EnableTagControl = !EnableTagControl;

    /// <summary>マッピングモーダルの 6 行(予約アクション・picker 表示用)。</summary>
    public IReadOnlyList<TagControlMappingRow> TagActionRows { get; private set; } = [];

    /// <summary>割り当て済みアクション数(0〜6。§2.12.6 マッピング設定カードの N/6 バッジ用)。</summary>
    public int MappedActionCount => _settings.TagActionMap.Values.Count(v => !string.IsNullOrEmpty(v));

    /// <summary>マッピング設定カードのバッジ「N/6」(モック準拠)。</summary>
    public string TagControlMappingBadge =>
        string.Create(CultureInfo.InvariantCulture, $"{MappedActionCount}/6");

    /// <summary>
    /// 現存タグ一覧(picker 用。WindowService が GetAllAsync の現存タグのみ供給 — major-1 補正)と
    /// 現在のマッピングから 6 行を組み立てる。WindowService が起動時/タグ変更時に呼ぶ。
    /// </summary>
    public void SetAvailableTags(IReadOnlyList<TagPickerOption> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _availableTags = tags;
        RebuildTagActionRows();
    }

    private IReadOnlyList<TagPickerOption> _availableTags = [];

    // 表示順・アイコンバッジ色はモック(M-UI-018 tagctrl_ui / CAD)準拠。順は左固定→右固定→占有→skip→
    // 左空白→右空白。色はアクション固定の淡色(割り当てタグ色とは独立)。resolver の競合順(Core)とは無関係。
    private static readonly (ViewerTagAction Action, string Glyph, string IconBg, string IconFg, string Key)[] ActionRowDefs =
    {
        (ViewerTagAction.ForceLeftPage, "◀", "#EAF1FE", "#2F6BED", "forceLeftPage"),
        (ViewerTagAction.ForceRightPage, "▶", "#EAFAF3", "#0F8A5E", "forceRightPage"),
        (ViewerTagAction.Spread, "↔", "#F3EFFE", "#7C3AED", "spread"),
        (ViewerTagAction.Skip, "⊘", "#FDECEC", "#DC2626", "skip"),
        (ViewerTagAction.LeftPageEmpty, "◑", "#FBF3DF", "#C99A1E", "leftPageEmpty"),
        (ViewerTagAction.RightPageEmpty, "◐", "#FDEEDE", "#C47D18", "rightPageEmpty"),
    };

    private void RebuildTagActionRows()
    {
        var rows = new List<TagControlMappingRow>(ActionRowDefs.Length);
        foreach (var (action, glyph, iconBg, iconFg, key) in ActionRowDefs)
        {
            var assignedId = _settings.TagActionMap.GetValueOrDefault(action);
            var assigned = assignedId is null
                ? null
                : _availableTags.FirstOrDefault(t => string.Equals(t.Id, assignedId, StringComparison.Ordinal));

            rows.Add(new TagControlMappingRow(
                this,
                action,
                glyph,
                iconBg,
                iconFg,
                Loc?.T($"viewer.tagControl.action.{key}.name") ?? key,
                key,
                Loc?.T($"viewer.tagControl.action.{key}.desc") ?? string.Empty,
                assigned,
                _availableTags,
                Loc?.T("viewer.tagControl.unassigned") ?? "未割り当て"));
        }

        TagActionRows = rows;
        OnPropertyChanged(nameof(TagActionRows));
        OnPropertyChanged(nameof(MappedActionCount));
        OnPropertyChanged(nameof(TagControlMappingBadge));
    }

    /// <summary>
    /// マッピングを更新する(picker 選択。即時保存→plan 無効化→再計算反映。REQ-077/REQ-078)。
    /// tagId=null でクリア(未割り当て)。
    /// </summary>
    public void SetTagActionMapping(ViewerTagAction action, string? tagId)
    {
        var map = new Dictionary<ViewerTagAction, string?>(_settings.TagActionMap)
        {
            [action] = ViewerSettingsModel.NormalizeTagId(tagId),
        };
        _settings = _settings with { TagActionMap = map };
        InvalidatePlan();
        Persist();
        RebuildTagActionRows();
        SyncTagControlSpreadFromMemory();
        RaisePositionChanged();
        CurrentIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SetNormalMode() => Mode = ViewerMode.Normal;

    [RelayCommand]
    private void SetScrollMode() => Mode = ViewerMode.Scroll;

    [RelayCommand]
    private void SetSpreadRightMode() => Mode = ViewerMode.SpreadRight;

    [RelayCommand]
    private void SetSpreadLeftMode() => Mode = ViewerMode.SpreadLeft;

    // ---- 表示設定(REQ-058/059)。変更は即時永続化 ----

    public ViewerSettingsModel Settings => _settings;

    public ResizeMode ResizeMode
    {
        get => _settings.ResizeMode;
        set => UpdateSettings(_settings with { ResizeMode = value });
    }

    public AlignMode AlignMode
    {
        get => _settings.AlignMode;
        set => UpdateSettings(_settings with { AlignMode = value });
    }

    public GapMode GapMode
    {
        get => _settings.GapMode;
        set => UpdateSettings(_settings with { GapMode = value });
    }

    public int CustomGapPx
    {
        get => _settings.CustomGapPx;
        set => UpdateSettings(_settings with { CustomGapPx = ViewerSettingsModel.NormalizeGapPx(value) });
    }

    public PageTurnMode PageTurnMode
    {
        get => _settings.PageTurnMode;
        set => UpdateSettings(_settings with { PageTurnMode = value });
    }

    public bool StartWithEmptyPage
    {
        get => _settings.StartWithEmptyPage;
        set => UpdateSettings(_settings with { StartWithEmptyPage = value });
    }

    // ---- モック改善: 単一フィット / 背景 / スクロール横揃え ----

    public FitMode FitMode
    {
        get => _settings.FitMode;
        set => UpdateSettings(_settings with { FitMode = value });
    }

    public BackgroundMode BackgroundMode
    {
        get => _settings.BackgroundMode;
        set => UpdateSettings(_settings with { BackgroundMode = value });
    }

    public ScrollHAlign ScrollHAlign
    {
        get => _settings.ScrollHAlign;
        set => UpdateSettings(_settings with { ScrollHAlign = value });
    }

    // seg(ピル)UI 用の選択状態(IsChecked バインド)。値変更は Set*Command 経由で行う。
    public bool IsResizeLarger => ResizeMode == ResizeMode.MatchLargerHeight;
    public bool IsResizeSmaller => ResizeMode == ResizeMode.MatchSmallerHeight;
    public bool IsResizeNone => ResizeMode == ResizeMode.NoResize;
    public bool IsAlignTop => AlignMode == AlignMode.Top;
    public bool IsAlignMiddle => AlignMode == AlignMode.Middle;
    public bool IsAlignBottom => AlignMode == AlignMode.Bottom;
    public bool IsGapTight => GapMode == GapMode.Tight;
    public bool IsGapLoose => GapMode == GapMode.Loose;
    public bool IsTurnDouble => PageTurnMode == PageTurnMode.DoublePage;
    public bool IsTurnSingle => PageTurnMode == PageTurnMode.SinglePage;
    public bool IsFitFit => FitMode == FitMode.Fit;
    public bool IsFitWidth => FitMode == FitMode.Width;
    public bool IsFitOne => FitMode == FitMode.One;
    public bool IsHAlignLeft => ScrollHAlign == ScrollHAlign.Left;
    public bool IsHAlignCenter => ScrollHAlign == ScrollHAlign.Center;
    public bool IsHAlignRight => ScrollHAlign == ScrollHAlign.Right;
    public bool IsBgDark => BackgroundMode == BackgroundMode.Dark;
    public bool IsBgLight => BackgroundMode == BackgroundMode.Light;
    public bool IsBgChecker => BackgroundMode == BackgroundMode.Checker;

    [RelayCommand]
    private void SetResize(string value) => ResizeMode = value switch
    {
        "larger" => ResizeMode.MatchLargerHeight,
        "smaller" => ResizeMode.MatchSmallerHeight,
        _ => ResizeMode.NoResize,
    };

    [RelayCommand]
    private void SetAlign(string value) => AlignMode = value switch
    {
        "top" => AlignMode.Top,
        "bottom" => AlignMode.Bottom,
        _ => AlignMode.Middle,
    };

    [RelayCommand]
    private void SetGap(string value) => GapMode = value == "loose" ? GapMode.Loose : GapMode.Tight;

    [RelayCommand]
    private void SetTurn(string value) => PageTurnMode = value == "single" ? PageTurnMode.SinglePage : PageTurnMode.DoublePage;

    [RelayCommand]
    private void SetFit(string value) => FitMode = value switch
    {
        "width" => FitMode.Width,
        "one" => FitMode.One,
        _ => FitMode.Fit,
    };

    [RelayCommand]
    private void SetHAlign(string value) => ScrollHAlign = value switch
    {
        "left" => ScrollHAlign.Left,
        "right" => ScrollHAlign.Right,
        _ => ScrollHAlign.Center,
    };

    [RelayCommand]
    private void SetBackground(string value) => BackgroundMode = value switch
    {
        "light" => BackgroundMode.Light,
        "checker" => BackgroundMode.Checker,
        _ => BackgroundMode.Dark,
    };

    // ---- 表示設定ドロワー(モック: 表示設定ボタンで開閉する右パネル)----
    [ObservableProperty] private bool _settingsOpen;

    [RelayCommand]
    private void ToggleSettings() => SettingsOpen = !SettingsOpen;

    // 単一の描画 host 切替: Fit=フィット Panel / Width・One=スクロール ScrollViewer。
    public bool ShowNormalFit => IsNormal && FitMode == FitMode.Fit;
    public bool ShowNormalScroll => IsNormal && FitMode != FitMode.Fit;

    /// <summary>縦スクロール各画像の横揃え(ScrollHAlign → Avalonia.Layout)。</summary>
    public HorizontalAlignment ScrollItemAlignment => ScrollHAlign switch
    {
        ScrollHAlign.Left => HorizontalAlignment.Left,
        ScrollHAlign.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center,
    };

    // ---- 下部バー(モック: 単一/見開きのみ表示。縦スクロールは非表示)----
    public bool ShowBottomBar => IsNormal || IsSpread;

    /// <summary>下部シークスライダー(単一のみ。2枚以上で有効)。値=現在 index・設定で移動。</summary>
    public bool ShowSeek => IsNormal && _ordered.Count > 1;

    public int SeekMax => _ordered.Count > 0 ? _ordered.Count - 1 : 0;

    public int SeekValue
    {
        get => CurrentIndex < 0 ? 0 : CurrentIndex;
        set
        {
            if (IsNormal)
            {
                SetIndex(value);
            }
        }
    }

    /// <summary>見開きの左右間/スクロールの画像間ギャップ(px)。tight=0、loose=customGapPx。</summary>
    public double EffectiveGapPx => _settings.GapMode == GapMode.Loose ? _settings.CustomGapPx : 0;

    // 設定 Flyout の選択肢(列挙値そのまま。SelectedItem に enum を直接束縛)
    public static IReadOnlyList<ResizeMode> ResizeOptions { get; } =
        [ResizeMode.NoResize, ResizeMode.MatchLargerHeight, ResizeMode.MatchSmallerHeight];

    public static IReadOnlyList<AlignMode> AlignOptions { get; } =
        [AlignMode.Top, AlignMode.Middle, AlignMode.Bottom];

    public static IReadOnlyList<GapMode> GapOptions { get; } = [GapMode.Tight, GapMode.Loose];

    public static IReadOnlyList<PageTurnMode> PageTurnOptions { get; } =
        [PageTurnMode.DoublePage, PageTurnMode.SinglePage];

    // ---- ナビゲーション ----

    /// <summary>
    /// 次へ。normal/scroll は 1 画像進む。spread は REQ-057 のステップ(SHIFT=1 / pageTurnMode)で送る。
    /// 端で停止(ループ・例外なし)。
    /// </summary>
    [RelayCommand]
    private void Next()
    {
        if (_ordered.Count == 0)
        {
            return;
        }

        // タグ制御 ON の見開き: プラン見開き単位で送る(OC-25。SHIFT/pageTurnMode 非適用 §2.12.4)
        if (IsTagControlActive)
        {
            TagControlAdvance(forward: true);
            return;
        }

        var current = CurrentIndex;
        int next;
        if (IsSpread)
        {
            next = PageTurnCalculator.Next(current, _ordered.Count, ResolveStep(), _settings.StartWithEmptyPage);
        }
        else
        {
            next = Math.Min(current + 1, _ordered.Count - 1);
        }

        SetIndex(next);
    }

    /// <summary>タグ制御 ON のプラン見開き送り(±1 クランプ・canonical を記憶へ書き戻し)。</summary>
    private void TagControlAdvance(bool forward)
    {
        var plan = EnsurePlan();
        var count = plan.Spreads.Count;
        var prev = ClampSpread(_tagControlSpread, plan);
        var next = forward
            ? TagControlNavigator.Next(prev, count)
            : TagControlNavigator.Prev(prev, count);

        if (next == prev)
        {
            return;
        }

        _tagControlSpread = next;
        WriteBackCanonicalImage();
        RaisePositionChanged();
        CurrentIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>前へ。normal/scroll は 1 画像戻る。spread は REQ-057 のステップで戻す。先頭で停止。</summary>
    [RelayCommand]
    private void Prev()
    {
        if (_ordered.Count == 0)
        {
            return;
        }

        if (IsTagControlActive)
        {
            TagControlAdvance(forward: false);
            return;
        }

        var current = CurrentIndex;
        int prev = IsSpread ? PageTurnCalculator.Prev(current, ResolveStep()) : Math.Max(current - 1, 0);
        SetIndex(prev);
    }

    /// <summary>
    /// 矢印キー(空間キー)。spread-right では右=前へ・左=次へ(進行方向反転 — 紙の右開き本と一致)。
    /// spread-left は右=次へ・左=前へ。normal/scroll は右=次へ・左=前へ(REQ-044 共通)。
    /// PageDown/PageUp は論理キーのため Next/Prev を直接束縛し、本メソッドは通らない(§2.9)。
    /// </summary>
    [RelayCommand]
    private void ArrowRight()
    {
        if (Mode == ViewerMode.SpreadRight)
        {
            Prev(); // 右開きで → は前へ
        }
        else
        {
            Next();
        }
    }

    [RelayCommand]
    private void ArrowLeft()
    {
        if (Mode == ViewerMode.SpreadRight)
        {
            Next(); // 右開きで ← は次へ
        }
        else
        {
            Prev();
        }
    }

    /// <summary>
    /// ページクリック(見開きのみ。表示領域を左右半分に二分した各半面)。
    /// 進行方向側の半面(右開き=左半面 / 左開き=右半面)=次へ、反対側=前へ(REQ-057)。
    /// isLeftHalf=true がクリックされた半面が左半面かどうか。normal/scroll では呼ばない(無操作)。
    /// </summary>
    public void OnPageClick(bool isLeftHalf)
    {
        if (!IsSpread)
        {
            return; // scroll/normal の余白/画像クリックはここを通らない(送らない)
        }

        var nextSideIsLeft = Direction == SpreadDirection.Right; // 右開き=左半面が次へ
        if (isLeftHalf == nextSideIsLeft)
        {
            Next();
        }
        else
        {
            Prev();
        }
    }

    /// <summary>scroll の追跡結果(OC-11)で現在位置を更新する(View 層の停止検出から呼ぶ)。</summary>
    public void UpdateScrollPosition(IReadOnlyList<(double Top, double Height)> imageRects, double viewportHeight, double scrollOffset)
    {
        if (_ordered.Count == 0 || Mode != ViewerMode.Scroll)
        {
            return;
        }

        var index = ScrollPositionTracker.FindCurrent(imageRects, viewportHeight, scrollOffset);
        UpdateScrollPositionByIndex(index);
    }

    /// <summary>
    /// scroll の追跡結果(OC-11 の戻り index)で現在位置を更新する。仮想化 View は実体化コンテナの
    /// 疎な部分集合から FindCurrent を計算し、実 item index へ写し戻したうえで本メソッドを呼ぶ
    /// (View 層が rect→index 写像の責務を持つため。計算核 OC-11 自体は ScrollPositionTracker のまま)。
    /// </summary>
    public void UpdateScrollPositionByIndex(int index)
    {
        if (_ordered.Count == 0 || Mode != ViewerMode.Scroll)
        {
            return;
        }

        if (index != CurrentIndex)
        {
            _memory.Set(Mode, Math.Clamp(index, 0, _ordered.Count - 1));
            RaisePositionChanged();
            // scroll 追跡では CurrentIndexChanged を発火しない(自分が動いた結果のため、再スクロールしない)
        }
    }

    /// <summary>SHIFT 修飾の保持状態を設定(見開きの 1 ページ送り解決。View 層の KeyDown/KeyUp から)。</summary>
    public void SetShift(bool held) => _shiftHeld = held;

    /// <summary>閉じる(Escape / 閉じるボタン / normal の画像外余白クリック — REQ-044)。</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 余白クリックで閉じてよいか(normal のみ。scroll/spread は無効 — REQ-054)。
    /// View 層は normal のときだけ IsBackgroundPoint と併用してこの規則を適用する。
    /// </summary>
    public bool CanCloseOnBackgroundClick => Mode == ViewerMode.Normal;

    /// <summary>見開きのステップ(SHIFT=1 / doublePage=2 / singlePage=1。REQ-057)。</summary>
    private int ResolveStep()
    {
        if (_shiftHeld)
        {
            return 1;
        }

        return _settings.PageTurnMode == PageTurnMode.DoublePage ? 2 : 1;
    }

    private void SetIndex(int index)
    {
        var clamped = Math.Clamp(index, 0, _ordered.Count - 1);
        if (clamped == CurrentIndex)
        {
            return;
        }

        _memory.Set(Mode, clamped);
        RaisePositionChanged();
        CurrentIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSettings(ViewerSettingsModel updated)
    {
        _settings = updated;
        Persist();
        // 空白開始など plan に影響する設定が変わり得るため無効化し、記憶画像から見開き同期
        InvalidatePlan();
        SyncTagControlSpreadFromMemory();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(ResizeMode));
        OnPropertyChanged(nameof(AlignMode));
        OnPropertyChanged(nameof(GapMode));
        OnPropertyChanged(nameof(CustomGapPx));
        OnPropertyChanged(nameof(PageTurnMode));
        OnPropertyChanged(nameof(StartWithEmptyPage));
        OnPropertyChanged(nameof(EffectiveGapPx));
        OnPropertyChanged(nameof(FitMode));
        OnPropertyChanged(nameof(BackgroundMode));
        OnPropertyChanged(nameof(ScrollHAlign));
        OnPropertyChanged(nameof(ShowNormalFit));
        OnPropertyChanged(nameof(ShowNormalScroll));
        OnPropertyChanged(nameof(ScrollItemAlignment));
        RaiseSettingOptionFlags();
        // ペアリング・高さ統一に影響するため再描画を促す
        RaisePositionChanged();
    }

    /// <summary>seg(ピル)選択状態の派生プロパティをまとめて通知する。</summary>
    private void RaiseSettingOptionFlags()
    {
        OnPropertyChanged(nameof(IsResizeLarger));
        OnPropertyChanged(nameof(IsResizeSmaller));
        OnPropertyChanged(nameof(IsResizeNone));
        OnPropertyChanged(nameof(IsAlignTop));
        OnPropertyChanged(nameof(IsAlignMiddle));
        OnPropertyChanged(nameof(IsAlignBottom));
        OnPropertyChanged(nameof(IsGapTight));
        OnPropertyChanged(nameof(IsGapLoose));
        OnPropertyChanged(nameof(IsTurnDouble));
        OnPropertyChanged(nameof(IsTurnSingle));
        OnPropertyChanged(nameof(IsFitFit));
        OnPropertyChanged(nameof(IsFitWidth));
        OnPropertyChanged(nameof(IsFitOne));
        OnPropertyChanged(nameof(IsHAlignLeft));
        OnPropertyChanged(nameof(IsHAlignCenter));
        OnPropertyChanged(nameof(IsHAlignRight));
        OnPropertyChanged(nameof(IsBgDark));
        OnPropertyChanged(nameof(IsBgLight));
        OnPropertyChanged(nameof(IsBgChecker));
    }

    private void Persist() => _persist?.Invoke(_settings);

    private void RaisePositionChanged()
    {
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentImagePath));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(CurrentSpreadPair));
        OnPropertyChanged(nameof(CurrentIsSpreadOccupy));
        OnPropertyChanged(nameof(CurrentSpreadOccupyImage));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SeekValue));
    }

    /// <summary>
    /// 指定点が「画像外余白」か(REQ-044 v1.3/CR-7 の判定。純粋関数 — unit 検査可能)。
    /// 画像は Uniform + DownOnly(縮小のみ・拡大なし)で表示領域中央へフィットされる前提。
    /// 画像なし(寸法 0 以下)は全面が余白。
    /// </summary>
    public static bool IsBackgroundPoint(
        double hostWidth, double hostHeight, double imageWidth, double imageHeight, double x, double y)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return true;
        }

        var scale = Math.Min(1.0, Math.Min(hostWidth / imageWidth, hostHeight / imageHeight));
        var renderWidth = imageWidth * scale;
        var renderHeight = imageHeight * scale;
        var left = (hostWidth - renderWidth) / 2;
        var top = (hostHeight - renderHeight) / 2;
        return x < left || x > left + renderWidth || y < top || y > top + renderHeight;
    }
}
