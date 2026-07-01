using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// タグ制御ページプランの 1 見開き(プラン見開き)。仕様 §2.12.2/§2.12.4。
/// 画面の左右ページ index(<c>null</c>=空白ページ)・<c>spread</c> 占有・canonical 現在画像を保持する。
/// </summary>
public readonly record struct TagControlSpread
{
    /// <summary>画面左ページの画像 index。<c>null</c>=空白ページ(無地)。</summary>
    public int? LeftIndex { get; init; }

    /// <summary>画面右ページの画像 index。<c>null</c>=空白ページ(無地)。</summary>
    public int? RightIndex { get; init; }

    /// <summary>この見開きを 1 枚の画像が左右占有する(<c>spread</c> アクション)。</summary>
    public bool IsSpread { get; init; }

    /// <summary>
    /// この見開きの canonical 現在画像 index(仕様 §2.12.4 M-1: 先読み面優先・
    /// 空白なら後読み面・spread 占有なら占有画像)。両面空白は構築上生じないため常に一意。
    /// </summary>
    public int CanonicalImage { get; init; }
}

/// <summary>
/// タグ制御ページプラン(OC-24)。プラン見開き列・画像→見開き index 対応・非 skip 総数/位置を保持する。
/// </summary>
public sealed class TagControlPlan
{
    /// <summary>プラン見開き列 <c>P[0..N-1]</c>(N=見開き数)。</summary>
    public IReadOnlyList<TagControlSpread> Spreads { get; }

    /// <summary>画像 index → それを含むプラン見開き index(各非 skip 画像はちょうど 1 見開きに属する)。</summary>
    public IReadOnlyDictionary<int, int> ImageToSpread { get; }

    /// <summary>非 skip 画像の枚数(位置/総数表示の総数。仕様 §2.12.4)。</summary>
    public int NonSkipCount { get; }

    /// <summary>画像 index → 非 skip 列での 1 起点位置(位置表示用。仕様 §2.12.4)。</summary>
    public IReadOnlyDictionary<int, int> NonSkipPosition { get; }

    internal TagControlPlan(
        IReadOnlyList<TagControlSpread> spreads,
        IReadOnlyDictionary<int, int> imageToSpread,
        int nonSkipCount,
        IReadOnlyDictionary<int, int> nonSkipPosition)
    {
        Spreads = spreads;
        ImageToSpread = imageToSpread;
        NonSkipCount = nonSkipCount;
        NonSkipPosition = nonSkipPosition;
    }
}

/// <summary>
/// タグ制御ページプラン構築器(OC-24・仕様 §2.12.2)。
/// <c>(画像列, 解決アクション列) → スロット列 → 見開き列</c> を決定的に構築する。
/// ECO-022 新設・純粋計算 Core(M-TAGCTRL-028。<see cref="SpreadPairCalculator"/> と同層)。
/// </summary>
public static class TagControlLayoutCalculator
{
    /// <summary>スロット種別。</summary>
    private enum SlotKind
    {
        /// <summary>実画像(Value=画像 index)。</summary>
        Image,
        /// <summary>空白ページ(無地)。</summary>
        Blank,
        /// <summary>spread 占有(Value=占有画像 index・2 スロットを 1 枚で span)。</summary>
        SpreadOccupy,
    }

    private readonly record struct Slot(SlotKind Kind, int Value);

    /// <summary>
    /// 順序付き (画像 index, 解決アクション) 列から決定的ページプランを構築する(OC-24)。
    /// skip は事前除去、padToParity・seed 抑止(先頭が配置アクション時)を適用し、
    /// 2 スロット=1 チャンクで見開き列へ畳む。
    /// </summary>
    /// <param name="items">
    /// 順序付き列。各要素=画像 index と解決アクション(<c>null</c>=アクション無し)。
    /// imageIndex は呼び出し元一覧の元 index(skip 除去前)。
    /// </param>
    /// <param name="direction">見開きの開き方向(右開き/左開き)。</param>
    /// <param name="startWithEmptyPage">空白ページ開始(既定 OFF)。</param>
    public static TagControlPlan Build(
        IReadOnlyList<(int imageIndex, ViewerTagAction? action)> items,
        SpreadDirection direction,
        bool startWithEmptyPage)
    {
        ArgumentNullException.ThrowIfNull(items);

        // ---- 1) skip 事前除去。非 skip 列を作る(位置/総数の母集合) ----
        var nonSkip = new List<(int imageIndex, ViewerTagAction? action)>(items.Count);
        foreach (var item in items)
        {
            if (item.action != ViewerTagAction.Skip)
            {
                nonSkip.Add(item);
            }
        }

        // 非 skip 列での 1 起点位置(仕様 §2.12.4)。
        var nonSkipPosition = new Dictionary<int, int>(nonSkip.Count);
        for (var i = 0; i < nonSkip.Count; i++)
        {
            nonSkipPosition[nonSkip[i].imageIndex] = i + 1;
        }

        // 画面右/左スロットのパリティ(仕様 §2.12.2):
        // 画面右 = 右開き:偶(0)/左開き:奇(1)。画面左 = 右開き:奇(1)/左開き:偶(0)。
        var rightParity = direction == SpreadDirection.Right ? 0 : 1;
        var leftParity = direction == SpreadDirection.Right ? 1 : 0;

        var slots = new List<Slot>(nonSkip.Count + 2);

        void Emit(Slot slot) => slots.Add(slot);

        // padToParity(p): 次に emit するスロット index のパリティが p になるまで空白を 1 つずつ emit
        // (「次スロット」= slots.Count。空白開始 ON では S[0] 予約済のため基点長=1)。
        void PadToParity(int parity)
        {
            while ((slots.Count % 2) != parity)
            {
                Emit(new Slot(SlotKind.Blank, -1));
            }
        }

        // ---- 2) 空白ページ開始 seed(仕様 §2.12.2 seed 抑止規則) ----
        // 先頭の非 skip 画像がアクション無しのときのみ S[0]=空白 を予約する。
        // 先頭が配置アクション(force*/empty/spread)を持つ場合は seed を予約しない
        // (そのアクションが開始パリティを定める=両面空白の見開きを防ぐ)。
        if (startWithEmptyPage && nonSkip.Count > 0 && nonSkip[0].action is null)
        {
            Emit(new Slot(SlotKind.Blank, -1));
        }

        // ---- 3) 各非 skip 画像を順に処理(仕様 §2.12.2 の構築規則) ----
        foreach (var (imageIndex, action) in nonSkip)
        {
            switch (action)
            {
                case null:
                    Emit(new Slot(SlotKind.Image, imageIndex));
                    break;

                case ViewerTagAction.ForceRightPage:
                    // 画面右へ寄せる: 次スロットを画面右パリティに合わせて画像のみ emit(相方は流入)。
                    PadToParity(rightParity);
                    Emit(new Slot(SlotKind.Image, imageIndex));
                    break;

                case ViewerTagAction.ForceLeftPage:
                    PadToParity(leftParity);
                    Emit(new Slot(SlotKind.Image, imageIndex));
                    break;

                case ViewerTagAction.LeftPageEmpty:
                    // 画像を画面右に・画面左を空白に固定(同一見開き。空白も明示 emit)。
                    // 右開き: emit(画像); emit(空白) / 左開き: emit(空白); emit(画像)。
                    PadToParity(0);
                    if (direction == SpreadDirection.Right)
                    {
                        Emit(new Slot(SlotKind.Image, imageIndex));
                        Emit(new Slot(SlotKind.Blank, -1));
                    }
                    else
                    {
                        Emit(new Slot(SlotKind.Blank, -1));
                        Emit(new Slot(SlotKind.Image, imageIndex));
                    }

                    break;

                case ViewerTagAction.RightPageEmpty:
                    // 画像を画面左に・画面右を空白に固定(同一見開き。空白も明示 emit)。
                    // 右開き: emit(空白); emit(画像) / 左開き: emit(画像); emit(空白)。
                    PadToParity(0);
                    if (direction == SpreadDirection.Right)
                    {
                        Emit(new Slot(SlotKind.Blank, -1));
                        Emit(new Slot(SlotKind.Image, imageIndex));
                    }
                    else
                    {
                        Emit(new Slot(SlotKind.Image, imageIndex));
                        Emit(new Slot(SlotKind.Blank, -1));
                    }

                    break;

                case ViewerTagAction.Spread:
                    // 見開き 1 つを当該画像が占有(両スロットを 1 枚で span)。
                    PadToParity(0);
                    Emit(new Slot(SlotKind.SpreadOccupy, imageIndex));
                    Emit(new Slot(SlotKind.SpreadOccupy, imageIndex));
                    break;

                case ViewerTagAction.Skip:
                    // 事前除去済みのため到達しない。
                    break;
            }
        }

        // ---- 4) 末尾パリティ揃え: 奇数長なら末尾に空白を補い 2 チャンク化 ----
        if ((slots.Count % 2) != 0)
        {
            Emit(new Slot(SlotKind.Blank, -1));
        }

        // ---- 5) スロット列 → 見開き列(2 スロット=1 チャンク・画面マッピング) ----
        var spreads = new List<TagControlSpread>(slots.Count / 2);
        var imageToSpread = new Dictionary<int, int>(nonSkip.Count);

        for (var k = 0; k * 2 + 1 < slots.Count; k++)
        {
            var first = slots[k * 2];       // S[2k] = 先読み面
            var second = slots[k * 2 + 1];  // S[2k+1] = 後読み面

            // 画面マッピング: 右開きは S[2k]=右ページ・S[2k+1]=左ページ。
            // 左開きは S[2k]=左ページ・S[2k+1]=右ページ。
            int? leftPage;
            int? rightPage;
            if (direction == SpreadDirection.Right)
            {
                rightPage = first.Kind == SlotKind.Blank ? null : first.Value;
                leftPage = second.Kind == SlotKind.Blank ? null : second.Value;
            }
            else
            {
                leftPage = first.Kind == SlotKind.Blank ? null : first.Value;
                rightPage = second.Kind == SlotKind.Blank ? null : second.Value;
            }

            var isSpread = first.Kind == SlotKind.SpreadOccupy;

            // canonical 現在画像(仕様 §2.12.4 M-1): 先読み面が画像ならそれ・空白なら後読み面・spread は占有画像。
            var canonical = first.Kind != SlotKind.Blank ? first.Value : second.Value;

            spreads.Add(new TagControlSpread
            {
                LeftIndex = leftPage,
                RightIndex = rightPage,
                IsSpread = isSpread,
                CanonicalImage = canonical,
            });

            // 画像→見開き対応(各非 skip 画像はちょうど 1 見開きに属する)。
            if (first.Kind is SlotKind.Image or SlotKind.SpreadOccupy)
            {
                imageToSpread[first.Value] = k;
            }

            if (second.Kind == SlotKind.Image)
            {
                imageToSpread[second.Value] = k;
            }
            // SpreadOccupy の second は first と同一画像のため再登録不要。
        }

        return new TagControlPlan(spreads, imageToSpread, nonSkip.Count, nonSkipPosition);
    }
}
