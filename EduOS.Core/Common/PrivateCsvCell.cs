using System.Text;

namespace EduOS.Core.Common;

/// <summary>
/// Encodes untrusted institution data as a quoted spreadsheet-safe CSV field.
/// Normalizes control and hidden prefix characters before formula detection.
/// Authorization and export row limits remain the responsibility of the caller.
/// </summary>
public static class PrivateCsvCell
{
    public static string Escape(string? value)
    {
        var source = value ?? string.Empty;
        var normalized = new StringBuilder(source.Length);
        foreach (var character in source)
            normalized.Append(char.IsControl(character) ||
                character is '\uFEFF' or '\u200B' ? ' ' : character);

        var safe = normalized.ToString();
        var first = 0;
        while (first < safe.Length && char.IsWhiteSpace(safe[first]))
            first++;
        if (first < safe.Length && (safe[first] is '=' or '+' or '-' or '@'))
            safe = "'" + safe;

        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
