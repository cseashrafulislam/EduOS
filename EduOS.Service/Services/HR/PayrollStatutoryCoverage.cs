namespace EduOS.Service.Services.HR;

/// <summary>
/// An institution explicitly configures effective-dated, fixed BDT withholding amounts.
/// This guard ensures every configured statutory category has an employee decision,
/// including explicit zero for an exemption. It does not calculate Bangladeshi tax law.
/// </summary>
public static class PayrollStatutoryCoverage
{
    public static bool IncludesEach(IEnumerable<long> requiredIds, IEnumerable<long> salaryLineComponentIds)
    {
        var present = salaryLineComponentIds.ToHashSet();
        return requiredIds.All(present.Contains);
    }
}
