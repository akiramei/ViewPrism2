using System.Collections;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-134: 再スキャンの再リンク候補照合(規則 3a)は O(missing + new) でなければならない。
/// 旧実装は新規ファイル 1 件ごとに missing 行全体を Where→OrderBy で線形走査し O(missing × new)
/// (混入=初回製造 b1f13ec・二段階化 ECO-129/130 も温存)。ここでは「missing 行は新規件数 N に
/// 依らず合計 M 回だけ走査される」不変条件を、走査回数を数える IReadOnlyList ラッパで固定する。
/// 併せて候補選択の挙動同値(同ハッシュ複数一致=id 序数昇順の先頭)が不変であることを pin する。
/// </summary>
[Trait("cp", "CP-SCAN-004")]
public sealed class CpScanCandidateIndexTests
{
    [Fact]
    public void 候補照合はmissing行を新規件数に依らず一度だけ走査する()
    {
        const int m = 200; // missing 行数
        const int n = 200; // 再スキャンの新規ファイル数
        var judge = new ScanJudge();

        var missing = new List<ImageRecord>(m);
        for (var i = 0; i < m; i++)
        {
            // hash は new 側と一致させない(候補ヒットの有無に依らず走査量が問題)
            missing.Add(NewMissing($"m-{i:D4}", HashOf($"missing-{i}")));
        }

        var counting = new CountingReadOnlyList<ImageRecord>(missing);
        var index = BuildIndex(counting);

        for (var i = 0; i < n; i++)
        {
            var hash = HashOf($"new-{i}");
            var facts = new ScanFileFacts(
                $"new-{i:D4}.jpg", 1, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => hash);
            var dbFacts = MakeDbFacts(index);
            var decision = judge.Judge(facts, dbFacts, isInitialScan: false);
            Assert.Equal(ScanDecisionKind.AddPending, decision.Kind);
        }

        // O(M+N): missing 行は写像構築時に M 回走査されるのみ。判定ごとの再走査(=O(M×N))は起きない。
        Assert.Equal(m, counting.AccessCount);
    }

    [Fact]
    public void 候補選択はid序数昇順の先頭で不変()
    {
        var judge = new ScanJudge();
        var hash = HashOf("x");
        var missingA = NewMissing("m-a", hash);
        var missingB = NewMissing("m-b", hash);

        // 入力順は B, A(挿入順でなく id 序数で選ぶことを検査)
        var counting = new CountingReadOnlyList<ImageRecord>(new List<ImageRecord> { missingB, missingA });
        var index = BuildIndex(counting);

        var decision = judge.Judge(
            new ScanFileFacts("new.jpg", 1, "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", () => hash),
            MakeDbFacts(index),
            isInitialScan: false);

        Assert.Equal(ScanDecisionKind.AddPending, decision.Kind);
        Assert.Equal("m-a", decision.CandidateLinkId);
    }

    // ECO-134 是正: 候補写像を一度だけ構築(counting list をここで M 回走査)し、
    // 判定器は写像 lookup(counting list 非走査)。→ AccessCount は N に依らず M。
    private static IReadOnlyDictionary<string, string> BuildIndex(IReadOnlyList<ImageRecord> missing)
        => ScanJudge.BuildMissingCandidateIndex(missing);

    private static ScanDbFacts MakeDbFacts(IReadOnlyDictionary<string, string> index)
        => new(null, index);

    private static ImageRecord NewMissing(string id, string hash) => new()
    {
        Id = id,
        SyncFolderId = "f-1",
        RelativePath = $"{id}.jpg",
        FileName = $"{id}.jpg",
        FileSize = 1,
        Hash = hash,
        Status = ImageStatus.Missing,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };

    private static string HashOf(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>走査(列挙)された要素数を数える IReadOnlyList ラッパ。</summary>
    private sealed class CountingReadOnlyList<T>(IReadOnlyList<T> inner) : IReadOnlyList<T>
    {
        private int _access;

        public int AccessCount => _access;

        public int Count => inner.Count;

        public T this[int index]
        {
            get
            {
                _access++;
                return inner[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in inner)
            {
                _access++;
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
