using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentFileTenantBoundaryTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(99L)]
    public async Task Student_admin_documents_and_photos_reject_swapped_tenant_context(long contextTenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = contextTenant;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-file-admin-scope-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var admin = new User("TenantAdmin");
        var documents = new StudentDocumentService(db, admin, null!,
            NullLogger<StudentDocumentService>.Instance, null!);
        var photos = new StudentPhotoService(db, admin, null!, null!,
            NullLogger<StudentPhotoService>.Instance);

        Assert.Equal(403, (await documents.GetTypesAsync()).StatusCode);
        Assert.Equal(403, (await documents.GetAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await documents.DownloadAsync(Guid.NewGuid(), Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await photos.DownloadAsync(Guid.NewGuid(), Guid.NewGuid())).StatusCode);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Guardian")]
    public async Task Student_portal_private_files_reject_swapped_tenant_context(string role)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 99L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-file-portal-scope-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var account = new User(role);
        var documents = new StudentDocumentService(db, account, null!,
            NullLogger<StudentDocumentService>.Instance, null!);
        var photos = new StudentPhotoService(db, account, null!, null!,
            NullLogger<StudentPhotoService>.Instance);

        Assert.Equal(403, (await documents.GetPortalAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await documents.DownloadPortalAsync(Guid.NewGuid(), Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await photos.GetPortalAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await photos.DownloadPortalAsync(Guid.NewGuid(), Guid.NewGuid())).StatusCode);
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => 42;
        public long UserId => 77;
        public string? FullName => "Tenant boundary fixture";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
