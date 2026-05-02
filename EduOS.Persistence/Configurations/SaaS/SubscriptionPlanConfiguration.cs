using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS
{
    public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
    {
        public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
        {
            builder.ToTable("SubscriptionPlans");
            builder.HasKey(p => p.Id);

            builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
            builder.Property(p => p.Code).IsRequired().HasMaxLength(50);
            builder.Property(p => p.Description).HasMaxLength(2000);
            builder.Property(p => p.ShortDescription).HasMaxLength(500);
            builder.Property(p => p.IconUrl).HasMaxLength(500);
            builder.Property(p => p.Currency).HasMaxLength(10);

            // Decimal precision
            builder.Property(p => p.MonthlyPrice).HasColumnType("decimal(18,2)");
            builder.Property(p => p.QuarterlyPrice).HasColumnType("decimal(18,2)");
            builder.Property(p => p.HalfYearlyPrice).HasColumnType("decimal(18,2)");
            builder.Property(p => p.YearlyPrice).HasColumnType("decimal(18,2)");
            builder.Property(p => p.SetupFee).HasColumnType("decimal(18,2)");

            // Indexes
            builder.HasIndex(p => p.Code).IsUnique().HasFilter("[IsDeleted] = 0");
            builder.HasIndex(p => new { p.IsActive, p.IsPubliclyVisible });

            // Relationships
            builder.HasMany(p => p.PlanFeatures)
                .WithOne(pf => pf.SubscriptionPlan)
                .HasForeignKey(pf => pf.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(p => p.Subscriptions)
                .WithOne(s => s.SubscriptionPlan)
                .HasForeignKey(s => s.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
