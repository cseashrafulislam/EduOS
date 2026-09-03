using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS;

public class InstitutionTypeDefinitionConfiguration : IEntityTypeConfiguration<InstitutionTypeDefinition>
{
    public void Configure(EntityTypeBuilder<InstitutionTypeDefinition> builder)
    {
        builder.ToTable("InstitutionTypeDefinitions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.NameBangla).HasMaxLength(150);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.AcademicCycleType).HasConversion<int>();
        builder.Property(x => x.TerminologyJson).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(x => x.DefaultSettingsJson).IsRequired().HasColumnType("nvarchar(max)");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.IsActive, x.IsPubliclyVisible, x.DisplayOrder });
    }
}
