using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

/// <summary>
/// In-app renewal payment reminders, using the canonical Notification and
/// CommunicationDelivery owners. No payment or external message is initiated.
/// </summary>
public sealed class SubscriptionRenewalReminderService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<SubscriptionRenewalReminderService> logger)
{
    public const string ReminderSource = "SubscriptionRenewalReminder";
    public const string PreferenceCode = "SubscriptionRenewalPaymentDue";

    public static string? AutomaticStage(int daysUntilDue) =>
        daysUntilDue switch
        {
            >= 4 and <= 7 => "due-7",
            >= 1 and <= 3 => "due-3",
            <= 0 and >= -2 => "due",
            <= -3 and >= -14 => "overdue",
            _ => null
        };

    private bool CanManage => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0;

    private async Task<(SubscriptionInvoice? Invoice, TenantSubscription? Period)> FindAsync(
        long tenantId, long invoiceId, CancellationToken ct)
    {
        var invoice = await db.SubscriptionInvoices.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == invoiceId && !x.IsDeleted, ct);
        if (invoice == null) return (null, null);
        var period = await db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == invoice.TenantSubscriptionId && !x.IsDeleted &&
            x.PreviousSubscriptionId.HasValue, ct);
        return (invoice, period);
    }

    private async Task<DateOnly> LocalDateAsync(long tenantId, DateTime now, CancellationToken ct)
    {
        var zoneId = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId && !x.IsDeleted)
            .Select(x => x.TimeZoneId).FirstOrDefaultAsync(ct);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId ?? "UTC");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(now, DateTimeKind.Utc), zone));
        }
        catch (TimeZoneNotFoundException) { return DateOnly.FromDateTime(now); }
        catch (InvalidTimeZoneException) { return DateOnly.FromDateTime(now); }
    }

    private async Task<long[]> OwnersAsync(long tenantId, CancellationToken ct) =>
        await db.TenantMemberships.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsOwner &&
                x.Status == MembershipStatus.Active && !x.IsDeleted &&
                db.ApplicationUsers.Any(u => u.Id == x.UserId && u.IsActive && !u.IsDeleted))
            .Select(x => x.UserId).Distinct().OrderBy(x => x).Take(20).ToArrayAsync(ct);

    private static Guid ReminderKey(long tenantId, long invoiceId, long recipientId, string stage)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"eduos:renewal:inapp:v1:{tenantId}:{invoiceId}:{recipientId}:{stage}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public async Task<ApiResponse<SubscriptionRenewalReminderDto>> GetStatusAsync(
        long invoiceId, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Tenant administrator required.", 403);
        var tenantId = user.TenantId;
        var (invoice, period) = await FindAsync(tenantId, invoiceId, ct);
        if (invoice == null || period == null)
            return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Renewal invoice not found.", 404);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = await LocalDateAsync(tenantId, now, ct);
        var last = await db.CommunicationDeliveries.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceType == ReminderSource &&
                x.SourceId == invoiceId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt).Select(x => (DateTime?)x.CreatedAt).FirstOrDefaultAsync(ct);
        var owners = await OwnersAsync(tenantId, ct);
        var manualStage = "manual-" + today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sentToday = owners.Length > 0 && await db.CommunicationDeliveries.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.SourceType == ReminderSource &&
                x.SourceId == invoiceId && x.IdempotencyKey ==
                ReminderKey(tenantId, invoiceId, owners[0], manualStage) && !x.IsDeleted, ct);
        return ApiResponse<SubscriptionRenewalReminderDto>.SuccessResponse(new()
        {
            InvoiceId = invoice.Id, DueDate = invoice.DueDate, DueAmount = invoice.DueAmount,
            CurrencyCode = invoice.CurrencyCode, LastReminderAt = last,
            HasActiveOwner = owners.Length > 0,
            CanSendToday = owners.Length > 0 && !sentToday && invoice.DueAmount > 0 &&
                invoice.State is InvoiceState.Issued or InvoiceState.PartiallyPaid &&
                period.State == SubscriptionState.PendingPayment
        });
    }

    public async Task<ApiResponse<SubscriptionRenewalReminderDto>> SendManualAsync(
        long invoiceId, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Tenant administrator required.", 403);
        var today = await LocalDateAsync(user.TenantId, clock.GetUtcNow().UtcDateTime, ct);
        return await SendAsync(user.TenantId, invoiceId,
            "manual-" + today.ToString("yyyyMMdd", CultureInfo.InvariantCulture), false, ct);
    }

    /// <summary>Host-only call; the job establishes an explicit tenant scope.</summary>
    public async Task<ApiResponse<SubscriptionRenewalReminderDto>> SendAutomaticAsync(
        long tenantId, long invoiceId, CancellationToken ct = default)
    {
        if (tenantId <= 0 || invoiceId <= 0 || (user.IsAuthenticated && !user.IsSuperAdmin))
            return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("System tenant scope required.", 403);
        var (invoice, period) = await FindAsync(tenantId, invoiceId, ct);
        if (invoice == null || period == null)
            return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Renewal invoice not found.", 404);
        var today = await LocalDateAsync(tenantId, clock.GetUtcNow().UtcDateTime, ct);
        var stage = AutomaticStage(invoice.DueDate.DayNumber - today.DayNumber);
        if (stage == null)
            return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Outside reminder window.", 409);
        return await SendAsync(tenantId, invoiceId, stage, true, ct);
    }

    private async Task<ApiResponse<SubscriptionRenewalReminderDto>> SendAsync(
        long tenantId, long invoiceId, string stage, bool automatic, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var (invoice, period) = await FindAsync(tenantId, invoiceId, ct);
            if (invoice == null || period == null)
                return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Renewal invoice not found.", 404);
            if (invoice.DueAmount <= 0 || invoice.State is not (InvoiceState.Issued or InvoiceState.PartiallyPaid) ||
                period.State != SubscriptionState.PendingPayment)
                return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Renewal invoice is no longer payable.", 409);
            var owners = await OwnersAsync(tenantId, ct);
            if (owners.Length == 0)
                return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("No active institution owner can receive reminders.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var title = "Subscription renewal payment pending";
            var body = $"Invoice {invoice.InvoiceNumber}: {invoice.DueAmount:0.00} {invoice.CurrencyCode} outstanding. Due {invoice.DueDate:yyyy-MM-dd}. Verify payment through Subscription & Billing.";
            var created = 0;
            foreach (var owner in owners)
            {
                var key = ReminderKey(tenantId, invoiceId, owner, stage);
                if (await db.CommunicationDeliveries.AnyAsync(x =>
                    x.TenantId == tenantId && x.IdempotencyKey == key && !x.IsDeleted, ct))
                    continue;
                // Respect the existing in-app preference when explicitly disabled.
                if (await db.NotificationPreferences.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenantId && x.UserId == owner &&
                    x.Channel == NotificationChannelType.InApp &&
                    x.EventCode == PreferenceCode && !x.IsEnabled && !x.IsDeleted, ct))
                    continue;
                db.CommunicationDeliveries.Add(new CommunicationDelivery
                {
                    TenantId = tenantId, IdempotencyKey = key,
                    Channel = NotificationChannelType.InApp, Destination = owner.ToString(CultureInfo.InvariantCulture),
                    Subject = title, Body = body, State = DeliveryState.Delivered,
                    UserId = owner, SourceType = ReminderSource, SourceId = invoiceId,
                    SentAt = now, DeliveredAt = now, CreatedAt = now
                });
                db.Notifications.Add(new Notification
                {
                    TenantId = tenantId, UserId = owner, Title = title, Body = body,
                    TypeCode = PreferenceCode, SourceType = ReminderSource,
                    SourceId = invoiceId, ActionUrl = "/SubscriptionBilling", CreatedAt = now
                });
                created++;
            }
            if (created > 0) await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("Renewal reminder {Stage} tenant {TenantId} invoice {InvoiceId} recipients {Count}",
                stage, tenantId, invoiceId, created);
            return ApiResponse<SubscriptionRenewalReminderDto>.SuccessResponse(new()
            {
                InvoiceId = invoiceId, DueDate = invoice.DueDate, DueAmount = invoice.DueAmount,
                CurrencyCode = invoice.CurrencyCode, LastReminderAt = created > 0 ? now : null,
                HasActiveOwner = true, CanSendToday = false
            }, created > 0 ? "In-app reminder delivered to institution owner(s)." : "Reminder already delivered or opted out.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent renewal reminder tenant {TenantId} invoice {InvoiceId}", tenantId, invoiceId);
            return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Reminder already being processed. Refresh status.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Renewal reminder failed tenant {TenantId} invoice {InvoiceId}", tenantId, invoiceId);
            return ApiResponse<SubscriptionRenewalReminderDto>.ErrorResponse("Unable to create reminder.", 500);
        }
    }
}
