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

        public async Task<ApiResponse<SubscriptionInvoiceDto>> GetByIdAsync(long invoiceId)
        {
            try
            {
                var invoice = _currentUser.IsSuperAdmin
                    ? await _invoiceRepo.GetByIdForPlatformAsync(invoiceId)
                    : await _invoiceRepo.GetByIdAsync(invoiceId);
                if (invoice == null ||
                    (invoice.TenantId != _currentUser.TenantId && !_currentUser.IsSuperAdmin))
                {
                    return ApiResponse<SubscriptionInvoiceDto>.ErrorResponse("Invoice not found", 404);
                }

                var dto = _mapper.Map<SubscriptionInvoiceDto>(invoice);
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
