using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS
{
    public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
    {
        public void Configure(EntityTypeBuilder<TenantSubscription> builder)
        {
            builder.ToTable("TenantSubscriptions");
            builder.HasKey(s => s.Id);

            builder.Property(s => s.BillingCycle).HasConversion<int>();
            builder.Property(s => s.Status).HasConversion<int>();
            builder.Property(s => s.Currency).HasMaxLength(10);
            builder.Property(s => s.CancellationReason).HasMaxLength(500);

            builder.Property(s => s.Price).HasColumnType("decimal(18,2)");
            builder.Property(s => s.DiscountAmount).HasColumnType("decimal(18,2)");
            builder.Property(s => s.TaxAmount).HasColumnType("decimal(18,2)");
            builder.Property(s => s.FinalAmount).HasColumnType("decimal(18,2)");

            // Indexes
            builder.HasIndex(s => new { s.TenantId, s.Status });
            builder.HasIndex(s => s.TenantId)
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [Status] IN (1, 2, 3, 6)");
            builder.HasIndex(s => s.EndDate);
            builder.HasIndex(s => s.NextBillingDate);

            builder.HasMany(s => s.Invoices)
                .WithOne(i => i.Subscription)
                .HasForeignKey(i => i.TenantSubscriptionId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
