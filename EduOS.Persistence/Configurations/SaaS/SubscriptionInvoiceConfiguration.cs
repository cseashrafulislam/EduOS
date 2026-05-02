using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS
{
    public class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
    {
        public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
        {
            builder.ToTable("SubscriptionInvoices");
            builder.HasKey(i => i.Id);

            builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(50);
            builder.Property(i => i.Currency).HasMaxLength(10);
            builder.Property(i => i.PaymentStatus).HasConversion<int>();

            builder.Property(i => i.Subtotal).HasColumnType("decimal(18,2)");
            builder.Property(i => i.DiscountAmount).HasColumnType("decimal(18,2)");
            builder.Property(i => i.TaxAmount).HasColumnType("decimal(18,2)");
            builder.Property(i => i.TotalAmount).HasColumnType("decimal(18,2)");
            builder.Property(i => i.PaidAmount).HasColumnType("decimal(18,2)");
            builder.Property(i => i.DueAmount).HasColumnType("decimal(18,2)");

            builder.Property(i => i.CustomerName).IsRequired().HasMaxLength(200);
            builder.Property(i => i.CustomerEmail).HasMaxLength(150);
            builder.Property(i => i.CustomerPhone).HasMaxLength(20);
            builder.Property(i => i.CustomerAddress).HasMaxLength(500);
            builder.Property(i => i.Description).HasMaxLength(1000);
            builder.Property(i => i.InternalNote).HasMaxLength(1000);

            builder.HasIndex(i => i.InvoiceNumber).IsUnique().HasFilter("[IsDeleted] = 0");
            builder.HasIndex(i => new { i.TenantId, i.PaymentStatus });
            builder.HasIndex(i => i.DueDate);

            builder.HasMany(i => i.Payments)
                .WithOne(p => p.Invoice)
                .HasForeignKey(p => p.SubscriptionInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
