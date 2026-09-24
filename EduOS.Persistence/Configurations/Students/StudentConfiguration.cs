using EduOS.Core.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Students;

public sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.Property(x => x.PublicId).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.FullNameBangla).HasMaxLength(200);
        builder.Property(x => x.PreferredLanguage).IsRequired().HasMaxLength(10).HasDefaultValue("bn-BD");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.PublicId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.StudentCode }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.AdmissionApplicationId }).IsUnique().HasFilter("[IsDeleted] = 0 AND [AdmissionApplicationId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.ClassId, x.SectionId, x.Roll }).IsUnique().HasFilter("[IsDeleted] = 0 AND [IsActive] = 1");

        builder.HasOne(x => x.AdmissionApplication).WithMany().HasForeignKey(x => x.AdmissionApplicationId).OnDelete(DeleteBehavior.Restrict);
    }
}
