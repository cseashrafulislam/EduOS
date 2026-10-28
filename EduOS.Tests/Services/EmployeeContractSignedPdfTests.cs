using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class EmployeeContractSignedPdfTests
{
    [Fact]
    public async Task Attached_signed_contract_requires_approved_terms_scanned_pdf_and_separate_reviewer()
    {
        await using var db = Database();
        var contractRef = Guid.NewGuid();
        var fileRef = Guid.NewGuid();
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj <<>> endobj\n%%EOF");
        db.Employees.Add(new Employee { Id = 1, TenantId = 41, FullName = "Staff",
            EmployeeCode = "HR-01", DesignationId = 1 });
        db.EmployeeContracts.Add(new EmployeeContract { Id = 1, TenantId = 41,
            PublicId = contractRef, EmployeeId = 1, ContractCode = "EC-01",
            EffectiveFrom = new DateOnly(2026, 10, 1), State = EmployeeContractState.Approved,
            CreatedBy = 5, ReviewedByUserId = 6 });
        db.FileAssets.Add(new FileAsset { Id = 1, TenantId = 41, PublicId = fileRef,
            StorageProvider = "LocalPrivate", StorageKey = "tenant-41/documents/signed.pdf",
            OriginalFileName = "signed.pdf", ContentType = "application/pdf",
            SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            Visibility = FileVisibility.Private, IsVerifiedSafe = true,
            MalwareScanStateCode = "Clean", UploadedByUserId = 9 });
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync("tenant-41/documents/signed.pdf"))
            .ReturnsAsync(new FileDownloadResult { Content = bytes });
        var uploader = new EmployeeContractService(db, new User(9), TimeProvider.System, storage.Object);
        var attach = await uploader.AttachSignedPdfAsync(contractRef,
            new AttachSignedEmployeeContractPdfRequestDto { FileReference = fileRef });
        attach.StatusCode.Should().Be(200);
        attach.Data!.Status.Should().Be("Pending");
        (await uploader.DownloadSignedPdfAsync(contractRef, false)).StatusCode.Should().Be(404);
        (await uploader.DownloadSignedPdfAsync(contractRef, true)).StatusCode.Should().Be(403);
        var reviewer = new EmployeeContractService(db, new User(10), TimeProvider.System, storage.Object);
        var preview = await reviewer.DownloadSignedPdfAsync(contractRef, true);
        preview.StatusCode.Should().Be(200);
        preview.Data!.Content.Should().Equal(bytes);
        (await db.AuditLogs.CountAsync()).Should().Be(2);
        (await db.Documents.SingleAsync()).EntityType.Should().Be("EmployeeContract");
    }

    [Fact]
    public async Task Unscanned_or_foreign_tenant_pdf_cannot_be_attached()
    {
        await using var db = Database();
        var contractRef = Guid.NewGuid();
        var fileRef = Guid.NewGuid();
        db.EmployeeContracts.Add(new EmployeeContract { Id = 1, TenantId = 41,
            PublicId = contractRef, EmployeeId = 1, ContractCode = "EC-02",
            EffectiveFrom = new DateOnly(2026, 10, 1), State = EmployeeContractState.Approved });
        db.FileAssets.Add(new FileAsset { Id = 1, TenantId = 41, PublicId = fileRef,
            StorageProvider = "LocalPrivate", StorageKey = "tenant-42/documents/stolen.pdf",
            ContentType = "application/pdf", OriginalFileName = "stolen.pdf",
            SizeBytes = 15, Sha256 = new string('A', 64), Visibility = FileVisibility.Private,
            IsVerifiedSafe = false, MalwareScanStateCode = "Pending", UploadedByUserId = 9 });
        await db.SaveChangesAsync();
        var result = await new EmployeeContractService(db, new User(9), TimeProvider.System,
            new Mock<IFileUploadService>().Object).AttachSignedPdfAsync(contractRef,
            new AttachSignedEmployeeContractPdfRequestDto { FileReference = fileRef });
        result.StatusCode.Should().Be(409);
        (await db.Documents.CountAsync()).Should().Be(0);
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("contract-signed-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }
    private sealed class User(long id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public bool IsTenantAdmin => true;
        public long TenantId => 41;
        public long UserId => id;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
