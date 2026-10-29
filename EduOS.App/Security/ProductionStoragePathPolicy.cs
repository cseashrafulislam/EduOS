namespace EduOS.App.Security;

/// <summary>
/// Prevents accidental publication of tenant documents and authentication
/// encryption keys through ASP.NET Core's public static-file directory.
/// The production host must additionally restrict OS permissions and protect
/// its backup/storage media; this check validates application paths only.
/// </summary>
public static class ProductionStoragePathPolicy
{
    public static void Validate(string contentRoot, string webRoot,
        string? privateStoragePath, string? dataProtectionKeysPath)
    {
        if (string.IsNullOrWhiteSpace(contentRoot) || string.IsNullOrWhiteSpace(webRoot))
            throw new InvalidOperationException("Production content and web roots must be configured.");

        var content = Path.GetFullPath(contentRoot);
        var publicRoot = Path.GetFullPath(webRoot, content);
        ValidatePrivateDirectory(content, publicRoot, privateStoragePath,
            "FileStorage:PrivateBasePath");
        ValidatePrivateDirectory(content, publicRoot, dataProtectionKeysPath,
            "DataProtection:KeysPath");
    }

    private static void ValidatePrivateDirectory(string contentRoot, string publicRoot,
        string? configuredPath, string settingName)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException(settingName + " is required in Production.");

        var path = Path.GetFullPath(configuredPath, contentRoot);
        if (IsSameOrBelow(path, publicRoot))
            throw new InvalidOperationException(
                settingName + " must be outside the publicly served web root.");
    }

    private static bool IsSameOrBelow(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }
}
