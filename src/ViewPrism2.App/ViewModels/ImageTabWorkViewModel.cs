using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 作業対象の蓄積ストア+作業スペース受け渡し(ECO-036 第2段 = M-UI-WORK-033)。
/// ECO-017 の session 蓄積(Set 意味論・チップ表示用)と ECO-020 の受け渡し
/// (WorkspaceService.AddImagesToDefaultAsync)を ImageTabViewModel(god-VM)から切り出したもの。
///
/// 境界(order §10.2): モードフラグ(_workMode)・排他・合成 UI プロパティはホスト残置 —
/// XAML/tests の全消費者はホスト公開契約のままで、本 VM の UI 直接消費者はゼロ。
/// 通知はホストの一括通知で閉じる(将来段に備え自 prop 通知も発行するが本段の挙動には関与しない)。
/// 順序保存(§10.2): 蓄積(同期 AddTargets)と受け渡し(HandOffAsync)は別メソッド —
/// 旧実装の「蓄積→選択クリア→マーカー通知→await 受け渡し」の順序をホスト殻が保持するため。
/// </summary>
public sealed partial class ImageTabWorkViewModel : ObservableObject
{
    private readonly WorkspaceService _workspaces;
    private readonly List<string> _targets = new();

    public ImageTabWorkViewModel(WorkspaceService workspaces)
    {
        _workspaces = workspaces;
    }

    /// <summary>蓄積件数(ホストの合成プロパティ HasWorkTargets/WorkTargetLabel が参照)。</summary>
    public int Count => _targets.Count;

    /// <summary>和集合追加(Set 意味論・重複なし。旧 AddToWork 1483-1484 と同一)。同期=蓄積のみ。</summary>
    public void AddTargets(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
            if (!_targets.Contains(id)) _targets.Add(id);
        OnPropertyChanged(string.Empty); // 本段では UI 直接消費者ゼロ(ホスト通知が正)— 将来段用
    }

    /// <summary>受け渡し: デフォルト作業スペースへ永続追加(ECO-020・旧 AddToWork 1487 と同一)。</summary>
    public async Task HandOffAsync(IReadOnlyList<string> ids)
        => await _workspaces.AddImagesToDefaultAsync(ids).ConfigureAwait(true);
}
