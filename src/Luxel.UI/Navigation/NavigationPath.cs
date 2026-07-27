namespace Luxel.UI;

/// <summary>Navigation path validation and canonicalization shared by hosts and controls.</summary>
public static class NavigationPath
{
    /// <summary>Validates an absolute path and removes trailing slashes except for the root path.</summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path[0] != '/')
            throw new ArgumentException("Navigation paths must start with '/'.", nameof(path));

        int length = path.Length;
        while (length > 1 && path[length - 1] == '/') length--;
        return length == path.Length ? path : path[..length];
    }
}
