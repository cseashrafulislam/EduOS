using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Learners;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPersonAddressWorkflowTests
{
    [Fact]
    public async Task Tenant_admin_can_create_read_and_replay_without_duplicate_audit()
    {
        await using var db = CreateDb(101);
        var student = await SeedAsync(db, 101);
        var service = CreateService(db, new CurrentUser(101));
        var request = Request();

        (await service.SaveAsync(student.PublicId, "Current", request)).Success.Should().BeTrue();
        (await service.SaveAsync(student.PublicId, "Current", request)).Success.Should().BeTrue();
        var read = await service.GetAsync(student.PublicId);
        read.Data.Should().ContainSingle(x => x.AddressTypeCode == "Current" && x.City == "Dhaka");
        (await db.PersonAddresses.CountAsync()).Should().Be(1);
        (await db.LearnerIdentityAccessLogs.CountAsync(x => x.Action == "PersonAddressSaved")).Should().Be(1);
    }

    [Fact]
    public async Task Reused_request_id_with_changed_data_conflicts()
    {
        await using var db = CreateDb(101);
        var student = await SeedAsync(db, 101);
        var service = CreateService(db, new CurrentUser(101));
        var request = Request();
        (await service.SaveAsync(student.PublicId, "Current", request)).Success.Should().BeTrue();
        request.City = "Changed";
        (await service.SaveAsync(student.PublicId, "Current", request)).StatusCode.Should().Be(409);
        (await db.PersonAddresses.SingleAsync()).City.Should().Be("Dhaka");
    }

    [Fact]
    public async Task Invalid_type_and_unauthorized_role_fail_without_writes()
    {
        await using var db = CreateDb(101);
        var student = await SeedAsync(db, 101);
        var service = CreateService(db, new CurrentUser(101));
        (await service.SaveAsync(student.PublicId, "Untrusted", Request())).StatusCode.Should().Be(400);
        var denied = CreateService(db, new CurrentUser(101, false));
        (await denied.SaveAsync(student.PublicId, "Current", Request())).StatusCode.Should().Be(403);
        (await db.PersonAddresses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Another_tenants_student_is_not_disclosed()
    {
        await using var db = CreateDb(202);
        var student = await SeedAsync(db, 202);
        var service = CreateService(db, new CurrentUser(101));
        (await service.GetAsync(student.PublicId)).StatusCode.Should().Be(404);
        (await service.SaveAsync(student.PublicId, "Current", Request())).StatusCode.Should().Be(404);
        (await db.PersonAddresses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Shared_global_person_is_not_editable_even_from_a_valid_local_student()
    {
        await using var db = CreateDb(101);
        var student = await SeedAsync(db, 101);
        db.StudentPersonLinks.Add(new StudentPersonLink
        {
            TenantId = 202, StudentId = student.Id, PersonId = student.PersonId,
            LinkedAt = DateTime.UtcNow, IsPrimary = true
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, new CurrentUser(101));
        (await service.GetAsync(student.PublicId)).StatusCode.Should().Be(404);
        (await service.SaveAsync(student.PublicId, "Current", Request())).StatusCode.Should().Be(404);
    }

    [Fact]
    public void Address_api_has_module_role_and_antiforgery_boundaries()
    {
        var type = typeof(StudentPersonAddressesController);
        type.GetCustomAttributes(true).Select(x => x.GetType().Name)
            .Should().Contain("AutoValidateAntiforgeryTokenAttribute");
        var source = Read("EduOS.App", "Controllers", "Api", "StudentPersonAddressesController.cs");
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        source.Should().Contain("[RequireModule(\"STUDENT\")]");
        var view = Read("EduOS.App", "Views", "LearnerIdentities", "Index.cshtml");
        view.Should().Contain("identityAddressForm");
        view.Should().Contain("identity-addresses.js");
    }

    private static SaveStudentPersonAddressRequestDto Request() => new()
    {
        ClientRequestId = Guid.NewGuid(), AddressLine1 = "House 12, Road 4",
        City = "Dhaka", CountryCode = "BD"
    };

    private static EduOSDbContext CreateDb(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("person-address-" + Guid.NewGuid().ToString("N")).Options;
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        context.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }

    private static async Task<Student> SeedAsync(EduOSDbContext db, long tenant)
    {
        var person = new Person { FullName = "Student Person" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        var student = new Student
        {
            TenantId = tenant, PersonId = person.Id,
            StudentCode = "S-" + Guid.NewGuid().ToString("N")[..10],
            FullName = "Student Person", AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StatusCode = "Active"
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        db.StudentPersonLinks.Add(new StudentPersonLink
        {
            TenantId = tenant, StudentId = student.Id, PersonId = person.Id,
            IsPrimary = true, LinkedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return student;
    }

    private static StudentPersonAddressService CreateService(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Student>(db),
        new GenericRepository<Guardian>(db),
        new GenericRepository<AdmissionApplicant>(db),
        new GenericRepository<Person>(db),
        new GenericRepository<StudentPersonLink>(db),
        new GenericRepository<PersonAddress>(db),
        new GenericRepository<LearnerConsentRequest>(db),
        new GenericRepository<LearnerDataGrant>(db),
        new GenericRepository<LearnerIdentityAccessLog>(db),
        db, user, NullLogger<StudentPersonAddressService>.Instance);

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    private sealed class CurrentUser(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Test Admin";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Teacher"];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
