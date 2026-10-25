namespace EduOS.Core.DTOs.Inventory;
/// <summary>Latest scheduled maintenance for an accessible institutional asset.</summary>
public sealed class AssetMaintenanceDueDto
{
    public Guid AssetReference { get; set; }
    public long? CampusId { get; set; }
    public string AssetTag { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public DateOnly LastMaintenanceDate { get; set; }
    public DateOnly NextMaintenanceDate { get; set; }
    public int DaysOverdue { get; set; }
}
