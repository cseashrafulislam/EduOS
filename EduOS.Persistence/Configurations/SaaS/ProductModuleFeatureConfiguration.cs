using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS;

public class ProductModuleFeatureConfiguration : IEntityTypeConfiguration<ProductModuleFeature>
{
    public void Configure(EntityTypeBuilder<ProductModuleFeature> builder)
    {
        builder.ToTable("ProductModuleFeatures");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ProductModuleId, x.FeatureId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => x.FeatureId);

        builder.HasOne(x => x.ProductModule)
            .WithMany(x => x.Features)
            .HasForeignKey(x => x.ProductModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Feature)
            .WithMany(x => x.ProductModules)
            .HasForeignKey(x => x.FeatureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
