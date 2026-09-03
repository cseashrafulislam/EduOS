using EduOS.Core.Entities.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Learners;

public class LearnerConsentRequestConfiguration : IEntityTypeConfiguration<LearnerConsentRequest>
{
    public void Configure(EntityTypeBuilder<LearnerConsentRequest> builder)
    {
        builder.ToTable("LearnerConsentRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasConversion<int>();
        builder.Property(x => x.RequestedScopes).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.HasIndex(x => x.PublicId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.PersonId, x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.RequestedStudentId);

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedStudent)
            .WithMany()
            .HasForeignKey(x => x.RequestedStudentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
