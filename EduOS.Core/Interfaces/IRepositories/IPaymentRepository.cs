using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IPaymentRepository : IGenericRepository<Payment>
    {
        Task<Payment?> GetByReceiptNoAsync(string receiptNo);
        Task<List<Payment>> GetByInvoiceAsync(int invoiceId);
        Task<List<Payment>> GetByStudentAsync(int studentId);
        Task<List<Payment>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int tenantId);
        Task<decimal> GetTotalCollectionAsync(DateTime date, int tenantId);
        Task<decimal> GetMonthlyCollectionAsync(int month, int year, int tenantId);
        Task<string> GenerateReceiptNoAsync(int tenantId);
    }
}
