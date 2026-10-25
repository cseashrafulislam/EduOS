using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduOS.BackgroundJobs.Jobs;

/// <summary>Tenant-isolated in-app reminders for unpaid renewal invoices. No outbound email or payment action.</summary>
public sealed class SubscriptionRenewalReminderJob(
    IServiceScopeFactory scopes, ILogger<SubscriptionRenewalReminderJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var listingScope = scopes.CreateScope();
        var listingDb = listingScope.ServiceProvider.GetRequiredService<EduOSDbContext>();
        long cursor = 0;
        var failures = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await (from i in listingDb.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
                join s in listingDb.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
                    on new { i.TenantId, Id = i.TenantSubscriptionId } equals new { s.TenantId, s.Id }
                where i.Id > cursor && i.TenantId > 0 && !i.IsDeleted && !s.IsDeleted &&
                    s.PreviousSubscriptionId.HasValue && s.State == SubscriptionState.PendingPayment &&
                    i.DueAmount > 0 && (i.State == InvoiceState.Issued || i.State == InvoiceState.PartiallyPaid)
                orderby i.Id
                select new { i.Id, i.TenantId }).Take(100).ToListAsync(ct);
            if (batch.Count == 0) break;
            cursor = batch[^1].Id;
            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();
                    using var tenantScope = db.BeginSystemTenantScope(item.TenantId);
                    var service = scope.ServiceProvider.GetRequiredService<SubscriptionRenewalReminderService>();
                    var response = await service.SendAutomaticAsync(item.TenantId, item.Id, ct);
                    if (!response.Success && response.StatusCode >= 500)
                        throw new InvalidOperationException($"Reminder failed: {response.StatusCode}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    logger.LogError(ex, "Renewal reminder failed for tenant {TenantId} invoice {InvoiceId}",
                        item.TenantId, item.Id);
                }
            }
        }
        if (failures > 0)
            throw new InvalidOperationException($"Renewal reminders failed for {failures} invoice(s).");
    }
}
