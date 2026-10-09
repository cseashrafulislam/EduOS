using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Payment-to-invoice association is owned by PaymentAllocation; do not invent a StudentPayment.InvoiceId.</summary>
public interface IStudentPaymentRepository : IGenericRepository<StudentPayment>
{
    Task<StudentPayment?> GetByReceiptNoAsync(string receiptNo, CancellationToken cancellationToken = default);
    Task<(List<StudentPayment> Items, int TotalCount)> GetByInvoiceAllocationAsync(long studentInvoiceId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<StudentPayment> Items, int TotalCount)> GetByStudentAsync(long studentId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<StudentPayment> Items, int TotalCount)> GetByPaymentDateRangeAsync(DateOnly fromDate, DateOnly toDate, PaymentState? state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<decimal> GetSuccessfulCollectionAsync(DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
