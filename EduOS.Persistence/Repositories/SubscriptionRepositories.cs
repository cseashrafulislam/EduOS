using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories.SaaS;

public class SubscriptionPlanRepository : GenericRepository<SubscriptionPlan>, ISubscriptionPlanRepository
{
    public SubscriptionPlanRepository(EduOSDbContext context) : base(context) { }

    public Task<List<SubscriptionPlan>> GetActivePublicPlansAsync(CancellationToken ct = default) =>
        _context.SubscriptionPlans.AsNoTracking()
            .Where(x => x.IsActive && x.IsPublic)
            .OrderBy(x => x.MonthlyPrice).ThenBy(x => x.Name)
            .ToListAsync(ct);

    public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        _context.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code, ct);

    public Task<SubscriptionPlan?> GetWithFeaturesAsync(long id, CancellationToken ct = default) =>
        _context.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<SubscriptionPlan?> GetTrialPlanAsync(CancellationToken ct = default) =>
        _context.SubscriptionPlans.AsNoTracking()
            .Where(x => x.IsActive && x.IsPublic && x.TrialDays > 0)
            .OrderBy(x => x.MonthlyPrice)
            .FirstOrDefaultAsync(ct);
}

public class TenantSubscriptionRepository : GenericRepository<TenantSubscription>, ITenantSubscriptionRepository
{
    public TenantSubscriptionRepository(EduOSDbContext context) : base(context) { }

    public Task<TenantSubscription?> GetActiveByTenantAsync(long tenantId, CancellationToken ct = default) =>
        _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.TenantId == tenantId
                && (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial || x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt)
            .FirstOrDefaultAsync(ct);

    public Task<TenantSubscription?> GetByIdForSystemAsync(long id, long tenantId, CancellationToken ct = default) =>
        _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.TenantId == tenantId, ct);

    public Task<List<TenantSubscription>> GetHistoryByTenantAsync(long tenantId, CancellationToken ct = default) =>
        _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.TenantId == tenantId)
            .OrderByDescending(x => x.StartsAt)
            .ToListAsync(ct);

    public Task<List<TenantSubscription>> GetExpiringSoonAsync(int daysAhead, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(Math.Clamp(daysAhead, 1, 365));
        return _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted
                && (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial || x.State == SubscriptionState.Grace)
                && x.EndsAt > now && x.EndsAt <= cutoff)
            .OrderBy(x => x.EndsAt)
            .ToListAsync(ct);
    }

    public Task<List<TenantSubscription>> GetExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted
                && (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial || x.State == SubscriptionState.Grace)
                && x.EndsAt < now)
            .OrderBy(x => x.EndsAt)
            .ToListAsync(ct);
    }
}

public class SubscriptionInvoiceRepository : GenericRepository<SubscriptionInvoice>, ISubscriptionInvoiceRepository
{
    public SubscriptionInvoiceRepository(EduOSDbContext context) : base(context) { }

    public override Task<SubscriptionInvoice?> GetByIdAsync(long id) =>
        _context.SubscriptionInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

    public Task<SubscriptionInvoice?> GetByIdForSystemAsync(long id, long tenantId, CancellationToken ct = default) =>
        _context.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id && x.TenantId == tenantId, ct);

    public Task<SubscriptionInvoice?> GetByIdForPlatformAsync(long id, CancellationToken ct = default) =>
        _context.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

    public Task<SubscriptionInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default) =>
        _context.SubscriptionInvoices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.InvoiceNumber == invoiceNumber, ct);

    public Task<List<SubscriptionInvoice>> GetByTenantAsync(long tenantId, CancellationToken ct = default) =>
        _context.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.TenantId == tenantId)
            .OrderByDescending(x => x.InvoiceDate)
            .ToListAsync(ct);

    public Task<List<SubscriptionInvoice>> GetUnpaidByTenantAsync(long tenantId, CancellationToken ct = default) =>
        _context.SubscriptionInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.TenantId == tenantId
                && x.State != InvoiceState.Paid && x.State != InvoiceState.Cancelled && x.State != InvoiceState.Refunded
                && x.DueAmount > 0)
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct);

    public Task<string> GenerateNextInvoiceNumberAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult($"INV-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid():N}".ToUpperInvariant());
    }
}

public class SubscriptionPaymentRepository : GenericRepository<SubscriptionPayment>, ISubscriptionPaymentRepository
{
    public SubscriptionPaymentRepository(EduOSDbContext context) : base(context) { }

    public Task<SubscriptionPayment?> GetByIdForPlatformAsync(long id, CancellationToken ct = default) =>
        _context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == id, ct);

    public Task<SubscriptionPayment?> GetByTransactionIdForCallbackAsync(string transactionId, CancellationToken ct = default) =>
        _context.SubscriptionPayments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.TransactionId == transactionId, ct);

    public Task<SubscriptionPayment?> GetByGatewayTransactionIdAsync(string gatewayTxnId, CancellationToken ct = default) =>
        _context.SubscriptionPayments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProviderTransactionId == gatewayTxnId, ct);

    public Task<List<SubscriptionPayment>> GetByInvoiceAsync(long invoiceId, CancellationToken ct = default) =>
        _context.SubscriptionPayments.AsNoTracking()
            .Where(x => x.SubscriptionInvoiceId == invoiceId)
            .OrderByDescending(x => x.InitiatedAt)
            .ToListAsync(ct);

    public Task<List<SubscriptionPayment>> GetByInvoiceForPlatformAsync(long invoiceId, long tenantId, CancellationToken ct = default) =>
        _context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted && x.TenantId == tenantId && x.SubscriptionInvoiceId == invoiceId)
            .OrderByDescending(x => x.InitiatedAt)
            .ToListAsync(ct);

    public Task<List<SubscriptionPayment>> GetPendingManualVerificationForPlatformAsync(CancellationToken ct = default) =>
        _context.SubscriptionPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.PaymentMethod == PaymentMethodType.BankTransfer
                && x.State == PaymentState.AwaitingVerification)
            .OrderBy(x => x.InitiatedAt)
            .ToListAsync(ct);
}
