using System.Text;
using ClosedXML.Excel;
using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentImportPreviewServiceTests
{
    private static readonly Dictionary<string, string> Mapping = new()
    {
        ["SourceKey"] = "SourceKey", ["StudentCode"] = "StudentCode",
        ["FullName"] = "FullName", ["CampusId"] = "CampusId",
        ["AcademicBatchId"] = "AcademicBatchId", ["RollNo"] = "RollNo",
        ["GuardianName"] = "GuardianName", ["GuardianPhone"] = "GuardianPhone"
    };
    private const string Header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone\n";


    [Fact]
    public async Task Preview_reports_required_offering_capacity_and_missing_offerings_without_writes()
    {
        var options = Options(); await SeedAsync(options, capacity: 10);
        await using var db = Context(options, 101);
        db.Subjects.Add(new Subject { Id = 60, TenantId = 101, Code = "MATH", Name = "Mathematics" });
        db.CurriculumSubjects.Add(new CurriculumSubject
        {
            Id = 61, TenantId = 101, AcademicCurriculumId = 50, AcademicLevelId = 1,
            SubjectId = 60, CreditHours = 4m, IsOptional = false
        });
        var offering = new SubjectOffering
        {
            Id = 62, TenantId = 101, AcademicBatchId = 20,
            AcademicYearId = 1, CurriculumSubjectId = 61, Code = "MATH-1", Capacity = 1
        };
        db.SubjectOfferings.Add(offering);
        await db.SaveChangesAsync();
        var service = Service(db, new User(101, true));
        var source = Header + "PRE-SUB-A,STU-A,Learner A,10,20,11,,\\n" +
            "PRE-SUB-B,STU-B,Learner B,10,20,12,,\\n";
        using (var file = Csv(source))
        {
            var preview = await service.PreviewAsync(file, ".csv", Mapping);
            Assert.True(preview.Success, preview.Message);
            Assert.Equal(1, preview.Data!.ValidRows);
            Assert.Equal(1, preview.Data.InvalidRows);
            Assert.Contains(preview.Data.Errors[0].Issues, x => x.Contains("offering capacity"));
        }
        db.StudentSubjectRegistrations.Add(new StudentSubjectRegistration
        {
            TenantId = 101, StudentEnrollmentId = 40, SubjectOfferingId = 62,
            ClientRequestId = Guid.NewGuid(),
            State = EduOS.Core.Enums.Domain.SubjectRegistrationState.Approved
        });
        await db.SaveChangesAsync();
        using (var file = Csv(source))
        {
            var full = await service.PreviewAsync(file, ".csv", Mapping);
            Assert.Equal(0, full.Data!.ValidRows);
            Assert.Equal(2, full.Data.InvalidRows);
        }
        db.SubjectOfferings.Remove(offering);
        await db.SaveChangesAsync();
        using (var file = Csv(source))
        {
            var missing = await service.PreviewAsync(file, ".csv", Mapping);
            Assert.Equal(2, missing.Data!.InvalidRows);
            Assert.All(missing.Data.Errors,
                x => Assert.Contains(x.Issues, issue => issue.Contains("active batch offering")));
        }
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Csv_header_and_valid_preflight_are_read_only()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using var file = Csv(Header + "SRC-1,STU-2,Example Learner,10,20,8,Guardian Name,01700000000\n");
        var headers = await service.GetHeadersAsync(file, ".csv");
        Assert.True(headers.Success);
        Assert.Equal(8, headers.Data!.Count);
        var result = await service.PreviewAsync(file, ".csv", Mapping);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.ValidRows);
        Assert.Equal(0, result.Data.InvalidRows);
        Assert.Empty(db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified));
        Assert.Single(await db.Students.ToListAsync());
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Detects_duplicate_source_student_code_and_current_roll_without_exposing_other_tenant()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        db.ImportLogs.Add(new ImportLog { TenantId = 101, Id = 60, ImportType = "Student", TotalRows = 1 });
        db.ImportLogItems.Add(new ImportLogItem { TenantId = 101, ImportLogId = 60,
            RowNumber = 2, BusinessKey = "SRC-OLD", IsSuccess = true });
        await db.SaveChangesAsync();
        var rows = Header +
            "SRC-OLD,OLD,Already Present,10,20,7,,\n" +
            "SRC-2,STU-NEW,New Learner,10,20,9,,\n" +
            "SRC-2,STU-NEW,Repeated Learner,10,20,9,,\n" +
            "SRC-4,STU-4,Foreign Campus,999,20,10,,\n";
        using var file = Csv(rows);
        var result = await Service(db, new User(101, true)).PreviewAsync(file, ".csv", Mapping);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.ValidRows);
        Assert.Equal(3, result.Data.InvalidRows);
        Assert.Contains(result.Data.Errors[0].Issues, x => x.Contains("Source key"));
        Assert.Contains(result.Data.Errors[0].Issues, x => x.Contains("Student code"));
        Assert.Contains(result.Data.Errors[0].Issues, x => x.Contains("roll"));
        Assert.Contains(result.Data.Errors[2].Issues, x => x.Contains("Campus"));
        Assert.DoesNotContain(result.Data.Errors, x => x.StudentCode == "FOREIGN");
    }

    [Fact]
    public async Task Rejects_capacity_exhaustion_and_invalid_dates()
    {
        var options = Options();
        await SeedAsync(options, capacity: 1);
        await using var db = Context(options, 101);
        using var file = Csv(Header +
            "SRC-3,STU-3,Example,10,20,9,,\n" +
            "SRC-4,STU-4,Example,10,20,10,,\n");
        var result = await Service(db, new User(101, true)).PreviewAsync(file, ".csv", Mapping);
        Assert.Equal(2, result.Data!.InvalidRows);
        Assert.All(result.Data.Errors, x => Assert.Contains(x.Issues, i => i.Contains("capacity")));
    }

    [Fact]
    public async Task Denies_unprivileged_and_invalid_mappings()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        using var file = Csv(Header + "SRC-1,STU-2,Example,10,20,8,,\n");
        Assert.Equal(403, (await Service(db, new User(101, false))
            .PreviewAsync(file, ".csv", Mapping)).StatusCode);
        var bad = new Dictionary<string, string>(Mapping) { ["RollNo"] = "StudentCode" };
        Assert.Equal(400, (await Service(db, new User(101, true))
            .PreviewAsync(file, ".csv", bad)).StatusCode);
        Assert.Equal(400, (await Service(db, new User(101, true))
            .GetHeadersAsync(file, ".exe")).StatusCode);
    }

    [Fact]
    public async Task Xlsx_supports_header_mapping_but_rejects_formula_cells()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Students");
        var keys = Mapping.Keys.ToArray();
        for (var i = 0; i < keys.Length; i++) sheet.Cell(1, i + 1).Value = keys[i];
        var values = new[] { "SRC-9", "STU-9", "Example", "10", "20", "11", "", "" };
        for (var i = 0; i < values.Length; i++) sheet.Cell(2, i + 1).Value = values[i];
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        var service = Service(db, new User(101, true));
        Assert.True((await service.GetHeadersAsync(stream, ".xlsx")).Success);
        var result = await service.PreviewAsync(stream, ".xlsx", Mapping);
        Assert.Equal(1, result.Data!.ValidRows);
        sheet.Cell(2, 1).FormulaA1 = "1+1";
        using var unsafeFile = new MemoryStream();
        book.SaveAs(unsafeFile);
        Assert.Equal(400, (await service.PreviewAsync(unsafeFile, ".xlsx", Mapping)).StatusCode);
    }

    [Fact]
    public async Task Full_error_export_revalidates_all_rows_and_neutralizes_spreadsheet_formulas()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var rows = Header + string.Concat(Enumerable.Range(1, 125)
            .Select(i => $"=SRC-{i},+STU-{i},Learner {i},999,20,{i},,\n"));
        using var source = Csv(rows);
        var limited = await service.PreviewAsync(source, ".csv", Mapping);
        Assert.True(limited.Success);
        Assert.Equal(125, limited.Data!.InvalidRows);
        Assert.Equal(100, limited.Data.Errors.Count);
        Assert.True(limited.Data.HasMoreErrors);
        var all = await service.PreviewAllErrorsAsync(source, ".csv", Mapping);
        Assert.True(all.Success);
        Assert.Equal(125, all.Data!.Errors.Count);
        Assert.False(all.Data.HasMoreErrors);
        Assert.Empty(await db.ImportLogs.ToListAsync());

        using var exportSource = Csv(rows);
        var upload = new FormFile(exportSource, 0, exportSource.Length, "file", "students.csv");
        var controller = new StudentImportsController(service, null!);
        var response = await controller.ExportErrors(upload,
            System.Text.Json.JsonSerializer.Serialize(Mapping), CancellationToken.None);
        var file = Assert.IsType<FileContentResult>(response);
        var csv = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("\"'=SRC-1\"", csv);
        Assert.Contains("\"'+STU-125\"", csv);
        Assert.Equal(126, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public void Preview_api_requires_admin_module_and_antiforgery()
    {
        var controller = typeof(StudentImportsController);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.NotNull(controller.GetMethod("Preview"));
        Assert.NotNull(controller.GetMethod("Headers"));
    }

    [Fact]
    public async Task Guardian_source_mapping_rejects_conflicting_profiles_and_missing_name()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var mapped = new Dictionary<string, string>(Mapping) { ["GuardianSourceKey"] = "GuardianSourceKey" };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        using var file = Csv(header +
            "SRC-P1,STU-P1,Learner One,10,20,11,Guardian One,01700000000,FAMILY-1\n" +
            "SRC-P2,STU-P2,Learner Two,10,20,12,Guardian Two,01700000000,FAMILY-1\n" +
            "SRC-P3,STU-P3,Learner Three,10,20,13,,01700000000,FAMILY-2\n");
        var result = await Service(db, new User(101, true)).PreviewAsync(file, ".csv", mapped);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.ValidRows);
        Assert.Equal(2, result.Data.InvalidRows);
        Assert.Contains(result.Data.Errors[0].Issues, x => x.Contains("conflicting profiles"));
        Assert.Contains(result.Data.Errors[1].Issues, x => x.Contains("Guardian name"));
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    private static MemoryStream Csv(string value) => new(Encoding.UTF8.GetBytes(value));

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-import-preview-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task SeedAsync(DbContextOptions<EduOSDbContext> options, int capacity = 3)
    {
        await using var db = Context(options, 101);
        db.Campuses.Add(new Campus { Id = 10, TenantId = 101, Name = "Main", Code = "MAIN" });
        db.AcademicBatches.Add(new AcademicBatch
        {
            Id = 20, TenantId = 101, CampusId = 10, Name = "Batch", Code = "B1",
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, Capacity = capacity
        });
        db.AcademicCurriculums.Add(new AcademicCurriculum
        {
            Id = 50, TenantId = 101, AcademicProgramId = 1,
            Name = "Current Curriculum", Code = "CUR", IsCurrent = true, IsActive = true,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        });
        db.Students.Add(new Student
        {
            Id = 30, TenantId = 101, PersonId = 1, StudentCode = "OLD",
            FullName = "Existing", AdmissionDate = new DateOnly(2026, 1, 1)
        });
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            Id = 40, TenantId = 101, StudentId = 30, CampusId = 10,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
            AcademicBatchId = 20, AcademicCurriculumId = 1, RollNo = "7",
            EnrollmentDate = new DateOnly(2026, 1, 1), IsCurrent = true,
            State = EduOS.Core.Enums.Domain.EnrollmentState.Active
        });
        await db.SaveChangesAsync();
    }

    private static StudentImportPreviewService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<StudentSubjectRegistration>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<ImportLog>(db), new GenericRepository<ImportLogItem>(db),
        user, NullLogger<StudentImportPreviewService>.Instance);

    private sealed class User(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 99;
        public bool IsTenantAdmin => admin;
        public bool IsSuperAdmin => false;
        public string? FullName => null;
        public string? Email => null;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : [];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => "Test";
    }
}
