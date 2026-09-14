using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260908120000_AddLearnerConsentResolutionAndDataGrants")]
public partial class AddLearnerConsentResolutionAndDataGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "LearnerConsentRequests",
            type: "rowversion",
            rowVersion: true,
            nullable: false);

        migrationBuilder.CreateTable(
            name: "LearnerDataGrants",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<long>(type: "bigint", nullable: false),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                ConsentRequestId = table.Column<long>(type: "bigint", nullable: false),
                Purpose = table.Column<int>(type: "int", nullable: false),
                GrantedScopes = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                GrantedByUserId = table.Column<long>(type: "bigint", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RevokedByUserId = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LearnerDataGrants", x => x.Id);
                table.ForeignKey(
                    name: "FK_LearnerDataGrants_LearnerConsentRequests_ConsentRequestId",
                    column: x => x.ConsentRequestId,
                    principalTable: "LearnerConsentRequests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerDataGrants_Persons_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Persons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerDataGrants_Students_StudentId",
                    column: x => x.StudentId,
                    principalTable: "Students",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerDataGrants_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LearnerDataGrants_ConsentRequestId",
            table: "LearnerDataGrants",
            column: "ConsentRequestId",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerDataGrants_PersonId",
            table: "LearnerDataGrants",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerDataGrants_PublicId",
            table: "LearnerDataGrants",
            column: "PublicId",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerDataGrants_StudentId",
            table: "LearnerDataGrants",
            column: "StudentId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerDataGrants_TenantId_PersonId_Status_ExpiresAt",
            table: "LearnerDataGrants",
            columns: new[] { "TenantId", "PersonId", "Status", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LearnerDataGrants");
        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "LearnerConsentRequests");
    }
}
