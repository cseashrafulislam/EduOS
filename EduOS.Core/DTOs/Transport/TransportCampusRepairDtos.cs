namespace EduOS.Core.DTOs.Transport;

public sealed class TransportCampusRepairRowDto
{
    public string Kind { get; set; } = "";
    public Guid Reference { get; set; }
    public string Label { get; set; } = "";
    public string RowVersion { get; set; } = "";
    public int LinkedRecords { get; set; }
    public bool CanApply => LinkedRecords == 0;
}

public sealed class TransportCampusRepairReviewDto
{
    public IReadOnlyList<TransportCampusRepairRowDto> Rows { get; set; } = Array.Empty<TransportCampusRepairRowDto>();
    public bool HasMore { get; set; }
}

public sealed class ApplyTransportCampusRepairDto
{
    public long CampusId { get; set; }
    public string RowVersion { get; set; } = "";
    public bool ConfirmHistoricalCampusReview { get; set; }
}
