using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921211000_AddAcademicInstructionWorkflow")]
public partial class AddAcademicInstructionWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [dbo].[LessonPlans] WHERE LEN([Status]) > 30)
    THROW 51000, 'Cannot enable lesson-plan workflow because a legacy status exceeds 30 characters.', 1;");

        migrationBuilder.AlterColumn<long>(name: "ClassId", table: "Substitutions", type: "bigint", nullable: true, oldClrType: typeof(long), oldType: "bigint");
        migrationBuilder.AlterColumn<string>(name: "Reason", table: "Substitutions", type: "nvarchar(1000)", maxLength: 1000, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500, oldNullable: true);
        migrationBuilder.AddColumn<long>(name: "AcademicBatchId", table: "Substitutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "AcademicTermId", table: "Substitutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "AcademicYearId", table: "Substitutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CancellationReason", table: "Substitutions", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "CancelledAt", table: "Substitutions", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>(name: "CancelledBy", table: "Substitutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "Substitutions", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "IsActive", table: "Substitutions", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<long>(name: "RoutineEntryId", table: "Substitutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "RoutineTimeSlotId", table: "Substitutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "RowVersion", table: "Substitutions", type: "rowversion", rowVersion: true, nullable: false);

        migrationBuilder.AlterColumn<long>(name: "ClassId", table: "LessonPlans", type: "bigint", nullable: true, oldClrType: typeof(long), oldType: "bigint");
        migrationBuilder.AlterColumn<string>(name: "Description", table: "LessonPlans", type: "nvarchar(2000)", maxLength: 2000, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "Status", table: "LessonPlans", type: "nvarchar(30)", maxLength: 30, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500);
        migrationBuilder.AddColumn<long>(name: "AcademicBatchId", table: "LessonPlans", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "AcademicTermId", table: "LessonPlans", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "AcademicYearId", table: "LessonPlans", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "LessonPlans", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "CompletedAt", table: "LessonPlans", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>(name: "InstructorAssignmentId", table: "LessonPlans", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "IsActive", table: "LessonPlans", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>(name: "LearningObjectives", table: "LessonPlans", type: "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "NaturalKey", table: "LessonPlans", type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ProgressNotes", table: "LessonPlans", type: "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<int>(name: "ProgressPercent", table: "LessonPlans", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "Resources", table: "LessonPlans", type: "nvarchar(2000)", maxLength: 2000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReviewRemarks", table: "LessonPlans", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ReviewedAt", table: "LessonPlans", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>(name: "ReviewedBy", table: "LessonPlans", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "RowVersion", table: "LessonPlans", type: "rowversion", rowVersion: true, nullable: false);
        migrationBuilder.AddColumn<DateTime>(name: "SubmittedAt", table: "LessonPlans", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>(name: "SubmittedBy", table: "LessonPlans", type: "bigint", nullable: true);

        migrationBuilder.CreateIndex(name: "IX_Substitutions_AcademicBatchId", table: "Substitutions", column: "AcademicBatchId");
        migrationBuilder.CreateIndex(name: "IX_Substitutions_AcademicTermId", table: "Substitutions", column: "AcademicTermId");
        migrationBuilder.CreateIndex(name: "IX_Substitutions_AcademicYearId", table: "Substitutions", column: "AcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_Substitutions_RoutineEntryId", table: "Substitutions", column: "RoutineEntryId");
        migrationBuilder.CreateIndex(name: "IX_Substitutions_RoutineTimeSlotId", table: "Substitutions", column: "RoutineTimeSlotId");
        migrationBuilder.CreateIndex(name: "UX_Substitutions_Tenant_Request", table: "Substitutions", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "UX_Substitutions_Tenant_Routine_Date", table: "Substitutions", columns: new[] { "TenantId", "RoutineEntryId", "Date" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [RoutineEntryId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "UX_Substitutions_Tenant_Substitute_Date_Slot", table: "Substitutions", columns: new[] { "TenantId", "SubstituteTeacherId", "Date", "RoutineTimeSlotId" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [RoutineTimeSlotId] IS NOT NULL");

        migrationBuilder.CreateIndex(name: "IX_LessonPlans_AcademicBatchId", table: "LessonPlans", column: "AcademicBatchId");
        migrationBuilder.CreateIndex(name: "IX_LessonPlans_AcademicTermId", table: "LessonPlans", column: "AcademicTermId");
        migrationBuilder.CreateIndex(name: "IX_LessonPlans_AcademicYearId", table: "LessonPlans", column: "AcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_LessonPlans_InstructorAssignmentId", table: "LessonPlans", column: "InstructorAssignmentId");
        migrationBuilder.CreateIndex(name: "UX_LessonPlans_Tenant_Request", table: "LessonPlans", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "UX_LessonPlans_Tenant_NaturalKey", table: "LessonPlans", columns: new[] { "TenantId", "NaturalKey" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [NaturalKey] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_LessonPlans_TenantId_AcademicBatchId_TeacherId_StartDate_EndDate_Status", table: "LessonPlans", columns: new[] { "TenantId", "AcademicBatchId", "TeacherId", "StartDate", "EndDate", "Status" });

        migrationBuilder.AddCheckConstraint(name: "CK_Substitutions_CancellationState", table: "Substitutions", sql: "([IsActive] = 1 AND [CancelledAt] IS NULL AND [CancelledBy] IS NULL AND [CancellationReason] IS NULL) OR ([IsActive] = 0 AND [CancelledAt] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [CancellationReason] IS NOT NULL)");
        migrationBuilder.AddCheckConstraint(name: "CK_LessonPlans_ProgressPercent", table: "LessonPlans", sql: "[ProgressPercent] >= 0 AND [ProgressPercent] <= 100");
        migrationBuilder.AddCheckConstraint(name: "CK_LessonPlans_CanonicalState", table: "LessonPlans", sql: "[InstructorAssignmentId] IS NULL OR (([Status] IN ('Draft','Submitted','Approved','Rejected') AND [ProgressPercent] = 0) OR ([Status] = 'InProgress' AND [ProgressPercent] BETWEEN 1 AND 99) OR ([Status] = 'Completed' AND [ProgressPercent] = 100))");

        AddForeignKeys(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropForeignKeys(migrationBuilder);
        migrationBuilder.DropCheckConstraint(name: "CK_Substitutions_CancellationState", table: "Substitutions");
        migrationBuilder.DropCheckConstraint(name: "CK_LessonPlans_ProgressPercent", table: "LessonPlans");
        migrationBuilder.DropCheckConstraint(name: "CK_LessonPlans_CanonicalState", table: "LessonPlans");

        foreach (var name in new[] { "IX_Substitutions_AcademicBatchId", "IX_Substitutions_AcademicTermId", "IX_Substitutions_AcademicYearId", "IX_Substitutions_RoutineEntryId", "IX_Substitutions_RoutineTimeSlotId", "UX_Substitutions_Tenant_Request", "UX_Substitutions_Tenant_Routine_Date", "UX_Substitutions_Tenant_Substitute_Date_Slot" })
            migrationBuilder.DropIndex(name: name, table: "Substitutions");
        foreach (var name in new[] { "IX_LessonPlans_AcademicBatchId", "IX_LessonPlans_AcademicTermId", "IX_LessonPlans_AcademicYearId", "IX_LessonPlans_InstructorAssignmentId", "UX_LessonPlans_Tenant_Request", "UX_LessonPlans_Tenant_NaturalKey", "IX_LessonPlans_TenantId_AcademicBatchId_TeacherId_StartDate_EndDate_Status" })
            migrationBuilder.DropIndex(name: name, table: "LessonPlans");

        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [dbo].[Substitutions] WHERE [ClassId] IS NULL)
    THROW 51001, 'Cannot roll back substitution workflow because canonical rows have no legacy ClassId.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[LessonPlans] WHERE [ClassId] IS NULL)
    THROW 51002, 'Cannot roll back lesson-plan workflow because canonical rows have no legacy ClassId.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[Substitutions] WHERE LEN([Reason]) > 500)
    THROW 51003, 'Cannot roll back substitution workflow because a reason exceeds the legacy limit.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[LessonPlans] WHERE LEN([Description]) > 500)
    THROW 51004, 'Cannot roll back lesson-plan workflow because a description exceeds the legacy limit.', 1;");

        foreach (var name in new[] { "AcademicBatchId", "AcademicTermId", "AcademicYearId", "CancellationReason", "CancelledAt", "CancelledBy", "ClientRequestId", "IsActive", "RoutineEntryId", "RoutineTimeSlotId", "RowVersion" })
            migrationBuilder.DropColumn(name: name, table: "Substitutions");
        foreach (var name in new[] { "AcademicBatchId", "AcademicTermId", "AcademicYearId", "ClientRequestId", "CompletedAt", "InstructorAssignmentId", "IsActive", "LearningObjectives", "NaturalKey", "ProgressNotes", "ProgressPercent", "Resources", "ReviewRemarks", "ReviewedAt", "ReviewedBy", "RowVersion", "SubmittedAt", "SubmittedBy" })
            migrationBuilder.DropColumn(name: name, table: "LessonPlans");

        migrationBuilder.AlterColumn<long>(name: "ClassId", table: "Substitutions", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "Reason", table: "Substitutions", type: "nvarchar(500)", maxLength: 500, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(1000)", oldMaxLength: 1000, oldNullable: true);
        migrationBuilder.AlterColumn<long>(name: "ClassId", table: "LessonPlans", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "Description", table: "LessonPlans", type: "nvarchar(500)", maxLength: 500, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(2000)", oldMaxLength: 2000, oldNullable: true);
        migrationBuilder.AlterColumn<string>(name: "Status", table: "LessonPlans", type: "nvarchar(500)", maxLength: 500, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(30)", oldMaxLength: 30);
    }

    private static void AddForeignKeys(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddForeignKey(name: "FK_Substitutions_AcademicBatches_AcademicBatchId", table: "Substitutions", column: "AcademicBatchId", principalTable: "AcademicBatches", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Substitutions_AcademicTerms_AcademicTermId", table: "Substitutions", column: "AcademicTermId", principalTable: "AcademicTerms", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Substitutions_AcademicYears_AcademicYearId", table: "Substitutions", column: "AcademicYearId", principalTable: "AcademicYears", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Substitutions_RoutineEntries_RoutineEntryId", table: "Substitutions", column: "RoutineEntryId", principalTable: "RoutineEntries", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Substitutions_RoutineTimeSlots_RoutineTimeSlotId", table: "Substitutions", column: "RoutineTimeSlotId", principalTable: "RoutineTimeSlots", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_LessonPlans_AcademicBatches_AcademicBatchId", table: "LessonPlans", column: "AcademicBatchId", principalTable: "AcademicBatches", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_LessonPlans_AcademicTerms_AcademicTermId", table: "LessonPlans", column: "AcademicTermId", principalTable: "AcademicTerms", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_LessonPlans_AcademicYears_AcademicYearId", table: "LessonPlans", column: "AcademicYearId", principalTable: "AcademicYears", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_LessonPlans_InstructorAssignments_InstructorAssignmentId", table: "LessonPlans", column: "InstructorAssignmentId", principalTable: "InstructorAssignments", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    private static void DropForeignKeys(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "FK_Substitutions_AcademicBatches_AcademicBatchId", "FK_Substitutions_AcademicTerms_AcademicTermId", "FK_Substitutions_AcademicYears_AcademicYearId", "FK_Substitutions_RoutineEntries_RoutineEntryId", "FK_Substitutions_RoutineTimeSlots_RoutineTimeSlotId" })
            migrationBuilder.DropForeignKey(name: name, table: "Substitutions");
        foreach (var name in new[] { "FK_LessonPlans_AcademicBatches_AcademicBatchId", "FK_LessonPlans_AcademicTerms_AcademicTermId", "FK_LessonPlans_AcademicYears_AcademicYearId", "FK_LessonPlans_InstructorAssignments_InstructorAssignmentId" })
            migrationBuilder.DropForeignKey(name: name, table: "LessonPlans");
    }
}
