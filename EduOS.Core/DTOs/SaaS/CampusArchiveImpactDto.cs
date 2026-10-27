namespace EduOS.Core.DTOs.SaaS;

/// <summary>Advisory tenant-scoped campus archive preview; the archive command rechecks under a transaction.</summary>
public sealed class CampusArchiveImpactDto
{
    public long CampusId { get; set; }
    public bool CanArchive { get; set; }
    public IReadOnlyList<string> Blockers { get; set; } = Array.Empty<string>();
}
