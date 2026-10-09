using EduOS.Core.Entities.Finance;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IStudentPaymentRepository : IGenericRepository<StudentPayment>
{
    Task<StudentPayment?> GetByReceiptNoAsync(string receiptNo);
    Task<List<StudentPayment>> GetByInvoiceAsync(long invoiceId);
    Task<List<StudentPayment>> GetByStudentAsync(long studentId);
    Task<List<StudentPayment>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, long tenantId);
    Task<decimal> GetTotalCollectionAsync(DateTime date, long tenantId);
    Task<decimal> GetMonthlyCollectionAsync(int month, int year, long tenantId);
    Task<string> GenerateReceiptNoAsync(long tenantId);
}
