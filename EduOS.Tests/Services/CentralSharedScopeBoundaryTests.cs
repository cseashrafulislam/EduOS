using EduOS.Core.DTOs.System;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services;
using EduOS.Service.Services.Communication;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CentralSharedScopeBoundaryTests
{
    [Fact]
    public async Task Privileged_read_preserves_canonical_owner_and_legacy_single_membership_roles()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 11L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("central-role-compatibility-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = context });
        CentralPrivilegedTestIdentity.Seed(db, 11, 10);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(10);
        user.SetupGet(x => x.TenantId).Returns(11);
        user.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(x => x.GetQueryable()).Returns(db.AuditLogs);
        var audit = new AuditLogService(repository.Object, user.Object, db,
            NullLogger<AuditLogService>.Instance);
        var role = await db.Roles.IgnoreQueryFilters().SingleAsync(x => x.Id == 9001);
        var member = db.TenantMemberships.Single(x => x.UserId == 10);

        member.IsOwner = true;
        role.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(200, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
        member.IsOwner = false;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);

        db.Roles.Add(new ApplicationRole
        {
            Id = 9003, Name = "TenantAdmin", NormalizedName = "TENANTADMIN",
            TenantId = null, IsSystemRole = false, IsActive = true
        });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 10, RoleId = 9003 });
        await db.SaveChangesAsync();
        Assert.Equal(200, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
        var second = new TenantMembership
        {
            Id = 9002, UserId = 10, TenantId = 22, Status = MembershipStatus.Suspended
        };
        using (db.BeginSystemTenantScope(22))
        {
            db.TenantMemberships.Add(second);
            await db.SaveChangesAsync();
        }
        Assert.Equal(403, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
        using (db.BeginSystemTenantScope(22))
        {
            second.Status = MembershipStatus.Left;
            await db.SaveChangesAsync();
        }
        Assert.Equal(200, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
    }

    [Fact]
    public async Task Shared_audit_integration_communication_messages_and_files_fail_closed_on_tenant_switch()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 22L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("central-scope-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = context });
        context.Items["TenantId"] = 11L;
        CentralPrivilegedTestIdentity.Seed(db, 11, 10);
        context.Items["TenantId"] = 22L;
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(10);
        user.SetupGet(x => x.TenantId).Returns(11);
        user.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var auditRepo = new Mock<IAuditLogRepository>();
        auditRepo.Setup(x => x.GetQueryable()).Returns(db.AuditLogs);
        var audit = new AuditLogService(auditRepo.Object, user.Object, db,
            NullLogger<AuditLogService>.Instance);
        var integration = new IntegrationAdministrationService(db, user.Object, null!, null!,
            NullLogger<IntegrationAdministrationService>.Instance);
        var communication = new CommunicationService(db, user.Object, null!, null!, db, null!,
            NullLogger<CommunicationService>.Instance);
        var messages = new MessageWorkspaceService(db, user.Object, db);
        var attachments = new MessageAttachmentService(db, user.Object, null!, db,
            NullLogger<MessageAttachmentService>.Instance);
        var administration = new CommunicationAdministrationService(
            new GenericRepository<CommunicationGateway>(db), new GenericRepository<MessageTemplate>(db),
            new GenericRepository<NoticeCategory>(db), new GenericRepository<CommunicationDelivery>(db),
            db, user.Object, new EphemeralDataProtectionProvider(), db,
            NullLogger<CommunicationAdministrationService>.Instance);

        Assert.Equal(403, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
        Assert.Equal(403, (await audit.GetStatisticsAsync(new AuditLogFilterDto())).StatusCode);
        Assert.Equal(403, (await audit.ExportCsvAsync(new AuditLogFilterDto())).StatusCode);
        Assert.Equal(403, (await integration.GetWebhookEndpointsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await integration.GetApiCredentialsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await integration.SaveWebhookEndpointAsync(null, new())).StatusCode);
        Assert.Equal(403, (await integration.CreateApiCredentialAsync(new())).StatusCode);
        Assert.Equal(403, (await communication.GetNoticesAsync(1, 20, null)).StatusCode);
        Assert.Equal(403, (await messages.GetMyThreadsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await attachments.GetForMessageAsync(999)).StatusCode);
        Assert.Equal(403, (await administration.GetNoticeCategoriesAsync()).StatusCode);
        Assert.Equal(403, (await administration.SaveNoticeCategoryAsync(null,
            new SaveNoticeCategoryRequestDto { Code = "SCOPE", Name = "Must not persist" })).StatusCode);
        Assert.False(db.ChangeTracker.HasChanges());

        // Scope is dynamic: valid same-institution calls must keep working after a fresh selection.
        context.Items["TenantId"] = 11L;
        Assert.Equal(200, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
        Assert.Equal(200, (await integration.GetWebhookEndpointsAsync(1, 20)).StatusCode);
        Assert.Equal(200, (await communication.GetNoticesAsync(1, 20, null)).StatusCode);
        Assert.Equal(200, (await messages.GetMyThreadsAsync(1, 20)).StatusCode);
        Assert.Equal(200, (await administration.GetNoticeCategoriesAsync()).StatusCode);

        // Existing role claims are not authority after an administrator is revoked.
        var role = await db.Roles.IgnoreQueryFilters().SingleAsync(x => x.Id == 9001);
        role.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await audit.ExportCsvAsync(new AuditLogFilterDto())).StatusCode);
        Assert.Equal(403, (await integration.GetApiCredentialsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await administration.GetGatewaysAsync()).StatusCode);
        role.IsActive = true;
        db.TenantMemberships.Single(x => x.UserId == 10).Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await audit.SearchAsync(new AuditLogFilterDto())).StatusCode);
        Assert.Equal(403, (await integration.GetWebhookEndpointsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await administration.GetNoticeCategoriesAsync()).StatusCode);
    }
}
