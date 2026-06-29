using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services;

/// <summary>
/// 作業スペースサービス(ECO-020 / REQ-074・REQ-075 / M-WORKSPACE-027)。
/// 名前付き・永続の画像集合に対する CRUD・デフォルト回転・受け渡し(追加)・移動・件数を所有する。
/// 不変条件: デフォルトは厳密に 1 つ(INV-W1)・所属は集合で件数/一覧は normal のみ(INV-W2)・
/// デフォルトはリネーム不可(INV-W3)・add/move は所属の論理操作のみで物理非破壊(INV-W4)・移動は原子(INV-W5)。
/// 状態遷移の原子性はリポジトリが担保し、本サービスは検証・採番・時刻整形を行う(描画から独立=unit 検査可能)。
/// </summary>
public sealed class WorkspaceService
{
    /// <summary>初回シードのデフォルト名。</summary>
    public const string DefaultName = "デフォルト";

    private readonly IWorkspaceRepository _repo;
    private readonly IClock _clock;

    public WorkspaceService(IWorkspaceRepository repo, IClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    /// <summary>デフォルトが無ければ 1 件シードする(初回起動・INV-W1)。既にあれば何もしない。</summary>
    public async Task<Workspace> EnsureDefaultExistsAsync()
    {
        var existing = await _repo.GetDefaultAsync().ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var ws = new Workspace
        {
            Id = IdGenerator.NewId(),
            Name = DefaultName,
            IsDefault = true,
            Seq = await _repo.GetMaxSeqAsync().ConfigureAwait(false) + 1,
            CreatedAt = _clock.UtcNowIso(),
        };
        await _repo.AddAsync(ws).ConfigureAwait(false);
        return ws;
    }

    /// <summary>全作業スペースを seq 昇順・normal 件数つきで返す(INV-W2)。</summary>
    public Task<IReadOnlyList<WorkspaceWithCount>> ListAsync() => _repo.GetAllWithNormalCountsAsync();

    /// <summary>所属画像(normal・安定順)を返す(INV-W2)。</summary>
    public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string workspaceId)
        => _repo.GetNormalImagesAsync(workspaceId);

    /// <summary>所属画像のうち deleted のものを返す(ECO-021/β-4: ゴミ箱 popup・件数バッジ用)。</summary>
    public Task<IReadOnlyList<ImageRecord>> GetDeletedImagesAsync(string workspaceId)
        => _repo.GetDeletedImagesAsync(workspaceId);

    /// <summary>
    /// デフォルト回転(ACT-0074・INV-W1): 新しい空デフォルトを作り、旧デフォルトを時刻名で降格する。
    /// 常にデフォルトが厳密に 1 つを保つ。新デフォルトを返す。デフォルト未シードなら先にシードする。
    /// </summary>
    public async Task<Workspace> CreateRotatingDefaultAsync()
    {
        var current = await _repo.GetDefaultAsync().ConfigureAwait(false);
        if (current is null)
        {
            // 異常系: デフォルトが無いならシードだけ行う(回転対象が無い)
            return await EnsureDefaultExistsAsync().ConfigureAwait(false);
        }

        var now = _clock.UtcNowIso();
        var newDefault = new Workspace
        {
            Id = IdGenerator.NewId(),
            Name = DefaultName,
            IsDefault = true,
            Seq = await _repo.GetMaxSeqAsync().ConfigureAwait(false) + 1,
            CreatedAt = now,
        };
        await _repo.CreateRotatingDefaultAsync(newDefault, current.Id, TimestampName(now)).ConfigureAwait(false);
        return newDefault;
    }

    /// <summary>
    /// リネーム(ACT-0076・INV-W3): デフォルトは不可。空白は「スペース」へフォールバック。
    /// 存在しない/デフォルトは <see cref="ErrorCode"/> を返す。
    /// </summary>
    public async Task<Result> RenameAsync(string id, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var ws = await _repo.GetByIdAsync(id).ConfigureAwait(false);
        if (ws is null)
        {
            return Result.Fail(ErrorCode.NotFound, "作業スペースが存在しません。");
        }

        if (ws.IsDefault)
        {
            return Result.Fail(ErrorCode.ValidationError, "デフォルトの作業スペースはリネームできません。");
        }

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            trimmed = "スペース";
        }

        await _repo.RenameAsync(id, trimmed).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>
    /// 受け渡し(DOM-0026): 画像タブ作業モード「追加」の行き先=デフォルトへ和集合追加する。
    /// デフォルト未シードなら先にシードする。書き込み先デフォルトを返す。物理非破壊(INV-W4)。
    /// </summary>
    public async Task<Workspace> AddImagesToDefaultAsync(IReadOnlyList<string> imageIds)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        var def = await EnsureDefaultExistsAsync().ConfigureAwait(false);
        if (imageIds.Count > 0)
        {
            await _repo.AddImagesAsync(def.Id, imageIds, _clock.UtcNowIso()).ConfigureAwait(false);
        }
        return def;
    }

    /// <summary>
    /// 移動(ACT-0077・INV-W5): 選択画像を現スペースから移動先へ原子移動する。
    /// 両スペースの存在を検証する。物理非破壊(INV-W4)。
    /// </summary>
    public async Task<Result> MoveImagesAsync(string fromWorkspaceId, string toWorkspaceId, IReadOnlyList<string> imageIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromWorkspaceId);
        ArgumentException.ThrowIfNullOrEmpty(toWorkspaceId);
        ArgumentNullException.ThrowIfNull(imageIds);

        if (string.Equals(fromWorkspaceId, toWorkspaceId, StringComparison.Ordinal))
        {
            return Result.Fail(ErrorCode.ValidationError, "同じ作業スペースへは移動できません。");
        }

        if (await _repo.GetByIdAsync(fromWorkspaceId).ConfigureAwait(false) is null ||
            await _repo.GetByIdAsync(toWorkspaceId).ConfigureAwait(false) is null)
        {
            return Result.Fail(ErrorCode.NotFound, "作業スペースが存在しません。");
        }

        if (imageIds.Count > 0)
        {
            await _repo.MoveImagesAsync(fromWorkspaceId, toWorkspaceId, imageIds, _clock.UtcNowIso()).ConfigureAwait(false);
        }
        return Result.Ok();
    }

    /// <summary>ISO 8601 UTC 文字列(yyyy-MM-ddTHH:mm:ss.fffZ)から表示用時刻名「yyyy/MM/dd HH:mm」を作る(決定論)。</summary>
    private static string TimestampName(string iso)
    {
        // 部分文字列で抽出(culture/tz 非依存・決定論)。書式は IsoTimestamp.Pattern に対応。
        if (iso.Length < 16)
        {
            return iso;
        }
        return $"{iso[0..4]}/{iso[5..7]}/{iso[8..10]} {iso[11..13]}:{iso[14..16]}";
    }
}
