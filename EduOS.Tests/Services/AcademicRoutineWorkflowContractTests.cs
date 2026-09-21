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
    public void Routine_writes_serialize_collision_checks_and_map_conflicts_to_409()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Academic", "AcademicRoutineService.cs"));
        source.Should().Contain("IsolationLevel = IsolationLevel.Serializable");
        source.Should().Contain("TransactionScopeAsyncFlowOption.Enabled");
        source.Should().Contain("x.RoutineTimeSlot.StartTime < slot.EndTime");
        source.Should().Contain("x.RoutineTimeSlot.EndTime > slot.StartTime");
        source.Should().Contain("x.AcademicBatchId == assignment.AcademicBatchId");
        source.Should().Contain("x.EmployeeId == assignment.EmployeeId");
        source.Should().Contain("x.RoomId == room.Id");
        source.Should().Contain("catch (TransactionAbortedException ex)");
        source.Should().Contain("Reload and try again.\", 409");
    }

    [Fact]
    public void Academic_schema_bridge_creates_snapshot_only_tables_and_collision_indexes()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Persistence", "Migrations", "20260921153000_AddCanonicalAcademicOperationsSchema.cs"));
        foreach (var table in new[] { "AcademicPrograms", "AcademicLevels", "AcademicTracks", "AcademicBatches", "AcademicCurriculums", "CurriculumSubjects", "InstructorAssignments", "RoutineTimeSlots", "RoutineEntries", "AcademicCalendarEvents", "Rooms", "ProgramCampuses", "SubjectPrerequisites" }) source.Should().Contain($"name: \"{table}\"");
        source.Should().Contain("IX_RoutineEntries_Tenant_Batch_Day");
        source.Should().Contain("IX_RoutineEntries_Tenant_Employee_Day");
        source.Should().Contain("IX_RoutineEntries_Tenant_Room_Day");
        source.Should().Contain("[Migration(\"20260921153000_AddCanonicalAcademicOperationsSchema\")]");
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
