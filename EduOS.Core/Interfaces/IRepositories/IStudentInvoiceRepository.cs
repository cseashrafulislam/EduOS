using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IStudentInvoiceRepository : IGenericRepository<StudentInvoice>
    {
        Task<StudentInvoice?> GetByInvoiceNoAsync(string invoiceNo);
        Task<List<StudentInvoice>> GetByStudentAsync(long studentId);
        Task<List<StudentInvoice>> GetByStatusAsync(string status, long tenantId);
        Task<List<StudentInvoice>> GetByMonthAsync(string month, int year, long tenantId);
        Task<List<StudentInvoice>> GetDueInvoicesAsync(long studentId);
        Task<decimal> GetTotalDueAsync(long studentId);
        Task<string> GenerateInvoiceNoAsync(long tenantId);
    }
}
