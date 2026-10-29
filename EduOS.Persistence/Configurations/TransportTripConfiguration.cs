using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EduOS.Persistence.Configurations;
public sealed class TransportTripConfiguration : IEntityTypeConfiguration<TransportTrip>
{
    public void Configure(EntityTypeBuilder<TransportTrip> b)
    {
        b.ToTable("TransportTrips", t =>
        {
            t.HasCheckConstraint("CK_TransportTrips_Direction", "[Direction] IN ('Pickup','Drop')");
            t.HasCheckConstraint("CK_TransportTrips_State", "[State] IN ('Planned','Dispatched')");
            t.HasCheckConstraint("CK_TransportTrips_Dispatch",
                "([State] = 'Planned' AND [TransportDriverId] IS NULL AND [DispatchRequestId] IS NULL AND [DispatchedAt] IS NULL AND [DispatchedBy] IS NULL AND [RiderCount] = 0) OR ([State] = 'Dispatched' AND [TransportDriverId] IS NOT NULL AND [DispatchRequestId] IS NOT NULL AND [DispatchedAt] IS NOT NULL AND [DispatchedBy] IS NOT NULL AND [RiderCount] > 0)");
        });
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.DispatchRequestId }).IsUnique().HasFilter("[DispatchRequestId] IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.VehicleId, x.OperatingDate, x.Direction }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CampusId, x.OperatingDate });
        b.HasIndex(x => new { x.TenantId, x.TransportDriverId, x.OperatingDate, x.Direction })
            .IsUnique().HasFilter("[TransportDriverId] IS NOT NULL");
        b.HasOne<Campus>().WithMany().HasForeignKey(x => new { x.TenantId, x.CampusId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Route>().WithMany().HasForeignKey(x => new { x.TenantId, x.RouteId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.TenantId, x.VehicleId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TransportDriver>().WithMany().HasForeignKey(x => new { x.TenantId, x.TransportDriverId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class TransportTripRiderConfiguration : IEntityTypeConfiguration<TransportTripRider>
{
    public void Configure(EntityTypeBuilder<TransportTripRider> b)
    {
        b.ToTable("TransportTripRiders");
        b.HasIndex(x => new { x.TenantId, x.TransportTripId, x.StudentTransportId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.StudentId, x.TransportTripId });
        b.HasOne<TransportTrip>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TransportTripId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StudentTransport>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudentTransportId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Student>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StudentEnrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudentEnrollmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
