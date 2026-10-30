using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace EduOS.Persistence.Migrations;
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030224500_ProcurementSupplierOnboarding")]
public sealed class ProcurementSupplierOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name:"ProcurementSuppliers",columns:table=>new{
            Id=table.Column<long>(type:"bigint",nullable:false).Annotation("SqlServer:Identity","1, 1"),
            TenantId=table.Column<long>(type:"bigint",nullable:false),
            PublicId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            ClientRequestId=table.Column<Guid>(type:"uniqueidentifier",nullable:false),
            CampusId=table.Column<long>(type:"bigint",nullable:false),
            SupplierCode=table.Column<string>(type:"nvarchar(40)",maxLength:40,nullable:false),
            Name=table.Column<string>(type:"nvarchar(200)",maxLength:200,nullable:false),
            Email=table.Column<string>(type:"nvarchar(200)",maxLength:200,nullable:true),
            Phone=table.Column<string>(type:"nvarchar(40)",maxLength:40,nullable:true),
            TaxRegistrationNo=table.Column<string>(type:"nvarchar(100)",maxLength:100,nullable:true),
            StateCode=table.Column<string>(type:"nvarchar(20)",maxLength:20,nullable:false),
            SubmittedAt=table.Column<DateTime>(type:"datetime2",nullable:true),
            DecidedAt=table.Column<DateTime>(type:"datetime2",nullable:true),
            DecidedByUserId=table.Column<long>(type:"bigint",nullable:true),
            DecisionReason=table.Column<string>(type:"nvarchar(500)",maxLength:500,nullable:true),
            CreatedAt=table.Column<DateTime>(type:"datetime2",nullable:false),
            CreatedBy=table.Column<long>(type:"bigint",nullable:true),
            UpdatedAt=table.Column<DateTime>(type:"datetime2",nullable:true),
            UpdatedBy=table.Column<long>(type:"bigint",nullable:true),
            IsDeleted=table.Column<bool>(type:"bit",nullable:false),
            DeletedAt=table.Column<DateTime>(type:"datetime2",nullable:true),
            DeletedBy=table.Column<long>(type:"bigint",nullable:true),
            RowVersion=table.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false)
        },constraints:table=>{
            table.PrimaryKey("PK_ProcurementSuppliers",x=>x.Id);
            table.UniqueConstraint("AK_ProcurementSuppliers_TenantId_Id",x=>new{x.TenantId,x.Id});
            table.CheckConstraint("CK_ProcurementSupplier_State","[StateCode] IN (N'Draft',N'Submitted',N'Approved',N'Rejected',N'Inactive')");
            table.ForeignKey("FK_ProcurementSuppliers_Tenants_TenantId",x=>x.TenantId,"Tenants","Id",onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSuppliers_Campuses_TenantId_CampusId",x=>new{x.TenantId,x.CampusId},"Campuses",new[]{"TenantId","Id"},onDelete:ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementSuppliers_AspNetUsers_DecidedByUserId",x=>x.DecidedByUserId,"AspNetUsers","Id",onDelete:ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_ProcurementSuppliers_DecidedByUserId","ProcurementSuppliers","DecidedByUserId");
        migrationBuilder.CreateIndex("IX_ProcurementSuppliers_PublicId","ProcurementSuppliers","PublicId",unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSuppliers_TenantId_ClientRequestId","ProcurementSuppliers",new[]{"TenantId","ClientRequestId"},unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSuppliers_TenantId_CampusId_SupplierCode","ProcurementSuppliers",new[]{"TenantId","CampusId","SupplierCode"},unique:true);
        migrationBuilder.CreateIndex("IX_ProcurementSuppliers_TenantId_CampusId_StateCode_Name","ProcurementSuppliers",new[]{"TenantId","CampusId","StateCode","Name"});
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.DropTable("ProcurementSuppliers");
}
