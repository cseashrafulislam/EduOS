namespace EduOS.Service.Services.Students;

/// <summary>A bounded handoff, not a distributed transaction or guarantee against later changes.</summary>
public static class DestinationTransferConfirmationPolicy
{
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);

    public static bool IsFresh(DateTime? confirmedAtUtc, DateTime nowUtc) =>
        confirmedAtUtc.HasValue && confirmedAtUtc.Value <= nowUtc &&
        nowUtc - confirmedAtUtc.Value <= Validity;
}
