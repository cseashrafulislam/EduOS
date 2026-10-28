using System.ComponentModel.DataAnnotations;
using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.DTOs.Assessment;

public sealed class RequestResultRescrutinyDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    public Guid StudentReference { get; set; }
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class DecideResultRescrutinyDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RequestRowVersion { get; set; } = string.Empty;
    public ResultRescrutinyAction Decision { get; set; }
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class ResultRescrutinyQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    [Range(1, long.MaxValue)] public long? BeforeId { get; set; }
    [Range(1, 50)] public int PageSize { get; set; } = 25;
}

public sealed class ResultRescrutinyItemDto
{
    public long RequestEventId { get; set; }
    public Guid CaseReference { get; set; }
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public ResultRescrutinyAction Status { get; set; }
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string RequestReason { get; set; } = string.Empty;
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public string RequestRowVersion { get; set; } = string.Empty;
}

public sealed class ResultRescrutinyPageDto
{
    public IReadOnlyList<ResultRescrutinyItemDto> Items { get; set; } = [];
    public bool HasMore { get; set; }
    public long? NextBeforeId { get; set; }
}
