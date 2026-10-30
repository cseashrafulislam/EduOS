using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HostelGuardianApplicationAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostelApplicationAuthorities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentGuardianId = table.Column<long>(type: "bigint", nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    GuardianId = table.Column<long>(type: "bigint", nullable: false),
                    CampusId = table.Column<long>(type: "bigint", nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostelApplicationAuthorities", x => x.Id);
                    table.UniqueConstraint("AK_HostelApplicationAuthorities_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_HostelAuthority_Expiry", "[ExpiresAtUtc] > [GrantedAtUtc]");
                    table.ForeignKey(
                        name: "FK_HostelApplicationAuthorities_Campuses_TenantId_CampusId",
                        columns: x => new { x.TenantId, x.CampusId },
                        principalTable: "Campuses",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplicationAuthorities_Guardians_TenantId_GuardianId",
                        columns: x => new { x.TenantId, x.GuardianId },
                        principalTable: "Guardians",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplicationAuthorities_StudentGuardians_TenantId_StudentGuardianId",
                        columns: x => new { x.TenantId, x.StudentGuardianId },
                        principalTable: "StudentGuardians",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplicationAuthorities_Students_TenantId_StudentId",
                        columns: x => new { x.TenantId, x.StudentId },
                        principalTable: "Students",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostelApplicationAuthorities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplicationAuthorities_TenantId_CampusId",
                table: "HostelApplicationAuthorities",
                columns: new[] { "TenantId", "CampusId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplicationAuthorities_TenantId_ClientRequestId",
                table: "HostelApplicationAuthorities",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplicationAuthorities_TenantId_GuardianId_StudentId_ExpiresAtUtc",
                table: "HostelApplicationAuthorities",
                columns: new[] { "TenantId", "GuardianId", "StudentId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplicationAuthorities_TenantId_StudentGuardianId_RevokedAtUtc_ExpiresAtUtc",
                table: "HostelApplicationAuthorities",
                columns: new[] { "TenantId", "StudentGuardianId", "RevokedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostelApplicationAuthorities_TenantId_StudentId",
                table: "HostelApplicationAuthorities",
                columns: new[] { "TenantId", "StudentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostelApplicationAuthorities");
        }
    }
}
