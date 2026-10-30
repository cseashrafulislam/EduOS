using EduOS.Core.DTOs.Library;
using System.Text;

namespace EduOS.App.Controllers.Api;

/// <summary>Only server-authorized, bounded pages; protects Excel from formula injection.</summary>
public static class LibraryLiabilityCsvWriter
{
    public static byte[] Write(IEnumerable<LibraryLiabilityDto> rows, bool bangla)
    {
        var csv = new StringBuilder();
        csv.AppendLine(bangla
            ? "তারিখ,ক্যাম্পাস,বই,অ্যাকসেশন,শিক্ষার্থী,শিক্ষার্থী কোড,ধরন,বিলম্বের দিন,দৈনিক হার (BDT),মূল্য (BDT),অবস্থা,পরিশোধ (BDT),বকেয়া (BDT),ইনভয়েস,কারণ,রিভিউ"
            : "Assessment date,Campus,Book,Accession,Student,Student code,Kind,Overdue days,Daily rate (BDT),Amount (BDT),State,Paid (BDT),Due (BDT),Invoice,Reason,Review");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', new[]
            {
                Cell(row.AssessmentDate.ToString("yyyy-MM-dd")), Cell(row.CampusId.ToString()),
                Cell(row.BookTitle), Cell(row.AccessionNumber), Cell(row.StudentName), Cell(row.StudentCode),
                Cell(row.Kind.ToString()), Cell(row.OverdueDays?.ToString()),
                Cell(row.DailyRate?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.IsSettled ? "Settled" : row.State.ToString()),
                Cell(row.PaidAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.DueAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(row.InvoiceNumber), Cell(row.Reason), Cell(row.ReviewReason)
            }));
        return new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(csv.ToString())).ToArray();
    }

    private static string Cell(string? value)
    {
        var text = value ?? "";
        if (!string.IsNullOrWhiteSpace(text) && "=+-@".Contains(text.TrimStart(' ', '\t', '\r', '\n')[0]))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
