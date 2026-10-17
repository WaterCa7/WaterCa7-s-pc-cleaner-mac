using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PcCleaner.Core;

/// <summary>A plist that cannot be read; the message is Russian.</summary>
public sealed class PlistException : Exception
{
    public PlistException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// XML property lists as plain .NET values: dict → Dictionary&lt;string, object?&gt;, array → List&lt;object?&gt;,
/// string → string, integer → long, real → double, true/false → bool, date → DateTimeOffset (UTC), data → byte[].
/// The DTD is never processed (no entity expansion, no external files).
/// </summary>
public static class Plist
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public static object? Parse(string xml)
    {
        XElement root;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), ReaderSettings);
            root = XDocument.Load(reader).Root ?? throw new PlistException("plist пуст");
        }
        catch (XmlException ex)
        {
            throw new PlistException($"plist повреждён: {ex.Message}", ex);
        }
        if (root.Name.LocalName == "plist")
        {
            var value = root.Elements().FirstOrDefault();
            return value is null ? null : Convert(value);
        }
        return Convert(root);
    }

    public static Dictionary<string, object?> ParseDict(string xml)
        => Parse(xml) as Dictionary<string, object?> ?? throw new PlistException("в plist нет словаря (dict) верхнего уровня");

    private static object? Convert(XElement e)
    {
        var text = e.Value;
        try
        {
            return e.Name.LocalName switch
            {
                "dict" => ConvertDict(e),
                "array" => e.Elements().Select(Convert).ToList(),
                "string" => text,
                "integer" => long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n
                    : ulong.Parse(text.Trim(), CultureInfo.InvariantCulture),
                "real" => double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                "true" => true,
                "false" => false,
                "date" => DateTimeOffset.Parse(text.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                "data" => System.Convert.FromBase64String(new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray())),
                _ => throw new PlistException($"неизвестный элемент plist «{e.Name.LocalName}»"),
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new PlistException($"неверное значение «{text}» в элементе {e.Name.LocalName}", ex);
        }
    }

    private static Dictionary<string, object?> ConvertDict(XElement dict)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        string? key = null;
        foreach (var child in dict.Elements())
        {
            if (child.Name.LocalName == "key")
            {
                key = child.Value;
                continue;
            }
            if (key is null)
                throw new PlistException("в словаре plist значение без ключа");
            result[key] = Convert(child);
            key = null;
        }
        return result;
    }
}

/// <summary>The dictionary of a plist file, or why it could not be read (Russian).</summary>
public sealed record PlistReadResult(Dictionary<string, object?>? Value, string? Problem);

public static class PlistFile
{
    /// <summary>Reads an XML or binary plist; binary ones are converted by /usr/bin/plutil. Never throws on bad files.</summary>
    public static async Task<PlistReadResult> ReadAsync(string path, ICommandRunner runner, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PlistReadResult(null, $"не удалось прочитать файл: {ErrorText.For(ex)}");
        }

        string xml;
        if (bytes.AsSpan().StartsWith("bplist"u8))
        {
            var converted = await runner.RunAsync("/usr/bin/plutil", new[] { "-convert", "xml1", "-o", "-", path }, ct: ct);
            if (!converted.Ok)
                return new PlistReadResult(null, $"plutil не смог прочитать двоичный plist: {converted.StdErr.Trim()}");
            xml = converted.StdOut;
        }
        else
        {
            xml = Encoding.UTF8.GetString(bytes);
        }

        try
        {
            return new PlistReadResult(Plist.ParseDict(xml), null);
        }
        catch (PlistException ex)
        {
            return new PlistReadResult(null, ex.Message);
        }
    }
}

public static class PlistExtensions
{
    public static string? GetString(this IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) ? v as string : null;

    public static bool? GetBool(this IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is bool b ? b : null;

    public static long? GetLong(this IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is long n ? n : null;

    public static IReadOnlyDictionary<string, object?>? GetDict(this IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) ? v as Dictionary<string, object?> : null;

    public static IReadOnlyList<object?>? GetArray(this IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) ? v as List<object?> : null;

    /// <summary>The strings of an array (other values are skipped); empty when there is no such array.</summary>
    public static IReadOnlyList<string> GetStrings(this IReadOnlyDictionary<string, object?> d, string key)
        => d.GetArray(key)?.OfType<string>().ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
}
