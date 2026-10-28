namespace EduOS.Service.Services.HR;

/// <summary>Conservative payroll statement classification: unverifiable GL is never labelled paid.</summary>
public static class PayrollReconciliationRules
{
    public static string Accrual(bool runPosted, bool journalValid) =>
        !runPosted ? "Unposted" : journalValid ? "Verified" : "NeedsReview";

    public static string Settlement(bool runPosted, bool journalValid, bool invalidPayment,
        decimal net, decimal paid, decimal pending)
    {
        if (!runPosted) return "NotPosted";
        if (!journalValid || invalidPayment || net < 0m || paid < 0m || pending < 0m ||
            paid + pending > net) return "NeedsReview";
        if (paid > net) return "Overpaid";
        if (paid == net) return "Paid";
        if (paid > 0m) return "Partial";
        return pending > 0m ? "PendingVerification" : "Outstanding";
    }
}
