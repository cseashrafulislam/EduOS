using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Library;

/// <summary>Tenant-scoped circulation register row; the issue's existing concurrency token is used for returns.</summary>
public sealed class LibraryCirculationRowDto
{
    public BookIssueDto Issue { get; set; } = new();
    public long? CampusId { get; set; }
    public int DaysOverdue { get; set; }
    public bool IsOverdue { get; set; }
}
