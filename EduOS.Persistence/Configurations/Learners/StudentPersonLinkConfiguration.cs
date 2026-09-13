using EduOS.Core.Entities.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Learners;

public class StudentPersonLinkConfiguration : IEntityTypeConfiguration<StudentPersonLink>
{
    public void Configure(EntityTypeBuilder<StudentPersonLink> builder)
    {
        builder.ToTable("StudentPersonLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<int>();

        builder.HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.PersonId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => x.PersonId);

        builder.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Person)
            .WithMany(x => x.StudentLinks)
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
