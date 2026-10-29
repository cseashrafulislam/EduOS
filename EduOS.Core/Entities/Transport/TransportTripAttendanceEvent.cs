using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Transport;

/// <summary>Append-only, server-timestamped attendance fact for one frozen dispatched rider.</summary>
public sealed class TransportTripAttendanceEvent : BaseTenantEntity
{
    public long TransportTripRiderId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int Sequence { get; set; }
    [Required, MaxLength(12)] public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}
