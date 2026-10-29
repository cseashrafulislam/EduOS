using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Enums.Domain;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Only a freshly authorized, server-paged circulation register may be exported.</summary>
public static class LibraryCirculationCsvWriter
{
    public static byte[] Write(IReadOnlyList<LibraryCirculationRowDto> rows, bool bangla)
    {
        if (rows.Count > 100) throw new ArgumentOutOfRangeException(nameof(rows));
        var output = new StringBuilder();
        Append(output, bangla
            ? ["শিক্ষার্থী", "বই", "অ্যাকসেশন", "ক্যাম্পাস", "ইস্যুর তারিখ", "ফেরতের নির্ধারিত তারিখ", "ফেরতের তারিখ", "অবস্থা", "বিলম্বের দিন"]
            : ["Student", "Book", "Accession", "Campus", "Issue date", "Due date", "Return date", "Status", "Overdue days"]);
        foreach (var row in rows)
        {
            var issue = row.Issue;
            var state = issue.State switch
            {
                BookIssueState.Issued => bangla ? "ইস্যুকৃত" : "Issued",
                BookIssueState.Returned => bangla ? "ফেরত" : "Returned",
                BookIssueState.Lost => bangla ? "হারানো" : "Lost",
                BookIssueState.Damaged => bangla ? "ক্ষতিগ্রস্ত" : "Damaged",
                _ => bangla ? "অন্যান্য" : "Other"
            };
            Append(output, [
                issue.StudentName, issue.BookTitle, issue.AccessionNumber,
                row.CampusId?.ToString(CultureInfo.InvariantCulture) ?? "",
                issue.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                issue.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                issue.ReturnDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                state, row.DaysOverdue.ToString(CultureInfo.InvariantCulture)
            ]);
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(output.ToString());
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }

    private static void Append(StringBuilder output, IEnumerable<string> fields)
    {
        var first = true;
        foreach (var value in fields)
        {
            if (!first) output.Append(',');
            first = false;
            output.Append(PrivateCsvCell.Escape(value));
        }
        output.Append("\r\n");
    }
}
