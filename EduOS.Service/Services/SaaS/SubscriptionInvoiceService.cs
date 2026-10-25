using AutoMapper;
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
