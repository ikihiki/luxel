using System.Text;

namespace Luxel.UI.Generators;

internal static class GeneratedIdentifier
{
    internal static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char c in value) result.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        return result.ToString();
    }
}
