using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IStudentInvoiceRepository : IGenericRepository<StudentInvoice>
    {
        Task<StudentInvoice?> GetByInvoiceNoAsync(string invoiceNo);
        Task<List<StudentInvoice>> GetByStudentAsync(int studentId);
        Task<List<StudentInvoice>> GetByStatusAsync(string status, int tenantId);
        Task<List<StudentInvoice>> GetByMonthAsync(string month, int year, int tenantId);
        Task<List<StudentInvoice>> GetDueInvoicesAsync(int studentId);
        Task<decimal> GetTotalDueAsync(int studentId);
        Task<string> GenerateInvoiceNoAsync(int tenantId);
    }
}
