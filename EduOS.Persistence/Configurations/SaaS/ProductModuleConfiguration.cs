using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS;

public class ProductModuleConfiguration : IEntityTypeConfiguration<ProductModule>
{
    public void Configure(EntityTypeBuilder<ProductModule> builder)
    {
        builder.ToTable("ProductModules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.NameBangla).HasMaxLength(150);
        builder.Property(x => x.Category).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.IconName).HasMaxLength(100);
        builder.Property(x => x.RoutePrefix).HasMaxLength(200);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.IsActive, x.Category, x.DisplayOrder });
    }
}
