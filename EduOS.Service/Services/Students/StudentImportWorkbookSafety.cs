using System.IO.Compression;

namespace EduOS.Service.Services.Students;

/// <summary>
/// Protects the in-memory XLSX reader from compressed XML expansion. These are
/// per-upload resource budgets, not caps on students, tenants or source rows.
/// Larger migrations can be split into independently recoverable source files.
/// </summary>
internal static class StudentImportWorkbookSafety
{
    private const long MaxExpandedBytes = 64L * 1024 * 1024;
    private const int MaxPackageEntries = 2048;

    public static void Validate(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
            throw new FormatException("The workbook must be readable and seekable.");
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count is < 1 or > MaxPackageEntries)
                throw new FormatException("Workbook package has an unsafe number of entries.");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                // The manifest is trusted only as a pre-allocation safeguard.
                // ClosedXML subsequently validates the actual package contents.
                if (entry.Length < 0 || entry.Length > MaxExpandedBytes - expanded)
                    throw new FormatException("Workbook exceeds the safe expanded-size budget. Split the file.");
                expanded += entry.Length;
            }
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException("Invalid XLSX workbook package.", ex);
        }
        finally { stream.Position = position; }
    }
}
