using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Students;

/// <summary>Destination-facing transfer metadata only; no source learner or guardian PII.</summary>
public sealed class DestinationTransferInboxDto
{
    public Guid Reference { get; set; }
    public long SourceTenantId { get; set; }
    public TransferRequestState State { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool DestinationEnrollmentLinked { get; set; }
    public DateTime? DestinationEnrollmentConfirmedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class DestinationTransferDecisionDto
{
    public bool Accept { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class DestinationTransferEnrollmentDto
{
    [Required] public Guid StudentReference { get; set; }
    [Required] public Guid EnrollmentReference { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class DestinationTransferConfirmationDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class CompleteTransferRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
