using EduOS.Core.Entities.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Learners;

public class LearnerIdentityAccessLogConfiguration : IEntityTypeConfiguration<LearnerIdentityAccessLog>
{
    public void Configure(EntityTypeBuilder<LearnerIdentityAccessLog> builder)
    {
        builder.ToTable("LearnerIdentityAccessLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<int>();
        builder.Property(x => x.Outcome).HasConversion<int>();
        builder.Property(x => x.Purpose).HasConversion<int>();
        builder.Property(x => x.ReasonCode).IsRequired().HasMaxLength(80);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.PersonId, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
        builder.HasIndex(x => x.ConsentRequestId);

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConsentRequest)
            .WithMany()
            .HasForeignKey(x => x.ConsentRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
