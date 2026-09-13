using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Learners;

/// <summary>
/// Platform-global human identity. Institution-owned academic and contact data
/// remains in tenant-scoped records and is never exposed from this entity directly.
/// </summary>
public class Person : BaseEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string? FullNameBangla { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;

    public virtual ICollection<PersonIdentifier> Identifiers { get; set; } = new List<PersonIdentifier>();
    public virtual ICollection<StudentPersonLink> StudentLinks { get; set; } = new List<StudentPersonLink>();
}
