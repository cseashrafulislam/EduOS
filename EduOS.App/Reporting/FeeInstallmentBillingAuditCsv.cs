using EduOS.Core.DTOs.Finance;
using System.Globalization;
using System.Text;

namespace EduOS.App.Reporting;

// CSV is a view of current authorized evidence, not a financial posting document.
public static class FeeInstallmentBillingAuditCsv
{
    public static string Build(StudentFeeInstallmentBillingAuditDto audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.Rows.Count > 12)
            throw new ArgumentOutOfRangeException(nameof(audit), "Instalment audit must be bounded to 12 months.");

        var csv = new StringBuilder("\uFEFF");
        Add(csv, "Student code", "Campus ID", "Agreement reference", "Currency",
            "Agreement state", "Contract gross", "Frozen gross", "Verified invoice net",
            "Billed months", "Unbilled months", "Review months", "Plan findings");
        Add(csv, audit.StudentCode, audit.CampusId.ToString(CultureInfo.InvariantCulture),
            audit.PlanReference.ToString("D"), audit.CurrencyCode,
            audit.PlanStateCode.ToString(CultureInfo.InvariantCulture),
            Money(audit.ContractGross), Money(audit.FrozenLineGross),
            Money(audit.VerifiedInvoiceNet),
            audit.BilledMonths.ToString(CultureInfo.InvariantCulture),
            audit.UnbilledMonths.ToString(CultureInfo.InvariantCulture),
            audit.ReviewMonths.ToString(CultureInfo.InvariantCulture),
            string.Join("; ", audit.PlanIssues));
        csv.Append("\r\n");
        Add(csv, "Month", "Contract due", "Contract gross", "Invoice number",
            "Invoice gross", "Invoice discount", "Invoice net", "Paid", "Due",
            "Journal link (not posting proof)", "Status", "Review findings");
        foreach (var row in audit.Rows)
            Add(csv, row.MonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.ContractDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money(row.ContractGross), row.InvoiceNumber,
                Money(row.InvoiceGross), Money(row.InvoiceDiscount), Money(row.InvoiceNet),
                Money(row.InvoicePaid), Money(row.InvoiceDue),
                row.JournalLinked ? "Link only" : "No link", row.Status,
                string.Join("; ", row.Issues));
        return csv.ToString();
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Money(decimal? amount) => amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";

    private static void Add(StringBuilder csv, params string?[] cells) =>
        csv.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");

    private static string Escape(string? value)
    {
        var raw = value ?? "";
        var leading = raw.TrimStart();
        if (leading.Length > 0 && "=+-@".Contains(leading[0]))
            raw = "'" + raw; // Prevent spreadsheet formula evaluation, including whitespace-prefixed formulas.
        return "\"" + raw.Replace("\"", "\"\"") + "\"";
    }
}
