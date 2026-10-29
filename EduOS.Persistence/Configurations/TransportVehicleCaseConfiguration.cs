using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations;

public sealed class TransportVehicleCaseConfiguration : IEntityTypeConfiguration<TransportVehicleCase>
{
    public void Configure(EntityTypeBuilder<TransportVehicleCase> b)
    {
        b.ToTable("TransportVehicleCases", table =>
        {
            table.HasCheckConstraint("CK_TransportVehicleCases_Type",
                "[CaseType] IN ('Maintenance', 'Incident')");
            table.HasCheckConstraint("CK_TransportVehicleCases_Severity",
                "[Severity] IN ('Low', 'Medium', 'High')");
            table.HasCheckConstraint("CK_TransportVehicleCases_State",
                "[State] IN ('Open', 'Resolved')");
            table.HasCheckConstraint("CK_TransportVehicleCases_Resolution",
                "([State] = 'Open' AND [ResolvedAt] IS NULL AND [ResolvedBy] IS NULL AND [Resolution] IS NULL) OR ([State] = 'Resolved' AND [ResolvedAt] IS NOT NULL AND [ResolvedBy] IS NOT NULL AND [Resolution] IS NOT NULL)");
        });
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CampusId, x.OccurredOn });
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.State });
        b.HasOne<Vehicle>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.VehicleId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Campus>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CampusId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
