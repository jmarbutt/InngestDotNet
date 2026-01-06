using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Inngest.Internal;

/// <summary>
/// JSON Canonicalization Scheme (JCS) implementation per RFC 8785.
/// This produces deterministic JSON output by:
/// - Sorting object keys lexicographically
/// - Using minimal whitespace
/// - Normalizing number representation
/// </summary>
public static class JsonCanonicalizer
{
    /// <summary>
    /// Canonicalize a JSON string per RFC 8785 (JCS).
    /// </summary>
    /// <param name="json">The JSON string to canonicalize</param>
    /// <returns>Canonicalized JSON string</returns>
    public static string Canonicalize(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            SerializeElement(doc.RootElement, sb);
            return sb.ToString();
        }
        catch (JsonException)
        {
            // If JSON is invalid, return original
            return json;
        }
    }

    /// <summary>
    /// Canonicalize JSON bytes per RFC 8785 (JCS).
    /// </summary>
    /// <param name="jsonBytes">The JSON bytes to canonicalize</param>
    /// <returns>Canonicalized JSON as UTF-8 bytes</returns>
    public static byte[] Canonicalize(byte[] jsonBytes)
    {
        if (jsonBytes.Length == 0)
            return jsonBytes;

        try
        {
            var json = Encoding.UTF8.GetString(jsonBytes);
            var canonicalized = Canonicalize(json);
            return Encoding.UTF8.GetBytes(canonicalized);
        }
        catch
        {
            // If parsing fails, return original bytes
            return jsonBytes;
        }
    }

    private static void SerializeElement(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                SerializeObject(element, sb);
                break;

            case JsonValueKind.Array:
                SerializeArray(element, sb);
                break;

            case JsonValueKind.String:
                SerializeString(element.GetString() ?? "", sb);
                break;

            case JsonValueKind.Number:
                SerializeNumber(element, sb);
                break;

            case JsonValueKind.True:
                sb.Append("true");
                break;

            case JsonValueKind.False:
                sb.Append("false");
                break;

            case JsonValueKind.Null:
                sb.Append("null");
                break;
        }
    }

    private static void SerializeObject(JsonElement obj, StringBuilder sb)
    {
        sb.Append('{');

        // Sort keys lexicographically (RFC 8785 requirement)
        var properties = obj.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < properties.Count; i++)
        {
            if (i > 0)
                sb.Append(',');

            var prop = properties[i];
            SerializeString(prop.Name, sb);
            sb.Append(':');
            SerializeElement(prop.Value, sb);
        }

        sb.Append('}');
    }

    private static void SerializeArray(JsonElement arr, StringBuilder sb)
    {
        sb.Append('[');

        int index = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (index > 0)
                sb.Append(',');

            SerializeElement(item, sb);
            index++;
        }

        sb.Append(']');
    }

    private static void SerializeString(string value, StringBuilder sb)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        // Control characters must be escaped as \uXXXX
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    private static void SerializeNumber(JsonElement element, StringBuilder sb)
    {
        // RFC 8785 requires specific number formatting:
        // - No leading zeros (except for 0 itself)
        // - No trailing zeros in fractional part
        // - Use 'e' notation for certain values
        // - Integer values should not have decimal point

        if (element.TryGetInt64(out long intValue))
        {
            sb.Append(intValue.ToString(CultureInfo.InvariantCulture));
        }
        else if (element.TryGetDouble(out double doubleValue))
        {
            // Format per ES6/ECMAScript number serialization (RFC 8785 uses this)
            sb.Append(SerializeDouble(doubleValue));
        }
        else
        {
            // Fallback to raw text
            sb.Append(element.GetRawText());
        }
    }

    /// <summary>
    /// Serialize a double value per ES6/ECMAScript spec (used by RFC 8785).
    /// This is a simplified implementation that handles common cases.
    /// </summary>
    private static string SerializeDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "null"; // JSON doesn't support NaN/Infinity
        }

        if (value == 0)
        {
            return "0";
        }

        // Check if it's actually an integer
        if (value == Math.Truncate(value) && value >= long.MinValue && value <= long.MaxValue)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        // Use G17 format for full precision, then normalize
        string result = value.ToString("G17", CultureInfo.InvariantCulture);

        // Normalize scientific notation: use lowercase 'e', ensure sign
        if (result.Contains('E'))
        {
            result = result.Replace("E", "e");
            int eIndex = result.IndexOf('e');
            if (eIndex > 0 && result[eIndex + 1] != '-' && result[eIndex + 1] != '+')
            {
                result = result.Insert(eIndex + 1, "+");
            }
        }

        return result;
    }
}
