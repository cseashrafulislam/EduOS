using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS;

public class InstitutionTypeModuleConfiguration : IEntityTypeConfiguration<InstitutionTypeModule>
{
    public void Configure(EntityTypeBuilder<InstitutionTypeModule> builder)
    {
        builder.ToTable("InstitutionTypeModules");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.InstitutionTypeDefinitionId, x.ProductModuleId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(x => x.InstitutionTypeDefinition)
            .WithMany(x => x.Modules)
            .HasForeignKey(x => x.InstitutionTypeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductModule)
            .WithMany(x => x.InstitutionTypes)
            .HasForeignKey(x => x.ProductModuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
