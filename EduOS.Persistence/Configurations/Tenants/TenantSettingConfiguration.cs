using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Tenants
{
    public class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
    {
        public void Configure(EntityTypeBuilder<TenantSetting> builder)
        {
            builder.ToTable("TenantSettings");
            builder.HasKey(s => s.Id);

            builder.Property(s => s.Category).IsRequired().HasMaxLength(50);
            builder.Property(s => s.SettingKey).IsRequired().HasMaxLength(100);
            builder.Property(s => s.SettingValue).HasColumnType("nvarchar(max)");
            builder.Property(s => s.DataType).HasMaxLength(20);
            builder.Property(s => s.Description).HasMaxLength(500);

            builder.HasIndex(s => new { s.TenantId, s.Category, s.SettingKey })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        }
    }
}
