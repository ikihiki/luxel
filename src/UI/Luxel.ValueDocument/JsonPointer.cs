using System.Globalization;

namespace Luxel.ValueDocument;

public static class JsonPointer
{
    public static string Escape(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
    }

    public static bool TryUnescape(string segment, out string value)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var result = new System.Text.StringBuilder(segment.Length);
        for (int i = 0; i < segment.Length; i++)
        {
            char current = segment[i];
            if (current != '~')
            {
                result.Append(current);
                continue;
            }
            if (++i >= segment.Length || (segment[i] != '0' && segment[i] != '1'))
            {
                value = string.Empty;
                return false;
            }
            result.Append(segment[i] == '0' ? '~' : '/');
        }
        value = result.ToString();
        return true;
    }

    public static bool TryResolve(ValueNode root, string pointer, out ValueNode? node)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(pointer);
        node = root;
        if (pointer.Length == 0) return true;
        if (pointer[0] != '/') { node = null; return false; }

        foreach (string encoded in pointer[1..].Split('/'))
        {
            if (!TryUnescape(encoded, out string segment)) { node = null; return false; }
            if (node is ValueObjectNode obj)
            {
                if (!obj.TryGetProperty(segment, out node)) return false;
            }
            else if (node is ValueArrayNode array)
            {
                if (!TryParseArrayIndex(segment, out int index) || index >= array.Items.Count)
                {
                    node = null;
                    return false;
                }
                node = array.Items[index];
            }
            else
            {
                node = null;
                return false;
            }
        }
        return true;
    }

    private static bool TryParseArrayIndex(string segment, out int index)
    {
        if (segment.Length == 0 || (segment.Length > 1 && segment[0] == '0'))
        {
            index = 0;
            return false;
        }
        return int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
    }
}
