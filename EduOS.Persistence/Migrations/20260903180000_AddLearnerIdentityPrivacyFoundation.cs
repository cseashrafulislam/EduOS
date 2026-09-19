using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260903180000_AddLearnerIdentityPrivacyFoundation")]
public partial class AddLearnerIdentityPrivacyFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Persons",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                FullNameBangla = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: false),
                Gender = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Persons", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PersonIdentifiers",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PersonId = table.Column<long>(type: "bigint", nullable: false),
                Type = table.Column<int>(type: "int", nullable: false),
                ProtectedValue = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                LookupDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                VerificationStatus = table.Column<int>(type: "int", nullable: false),
                VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                VerificationProvider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonIdentifiers", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonIdentifiers_Persons_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Persons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StudentPersonLinks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                PersonId = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                LinkedByUserId = table.Column<long>(type: "bigint", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentPersonLinks", x => x.Id);
                table.ForeignKey(
                    name: "FK_StudentPersonLinks_Persons_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Persons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StudentPersonLinks_Students_StudentId",
                    column: x => x.StudentId,
                    principalTable: "Students",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StudentPersonLinks_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LearnerConsentRequests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PersonId = table.Column<long>(type: "bigint", nullable: false),
                RequestedStudentId = table.Column<long>(type: "bigint", nullable: false),
                RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                Purpose = table.Column<int>(type: "int", nullable: false),
                RequestedScopes = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ResolvedByUserId = table.Column<long>(type: "bigint", nullable: true),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LearnerConsentRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_LearnerConsentRequests_Persons_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Persons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerConsentRequests_Students_RequestedStudentId",
                    column: x => x.RequestedStudentId,
                    principalTable: "Students",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerConsentRequests_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LearnerIdentityAccessLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PersonId = table.Column<long>(type: "bigint", nullable: true),
                StudentId = table.Column<long>(type: "bigint", nullable: true),
                ConsentRequestId = table.Column<long>(type: "bigint", nullable: true),
                UserId = table.Column<long>(type: "bigint", nullable: false),
                Action = table.Column<int>(type: "int", nullable: false),
                Outcome = table.Column<int>(type: "int", nullable: false),
                Purpose = table.Column<int>(type: "int", nullable: true),
                ReasonCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LearnerIdentityAccessLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_LearnerIdentityAccessLogs_LearnerConsentRequests_ConsentRequestId",
                    column: x => x.ConsentRequestId,
                    principalTable: "LearnerConsentRequests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerIdentityAccessLogs_Persons_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Persons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerIdentityAccessLogs_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Persons_PublicId",
            table: "Persons",
            column: "PublicId",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_PersonIdentifiers_PersonId",
            table: "PersonIdentifiers",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonIdentifiers_Type_LookupDigest",
            table: "PersonIdentifiers",
            columns: new[] { "Type", "LookupDigest" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_StudentPersonLinks_PersonId",
            table: "StudentPersonLinks",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_StudentPersonLinks_StudentId",
            table: "StudentPersonLinks",
            column: "StudentId");

        migrationBuilder.CreateIndex(
            name: "IX_StudentPersonLinks_TenantId_PersonId",
            table: "StudentPersonLinks",
            columns: new[] { "TenantId", "PersonId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_StudentPersonLinks_TenantId_StudentId",
            table: "StudentPersonLinks",
            columns: new[] { "TenantId", "StudentId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerConsentRequests_PersonId",
            table: "LearnerConsentRequests",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerConsentRequests_PublicId",
            table: "LearnerConsentRequests",
            column: "PublicId",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerConsentRequests_RequestedStudentId",
            table: "LearnerConsentRequests",
            column: "RequestedStudentId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerConsentRequests_TenantId_PersonId_Status_ExpiresAt",
            table: "LearnerConsentRequests",
            columns: new[] { "TenantId", "PersonId", "Status", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_ConsentRequestId",
            table: "LearnerIdentityAccessLogs",
            column: "ConsentRequestId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_PersonId",
            table: "LearnerIdentityAccessLogs",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_TenantId_PersonId_CreatedAt",
            table: "LearnerIdentityAccessLogs",
            columns: new[] { "TenantId", "PersonId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_LearnerIdentityAccessLogs_TenantId_UserId_CreatedAt",
            table: "LearnerIdentityAccessLogs",
            columns: new[] { "TenantId", "UserId", "CreatedAt" });

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LearnerIdentityAccessLogs");
        migrationBuilder.DropTable(name: "StudentPersonLinks");
        migrationBuilder.DropTable(name: "LearnerConsentRequests");
        migrationBuilder.DropTable(name: "PersonIdentifiers");
        migrationBuilder.DropTable(name: "Persons");
    }
}
