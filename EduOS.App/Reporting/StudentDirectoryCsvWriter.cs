using System.Globalization;
using System.Text;
using EduOS.Core.DTOs.Students;

namespace EduOS.App.Reporting;

/// <summary>
/// Privacy-minimal, formula-safe CSV for one already-authorized directory page.
/// Never include contact, guardian, Person identifiers or historic enrollments.
/// </summary>
public static class StudentDirectoryCsvWriter
{
    public static byte[] Write(IReadOnlyList<StudentDirectoryListItemDto> rows, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 100)
            throw new ArgumentOutOfRangeException(nameof(rows), "A directory export is limited to 100 rows.");

        var csv = new StringBuilder("\uFEFF");
        var bangla = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "bn";
        csv.AppendLine(bangla
            ? "শিক্ষার্থী কোড,পূর্ণ নাম,রোল,শিক্ষাবর্ষ,শ্রেণি,ব্যাচ,অবস্থা"
            : "Student Code,Full Name,Roll,Academic Year,Level,Batch,Status");
        foreach (var row in rows)
        {
            csv.AppendJoin(",", new[]
            {
                Cell(row.StudentCode), Cell(row.FullName), Cell(row.RollNo),
                Cell(row.AcademicYear), Cell(row.AcademicLevelName),
                Cell(row.AcademicBatchName), Cell(row.Status)
            });
            csv.AppendLine();
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Cell(string? text)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var candidate = value.TrimStart();
        if (candidate.Length > 0 && candidate[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
