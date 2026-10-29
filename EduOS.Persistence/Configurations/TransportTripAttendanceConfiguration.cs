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
                "([Sequence] = 1 AND [EventType] IN ('Boarded','NoShow') AND [CorrectsEventId] IS NULL AND [CorrectionReason] IS NULL) OR ([Sequence] = 2 AND [EventType] = 'Dropped' AND [CorrectsEventId] IS NULL AND [CorrectionReason] IS NULL) OR ([Sequence] BETWEEN 3 AND 12 AND [EventType] IN ('Dropped','NoShow') AND [CorrectsEventId] IS NOT NULL AND [CorrectionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([CorrectionReason]))) BETWEEN 15 AND 500)");
        });
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.TransportTripRiderId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CorrectsEventId }).HasFilter("[CorrectsEventId] IS NOT NULL");
        b.HasOne<TransportTripAttendanceEvent>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CorrectsEventId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TransportTripRider>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.TransportTripRiderId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
