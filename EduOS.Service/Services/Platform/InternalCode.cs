namespace EduOS.Service.Services.Platform;

/// <summary>Opaque server-generated codes for internal masters; never use for document numbers.</summary>
public static class InternalCode
{
    public static string New(string prefix, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !prefix.All(c => c is >= 'A' and <= 'Z'))
            throw new ArgumentException("An uppercase alphabetic prefix is required.", nameof(prefix));
        var length = Math.Min(32, maxLength - prefix.Length - 1);
        if (length < 16) throw new ArgumentOutOfRangeException(nameof(maxLength));
        return prefix + "-" + Guid.NewGuid().ToString("N")[..length].ToUpperInvariant();
    }
}
