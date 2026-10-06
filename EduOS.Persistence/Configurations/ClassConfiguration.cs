using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations;

public class ClassConfiguration : IEntityTypeConfiguration<AcademicLevel>
{
    public void Configure(EntityTypeBuilder<AcademicLevel> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(x => new { x.TenantId, x.AcademicProgramId, x.Code })
            .IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.AcademicProgramId, x.DisplayOrder });
    }
}
