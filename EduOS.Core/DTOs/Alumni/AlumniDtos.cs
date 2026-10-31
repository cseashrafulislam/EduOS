using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Alumni;

public sealed class AlumniConsentRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public Guid StudentReference { get; set; }
    public bool DirectoryOptIn { get; set; }
    [StringLength(200)] public string? ContactEmail { get; set; }
    [StringLength(30)] public string? ContactPhone { get; set; }
    // Empty only on first registration. Required for updates, including revocation.
    [StringLength(100)] public string? RowVersion { get; set; }
}

public sealed class AlumniSelfDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public bool Registered { get; set; }
    public bool DirectoryOptIn { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class AlumniDirectoryItemDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public long CampusId { get; set; }
}

public sealed class AlumniDirectoryQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
    [StringLength(100)] public string? Search { get; set; }
}
