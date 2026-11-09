using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduOS.Persistence.Migrations;

partial class EduOSDbContextModelSnapshot
{
    private static void AddStudentInvoiceDiscountAttributionSnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("EduOS.Core.Entities.Finance.StudentInvoiceDiscountAttribution", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<long>("StudentInvoiceLineId").HasColumnType("bigint");
            b.Property<long>("StudentDiscountId").HasColumnType("bigint");
            b.Property<long>("DiscountRuleId").HasColumnType("bigint");
            b.Property<decimal>("Amount").HasColumnType("decimal(18,2)");
            b.Property<decimal>("RuleValueSnapshot").HasColumnType("decimal(18,4)");
            b.Property<bool>("IsPercentageSnapshot").HasColumnType("bit");
            b.Property<long?>("RuleFeeHeadIdSnapshot").HasColumnType("bigint");
            b.Property<DateTime>("CreatedAt").HasColumnType("datetime2");
            b.Property<long?>("CreatedBy").HasColumnType("bigint");
            b.Property<DateTime?>("UpdatedAt").HasColumnType("datetime2");
            b.Property<long?>("UpdatedBy").HasColumnType("bigint");
            b.Property<bool>("IsDeleted").HasColumnType("bit");
            b.Property<DateTime?>("DeletedAt").HasColumnType("datetime2");
            b.Property<long?>("DeletedBy").HasColumnType("bigint");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().IsRequired()
                .ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.HasKey("Id");
            b.HasAlternateKey("TenantId", "Id");
            b.HasIndex("TenantId", "StudentInvoiceLineId", "StudentDiscountId").IsUnique();
            b.HasIndex("TenantId", "StudentDiscountId", "StudentInvoiceLineId");
            b.HasIndex("TenantId", "DiscountRuleId");
            b.ToTable("StudentInvoiceDiscountAttributions", t =>
                t.HasCheckConstraint("CK_StudentInvoiceDiscountAttribution_PositiveAmount", "[Amount] > 0"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.Finance.StudentInvoiceDiscountAttribution", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Finance.StudentInvoiceLine", "StudentInvoiceLine").WithMany()
                .HasForeignKey("TenantId", "StudentInvoiceLineId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Finance.StudentDiscount", null).WithMany()
                .HasForeignKey("TenantId", "StudentDiscountId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Finance.DiscountRule", null).WithMany()
                .HasForeignKey("TenantId", "DiscountRuleId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
            b.Navigation("StudentInvoiceLine");
        });
        AddAdaptiveStudySnapshot(modelBuilder);
    }
}
