using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// V3 Alumni opt-in records and immutable consent command/audit history.
/// These SQL-owned tables deliberately do not modify V1 Student/Enrollment models or
/// the EF model snapshot; access is through parameterized EduOSDbContext SQL queries.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261031220000_V3AlumniConsentDirectory")]
public sealed class V3AlumniConsentDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AlumniProfiles", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            StudentId = table.Column<long>(type: "bigint", nullable: false),
            StudentExitRecordId = table.Column<long>(type: "bigint", nullable: false),
            CampusId = table.Column<long>(type: "bigint", nullable: false),
            DirectoryOptIn = table.Column<bool>(type: "bit", nullable: false),
            ContactEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
            ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
            ConsentRecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniProfiles", x => x.Id);
            table.UniqueConstraint("AK_AlumniProfiles_TenantId_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_AlumniProfiles_PrivateContact",
                "[DirectoryOptIn] = 1 OR ([ContactEmail] IS NULL AND [ContactPhone] IS NULL)");
            table.ForeignKey("FK_AlumniProfiles_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniProfiles_Students_TenantId_StudentId",
                x => new { x.TenantId, x.StudentId }, "Students",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniProfiles_StudentExitRecords_TenantId_StudentExitRecordId",
                x => new { x.TenantId, x.StudentExitRecordId }, "StudentExitRecords",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AlumniProfiles_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniProfiles_Tenant_Student", "AlumniProfiles",
            new[] { "TenantId", "StudentId" }, unique: true);
        migrationBuilder.CreateIndex("UX_AlumniProfiles_Tenant_PublicId", "AlumniProfiles",
            new[] { "TenantId", "PublicId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AlumniProfiles_Tenant_Directory_Campus_Created",
            "AlumniProfiles", new[] { "TenantId", "DirectoryOptIn", "CampusId", "CreatedAtUtc" });

        migrationBuilder.CreateTable("AlumniConsentCommands", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            AlumniProfileId = table.Column<long>(type: "bigint", nullable: false),
            RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniConsentCommands", x => x.Id);
            table.ForeignKey("FK_AlumniConsentCommands_AlumniProfiles_TenantId_AlumniProfileId",
                x => new { x.TenantId, x.AlumniProfileId }, "AlumniProfiles",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_AlumniConsentCommands_Tenant_Request", "AlumniConsentCommands",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_AlumniConsentCommands_Tenant_Profile", "AlumniConsentCommands",
            new[] { "TenantId", "AlumniProfileId", "CreatedAtUtc" });

        migrationBuilder.CreateTable("AlumniProfileAudits", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            AlumniProfileId = table.Column<long>(type: "bigint", nullable: false),
            ActionCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AlumniProfileAudits", x => x.Id);
            table.ForeignKey("FK_AlumniProfileAudits_AlumniProfiles_TenantId_AlumniProfileId",
                x => new { x.TenantId, x.AlumniProfileId }, "AlumniProfiles",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_AlumniProfileAudits_Tenant_Profile_Time",
            "AlumniProfileAudits", new[] { "TenantId", "AlumniProfileId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AlumniProfileAudits");
        migrationBuilder.DropTable("AlumniConsentCommands");
        migrationBuilder.DropTable("AlumniProfiles");
    }
}
