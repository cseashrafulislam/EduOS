using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Library;

/// <summary>
/// Per-user pointer to an authorized educational Document, not a duplicate copy.
/// One row per tenant/user/document survives toggles for deterministic retries.
/// </summary>
public sealed class StudyBookmark : BaseTenantEntity
{
    public long UserId { get; set; }
    public long DocumentId { get; set; }
    // Canonical per-user manual reader position. No quiz answers or grades are
    // synchronized from offline clients.
    public int LastReadPage { get; set; }
    public Guid? LastProgressCommandId { get; set; }
    public DateTime? ProgressUpdatedAtUtc { get; set; }
}
