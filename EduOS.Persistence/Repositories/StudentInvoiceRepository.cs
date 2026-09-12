using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class StudentInvoiceRepository : GenericRepository<StudentInvoice>, IStudentInvoiceRepository
    {
        public StudentInvoiceRepository(EduOSDbContext context) : base(context) { }
        public async Task<StudentInvoice?> GetByInvoiceNoAsync(string invoiceNo) => await _dbSet.Include(i => i.Items).Include(i => i.Student).FirstOrDefaultAsync(i => i.InvoiceNo == invoiceNo);
        public async Task<List<StudentInvoice>> GetByStudentAsync(long studentId) => await _dbSet.Where(i => i.StudentId == studentId).OrderByDescending(i => i.CreatedDate).ToListAsync();
        public async Task<List<StudentInvoice>> GetByStatusAsync(string status, long tenantId) => await _dbSet.Include(i => i.Student).Where(i => i.Status == status && i.TenantId == tenantId).ToListAsync();
        public async Task<List<StudentInvoice>> GetByMonthAsync(string month, int year, long tenantId) => await _dbSet.Where(i => i.Month == month && i.Year == year && i.TenantId == tenantId).ToListAsync();
        public async Task<List<StudentInvoice>> GetDueInvoicesAsync(long studentId) => await _dbSet.Where(i => i.StudentId == studentId && (i.Status == "Unpaid" || i.Status == "Partial")).OrderBy(i => i.DueDate).ToListAsync();
        public async Task<decimal> GetTotalDueAsync(long studentId) => await _dbSet.Where(i => i.StudentId == studentId && (i.Status == "Unpaid" || i.Status == "Partial")).SumAsync(i => i.DueAmount);
        public Task<string> GenerateInvoiceNoAsync(long tenantId) => Task.FromResult($"INV-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid():N}"[..21].ToUpperInvariant());
    }
}
