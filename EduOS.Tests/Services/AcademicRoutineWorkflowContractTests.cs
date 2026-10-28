using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class AcademicRoutineWorkflowContractTests
{
    [Fact]
    public void Controller_requires_auth_module_antiforgery_and_privileged_mutations()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.App", "Controllers", "Api", "AcademicRoutinesController.cs"));
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin,Principal,VicePrincipal,Teacher\")]");
        source.Should().Contain("[RequireModule(\"ACADEMIC\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        source.Should().Contain("[ResponseCache(NoStore = true");
        source.Should().Contain("TenantAdmin,Principal,VicePrincipal");
        source.Should().NotContain("AllowAnonymous");
    }

    [Fact]
    public void Routine_writes_use_serializable_transactions_and_check_real_batch_teacher_room_collisions()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Academic", "AcademicRoutineService.cs"));
        source.Should().Contain("IsolationLevel.Serializable");
        source.Split("IsolationLevel.Serializable").Length.Should().Be(4,
            "time-slot, instructor-assignment and routine-entry writes must serialize their checks");
        source.Should().Contain("_unitOfWork.ExecuteInTransactionAsync(async token =>");
        source.Should().Contain("var overlappingEntries =");
        source.Should().Contain("other.DayOfWeek == request.DayOfWeek && other.IsActive");
        source.Should().Contain("other.EffectiveFrom <= request.EffectiveTo.GetValueOrDefault(DateOnly.MaxValue)");
        source.Should().Contain("otherSlot.StartTime < slot.EndTime");
        source.Should().Contain("otherSlot.EndTime > slot.StartTime");
        source.Should().Contain("otherOffering.AcademicBatchId == batch.Id");
        source.Should().Contain("otherAssignment.EmployeeId == assignment.EmployeeId");
        source.Should().Contain("other.RoomId == room.Id");
        source.Should().Contain("AnyAsync(token)");
        source.Should().NotContain("Schedule conflict check exceeded its safe limit.");
        source.Should().NotContain(".Take(2000).ToListAsync(token)");
        source.Should().Contain("catch (DbUpdateException ex)");
        source.Should().Contain("Routine entry conflicts with another update.");
        var uow = File.ReadAllText(FindRepositoryFile("EduOS.Persistence", "Context", "EduOSDbContext.cs"));
        uow.Should().Contain("Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)");
        uow.Should().Contain("Database.CreateExecutionStrategy()");
    }

    [Fact]
    public void Academic_schema_and_index_support_concurrent_routine_overlap_checks()
    {
        var initial = File.ReadAllText(FindRepositoryFile("EduOS.Persistence", "Migrations", "20261010084739_Initials.cs"));
        foreach (var table in new[] { "AcademicPrograms", "AcademicLevels", "AcademicTracks", "AcademicBatches",
                     "AcademicCurriculums", "CurriculumSubjects", "InstructorAssignments", "RoutineTimeSlots",
                     "RoutineEntries", "AcademicCalendarEvents", "Rooms", "ProgramCampuses", "SubjectPrerequisites" })
            initial.Should().Contain($"name: \"{table}\"");
        var indexMigration = File.ReadAllText(FindRepositoryFile("EduOS.Persistence", "Migrations",
            "20261011090000_RoutineCollisionIndex.cs"));
        indexMigration.Should().Contain("IX_RoutineEntries_Tenant_Day_Active_From");
        indexMigration.Should().Contain("new[] { \"TenantId\", \"DayOfWeek\", \"IsActive\", \"EffectiveFrom\" }");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
