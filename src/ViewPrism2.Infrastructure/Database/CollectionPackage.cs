using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Package;

namespace ViewPrism2.Infrastructure.Database;

/// <summary>
/// コレクション論理パッケージ V1 の形式定数(ECO-073 §3.1)。
/// transport=単一 UTF-8 JSON。互換性判定は format/formatVersion/minReaderVersion/features で行い、
/// schema_migrations や app_version を判定に使わない(app_version は診断表示のみ)。
/// </summary>
public static class CollectionPackageFormat
{
    public const string Format = "viewprism2-logical-package";
    public const int FormatVersion = 1;

    /// <summary>この読み込み実装が理解できる論理形式の上限。</summary>
    public const int ReaderVersion = 1;

    public const string Kind = "collection";
    public const string FileExtension = ".viewprism2-collection.json";
    public const string PartialExtension = ".partial";

    /// <summary>
    /// 管理フォルダの既定(ECO-074/案A裁定: ユーザー文書配下=持ち出し・目視確認のしやすさ優先。
    /// A層 SS-002 の %APPDATA%/ViewPrism2/snapshots と命名対称)。
    /// </summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ViewPrism2", "collections");

    /// <summary>既知の必須 feature(未知の必須 feature を含むファイルは拒否)。</summary>
    public static readonly IReadOnlySet<string> KnownFeatures =
        new HashSet<string>(StringComparer.Ordinal) { "tag-definition-v1", "image-fingerprint-v1" };

    public const string FingerprintAlgorithm = "sha256";
}

/// <summary>パッケージ形式違反(厳格拒否 §3.5)。DB を変更する前に投げる。</summary>
public sealed class PackageFormatException(string message) : Exception(message);

public sealed record PackageCollection(string SourceId, string Name, PackageRootHint? RootHint);

public sealed record PackageRootHint(string Platform, string Path);

public sealed record PackageFingerprint(string Algorithm, int Version, string Value, long? SizeBytes = null);

public sealed record PackageImageTag(string TagSourceId, string? Value);

public sealed record PackageImage(
    string SourceId,
    string RelativePath,
    long FileSize,
    string CreatedDate,
    string ModifiedDate,
    PackageFingerprint Fingerprint,
    IReadOnlyList<PackageImageTag> Tags);

/// <summary>ヘッダ(images 以外の全て)。プロパティ順に依存せず 1 走査で読む(images はスキップ+件数のみ)。</summary>
public sealed record PackageHeader(
    int FormatVersion,
    int MinReaderVersion,
    IReadOnlyList<string> Features,
    string? BackupId,
    string? SourceLibraryId,
    string? CreatedAt,
    string? AppVersion,
    PackageCollection Collection,
    IReadOnlyList<PackageTagDef> Tags,
    long ImageCount);

/// <summary>
/// パッケージ JSON のストリーミング読取(§3.5: 読み込み/検証/プレビューも全件メモリ展開しない)。
/// Utf8JsonReader を増分バッファで回し、images は 1 要素ずつ生成する。
/// 同一オブジェクト内の重複プロパティ・未知の必須 feature・不正パス等は PackageFormatException。
/// </summary>
public static class PackageJson
{
    /// <summary>ヘッダ走査(pass1)。images はスキップして件数だけ数える。</summary>
    public static PackageHeader ReadHeader(Stream stream)
    {
        var pump = new JsonPump(stream);
        Expect(pump.Read() && pump.TokenType == JsonTokenType.StartObject, "JSON オブジェクトではありません");

        string? format = null;
        int? formatVersion = null, minReaderVersion = null;
        string? kind = null, backupId = null, sourceLibraryId = null, createdAt = null, appVersion = null;
        List<string>? features = null;
        PackageCollection? collection = null;
        List<PackageTagDef>? tags = null;
        long imageCount = -1;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (pump.Read() && pump.TokenType != JsonTokenType.EndObject)
        {
            Expect(pump.TokenType == JsonTokenType.PropertyName, "プロパティ名を期待しました");
            var name = pump.StringValue!;
            Expect(seen.Add(name), $"重複プロパティ: {name}");
            switch (name)
            {
                case "format": format = ReadString(pump); break;
                case "formatVersion": formatVersion = (int)ReadInt(pump); break;
                case "minReaderVersion": minReaderVersion = (int)ReadInt(pump); break;
                case "kind": kind = ReadString(pump); break;
                case "backupId": backupId = ReadStringOrNull(pump); break;
                case "sourceLibraryId": sourceLibraryId = ReadStringOrNull(pump); break;
                case "createdAt": createdAt = ReadStringOrNull(pump); break;
                case "appVersion": appVersion = ReadStringOrNull(pump); break;
                case "features": features = ReadStringArray(pump); break;
                case "collection": collection = ParseCollection(ReadNodeObject(pump)); break;
                case "tags": tags = ReadTagArray(pump); break;
                case "images": imageCount = SkipImagesCounting(pump); break;
                default: SkipValue(pump); break; // 未知の任意フィールドは無視(前方互換)
            }
        }

        Expect(format == CollectionPackageFormat.Format, "ViewPrism2 のコレクションパッケージではありません");
        Expect(kind == CollectionPackageFormat.Kind, $"kind '{kind}' は取り込み対象外です");
        Expect(formatVersion is >= 1, "formatVersion がありません");
        Expect(minReaderVersion is >= 1, "minReaderVersion がありません");
        if (minReaderVersion > CollectionPackageFormat.ReaderVersion)
        {
            throw new PackageFormatException(
                $"互換性のないバージョンで作成されています(要求リーダー v{minReaderVersion} > 対応 v{CollectionPackageFormat.ReaderVersion})");
        }

        features ??= [];
        var unknown = features.Where(f => !CollectionPackageFormat.KnownFeatures.Contains(f)).ToList();
        Expect(unknown.Count == 0, $"未知の必須 feature: {string.Join(", ", unknown)}");
        Expect(collection is not null, "collection がありません");
        Expect(tags is not null, "tags がありません");
        Expect(imageCount >= 0, "images がありません");

        return new PackageHeader(
            formatVersion!.Value, minReaderVersion!.Value, features, backupId, sourceLibraryId,
            createdAt, appVersion, collection!, tags!, imageCount);
    }

    /// <summary>images 走査(pass2)。1 要素ずつ検証して返す(全件をメモリへ展開しない)。</summary>
    public static IEnumerable<PackageImage> ReadImages(Stream stream, IReadOnlySet<string> knownTagIds)
    {
        var pump = new JsonPump(stream);
        Expect(pump.Read() && pump.TokenType == JsonTokenType.StartObject, "JSON オブジェクトではありません");
        while (pump.Read() && pump.TokenType != JsonTokenType.EndObject)
        {
            Expect(pump.TokenType == JsonTokenType.PropertyName, "プロパティ名を期待しました");
            if (pump.StringValue != "images")
            {
                SkipValue(pump);
                continue;
            }

            Expect(pump.Read() && pump.TokenType == JsonTokenType.StartArray, "images は配列ではありません");
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            while (pump.Read() && pump.TokenType != JsonTokenType.EndArray)
            {
                Expect(pump.TokenType == JsonTokenType.StartObject, "images 要素がオブジェクトではありません");
                var image = ParseImage(ReadNodeObject(pump, alreadyAtStart: true), knownTagIds);
                Expect(seenIds.Add(image.SourceId), $"画像 source_id が重複: {image.SourceId}");
                yield return image;
            }

            yield break;
        }

        throw new PackageFormatException("images がありません");
    }

    // ---- 要素パース(小さなオブジェクトは JsonNode 化して検証する。JsonObject が重複キーを拒否) ----

    private static PackageCollection ParseCollection(JsonObject node)
    {
        var sourceId = RequireString(node, "sourceId", "collection");
        var name = RequireString(node, "name", "collection");
        PackageRootHint? hint = null;
        if (node["rootHint"] is JsonObject h)
        {
            hint = new PackageRootHint(
                h["platform"]?.GetValue<string>() ?? "unknown",
                h["path"]?.GetValue<string>() ?? "");
        }

        return new PackageCollection(sourceId, name, hint);
    }

    private static PackageTagDef ParseTag(JsonObject node)
    {
        var sourceId = RequireString(node, "sourceId", "tag");
        var name = RequireString(node, "name", "tag");
        var type = node["type"]?.GetValue<string>() switch
        {
            "simple" => TagType.Simple,
            "textual" => TagType.Textual,
            "numeric" => TagType.Numeric,
            var t => throw new PackageFormatException($"タグ '{name}' の型が不正です: {t}"),
        };
        var predefined = node["predefinedValues"] is JsonArray arr
            ? arr.Select(v => v?.GetValue<string>()
                ?? throw new PackageFormatException($"タグ '{name}' の predefinedValues に null")).ToList()
            : [];
        return new PackageTagDef(
            sourceId, name, type,
            node["parentSourceId"]?.GetValue<string>(),
            node["color"]?.GetValue<string>(),
            node["description"]?.GetValue<string>(),
            predefined,
            GetOptionalDouble(node["minimum"]),
            GetOptionalDouble(node["maximum"]),
            GetOptionalDouble(node["step"]),
            node["unit"]?.GetValue<string>());
    }

    /// <summary>整数/実数の両トークンを受ける(JsonValue(long) は GetValue&lt;double&gt; で読めない)。</summary>
    private static double? GetOptionalDouble(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        _ => throw new PackageFormatException("数値を期待しました"),
    };

    private static PackageImage ParseImage(JsonObject node, IReadOnlySet<string> knownTagIds)
    {
        var sourceId = RequireString(node, "sourceId", "image");
        var relativePath = RequireString(node, "relativePath", "image");
        ValidateRelativePath(relativePath);
        var fileSize = node["fileSize"]?.GetValue<long>()
            ?? throw new PackageFormatException($"画像 {relativePath}: fileSize がありません");
        Expect(fileSize >= 0, $"画像 {relativePath}: fileSize が不正");
        var created = RequireString(node, "createdDate", $"image {relativePath}");
        var modified = RequireString(node, "modifiedDate", $"image {relativePath}");

        if (node["fingerprint"] is not JsonObject fp)
        {
            throw new PackageFormatException($"画像 {relativePath}: fingerprint がありません");
        }

        var fingerprint = new PackageFingerprint(
            RequireString(fp, "algorithm", "fingerprint"),
            (int)(GetOptionalDouble(fp["version"]) ?? 0),
            RequireString(fp, "value", "fingerprint"),
            (long?)GetOptionalDouble(fp["sizeBytes"]));
        Expect(fingerprint.Algorithm == CollectionPackageFormat.FingerprintAlgorithm,
            $"画像 {relativePath}: 未対応の指紋 {fingerprint.Algorithm}");

        var tags = new List<PackageImageTag>();
        if (node["tags"] is JsonArray tagArr)
        {
            foreach (var t in tagArr)
            {
                if (t is not JsonObject to)
                {
                    throw new PackageFormatException($"画像 {relativePath}: tags 要素が不正");
                }

                var tagId = RequireString(to, "tagSourceId", $"image {relativePath} tag");
                Expect(knownTagIds.Contains(tagId), $"画像 {relativePath}: 存在しないタグへの参照 {tagId}");
                // value は省略不可(simple の正常付与と壊れたレコードを区別する・§3.1)
                Expect(to.ContainsKey("value"), $"画像 {relativePath}: タグ値フィールドが省略されています");
                tags.Add(new PackageImageTag(tagId, to["value"]?.GetValue<string>()));
            }
        }

        return new PackageImage(sourceId, relativePath, fileSize, created, modified, fingerprint, tags);
    }

    /// <summary>OS 非依存 `/` 区切り。絶対パス・`..`・NUL・ルート外脱出を拒否(§3.1)。</summary>
    public static void ValidateRelativePath(string path)
    {
        Expect(path.Length > 0, "relativePath が空です");
        Expect(!path.Contains('\0'), "relativePath に NUL が含まれます");
        Expect(!path.Contains('\\'), $"relativePath は '/' 区切りでなければなりません: {path}");
        Expect(!path.StartsWith('/') && !(path.Length >= 2 && path[1] == ':'),
            $"relativePath が絶対パスです: {path}");
        Expect(path.Split('/').All(seg => seg is not ("" or "." or "..")),
            $"relativePath がルート外へ脱出します: {path}");
    }

    // ---- ストリーミング補助 ----

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new PackageFormatException(message);
        }
    }

    private static string ReadString(JsonPump pump)
    {
        Expect(pump.Read() && pump.TokenType == JsonTokenType.String, "文字列を期待しました");
        return pump.StringValue!;
    }

    private static string? ReadStringOrNull(JsonPump pump)
    {
        Expect(pump.Read(), "値を期待しました");
        return pump.TokenType switch
        {
            JsonTokenType.String => pump.StringValue,
            JsonTokenType.Null => null,
            _ => throw new PackageFormatException("文字列または null を期待しました"),
        };
    }

    private static long ReadInt(JsonPump pump)
    {
        Expect(pump.Read() && pump.TokenType == JsonTokenType.Number, "数値を期待しました");
        return long.TryParse(pump.RawNumber, out var v)
            ? v
            : throw new PackageFormatException($"整数ではありません: {pump.RawNumber}");
    }

    private static List<string> ReadStringArray(JsonPump pump)
    {
        Expect(pump.Read() && pump.TokenType == JsonTokenType.StartArray, "配列を期待しました");
        var list = new List<string>();
        while (pump.Read() && pump.TokenType != JsonTokenType.EndArray)
        {
            Expect(pump.TokenType == JsonTokenType.String, "文字列配列を期待しました");
            list.Add(pump.StringValue!);
        }

        return list;
    }

    private static List<PackageTagDef> ReadTagArray(JsonPump pump)
    {
        Expect(pump.Read() && pump.TokenType == JsonTokenType.StartArray, "tags は配列ではありません");
        var list = new List<PackageTagDef>();
        while (pump.Read() && pump.TokenType != JsonTokenType.EndArray)
        {
            Expect(pump.TokenType == JsonTokenType.StartObject, "tags 要素がオブジェクトではありません");
            list.Add(ParseTag(ReadNodeObject(pump, alreadyAtStart: true)));
        }

        return list;
    }

    /// <summary>現在位置の値 1 個を JsonNode オブジェクトへ(小要素専用。重複キーは JsonObject が拒否)。</summary>
    private static JsonObject ReadNodeObject(JsonPump pump, bool alreadyAtStart = false)
    {
        if (!alreadyAtStart)
        {
            Expect(pump.Read() && pump.TokenType == JsonTokenType.StartObject, "オブジェクトを期待しました");
        }

        return (JsonObject)ReadNodeValue(pump, atValueToken: true)!;
    }

    private static JsonNode? ReadNodeValue(JsonPump pump, bool atValueToken = false)
    {
        if (!atValueToken)
        {
            Expect(pump.Read(), "値を期待しました");
        }

        switch (pump.TokenType)
        {
            case JsonTokenType.StartObject:
                var obj = new JsonObject();
                while (pump.Read() && pump.TokenType != JsonTokenType.EndObject)
                {
                    Expect(pump.TokenType == JsonTokenType.PropertyName, "プロパティ名を期待しました");
                    var name = pump.StringValue!;
                    try
                    {
                        obj.Add(name, ReadNodeValue(pump));
                    }
                    catch (ArgumentException)
                    {
                        throw new PackageFormatException($"重複プロパティ: {name}");
                    }
                }

                return obj;
            case JsonTokenType.StartArray:
                var arr = new JsonArray();
                while (pump.Read() && pump.TokenType != JsonTokenType.EndArray)
                {
                    arr.Add(ReadNodeValue(pump, atValueToken: true));
                }

                return arr;
            case JsonTokenType.String: return JsonValue.Create(pump.StringValue);
            case JsonTokenType.Number:
                return long.TryParse(pump.RawNumber, out var l)
                    ? JsonValue.Create(l)
                    : JsonValue.Create(double.Parse(pump.RawNumber, System.Globalization.CultureInfo.InvariantCulture));
            case JsonTokenType.True: return JsonValue.Create(true);
            case JsonTokenType.False: return JsonValue.Create(false);
            case JsonTokenType.Null: return null;
            default: throw new PackageFormatException($"不正なトークン: {pump.TokenType}");
        }
    }

    private static void SkipValue(JsonPump pump)
    {
        Expect(pump.Read(), "値を期待しました");
        var depth = pump.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray ? 1 : 0;
        while (depth > 0 && pump.Read())
        {
            depth += pump.TokenType switch
            {
                JsonTokenType.StartObject or JsonTokenType.StartArray => 1,
                JsonTokenType.EndObject or JsonTokenType.EndArray => -1,
                _ => 0,
            };
        }
    }

    private static long SkipImagesCounting(JsonPump pump)
    {
        Expect(pump.Read() && pump.TokenType == JsonTokenType.StartArray, "images は配列ではありません");
        long count = 0;
        while (pump.Read() && pump.TokenType != JsonTokenType.EndArray)
        {
            Expect(pump.TokenType == JsonTokenType.StartObject, "images 要素がオブジェクトではありません");
            count++;
            var depth = 1;
            while (depth > 0 && pump.Read())
            {
                depth += pump.TokenType switch
                {
                    JsonTokenType.StartObject or JsonTokenType.StartArray => 1,
                    JsonTokenType.EndObject or JsonTokenType.EndArray => -1,
                    _ => 0,
                };
            }
        }

        return count;
    }

    private static string RequireString(JsonObject node, string key, string context)
        => node[key]?.GetValue<string>()
           ?? throw new PackageFormatException($"{context}: {key} がありません");
}

/// <summary>
/// Utf8JsonReader の増分ポンプ(Stream から必要分だけバッファへ読み足す)。
/// 途中で切れたファイルは Read が JsonException を投げる=PackageFormatException へ写像。
/// </summary>
internal sealed class JsonPump(Stream stream)
{
    private byte[] _buffer = new byte[64 * 1024];
    private int _length;
    private int _consumed;
    private bool _final;
    private JsonReaderState _state = new(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });

    public JsonTokenType TokenType { get; private set; }

    public string? StringValue { get; private set; }

    public string RawNumber { get; private set; } = "";

    public bool Read()
    {
        while (true)
        {
            var span = new ReadOnlySpan<byte>(_buffer, _consumed, _length - _consumed);
            var reader = new Utf8JsonReader(span, _final, _state);
            bool got;
            try
            {
                got = reader.Read();
            }
            catch (JsonException ex)
            {
                throw new PackageFormatException($"JSON が不正です: {ex.Message}");
            }

            if (got)
            {
                TokenType = reader.TokenType;
                StringValue = TokenType is JsonTokenType.String or JsonTokenType.PropertyName ? reader.GetString() : null;
                RawNumber = TokenType == JsonTokenType.Number
                    ? System.Text.Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan)
                    : "";
                _state = reader.CurrentState;
                _consumed += (int)reader.BytesConsumed;
                return true;
            }

            _state = reader.CurrentState;
            _consumed += (int)reader.BytesConsumed;
            if (_final)
            {
                return false;
            }

            Refill();
        }
    }

    private void Refill()
    {
        var remaining = _length - _consumed;
        if (remaining > 0)
        {
            Array.Copy(_buffer, _consumed, _buffer, 0, remaining);
        }

        _length = remaining;
        _consumed = 0;
        if (_length == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2); // 単一トークンがバッファ超(長大文字列)
        }

        var read = stream.Read(_buffer, _length, _buffer.Length - _length);
        if (read == 0)
        {
            _final = true; // 途中で切れたファイル → 次の Read が JsonException(=拒否)
        }

        _length += read;
    }
}
