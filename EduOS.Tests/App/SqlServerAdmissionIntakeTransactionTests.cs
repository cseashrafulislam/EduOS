using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Admission;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerAdmissionIntakeTransactionTests
{
    [Fact]
    public async Task Admission_draft_create_and_rejected_update_use_retry_safe_atomic_sql_transaction()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        // Intentionally match production EnableRetryOnFailure to catch accidental
        // user-created TransactionScope failures that InMemory cannot reproduce.
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection, sql => sql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, yearId, campusId, levelId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-INTAKE-" + suffix, Name = "Admission CI",
                Email = "intake-" + suffix + "@example.test", State = TenantState.Active
            };
            setup.Tenants.Add(tenant);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var year = new AcademicYear
                {
                    TenantId = tenantId, Code = "YR" + suffix, Name = "Admission Year",
                    StartDate = new DateOnly(2026, 1, 1),
                    EndDate = new DateOnly(2027, 12, 31), IsActive = true
                };
                var campus = new Campus
                {
                    TenantId = tenantId, Code = "C" + suffix,
                    Name = "Admission Test Campus", IsActive = true
                };
                var program = new AcademicProgram
                {
                    TenantId = tenantId, Code = "P" + suffix,
                    Name = "School Program", IsActive = true
                };
                setup.AddRange(year, campus, program);
                await setup.SaveChangesAsync();
                var level = new AcademicLevel
                {
                    TenantId = tenantId, Code = "L" + suffix, Name = "Class One",
                    AcademicProgramId = program.Id, LevelNo = 1, IsActive = true
                };
                setup.AcademicLevels.Add(level);
                await setup.SaveChangesAsync();
                yearId = year.Id; campusId = campus.Id; levelId = level.Id;
            }
        }
        var access = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        access.HttpContext.Items["TenantId"] = tenantId;
        await using var db = new EduOSDbContext(options, access);
        var sut = Service(db, new AdmissionAdmin(tenantId));
        var request = new CreateAdmissionIntakeFormDto
        {
            ClientRequestId = Guid.NewGuid(), Code = "ADM_" + suffix,
            Title = "Class One Admission", AcademicYearId = yearId,
            CampusId = campusId, AcademicLevelId = levelId,
            OpensAtUtc = DateTime.UtcNow.AddHours(1),
            ClosesAtUtc = DateTime.UtcNow.AddDays(25),
            Currency = "BDT", ApplicationFee = 0m,
            Fields = [new AdmissionFormFieldDto
            {
                FieldKey = "guardian_relation", Label = "Guardian Relation",
                DataType = CustomFieldDataType.Text, DisplayOrder = 0, IsActive = true
            }]
        };
        var created = await sut.CreateFormAsync(request);
        Assert.True(created.Success, created.Message);
        Assert.Equal(201, created.StatusCode);
        Assert.Single(created.Data!.Fields);
        var duplicate = await sut.CreateFormAsync(request);
        Assert.Equal(409, duplicate.StatusCode);

        // Trigger a rejection AFTER the update delegate has mutated tracked form/
        // field state. UoW must roll it back, rather than committing a 409 result.
        db.ChangeTracker.Clear();
        var rejected = await sut.UpdateFormAsync(created.Data.Id, new UpdateAdmissionIntakeFormDto
        {
            RowVersion = created.Data.RowVersion, Code = request.Code,
            Title = "This must never persist",
            AcademicYearId = yearId, CampusId = campusId,
            AcademicLevelId = levelId, OpensAtUtc = request.OpensAtUtc,
            ClosesAtUtc = request.ClosesAtUtc, Currency = "BDT",
            Fields = [new AdmissionFormFieldDto
            {
                Id = 999999999, FieldKey = "new_field_with_foreign_id",
                Label = "Reject this field", DataType = CustomFieldDataType.Text,
                IsActive = true
            }]
        });
        Assert.Equal(409, rejected.StatusCode);
        await using var verify = new EduOSDbContext(options, access);
        var persisted = await verify.AdmissionIntakeForms.AsNoTracking()
            .SingleAsync(x => x.TenantId == tenantId && x.Id == created.Data.Id);
        var persistedFields = await verify.AdmissionFormFields.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AdmissionIntakeFormId == persisted.Id)
            .ToListAsync();
        Assert.Equal("Class One Admission", persisted.Title);
        Assert.Single(persistedFields);
        Assert.True(persistedFields[0].IsActive);
        Assert.Equal("guardian_relation", persistedFields[0].FieldKey);
    }

    private static AdmissionIntakeService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<AdmissionIntakeForm>(db),
        new GenericRepository<AdmissionFormField>(db),
        new GenericRepository<AdmissionApplicant>(db),
        new GenericRepository<AdmissionApplicantDocument>(db),
        new GenericRepository<FileAsset>(db),
        new GenericRepository<DocumentTypeDefinition>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<AcademicLevel>(db),
        db, user, new Mock<IFileUploadService>().Object,
        TimeProvider.System, NullLogger<AdmissionIntakeService>.Instance);

    private sealed class AdmissionAdmin(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenant;
        public string? FullName => "Admission SQL CI";
        public string? Email => "admission@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL CI";
    }
}
