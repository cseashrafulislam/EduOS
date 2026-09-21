using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921153000_AddCanonicalAcademicOperationsSchema")]
public partial class AddCanonicalAcademicOperationsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AcademicPrograms",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CampusId = table.Column<long>(type: "bigint", nullable: true),
                DepartmentId = table.Column<long>(type: "bigint", nullable: true),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ShortName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                DurationInMonths = table.Column<int>(type: "int", nullable: false),
                AwardTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                IsAdmissionOpen = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicPrograms", x => x.Id);
                table.ForeignKey("FK_AcademicPrograms_Departments_DepartmentId", x => x.DepartmentId, "Departments", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicPrograms_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AcademicCalendarEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CampusId = table.Column<long>(type: "bigint", nullable: true),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: true),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                EventType = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Location = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                IsHoliday = table.Column<bool>(type: "bit", nullable: false),
                IsPublicVisible = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicCalendarEvents", x => x.Id);
                table.ForeignKey("FK_AcademicCalendarEvents_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicCalendarEvents_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicCalendarEvents_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Rooms",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CampusId = table.Column<long>(type: "bigint", nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                BuildingName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Floor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                Capacity = table.Column<int>(type: "int", nullable: false),
                IsLab = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Rooms", x => x.Id);
                table.ForeignKey("FK_Rooms_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RoutineTimeSlots",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                IsBreak = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoutineTimeSlots", x => x.Id);
                table.ForeignKey("FK_RoutineTimeSlots_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SubjectPrerequisites",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                SubjectId = table.Column<long>(type: "bigint", nullable: false),
                PrerequisiteSubjectId = table.Column<long>(type: "bigint", nullable: false),
                IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubjectPrerequisites", x => x.Id);
                table.ForeignKey("FK_SubjectPrerequisites_Subjects_PrerequisiteSubjectId", x => x.PrerequisiteSubjectId, "Subjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SubjectPrerequisites_Subjects_SubjectId", x => x.SubjectId, "Subjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SubjectPrerequisites_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AcademicLevels",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicProgramId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                LevelNo = table.Column<int>(type: "int", nullable: false),
                IsPromotable = table.Column<bool>(type: "bit", nullable: false),
                IsTerminalLevel = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicLevels", x => x.Id);
                table.ForeignKey("FK_AcademicLevels_AcademicPrograms_AcademicProgramId", x => x.AcademicProgramId, "AcademicPrograms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicLevels_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AcademicTracks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicProgramId = table.Column<long>(type: "bigint", nullable: true),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicTracks", x => x.Id);
                table.ForeignKey("FK_AcademicTracks_AcademicPrograms_AcademicProgramId", x => x.AcademicProgramId, "AcademicPrograms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicTracks_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AcademicCurriculums",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicProgramId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                EffectiveFromAcademicYearId = table.Column<long>(type: "bigint", nullable: true),
                EffectiveToAcademicYearId = table.Column<long>(type: "bigint", nullable: true),
                IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicCurriculums", x => x.Id);
                table.ForeignKey("FK_AcademicCurriculums_AcademicPrograms_AcademicProgramId", x => x.AcademicProgramId, "AcademicPrograms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicCurriculums_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ProgramCampuses",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicProgramId = table.Column<long>(type: "bigint", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                IsAdmissionOpen = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProgramCampuses", x => x.Id);
                table.ForeignKey("FK_ProgramCampuses_AcademicPrograms_AcademicProgramId", x => x.AcademicProgramId, "AcademicPrograms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProgramCampuses_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AcademicBatches",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                AcademicProgramId = table.Column<long>(type: "bigint", nullable: false),
                AcademicLevelId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTrackId = table.Column<long>(type: "bigint", nullable: true),
                MediumId = table.Column<long>(type: "bigint", nullable: true),
                ShiftId = table.Column<long>(type: "bigint", nullable: true),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                DeliveryMode = table.Column<int>(type: "int", nullable: false),
                Capacity = table.Column<int>(type: "int", nullable: false),
                StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicBatches", x => x.Id);
                table.ForeignKey("FK_AcademicBatches_AcademicLevels_AcademicLevelId", x => x.AcademicLevelId, "AcademicLevels", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_AcademicPrograms_AcademicProgramId", x => x.AcademicProgramId, "AcademicPrograms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_AcademicTracks_AcademicTrackId", x => x.AcademicTrackId, "AcademicTracks", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_Mediums_MediumId", x => x.MediumId, "Mediums", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_Shifts_ShiftId", x => x.ShiftId, "Shifts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicBatches_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CurriculumSubjects",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicCurriculumId = table.Column<long>(type: "bigint", nullable: false),
                AcademicLevelId = table.Column<long>(type: "bigint", nullable: false),
                SubjectId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTrackId = table.Column<long>(type: "bigint", nullable: true),
                MediumId = table.Column<long>(type: "bigint", nullable: true),
                FullMarks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                PassMarks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreditHours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                IsOptional = table.Column<bool>(type: "bit", nullable: false),
                HasPractical = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CurriculumSubjects", x => x.Id);
                table.ForeignKey("FK_CurriculumSubjects_AcademicCurriculums_AcademicCurriculumId", x => x.AcademicCurriculumId, "AcademicCurriculums", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CurriculumSubjects_AcademicLevels_AcademicLevelId", x => x.AcademicLevelId, "AcademicLevels", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CurriculumSubjects_AcademicTracks_AcademicTrackId", x => x.AcademicTrackId, "AcademicTracks", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CurriculumSubjects_Mediums_MediumId", x => x.MediumId, "Mediums", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CurriculumSubjects_Subjects_SubjectId", x => x.SubjectId, "Subjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CurriculumSubjects_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "InstructorAssignments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicBatchId = table.Column<long>(type: "bigint", nullable: false),
                SubjectId = table.Column<long>(type: "bigint", nullable: false),
                EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                IsClassAdvisor = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstructorAssignments", x => x.Id);
                table.ForeignKey("FK_InstructorAssignments_AcademicBatches_AcademicBatchId", x => x.AcademicBatchId, "AcademicBatches", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InstructorAssignments_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InstructorAssignments_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InstructorAssignments_Employee_EmployeeId", x => x.EmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InstructorAssignments_Subjects_SubjectId", x => x.SubjectId, "Subjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InstructorAssignments_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RoutineEntries",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AcademicBatchId = table.Column<long>(type: "bigint", nullable: false),
                RoutineTimeSlotId = table.Column<long>(type: "bigint", nullable: false),
                DayOfWeek = table.Column<int>(type: "int", nullable: false),
                SubjectId = table.Column<long>(type: "bigint", nullable: false),
                EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                RoomId = table.Column<long>(type: "bigint", nullable: true),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoutineEntries", x => x.Id);
                table.ForeignKey("FK_RoutineEntries_AcademicBatches_AcademicBatchId", x => x.AcademicBatchId, "AcademicBatches", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RoutineEntries_Employee_EmployeeId", x => x.EmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RoutineEntries_RoutineTimeSlots_RoutineTimeSlotId", x => x.RoutineTimeSlotId, "RoutineTimeSlots", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RoutineEntries_Subjects_SubjectId", x => x.SubjectId, "Subjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RoutineEntries_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        CreateIndexes(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CurriculumSubjects");
        migrationBuilder.DropTable(name: "InstructorAssignments");
        migrationBuilder.DropTable(name: "ProgramCampuses");
        migrationBuilder.DropTable(name: "RoutineEntries");
        migrationBuilder.DropTable(name: "SubjectPrerequisites");
        migrationBuilder.DropTable(name: "AcademicCurriculums");
        migrationBuilder.DropTable(name: "Rooms");
        migrationBuilder.DropTable(name: "RoutineTimeSlots");
        migrationBuilder.DropTable(name: "AcademicBatches");
        migrationBuilder.DropTable(name: "AcademicCalendarEvents");
        migrationBuilder.DropTable(name: "AcademicLevels");
        migrationBuilder.DropTable(name: "AcademicTracks");
        migrationBuilder.DropTable(name: "AcademicPrograms");
    }

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex("IX_AcademicPrograms_DepartmentId", "AcademicPrograms", "DepartmentId");
        migrationBuilder.CreateIndex("IX_AcademicPrograms_TenantId", "AcademicPrograms", "TenantId");
        migrationBuilder.CreateIndex("IX_AcademicCalendarEvents_AcademicTermId", "AcademicCalendarEvents", "AcademicTermId");
        migrationBuilder.CreateIndex("IX_AcademicCalendarEvents_AcademicYearId", "AcademicCalendarEvents", "AcademicYearId");
        migrationBuilder.CreateIndex("IX_AcademicCalendarEvents_TenantId", "AcademicCalendarEvents", "TenantId");
        migrationBuilder.CreateIndex("IX_Rooms_TenantId", "Rooms", "TenantId");
        migrationBuilder.CreateIndex("IX_RoutineTimeSlots_TenantId", "RoutineTimeSlots", "TenantId");
        migrationBuilder.CreateIndex("IX_RoutineTimeSlots_Tenant_Time", "RoutineTimeSlots", new[] { "TenantId", "IsActive", "StartTime", "EndTime" });
        migrationBuilder.CreateIndex("IX_SubjectPrerequisites_PrerequisiteSubjectId", "SubjectPrerequisites", "PrerequisiteSubjectId");
        migrationBuilder.CreateIndex("IX_SubjectPrerequisites_SubjectId", "SubjectPrerequisites", "SubjectId");
        migrationBuilder.CreateIndex("IX_SubjectPrerequisites_TenantId", "SubjectPrerequisites", "TenantId");
        migrationBuilder.CreateIndex("IX_AcademicLevels_AcademicProgramId", "AcademicLevels", "AcademicProgramId");
        migrationBuilder.CreateIndex("IX_AcademicLevels_TenantId", "AcademicLevels", "TenantId");
        migrationBuilder.CreateIndex("IX_AcademicTracks_AcademicProgramId", "AcademicTracks", "AcademicProgramId");
        migrationBuilder.CreateIndex("IX_AcademicTracks_TenantId", "AcademicTracks", "TenantId");
        migrationBuilder.CreateIndex("IX_AcademicCurriculums_AcademicProgramId", "AcademicCurriculums", "AcademicProgramId");
        migrationBuilder.CreateIndex("IX_AcademicCurriculums_TenantId", "AcademicCurriculums", "TenantId");
        migrationBuilder.CreateIndex("IX_ProgramCampuses_AcademicProgramId", "ProgramCampuses", "AcademicProgramId");
        migrationBuilder.CreateIndex("IX_ProgramCampuses_TenantId", "ProgramCampuses", "TenantId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_AcademicLevelId", "AcademicBatches", "AcademicLevelId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_AcademicProgramId", "AcademicBatches", "AcademicProgramId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_AcademicTermId", "AcademicBatches", "AcademicTermId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_AcademicTrackId", "AcademicBatches", "AcademicTrackId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_AcademicYearId", "AcademicBatches", "AcademicYearId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_MediumId", "AcademicBatches", "MediumId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_ShiftId", "AcademicBatches", "ShiftId");
        migrationBuilder.CreateIndex("IX_AcademicBatches_TenantId", "AcademicBatches", "TenantId");
        migrationBuilder.CreateIndex("IX_CurriculumSubjects_AcademicCurriculumId", "CurriculumSubjects", "AcademicCurriculumId");
        migrationBuilder.CreateIndex("IX_CurriculumSubjects_AcademicLevelId", "CurriculumSubjects", "AcademicLevelId");
        migrationBuilder.CreateIndex("IX_CurriculumSubjects_AcademicTrackId", "CurriculumSubjects", "AcademicTrackId");
        migrationBuilder.CreateIndex("IX_CurriculumSubjects_MediumId", "CurriculumSubjects", "MediumId");
        migrationBuilder.CreateIndex("IX_CurriculumSubjects_SubjectId", "CurriculumSubjects", "SubjectId");
        migrationBuilder.CreateIndex("IX_CurriculumSubjects_TenantId", "CurriculumSubjects", "TenantId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_AcademicBatchId", "InstructorAssignments", "AcademicBatchId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_AcademicTermId", "InstructorAssignments", "AcademicTermId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_AcademicYearId", "InstructorAssignments", "AcademicYearId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_EmployeeId", "InstructorAssignments", "EmployeeId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_SubjectId", "InstructorAssignments", "SubjectId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_TenantId", "InstructorAssignments", "TenantId");
        migrationBuilder.CreateIndex("IX_InstructorAssignments_Tenant_Batch_Term", "InstructorAssignments", new[] { "TenantId", "AcademicBatchId", "AcademicYearId", "AcademicTermId", "SubjectId", "IsActive" });
        migrationBuilder.CreateIndex("IX_InstructorAssignments_Tenant_Employee_Term", "InstructorAssignments", new[] { "TenantId", "EmployeeId", "AcademicYearId", "AcademicTermId", "IsActive" });
        migrationBuilder.CreateIndex("IX_RoutineEntries_AcademicBatchId", "RoutineEntries", "AcademicBatchId");
        migrationBuilder.CreateIndex("IX_RoutineEntries_EmployeeId", "RoutineEntries", "EmployeeId");
        migrationBuilder.CreateIndex("IX_RoutineEntries_RoutineTimeSlotId", "RoutineEntries", "RoutineTimeSlotId");
        migrationBuilder.CreateIndex("IX_RoutineEntries_SubjectId", "RoutineEntries", "SubjectId");
        migrationBuilder.CreateIndex("IX_RoutineEntries_TenantId", "RoutineEntries", "TenantId");
        migrationBuilder.CreateIndex("IX_RoutineEntries_Tenant_Batch_Day", "RoutineEntries", new[] { "TenantId", "AcademicYearId", "AcademicTermId", "AcademicBatchId", "DayOfWeek", "IsActive" });
        migrationBuilder.CreateIndex("IX_RoutineEntries_Tenant_Employee_Day", "RoutineEntries", new[] { "TenantId", "AcademicYearId", "AcademicTermId", "EmployeeId", "DayOfWeek", "IsActive" });
        migrationBuilder.CreateIndex("IX_RoutineEntries_Tenant_Room_Day", "RoutineEntries", new[] { "TenantId", "AcademicYearId", "AcademicTermId", "RoomId", "DayOfWeek", "IsActive" });
    }
}
