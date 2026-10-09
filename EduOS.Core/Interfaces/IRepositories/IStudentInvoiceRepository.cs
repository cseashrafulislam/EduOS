using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Invoice lifecycle is InvoiceState. Amounts are posted server-side; issuing invoice numbers belongs to NumberSeries.</summary>
public interface IStudentInvoiceRepository : IGenericRepository<StudentInvoice>
{
    Task<StudentInvoice?> GetByInvoiceNoAsync(string invoiceNo, CancellationToken cancellationToken = default);
    Task<(List<StudentInvoice> Items, int TotalCount)> GetByEnrollmentAsync(long studentEnrollmentId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<StudentInvoice> Items, int TotalCount)> GetByStateAsync(InvoiceState state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<StudentInvoice> Items, int TotalCount)> GetByInvoiceDateRangeAsync(DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<StudentInvoice> Items, int TotalCount)> GetOutstandingByStudentAsync(long studentId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<decimal> GetOutstandingTotalByStudentAsync(long studentId, CancellationToken cancellationToken = default);
}
