using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class PaymentRepository : GenericRepository<StudentPayment>, IStudentPaymentRepository
{
    public PaymentRepository(EduOSDbContext context) : base(context) { }

    public Task<StudentPayment?> GetByReceiptNoAsync(string receiptNo) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.ReceiptNumber == receiptNo);

    public async Task<List<StudentPayment>> GetByInvoiceAsync(long invoiceId)
    {
        var paymentIds = _context.Set<PaymentAllocation>().Where(x => x.StudentInvoiceId == invoiceId).Select(x => x.StudentPaymentId);
        return await _dbSet.AsNoTracking().Where(x => paymentIds.Contains(x.Id))
            .OrderByDescending(x => x.PaymentDate).ToListAsync();
    }

    public Task<List<StudentPayment>> GetByStudentAsync(long studentId) =>
        _dbSet.AsNoTracking().Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.PaymentDate).ToListAsync();

    public Task<List<StudentPayment>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, long tenantId)
    {
        var from = DateOnly.FromDateTime(fromDate);
        var to = DateOnly.FromDateTime(toDate);
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.PaymentDate >= from && x.PaymentDate <= to)
            .OrderByDescending(x => x.PaymentDate).ToListAsync();
    }

    public async Task<decimal> GetTotalCollectionAsync(DateTime date, long tenantId)
    {
        var target = DateOnly.FromDateTime(date);
        return await _dbSet.Where(x => x.TenantId == tenantId && x.PaymentDate == target && x.State == PaymentState.Successful)
            .SumAsync(x => x.Amount);
    }

    public Task<decimal> GetMonthlyCollectionAsync(int month, int year, long tenantId)
    {
        if (month < 1 || month > 12 || year < 1 || year > 9998)
            throw new ArgumentOutOfRangeException(nameof(month), "Month and year are outside the supported date range.");

        var firstDay = new DateOnly(year, month, 1);
        var nextMonth = firstDay.AddMonths(1);
        return _dbSet.Where(x => x.TenantId == tenantId
            && x.PaymentDate >= firstDay && x.PaymentDate < nextMonth
            && x.State == PaymentState.Successful).SumAsync(x => x.Amount);
    }

    public Task<string> GenerateReceiptNoAsync(long tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return Task.FromResult($"RCP-{tenantId}-{suffix}");
    }

    public Task<StudentPayment?> GetByReceiptNoAsync(string receiptNo, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.ReceiptNumber == receiptNo, cancellationToken);
    public Task<(List<StudentPayment> Items, int TotalCount)> GetByInvoiceAllocationAsync(long studentInvoiceId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var ids = _context.Set<PaymentAllocation>().Where(x => x.StudentInvoiceId == studentInvoiceId).Select(x => x.StudentPaymentId);
        return PageAsync(_dbSet.AsNoTracking().Where(x => ids.Contains(x.Id))
            .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    }
    public Task<(List<StudentPayment> Items, int TotalCount)> GetByStudentAsync(long studentId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<StudentPayment> Items, int TotalCount)> GetByPaymentDateRangeAsync(DateOnly fromDate, DateOnly toDate, PaymentState? state, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.PaymentDate >= fromDate && x.PaymentDate <= toDate
            && (!state.HasValue || x.State == state.Value))
            .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<decimal> GetSuccessfulCollectionAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.PaymentDate >= fromDate && x.PaymentDate <= toDate
            && x.State == PaymentState.Successful).SumAsync(x => x.Amount, cancellationToken);
}
