using System.Globalization;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class StudentInvoiceRepository : GenericRepository<StudentInvoice>, IStudentInvoiceRepository
{
    public StudentInvoiceRepository(EduOSDbContext context) : base(context) { }

    public Task<StudentInvoice?> GetByInvoiceNoAsync(string invoiceNo) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.InvoiceNumber == invoiceNo);

    public async Task<List<StudentInvoice>> GetByStudentAsync(long studentId)
    {
        var enrollmentIds = _context.Set<StudentEnrollment>()
            .Where(x => x.StudentId == studentId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => enrollmentIds.Contains(x.StudentEnrollmentId))
            .OrderByDescending(x => x.InvoiceDate).ToListAsync();
    }

    public Task<List<StudentInvoice>> GetByStatusAsync(string status, long tenantId)
    {
        if (!Enum.TryParse<InvoiceState>(status, true, out var state))
            return Task.FromResult(new List<StudentInvoice>());
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.State == state)
            .OrderByDescending(x => x.InvoiceDate).ToListAsync();
    }

    public Task<List<StudentInvoice>> GetByMonthAsync(string month, int year, long tenantId)
    {
        var monthNumber = ParseMonth(month);
        if (monthNumber is < 1 or > 12) return Task.FromResult(new List<StudentInvoice>());
        var start = new DateOnly(year, monthNumber, 1);
        var end = start.AddMonths(1);
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId
                && x.InvoiceDate >= start && x.InvoiceDate < end)
            .OrderBy(x => x.InvoiceDate).ToListAsync();
    }

    public async Task<List<StudentInvoice>> GetDueInvoicesAsync(long studentId)
    {
        var enrollmentIds = _context.Set<StudentEnrollment>()
            .Where(x => x.StudentId == studentId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => enrollmentIds.Contains(x.StudentEnrollmentId)
                && x.DueAmount > 0 && x.State != InvoiceState.Cancelled && x.State != InvoiceState.Refunded)
            .OrderBy(x => x.DueDate).ToListAsync();
    }

    public async Task<decimal> GetTotalDueAsync(long studentId)
    {
        var enrollmentIds = _context.Set<StudentEnrollment>()
            .Where(x => x.StudentId == studentId).Select(x => x.Id);
        return await _dbSet.Where(x => enrollmentIds.Contains(x.StudentEnrollmentId)
                && x.DueAmount > 0 && x.State != InvoiceState.Cancelled && x.State != InvoiceState.Refunded)
            .SumAsync(x => x.DueAmount);
    }

    public Task<string> GenerateInvoiceNoAsync(long tenantId)
    {
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        var prefix = $"SINV-{tenantId}-{DateTime.UtcNow:yyyyMM}-";
        var available = 50 - prefix.Length;
        if (available < 12) throw new InvalidOperationException("Invoice prefix exceeds the number-series length.");
        var suffix = Guid.NewGuid().ToString("N")[..Math.Min(available, 32)].ToUpperInvariant();
        return Task.FromResult(prefix + suffix);
    }

    private static int ParseMonth(string value)
    {
        if (int.TryParse(value, out var numeric)) return numeric;
        return DateTime.TryParseExact(value, new[] { "MMM", "MMMM" }, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed.Month : 0;
    }
}
