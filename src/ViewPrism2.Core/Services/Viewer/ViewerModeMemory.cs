using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// モード別位置記憶(OC-13、仕様 §2.9 REQ-054)。純粋計算/状態保持(M-VIEWERCORE-017)。
/// モードごとに最後に表示した画像 index を記憶し、モード切替時にそのモードの前回位置を復元する。
/// 各モードの記憶の初期値はビューア起動時の index(クリックした画像)— 未訪問モードへの初回切替は
/// 起動時の画像から始まる(原典準拠)。記憶はビューアを開いている間のみ(永続化しない — REQ-059)。
/// </summary>
public sealed class ViewerModeMemory
{
    private readonly Dictionary<ViewerMode, int> _byMode;

    /// <param name="initialIndex">ビューア起動時の index。全モードの初期値となる。</param>
    public ViewerModeMemory(int initialIndex)
    {
        _byMode = new Dictionary<ViewerMode, int>
        {
            [ViewerMode.Normal] = initialIndex,
            [ViewerMode.Scroll] = initialIndex,
            [ViewerMode.SpreadRight] = initialIndex,
            [ViewerMode.SpreadLeft] = initialIndex,
        };
    }

    /// <summary>当該モードの記憶 index を返す。</summary>
    public int Get(ViewerMode mode) => _byMode[mode];

    /// <summary>当該モードの記憶 index を更新する。</summary>
    public void Set(ViewerMode mode, int index) => _byMode[mode] = index;
}
