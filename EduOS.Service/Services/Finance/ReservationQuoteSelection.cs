namespace EduOS.Service.Services.Finance;

/// <summary>
/// Validates an authorized Finance estimate override. This is deliberately a pure
/// quotation policy, not a posting, invoice adjustment or reservation approval.
/// </summary>
public static class ReservationQuoteSelection
{
    public sealed record Result(decimal? SelectedAmount, bool IsManual, string Mode);

    public static Result Select(decimal? automaticAmount, bool manualCampusMode,
        bool allowManualOverride, decimal? manualAmount, string? reason)
    {
        if (!manualAmount.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("An override reason requires a manual amount.");
            return new Result(manualCampusMode ? null : automaticAmount, false,
                manualCampusMode ? "ManualRequired" : automaticAmount.HasValue ? "Automatic" : "ManualRequired");
        }

        if (!manualCampusMode && !allowManualOverride)
            throw new InvalidOperationException("This campus does not permit manual billing overrides.");
        var amount = manualAmount.Value;
        if (amount < -999999999999.99m || amount > 999999999999.99m ||
            decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(manualAmount),
                "Signed adjustment must use at most two decimal places and stay within the permitted range.");
        var note = reason?.Trim();
        if (note == null || note.Length is < 20 or > 1000)
            throw new ArgumentException("Manual adjustment requires a reason of 20-1000 characters.");
        return new Result(amount, true, "ManualOverride");
    }
}
