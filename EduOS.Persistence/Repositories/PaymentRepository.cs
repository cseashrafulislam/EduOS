using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class PaymentRepository : GenericRepository<Payment>, IPaymentRepository
    {
        public PaymentRepository(EduOSDbContext context) : base(context) { }
        public async Task<Payment?> GetByReceiptNoAsync(string receiptNo) => await _dbSet.Include(p => p.Invoice).Include(p => p.Student).FirstOrDefaultAsync(p => p.ReceiptNo == receiptNo);
        public async Task<List<Payment>> GetByInvoiceAsync(long invoiceId) => await _dbSet.Where(p => p.InvoiceId == invoiceId).OrderByDescending(p => p.PaymentDate).ToListAsync();
        public async Task<List<Payment>> GetByStudentAsync(long studentId) => await _dbSet.Include(p => p.Invoice).Where(p => p.StudentId == studentId).OrderByDescending(p => p.PaymentDate).ToListAsync();
        public async Task<List<Payment>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, long tenantId) => await _dbSet.Include(p => p.Student).Where(p => p.TenantId == tenantId && p.PaymentDate.Date >= fromDate.Date && p.PaymentDate.Date <= toDate.Date).OrderByDescending(p => p.PaymentDate).ToListAsync();
        public async Task<decimal> GetTotalCollectionAsync(DateTime date, long tenantId) => await _dbSet.Where(p => p.TenantId == tenantId && p.PaymentDate.Date == date.Date).SumAsync(p => p.Amount);
        public async Task<decimal> GetMonthlyCollectionAsync(int month, int year, long tenantId) => await _dbSet.Where(p => p.TenantId == tenantId && p.PaymentDate.Month == month && p.PaymentDate.Year == year).SumAsync(p => p.Amount);
        public Task<string> GenerateReceiptNoAsync(long tenantId) => Task.FromResult($"RCP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..25].ToUpperInvariant());
    }
}
