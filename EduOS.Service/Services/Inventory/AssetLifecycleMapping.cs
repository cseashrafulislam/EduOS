using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    private static AssetCategoryDto Map(AssetCategory x) => new()
    {
        Id = x.Id, Code = x.Code, Name = x.Name, IsActive = x.IsActive,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private static AssetDto Map(Asset x) => new()
    {
        Reference = x.PublicId, AssetCategoryId = x.AssetCategoryId, InventoryItemId = x.InventoryItemId,
        InventoryLocationId = x.InventoryLocationId, AssetTag = x.AssetTag, Name = x.Name,
        SerialNumber = x.SerialNumber, PurchaseDate = x.PurchaseDate, PurchaseCost = x.PurchaseCost,
        StateCode = x.StateCode, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private async Task<AssetAssignmentDto> MapAssignmentAsync(AssetAssignment x, Guid assetRef, CancellationToken ct)
    {
        Guid? employee = x.EmployeeId.HasValue
            ? await _db.Employees.AsNoTracking().Where(e => e.TenantId == Tenant && e.Id == x.EmployeeId && !e.IsDeleted)
                .Select(e => (Guid?)e.PublicId).FirstOrDefaultAsync(ct) : null;
        Guid? student = x.StudentId.HasValue
            ? await _db.Students.AsNoTracking().Where(s => s.TenantId == Tenant && s.Id == x.StudentId && !s.IsDeleted)
                .Select(s => (Guid?)s.PublicId).FirstOrDefaultAsync(ct) : null;
        return new AssetAssignmentDto
        {
            Id = x.Id, AssetReference = assetRef, EmployeeReference = employee, StudentReference = student,
            OrganizationUnitId = x.OrganizationUnitId, AssignedOn = x.AssignedOn, ReturnedOn = x.ReturnedOn,
            StateCode = x.StateCode, RowVersion = Convert.ToBase64String(x.RowVersion)
        };
    }
    private static AssetMaintenanceDto Map(AssetMaintenance x, Guid assetRef) => new()
    {
        Id = x.Id, AssetReference = assetRef, MaintenanceDate = x.MaintenanceDate,
        WorkDescription = x.WorkDescription, Cost = x.Cost, NextMaintenanceDate = x.NextMaintenanceDate
    };
}
