using EduOS.Core.Entities.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Learners;

public sealed class LearnerDataGrantConfiguration : IEntityTypeConfiguration<LearnerDataGrant>
{
    public void Configure(EntityTypeBuilder<LearnerDataGrant> builder)
    {
        builder.ToTable("LearnerDataGrants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasConversion<int>();
        builder.Property(x => x.GrantedScopes).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.PublicId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => x.ConsentRequestId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => x.StudentId);
        builder.HasIndex(x => new { x.TenantId, x.PersonId, x.Status, x.ExpiresAt });

        builder.HasOne(x => x.Person)
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConsentRequest)
            .WithMany()
            .HasForeignKey(x => x.ConsentRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
