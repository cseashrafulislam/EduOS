using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class PaymentRepository : GenericRepository<Payment>, IPaymentRepository
    {
        public PaymentRepository(EduOSDbContext context) : base(context) { }

        public async Task<Payment?> GetByReceiptNoAsync(string receiptNo)
        {
            return await _dbSet
                .Include(p => p.Invoice)
                .Include(p => p.Student)
                .FirstOrDefaultAsync(p => p.ReceiptNo == receiptNo);
        }

        public async Task<List<Payment>> GetByInvoiceAsync(int invoiceId)
        {
            return await _dbSet
                .Where(p => p.InvoiceId == invoiceId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<Payment>> GetByStudentAsync(int studentId)
        {
            return await _dbSet
                .Include(p => p.Invoice)
                .Where(p => p.StudentId == studentId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<Payment>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int tenantId)
        {
            return await _dbSet
                .Include(p => p.Student)
                .Where(p => p.TenantId == tenantId 
                    && p.PaymentDate.Date >= fromDate.Date 
                    && p.PaymentDate.Date <= toDate.Date)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalCollectionAsync(DateTime date, int tenantId)
        {
            return await _dbSet
                .Where(p => p.TenantId == tenantId && p.PaymentDate.Date == date.Date)
                .SumAsync(p => p.Amount);
        }

        public async Task<decimal> GetMonthlyCollectionAsync(int month, int year, int tenantId)
        {
            return await _dbSet
                .Where(p => p.TenantId == tenantId 
                    && p.PaymentDate.Month == month 
                    && p.PaymentDate.Year == year)
                .SumAsync(p => p.Amount);
        }

        public async Task<string> GenerateReceiptNoAsync(int tenantId)
        {
            var count = await _dbSet.CountAsync(p => p.TenantId == tenantId);
            return $"RCP{(count + 1):D6}";
        }
    }
}
