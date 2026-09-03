using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories.SaaS
{
    public class SubscriptionPlanRepository : GenericRepository<SubscriptionPlan>, ISubscriptionPlanRepository
    {
        public SubscriptionPlanRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<SubscriptionPlan>> GetActivePublicPlansAsync(CancellationToken ct = default)
        {
            return await _context.SubscriptionPlans
                .Include(p => p.PlanFeatures)
                    .ThenInclude(pf => pf.Feature)
                .Where(p => p.IsActive && p.IsPubliclyVisible)
                .OrderBy(p => p.DisplayOrder)
                .ToListAsync(ct);
        }

        public async Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default)
        {
            return await _context.SubscriptionPlans
                .Include(p => p.PlanFeatures)
                    .ThenInclude(pf => pf.Feature)
                .FirstOrDefaultAsync(p => p.Code == code, ct);
        }

        public async Task<SubscriptionPlan?> GetWithFeaturesAsync(long id, CancellationToken ct = default)
        {
            return await _context.SubscriptionPlans
                .Include(p => p.PlanFeatures)
                    .ThenInclude(pf => pf.Feature)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
        }

        public async Task<SubscriptionPlan?> GetTrialPlanAsync(CancellationToken ct = default)
        {
            return await _context.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.IsFreeTrial && p.IsActive, ct);
        }
    }

    public class TenantSubscriptionRepository : GenericRepository<TenantSubscription>, ITenantSubscriptionRepository
    {
        public TenantSubscriptionRepository(EduOSDbContext context) : base(context) { }

        public async Task<TenantSubscription?> GetActiveByTenantAsync(long tenantId, CancellationToken ct = default)
        {
            return await _context.TenantSubscriptions
                .IgnoreQueryFilters()
                .Include(s => s.SubscriptionPlan)
                .Where(s => !s.IsDeleted &&
                           s.TenantId == tenantId &&
                           (s.Status == SubscriptionStatus.Active ||
                            s.Status == SubscriptionStatus.Trialing ||
                            s.Status == SubscriptionStatus.PendingPayment ||
                            s.Status == SubscriptionStatus.CancelAtPeriodEnd))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<TenantSubscription?> GetByIdForSystemAsync(
            long id, long tenantId, CancellationToken ct = default)
        {
            return await _context.TenantSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => !s.IsDeleted && s.Id == id && s.TenantId == tenantId,
                    ct);
        }

        public async Task<List<TenantSubscription>> GetHistoryByTenantAsync(long tenantId, CancellationToken ct = default)
        {
            return await _context.TenantSubscriptions
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.TenantId == tenantId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<List<TenantSubscription>> GetExpiringSoonAsync(int daysAhead, CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow.AddDays(daysAhead);
            return await _context.TenantSubscriptions
                .IgnoreQueryFilters()
                .Include(s => s.Tenant)
                .Where(s => !s.IsDeleted &&
                           s.Status == SubscriptionStatus.Active &&
                           s.EndDate <= cutoff &&
                           s.EndDate > DateTime.UtcNow)
                .ToListAsync(ct);
        }

        public async Task<List<TenantSubscription>> GetExpiredAsync(CancellationToken ct = default)
        {
            return await _context.TenantSubscriptions
                .IgnoreQueryFilters()
                .Where(s => !s.IsDeleted &&
                           (s.Status == SubscriptionStatus.Active ||
                             s.Status == SubscriptionStatus.Trialing) &&
                            s.EndDate < DateTime.UtcNow)
                .ToListAsync(ct);
        }
    }

    public class SubscriptionInvoiceRepository : GenericRepository<SubscriptionInvoice>, ISubscriptionInvoiceRepository
    {
        public SubscriptionInvoiceRepository(EduOSDbContext context) : base(context) { }

        public async Task<SubscriptionInvoice?> GetByIdForSystemAsync(
            long id, long tenantId, CancellationToken ct = default)
        {
            return await _context.SubscriptionInvoices
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    i => !i.IsDeleted && i.Id == id && i.TenantId == tenantId,
                    ct);
        }

        public async Task<SubscriptionInvoice?> GetByIdForPlatformAsync(
            long id, CancellationToken ct = default)
        {
            return await _context.SubscriptionInvoices
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => !i.IsDeleted && i.Id == id, ct);
        }

        public async Task<SubscriptionInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default)
        {
            return await _context.SubscriptionInvoices
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber, ct);
        }

        public async Task<List<SubscriptionInvoice>> GetByTenantAsync(long tenantId, CancellationToken ct = default)
        {
            return await _context.SubscriptionInvoices
                .IgnoreQueryFilters()
                .Where(i => !i.IsDeleted && i.TenantId == tenantId)
                .OrderByDescending(i => i.IssueDate)
                .ToListAsync(ct);
        }

        public async Task<List<SubscriptionInvoice>> GetUnpaidByTenantAsync(long tenantId, CancellationToken ct = default)
        {
            return await _context.SubscriptionInvoices
                .IgnoreQueryFilters()
                .Where(i => !i.IsDeleted &&
                           i.TenantId == tenantId &&
                           (i.PaymentStatus == PaymentStatus.Pending ||
                            i.PaymentStatus == PaymentStatus.AwaitingVerification))
                .OrderByDescending(i => i.IssueDate)
                .ToListAsync(ct);
        }

        public async Task<string> GenerateNextInvoiceNumberAsync(CancellationToken ct = default)
        {
            // Format: INV-YYYYMM-NNNNN  (e.g. INV-202605-00001)
            var prefix = $"INV-{DateTime.UtcNow:yyyyMM}-";

            var lastNumber = await _context.SubscriptionInvoices
                .IgnoreQueryFilters()
                .Where(i => !i.IsDeleted && i.InvoiceNumber.StartsWith(prefix))
                .OrderByDescending(i => i.InvoiceNumber)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(ct);

            int nextSeq = 1;
            if (!string.IsNullOrEmpty(lastNumber))
            {
                var seqPart = lastNumber.Substring(prefix.Length);
                if (int.TryParse(seqPart, out var lastSeq))
                    nextSeq = lastSeq + 1;
            }

            return $"{prefix}{nextSeq:D5}";
        }
    }

    public class SubscriptionPaymentRepository : GenericRepository<SubscriptionPayment>, ISubscriptionPaymentRepository
    {
        public SubscriptionPaymentRepository(EduOSDbContext context) : base(context) { }

        public async Task<SubscriptionPayment?> GetByIdForPlatformAsync(
            long id, CancellationToken ct = default)
        {
            return await _context.SubscriptionPayments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == id, ct);
        }

        public async Task<SubscriptionPayment?> GetByTransactionIdForCallbackAsync(
            string transactionId, CancellationToken ct = default)
        {
            return await _context.SubscriptionPayments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    p => !p.IsDeleted && p.TransactionId == transactionId,
                    ct);
        }

        public async Task<SubscriptionPayment?> GetByGatewayTransactionIdAsync(string gatewayTxnId, CancellationToken ct = default)
        {
            return await _context.SubscriptionPayments
                .FirstOrDefaultAsync(p => p.GatewayTransactionId == gatewayTxnId, ct);
        }

        public async Task<List<SubscriptionPayment>> GetByInvoiceAsync(long invoiceId, CancellationToken ct = default)
        {
            return await _context.SubscriptionPayments
                .Where(p => p.SubscriptionInvoiceId == invoiceId)
                .OrderByDescending(p => p.InitiatedAt)
                .ToListAsync(ct);
        }

        public async Task<List<SubscriptionPayment>> GetByInvoiceForPlatformAsync(
            long invoiceId, long tenantId, CancellationToken ct = default)
        {
            return await _context.SubscriptionPayments
                .IgnoreQueryFilters()
                .Where(p => !p.IsDeleted &&
                            p.TenantId == tenantId &&
                            p.SubscriptionInvoiceId == invoiceId)
                .OrderByDescending(p => p.InitiatedAt)
                .ToListAsync(ct);
        }

        public async Task<List<SubscriptionPayment>> GetPendingManualVerificationForPlatformAsync(
            CancellationToken ct = default)
        {
            return await _context.SubscriptionPayments
                .IgnoreQueryFilters()
                .Where(p => !p.IsDeleted &&
                           p.PaymentMethod == PaymentMethod.ManualBankTransfer &&
                           p.Status == PaymentStatus.AwaitingVerification)
                .OrderBy(p => p.InitiatedAt)
                .ToListAsync(ct);
        }
    }
}
