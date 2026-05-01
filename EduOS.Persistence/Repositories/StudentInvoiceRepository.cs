using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class StudentInvoiceRepository : GenericRepository<StudentInvoice>, IStudentInvoiceRepository
    {
        public StudentInvoiceRepository(EduOSDbContext context) : base(context) { }

        public async Task<StudentInvoice?> GetByInvoiceNoAsync(string invoiceNo)
        {
            return await _dbSet
                .Include(i => i.Items)
                .Include(i => i.Student)
                .FirstOrDefaultAsync(i => i.InvoiceNo == invoiceNo);
        }

        public async Task<List<StudentInvoice>> GetByStudentAsync(int studentId)
        {
            return await _dbSet
                .Where(i => i.StudentId == studentId)
                .OrderByDescending(i => i.CreatedDate)
                .ToListAsync();
        }

        public async Task<List<StudentInvoice>> GetByStatusAsync(string status, int tenantId)
        {
            return await _dbSet
                .Include(i => i.Student)
                .Where(i => i.Status == status && i.TenantId == tenantId)
                .ToListAsync();
        }

        public async Task<List<StudentInvoice>> GetByMonthAsync(string month, int year, int tenantId)
        {
            return await _dbSet
                .Where(i => i.Month == month && i.Year == year && i.TenantId == tenantId)
                .ToListAsync();
        }

        public async Task<List<StudentInvoice>> GetDueInvoicesAsync(int studentId)
        {
            return await _dbSet
                .Where(i => i.StudentId == studentId 
                    && (i.Status == "Unpaid" || i.Status == "Partial"))
                .OrderBy(i => i.DueDate)
                .ToListAsync();
        }

        public async Task<decimal> GetTotalDueAsync(int studentId)
        {
            return await _dbSet
                .Where(i => i.StudentId == studentId 
                    && (i.Status == "Unpaid" || i.Status == "Partial"))
                .SumAsync(i => i.DueAmount);
        }

        public async Task<string> GenerateInvoiceNoAsync(int tenantId)
        {
            var dateStr = DateTime.UtcNow.ToString("yyyyMM");
            var count = await _dbSet
                .CountAsync(i => i.TenantId == tenantId 
                    && i.CreatedDate.Year == DateTime.UtcNow.Year
                    && i.CreatedDate.Month == DateTime.UtcNow.Month);
            return $"INV{dateStr}-{(count + 1):D4}";
        }
    }
}
