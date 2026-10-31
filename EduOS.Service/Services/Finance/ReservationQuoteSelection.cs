namespace EduOS.Service.Services.Finance;

/// <summary>
/// Validates an authorized Finance estimate override. This is deliberately a pure
/// quotation policy, not a posting, invoice adjustment or reservation approval.
/// </summary>
public static class ReservationQuoteSelection
{
    public sealed record Result(decimal? SelectedAmount, bool IsManual, string Mode);

    public static Result Select(decimal? automaticAmount, bool manualCampusMode,
        bool allowManualOverride, decimal? manualAmount, string? reason,
        decimal? manualDelta = null)
    {
        if (manualAmount.HasValue && manualDelta.HasValue)
            throw new ArgumentException("Choose either a manual final amount or an adjustment to the automatic amount.");
        if (!manualAmount.HasValue && !manualDelta.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("An override reason requires a manual amount.");
            return new Result(manualCampusMode ? null : automaticAmount, false,
                manualCampusMode ? "ManualRequired" : automaticAmount.HasValue ? "Automatic" : "ManualRequired");
        }

        if (!manualCampusMode && !allowManualOverride)
            throw new InvalidOperationException("This campus does not permit manual billing overrides.");
        if (manualDelta.HasValue && (manualCampusMode || !automaticAmount.HasValue))
            throw new InvalidOperationException("Auto + Manual requires an available automatic calculation.");
        // Auto + Manual changes the calculated proposal by the user-entered
        // signed correction; Manual instead replaces it with an explicit total.
        // Use checked decimal arithmetic and the same cent precision for both.
        decimal amount;
        try { amount = checked((manualDelta.HasValue ? automaticAmount!.Value : 0m) +
            (manualDelta ?? manualAmount!.Value)); }
        catch (OverflowException)
        { throw new ArgumentOutOfRangeException(nameof(manualDelta), "Adjustment is outside the supported monetary range."); }
        var input = manualDelta ?? manualAmount!.Value;
        if (input < -999999999999.99m || input > 999999999999.99m ||
            decimal.Round(input, 2) != input)
            throw new ArgumentOutOfRangeException(nameof(manualDelta),
                "Manual amount must have at most two decimal places and stay within the permitted range.");
        if (amount < -999999999999.99m || amount > 999999999999.99m ||
            decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(manualAmount),
                "Signed adjustment must use at most two decimal places and stay within the permitted range.");
        var note = reason?.Trim();
        if (note == null || note.Length is < 20 or > 1000)
            throw new ArgumentException("Manual adjustment requires a reason of 20-1000 characters.");
        return new Result(amount, true, manualDelta.HasValue ? "AutoPlusManual" : "ManualOverride");
    }
}
