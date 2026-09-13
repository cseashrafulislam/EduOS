using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IPaymentRepository : IGenericRepository<Payment>
    {
        Task<Payment?> GetByReceiptNoAsync(string receiptNo);
        Task<List<Payment>> GetByInvoiceAsync(long invoiceId);
        Task<List<Payment>> GetByStudentAsync(long studentId);
        Task<List<Payment>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, long tenantId);
        Task<decimal> GetTotalCollectionAsync(DateTime date, long tenantId);
        Task<decimal> GetMonthlyCollectionAsync(int month, int year, long tenantId);
        Task<string> GenerateReceiptNoAsync(long tenantId);
    }
}
