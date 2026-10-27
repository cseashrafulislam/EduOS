using EduOS.Core.Common;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Net.Mail;

namespace EduOS.Service.Services.Tenants;

public sealed partial class InstitutionRegistrationService
{
    private const string VerificationRecoveryAction = "InstitutionVerificationEmailRequested";
    private const string VerificationRecoveryMessage =
        "If an eligible institution registration is awaiting verification, an email will be sent.";

    public async Task<ApiResponse<bool>> ResendVerificationEmailAsync(string? email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 200 ||
            !MailAddress.TryCreate(email.Trim(), out var parsed) ||
            !string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return Error<bool>("Enter a valid email address.", 400);

        // Never reveal whether an account, owner membership or pending institution exists.
        var generic = ApiResponse<bool>.SuccessResponse(true, VerificationRecoveryMessage);
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(_portalDomain) ||
            Uri.CheckHostName(_portalDomain.Trim()) != UriHostNameType.Dns)
            return Error<bool>("Verification email service is temporarily unavailable.", 503);

        var account = await _users.FindByEmailAsync(normalized);
        if (account == null || account.IsDeleted || !account.IsActive || account.EmailConfirmed)
            return generic;

        var tenantIds = await (from membership in _memberships.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            join tenant in _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on membership.TenantId equals tenant.Id
            where membership.UserId == account.Id && membership.IsOwner &&
                membership.Status == MembershipStatus.Active && !membership.IsDeleted &&
                tenant.Email == normalized && tenant.State == TenantState.PendingVerification &&
                tenant.EmailVerifiedAt == null && !tenant.IsDeleted
            select tenant.Id).Take(2).ToListAsync(cancellationToken);
        if (tenantIds.Count != 1) return generic;

        var tenantId = tenantIds[0];
        bool shouldDispatch;
        try
        {
            shouldDispatch = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                // Revalidate inside the transaction: registration may be verified or
                // ownership revoked between the anonymous lookup and the resend.
                var tenant = await _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == tenantId && !x.IsDeleted &&
                        x.Email == normalized && x.State == TenantState.PendingVerification &&
                        x.EmailVerifiedAt == null, token);
                if (tenant == null) return false;
                if (!await _memberships.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenantId && x.UserId == account.Id &&
                        x.IsOwner && !x.IsDeleted && x.Status == MembershipStatus.Active, token))
                    return false;
                if (!await _users.Users.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.Id == account.Id && !x.IsDeleted &&
                        x.IsActive && !x.EmailConfirmed && x.Email == normalized, token))
                    return false;

                // Persisted tenant-level cooldown works across application instances.
                // Serializable predicate locks prevent simultaneous requests issuing
                // multiple messages for the same institution.
                if (await _auditLogs.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenantId && x.EntityId == tenantId &&
                        x.Action == VerificationRecoveryAction && !x.IsDeleted &&
                        x.OccurredAt >= now.AddMinutes(-15), token))
                    return false;

                await _auditLogs.AddAsync(new AuditLog
                {
                    TenantId = tenantId,
                    Action = VerificationRecoveryAction,
                    EntityName = nameof(Tenant),
                    EntityId = tenantId,
                    OccurredAt = now,
                    IsSuccess = true,
                    NewValue = "Requested"
                });
                await _uow.SaveChangesAsync(token);
                return true;
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anonymous callers receive the same generic response on contention.
            _logger.LogWarning(ex, "Verification recovery persistence conflict for tenant {TenantId}", tenantId);
            return generic;
        }

        if (!shouldDispatch) return generic;
        try
        {
            var token = await _users.GenerateEmailConfirmationTokenAsync(account);
            var link = "https://" + _portalDomain.Trim().ToLowerInvariant() +
                "/api/institution-onboarding/verify-email?email=" +
                Uri.EscapeDataString(normalized) + "&token=" + Uri.EscapeDataString(token);
            var institutionName = await _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == tenantId).Select(x => x.Name).FirstAsync(cancellationToken);
            _jobs.Enqueue<IEmailJob>(job => job.SendVerificationEmailAsync(
                normalized, institutionName, account.FullName ?? institutionName, link));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cooldown is durable; never disclose account existence or tokens.
            _logger.LogError(ex, "Verification recovery dispatch failed for tenant {TenantId}", tenantId);
        }
        return generic;
    }
}
