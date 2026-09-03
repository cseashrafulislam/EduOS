using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
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
                var invoices = await _invoiceRepo.GetByTenantAsync(_currentUser.TenantId);
                var dtos = _mapper.Map<List<SubscriptionInvoiceDto>>(invoices);
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
                var invoices = await _invoiceRepo.GetUnpaidByTenantAsync(_currentUser.TenantId);
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
