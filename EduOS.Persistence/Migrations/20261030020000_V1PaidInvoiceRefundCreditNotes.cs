using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Additive historical credit-note classification and journal-source lookup.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030020000_V1PaidInvoiceRefundCreditNotes")]
public partial class V1PaidInvoiceRefundCreditNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name:"IsCreditNote",table:"RefundAllocations",type:"bit",
            nullable:false,defaultValue:false);
        migrationBuilder.CreateIndex(
            name:"IX_Journals_Tenant_SourceType_SourceId",table:"Journals",
            columns:new[]{"TenantId","SourceType","SourceId"});
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name:"IX_Journals_Tenant_SourceType_SourceId",table:"Journals");
        migrationBuilder.DropColumn(name:"IsCreditNote",table:"RefundAllocations");
    }
}
