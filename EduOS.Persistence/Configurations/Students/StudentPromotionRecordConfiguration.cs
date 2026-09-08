using EduOS.Core.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Students;

public sealed class StudentPromotionRecordConfiguration : IEntityTypeConfiguration<StudentPromotionRecord>
{
    public void Configure(EntityTypeBuilder<StudentPromotionRecord> builder)
    {
        builder.ToTable("StudentPromotionRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Decision).HasConversion<int>();
        builder.Property(x => x.FromRoll).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ToRoll).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => x.PublicId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.FromEnrollmentId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.ProcessedAt });

        builder.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromEnrollment).WithMany().HasForeignKey(x => x.FromEnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToEnrollment).WithMany().HasForeignKey(x => x.ToEnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromAcademicYear).WithMany().HasForeignKey(x => x.FromAcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToAcademicYear).WithMany().HasForeignKey(x => x.ToAcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromClass).WithMany().HasForeignKey(x => x.FromClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToClass).WithMany().HasForeignKey(x => x.ToClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromSection).WithMany().HasForeignKey(x => x.FromSectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToSection).WithMany().HasForeignKey(x => x.ToSectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromGroup).WithMany().HasForeignKey(x => x.FromGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToGroup).WithMany().HasForeignKey(x => x.ToGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
