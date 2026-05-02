using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS
{
    public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
    {
        public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
        {
            builder.ToTable("SubscriptionPayments");
            builder.HasKey(p => p.Id);

            builder.Property(p => p.TransactionId).IsRequired().HasMaxLength(100);
            builder.Property(p => p.GatewayTransactionId).HasMaxLength(200);
            builder.Property(p => p.GatewayReference).HasMaxLength(200);
            builder.Property(p => p.Currency).HasMaxLength(10);
            builder.Property(p => p.PaymentMethod).HasConversion<int>();
            builder.Property(p => p.Status).HasConversion<int>();

            builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");

            builder.Property(p => p.PayerBankName).HasMaxLength(200);
            builder.Property(p => p.PayerAccountNumber).HasMaxLength(100);
            builder.Property(p => p.DepositSlipNumber).HasMaxLength(100);
            builder.Property(p => p.DepositSlipUrl).HasMaxLength(500);

            builder.Property(p => p.VerificationNote).HasMaxLength(1000);
            builder.Property(p => p.GatewayResponse).HasColumnType("nvarchar(max)");
            builder.Property(p => p.FailureReason).HasMaxLength(1000);

            builder.HasIndex(p => p.TransactionId).IsUnique().HasFilter("[IsDeleted] = 0");
            builder.HasIndex(p => p.GatewayTransactionId);
            builder.HasIndex(p => new { p.TenantId, p.Status });
        }
    }
}
