using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS
{
    public class FeatureConfiguration : IEntityTypeConfiguration<Feature>
    {
        public void Configure(EntityTypeBuilder<Feature> builder)
        {
            builder.ToTable("Features");
            builder.HasKey(f => f.Id);

            builder.Property(f => f.Name).IsRequired().HasMaxLength(100);
            builder.Property(f => f.Code).IsRequired().HasMaxLength(100);
            builder.Property(f => f.Description).HasMaxLength(1000);
            builder.Property(f => f.Category).HasMaxLength(50);
            builder.Property(f => f.IconName).HasMaxLength(50);

            builder.HasIndex(f => f.Code).IsUnique().HasFilter("[IsDeleted] = 0");
            builder.HasIndex(f => f.Category);
        }
    }
}
