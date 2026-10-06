using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Students;

public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<StudentEnrollment>
{
    public void Configure(EntityTypeBuilder<StudentEnrollment> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.AcademicYearId, x.AcademicTermId });
        builder.HasIndex(x => new { x.TenantId, x.AcademicBatchId, x.RollNo })
            .IsUnique().HasFilter("[IsDeleted] = 0 AND [IsActive] = 1");
        builder.HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique().HasFilter("[IsDeleted] = 0 AND [IsCurrent] = 1");
    }
}
