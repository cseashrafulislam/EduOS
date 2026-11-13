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
    public static byte[] Write(IReadOnlyList<StudentDirectoryListItemDto> rows, CultureInfo? culture = null,
        bool includeSection = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > 100)
            throw new ArgumentOutOfRangeException(nameof(rows), "A directory export is limited to 100 rows.");

        var csv = new StringBuilder("\uFEFF");
        var bangla = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "bn";
        // The default 7-column CSV is a stable contract for existing consumers.
        // The newer UI explicitly opts in to the Section-aware 8-column projection.
        csv.AppendLine(bangla
            ? (includeSection ? "শিক্ষার্থী কোড,পূর্ণ নাম,রোল,শিক্ষাবর্ষ,শ্রেণি,শাখা,ব্যাচ,অবস্থা"
                              : "শিক্ষার্থী কোড,পূর্ণ নাম,রোল,শিক্ষাবর্ষ,শ্রেণি,ব্যাচ,অবস্থা")
            : (includeSection ? "Student Code,Full Name,Roll,Academic Year,Level,Section,Batch,Status"
                              : "Student Code,Full Name,Roll,Academic Year,Level,Batch,Status"));
        foreach (var row in rows)
        {
            var values = new List<string>
            {
                Cell(row.StudentCode), Cell(row.FullName), Cell(row.RollNo),
                Cell(row.AcademicYear), Cell(row.AcademicLevelName)
            };
            if (includeSection) values.Add(Cell(row.AcademicSectionName));
            values.Add(Cell(row.AcademicBatchName));
            values.Add(Cell(row.Status));
            csv.AppendJoin(",", values);
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
