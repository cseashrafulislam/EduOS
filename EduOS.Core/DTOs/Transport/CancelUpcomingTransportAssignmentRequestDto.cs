using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

/// <summary>Optimistic concurrency token for cancelling an unstarted transport reservation.</summary>
public sealed class CancelUpcomingTransportAssignmentRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
