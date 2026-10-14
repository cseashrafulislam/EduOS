using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DocumentVerificationSecurityTests
{
    private static EduOSDbContext Context() => new(new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase("document-review-" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public void ReviewEndpointsRequireTenantAdminAndAntiforgery()
    {
        foreach (var type in new[] { typeof(DocumentVerificationController), typeof(DocumentVerificationApiController) })
            Assert.Equal("TenantAdmin", Assert.IsType<AuthorizeAttribute>(
                type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Single()).Roles);
        Assert.NotEmpty(typeof(DocumentVerificationApiController)
            .GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public async Task NonAdminCannotReadOrReviewProtectedDocuments()
    {
        await using var db = Context();
        var service = new DocumentVerificationService(db, new User(false), null!,
            NullLogger<DocumentVerificationService>.Instance);
        Assert.Equal(403, (await service.GetReviewQueueAsync("Pending", 1, 20)).StatusCode);
        Assert.Equal(403, (await service.ReviewAsync(Guid.NewGuid(),
            new DocumentVerificationDecisionDto { Approve = true, RowVersion = "AQ==" })).StatusCode);
        Assert.Equal(403, (await service.DownloadForReviewAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await service.DownloadApprovedAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task RejectionRequiresReasonAndReviewRequiresValidVersion()
    {
        await using var db = Context();
        var service = new DocumentVerificationService(db, new User(true), null!,
            NullLogger<DocumentVerificationService>.Instance);
        Assert.Equal(400, (await service.ReviewAsync(Guid.NewGuid(),
            new DocumentVerificationDecisionDto { Approve = false, RowVersion = "AQ==" })).StatusCode);
        Assert.Equal(400, (await service.ReviewAsync(Guid.NewGuid(),
            new DocumentVerificationDecisionDto { Approve = true, RowVersion = "" })).StatusCode);
    }

    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => 42;
        public long UserId => 77;
        public string? FullName => "Reviewer";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Teacher"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}