namespace EduOS.Core.DTOs.Students;

public sealed class StudentSupportEnrollmentOption
{
    public Guid StudentReference { get; set; }
    public Guid EnrollmentReference { get; set; }
    public string StudentCode { get; set; } = "";
    public string StudentName { get; set; } = "";
    public long CampusId { get; set; }
    public string RollNo { get; set; } = "";
}

public sealed class StudentSupportAidRuleOption
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsPercentage { get; set; }
    public decimal Value { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
