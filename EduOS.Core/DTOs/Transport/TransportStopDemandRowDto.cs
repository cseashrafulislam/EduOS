namespace EduOS.Core.DTOs.Transport;

public sealed class TransportStopDemandRowDto
{
    public long StopId { get; set; }
    public Guid RouteReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public int SequenceNo { get; set; }
    public bool RouteActive { get; set; }
    public bool StopActive { get; set; }
    public int CurrentPickups { get; set; }
    public int CurrentDropoffs { get; set; }
    public bool NeedsAttention { get; set; }
    public bool InactiveWithRiders { get; set; }
}
