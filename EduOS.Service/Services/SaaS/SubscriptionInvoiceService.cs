using AutoMapper;
using System.Globalization;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS
{
    public class SubscriptionInvoiceService : ISubscriptionInvoiceService
    {
        private readonly ISubscriptionInvoiceRepository _invoiceRepo;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly ILogger<SubscriptionInvoiceService> _logger;

        public SubscriptionInvoiceService(
            ISubscriptionInvoiceRepository invoiceRepo,
            ICurrentUserService currentUser,
            IMapper mapper,
            ILogger<SubscriptionInvoiceService> logger)
        {
            _invoiceRepo = invoiceRepo;
            _currentUser = currentUser;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetMyInvoicesAsync()
        {
            try
            {
                if (!_currentUser.IsAuthenticated || _currentUser.TenantId <= 0)
                    return ApiResponse<List<SubscriptionInvoiceDto>>.ErrorResponse("Tenant access required.", 403);
                var page = await _invoiceRepo.GetByTenantAsync(_currentUser.TenantId, 1, 100);
                if (page.TotalCount > 100)
                    return ApiResponse<List<SubscriptionInvoiceDto>>.ErrorResponse(
                        "More than 100 invoices exist. Use paged subscription invoice history.", 409);
                var dtos = _mapper.Map<List<SubscriptionInvoiceDto>>(page.Items);
                return ApiResponse<List<SubscriptionInvoiceDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load invoices");
                return ApiResponse<List<SubscriptionInvoiceDto>>.ErrorResponse("Failed to load invoices", 500);
            }
        }


        /// <summary>Tenant-scoped invoice history with stable server-side paging and optional state filtering.</summary>
        public async Task<ApiResponse<PagedResult<SubscriptionInvoiceDto>>> GetPageAsync(
            int page, int pageSize, InvoiceState? state = null, CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated || _currentUser.TenantId <= 0)
                return ApiResponse<PagedResult<SubscriptionInvoiceDto>>.ErrorResponse("Tenant access required.", 403);
            if (page < 1 || pageSize < 1 || pageSize > 100 ||
                (long)(page - 1) * pageSize > int.MaxValue ||
                (state.HasValue && !Enum.IsDefined(state.Value)))
                return ApiResponse<PagedResult<SubscriptionInvoiceDto>>.ErrorResponse("Invalid invoice history filters.", 400);
            try
            {
                var tenantId = _currentUser.TenantId;
                var result = state.HasValue
                    ? await _invoiceRepo.GetByStateAsync(tenantId, state.Value, page, pageSize, ct)
                    : await _invoiceRepo.GetByTenantAsync(tenantId, page, pageSize, ct);
                return ApiResponse<PagedResult<SubscriptionInvoiceDto>>.SuccessResponse(
                    new PagedResult<SubscriptionInvoiceDto>
                    {
                        Items = _mapper.Map<List<SubscriptionInvoiceDto>>(result.Items),
                        TotalCount = result.TotalCount,
                        Page = page,
                        PageSize = pageSize
                    });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load paged subscription invoices");
                return ApiResponse<PagedResult<SubscriptionInvoiceDto>>.ErrorResponse("Failed to load invoices", 500);
            }
        }

        /// <summary>Read the historical invoice and its tenant-scoped persisted line items without changing amounts.</summary>
        public async Task<ApiResponse<SubscriptionInvoiceDto>> GetByIdAsync(long invoiceId)
        {
            if (!_currentUser.IsAuthenticated || (!_currentUser.IsSuperAdmin && _currentUser.TenantId <= 0))
                return ApiResponse<SubscriptionInvoiceDto>.ErrorResponse("Tenant access required.", 403);
            if (invoiceId <= 0)
                return ApiResponse<SubscriptionInvoiceDto>.ErrorResponse("Invalid invoice id.", 400);
            try
            {
                var invoice = _currentUser.IsSuperAdmin
                    ? await _invoiceRepo.GetByIdForPlatformAsync(invoiceId)
                    : await _invoiceRepo.GetByIdForSystemAsync(invoiceId, _currentUser.TenantId);
                if (invoice == null || invoice.IsDeleted ||
                    (!_currentUser.IsSuperAdmin && invoice.TenantId != _currentUser.TenantId))
                    return ApiResponse<SubscriptionInvoiceDto>.ErrorResponse("Invoice not found", 404);

                // Always scope child rows to the invoice's persisted tenant, including for platform administrators.
                var lines = await _invoiceRepo.GetLinesForInvoiceAsync(invoice.TenantId, invoice.Id);
                var dto = _mapper.Map<SubscriptionInvoiceDto>(invoice);
                dto.Lines = lines.Select(x => new SubscriptionInvoiceLineDto
                {
                    Id = x.Id,
                    Description = x.Description,
                    Quantity = x.Quantity,
                    UnitPrice = x.UnitPrice,
                    Amount = x.Amount
                }).ToList();
                return ApiResponse<SubscriptionInvoiceDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load invoice {Id}", invoiceId);
                return ApiResponse<SubscriptionInvoiceDto>.ErrorResponse("Failed to load invoice", 500);
            }
        }


        /// <summary>Download one tenant-owned invoice and its persisted lines without modifying financial records.</summary>
        public async Task<ApiResponse<string>> ExportCsvAsync(long invoiceId)
        {
            // This is a tenant self-service export, not the cross-tenant platform investigation API.
            if (!_currentUser.IsAuthenticated || _currentUser.TenantId <= 0 || _currentUser.IsSuperAdmin)
                return ApiResponse<string>.ErrorResponse("Institution session required.", 403);
            var result = await GetByIdAsync(invoiceId);
            if (!result.Success || result.Data == null)
                return ApiResponse<string>.ErrorResponse(result.Message, result.StatusCode);
            var invoice = result.Data;
            var csv = new StringBuilder(512);
            csv.Append("Invoice Number,").AppendLine(CsvText(invoice.InvoiceNumber));
            csv.Append("Invoice Date,").AppendLine(CsvText(invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            csv.Append("Due Date,").AppendLine(CsvText(invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            csv.Append("Status,").AppendLine(CsvText(invoice.State.ToString()));
            csv.Append("Currency,").AppendLine(CsvText(invoice.CurrencyCode));
            csv.Append("Subtotal,").AppendLine(CsvNumber(invoice.Subtotal));
            csv.Append("Tax,").AppendLine(CsvNumber(invoice.TaxAmount));
            csv.Append("Total,").AppendLine(CsvNumber(invoice.TotalAmount));
            csv.Append("Paid,").AppendLine(CsvNumber(invoice.PaidAmount));
            csv.Append("Outstanding,").AppendLine(CsvNumber(invoice.DueAmount));
            csv.AppendLine().AppendLine("Description,Quantity,Unit Price,Amount");
            foreach (var line in invoice.Lines)
                csv.Append(CsvText(line.Description)).Append(',')
                    .Append(CsvNumber(line.Quantity)).Append(',')
                    .Append(CsvNumber(line.UnitPrice)).Append(',')
                    .AppendLine(CsvNumber(line.Amount));
            return ApiResponse<string>.SuccessResponse(csv.ToString());
        }

        private static string CsvNumber(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        // Protect spreadsheet users from formula injection, including after leading whitespace.
        private static string CsvText(string? value)
        {
            var text = value ?? string.Empty;
            var first = text.TrimStart(' ', '\t', '\r', '\n');
            if (first.Length > 0 && "=+-@".Contains(first[0]))
                text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        public async Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetUnpaidAsync()
        {
            try
            {
                if (!_currentUser.IsAuthenticated || _currentUser.TenantId <= 0)
                    return ApiResponse<List<SubscriptionInvoiceDto>>.ErrorResponse("Tenant access required.", 403);
                var issued = await _invoiceRepo.GetByStateAsync(_currentUser.TenantId, InvoiceState.Issued, 1, 100);
                var partial = await _invoiceRepo.GetByStateAsync(_currentUser.TenantId, InvoiceState.PartiallyPaid, 1, 100);
                if (issued.TotalCount > 100 || partial.TotalCount > 100)
                    return ApiResponse<List<SubscriptionInvoiceDto>>.ErrorResponse(
                        "Unpaid invoices exceed legacy list size. Use paged subscription invoices.", 409);
                var invoices = issued.Items.Concat(partial.Items).OrderByDescending(x => x.InvoiceDate).ToList();
                var dtos = _mapper.Map<List<SubscriptionInvoiceDto>>(invoices);
                return ApiResponse<List<SubscriptionInvoiceDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load unpaid invoices");
                return ApiResponse<List<SubscriptionInvoiceDto>>.ErrorResponse("Failed", 500);
            }
        }
    }
}
