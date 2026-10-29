using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations;
public sealed class TransportTripAttendanceConfiguration : IEntityTypeConfiguration<TransportTripAttendanceEvent>
{
    public void Configure(EntityTypeBuilder<TransportTripAttendanceEvent> b)
    {
        b.ToTable("TransportTripAttendanceEvents", t =>
        {
            t.HasCheckConstraint("CK_TransportTripAttendanceEvents_Type",
                "[EventType] IN ('Boarded','Dropped','NoShow')");
            t.HasCheckConstraint("CK_TransportTripAttendanceEvents_Sequence",
                "([Sequence] = 1 AND [EventType] IN ('Boarded','NoShow')) OR ([Sequence] = 2 AND [EventType] = 'Dropped')");
        });
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.TransportTripRiderId, x.Sequence }).IsUnique();
        b.HasOne<TransportTripRider>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TransportTripRiderId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
