using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations
{
    public class ClassConfiguration : IEntityTypeConfiguration<Class>
    {
        public void Configure(EntityTypeBuilder<Class> builder)
        {
            builder.ToTable("Classes");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(c => c.NumericValue)
                .IsRequired();

            builder.HasIndex(c => new { c.TenantId, c.Name })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // Soft delete query filter
            builder.HasQueryFilter(c => !c.IsDeleted);
        }
    }
}