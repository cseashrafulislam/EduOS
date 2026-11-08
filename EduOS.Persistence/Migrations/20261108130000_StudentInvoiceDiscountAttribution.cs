using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261108130000_StudentInvoiceDiscountAttribution")]
public sealed class StudentInvoiceDiscountAttributionMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("StudentInvoiceDiscountAttributions", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            StudentInvoiceLineId = t.Column<long>("bigint", nullable: false),
            StudentDiscountId = t.Column<long>("bigint", nullable: false),
            DiscountRuleId = t.Column<long>("bigint", nullable: false),
            Amount = t.Column<decimal>("decimal(18,2)", nullable: false),
            RuleValueSnapshot = t.Column<decimal>("decimal(18,4)", nullable: false),
            IsPercentageSnapshot = t.Column<bool>("bit", nullable: false),
            RuleFeeHeadIdSnapshot = t.Column<long>("bigint", nullable: true),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_StudentInvoiceDiscountAttributions", x => x.Id);
            t.UniqueConstraint("AK_StudentInvoiceDiscountAttributions_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_StudentInvoiceDiscountAttribution_PositiveAmount", "[Amount] > 0");
            t.ForeignKey("FK_StudentInvoiceDiscountAttributions_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentInvoiceDiscountAttributions_StudentInvoiceLines_TenantId_StudentInvoiceLineId",
                x => new { x.TenantId, x.StudentInvoiceLineId }, "StudentInvoiceLines",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentInvoiceDiscountAttributions_StudentDiscounts_TenantId_StudentDiscountId",
                x => new { x.TenantId, x.StudentDiscountId }, "StudentDiscounts",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudentInvoiceDiscountAttributions_DiscountRules_TenantId_DiscountRuleId",
                x => new { x.TenantId, x.DiscountRuleId }, "DiscountRules",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_StudentInvoiceDiscountAttributions_TenantId_StudentInvoiceLineId_StudentDiscountId",
            "StudentInvoiceDiscountAttributions", new[] { "TenantId", "StudentInvoiceLineId", "StudentDiscountId" },
            unique: true);
        m.CreateIndex("IX_StudentInvoiceDiscountAttributions_TenantId_StudentDiscountId_StudentInvoiceLineId",
            "StudentInvoiceDiscountAttributions", new[] { "TenantId", "StudentDiscountId", "StudentInvoiceLineId" });
        m.CreateIndex("IX_StudentInvoiceDiscountAttributions_TenantId_DiscountRuleId",
            "StudentInvoiceDiscountAttributions", new[] { "TenantId", "DiscountRuleId" });
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("StudentInvoiceDiscountAttributions");
}
