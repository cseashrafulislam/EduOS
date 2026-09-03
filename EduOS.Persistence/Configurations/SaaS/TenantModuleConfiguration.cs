using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS;

public class TenantModuleConfiguration : IEntityTypeConfiguration<TenantModule>
{
    public void Configure(EntityTypeBuilder<TenantModule> builder)
    {
        builder.ToTable("TenantModules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActivationSource).HasConversion<int>();
        builder.Property(x => x.DisabledReason).HasMaxLength(500);
        builder.Property(x => x.ConfigurationJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.ProductModuleId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.IsEnabled, x.EffectiveFromUtc, x.EffectiveUntilUtc });
        builder.HasIndex(x => x.ProductModuleId);

        builder.HasOne(x => x.Tenant)
            .WithMany(x => x.Modules)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductModule)
            .WithMany(x => x.TenantModules)
            .HasForeignKey(x => x.ProductModuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
