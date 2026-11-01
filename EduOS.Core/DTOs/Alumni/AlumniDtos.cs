using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Alumni;

public sealed class AlumniConsentRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public Guid StudentReference { get; set; }
    public bool DirectoryOptIn { get; set; }
    // Independent permission: directory visibility never implies outbound messaging.
    public bool InvitationEmailOptIn { get; set; }
    public bool InvitationSmsOptIn { get; set; }
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
    public bool InvitationEmailOptIn { get; set; }
    public bool InvitationSmsOptIn { get; set; }
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

public sealed class AlumniEventDraftDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, StringLength(160, MinimumLength = 3)] public string Title { get; set; } = "";
    [StringLength(1500)] public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
}

public sealed class AlumniEventActionDto
{
    [Required] public string RowVersion { get; set; } = "";
}

public sealed class AlumniEventRsvpDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public Guid EventReference { get; set; }
    [Required] public Guid StudentReference { get; set; }
    [Range(1, 2)] public int Response { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class AlumniEventQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
    // Optional for legacy clients; the alumni UI always supplies its selected graduate.
    // Admin reporting ignores this value and remains tenant/campus scoped.
    public Guid? StudentReference { get; set; }
}

public sealed class AlumniEventItemDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Status { get; set; }
    public string RowVersion { get; set; } = "";
    public int GoingCount { get; set; }
    public int DeclinedCount { get; set; }
    public int CheckedInCount { get; set; }
    public int? MyResponse { get; set; }
    public string? MyRowVersion { get; set; }
    public bool CanManage { get; set; }
}

public sealed class AlumniEventAttendanceQueryDto
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
}
public sealed class AlumniEventAttendanceItemDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateTime? CheckedInAtUtc { get; set; }
    public string RowVersion { get; set; } = "";
}
public sealed class AlumniEventCheckInDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public Guid StudentReference { get; set; }
    public bool Present { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class AlumniEventCampusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

// Invitations use separate, revocable channel consent. No destination/credential is
// returned in the preview or delivery report.
public sealed class AlumniInvitationSummaryDto
{
    public int Channel { get; set; }
    public bool GatewayReady { get; set; }
    public int EligibleCount { get; set; }
    public int RemainingCount { get; set; }
    public int AcceptedCount { get; set; }
    public int ReviewCount { get; set; }
    public int SuppressedCount { get; set; }
}

public sealed class AlumniInvitationSendDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Range(2, 3)] public int Channel { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AlumniInvitationBatchDto
{
    public Guid ClientRequestId { get; set; }
    public int ReservedCount { get; set; }
    public int AcceptedCount { get; set; }
    public int ReviewCount { get; set; }
    public int SuppressedCount { get; set; }
    public bool AlreadyProcessed { get; set; }
}
