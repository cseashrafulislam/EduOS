namespace EduOS.Core.DTOs.Transport;

public sealed class TransportCampusAssignmentRowDto
{
    public string Kind { get; set; } = "";
    public Guid Reference { get; set; }
    public string Label { get; set; } = "";
    public string RowVersion { get; set; } = "";
    public int LinkedRecords { get; set; }
    public bool CanApply => LinkedRecords == 0;
}

public sealed class TransportCampusAssignmentReviewDto
{
    public IReadOnlyList<TransportCampusAssignmentRowDto> Rows { get; set; } = Array.Empty<TransportCampusAssignmentRowDto>();
    public bool HasMore { get; set; }
}

public sealed class ApplyTransportCampusAssignmentDto
{
    public long CampusId { get; set; }
    public string RowVersion { get; set; } = "";
    public bool ConfirmHistoricalCampusReview { get; set; }
}
