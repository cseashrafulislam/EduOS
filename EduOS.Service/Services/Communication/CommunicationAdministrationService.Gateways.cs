using Microsoft.AspNetCore.DataProtection;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    public async Task<ApiResponse<IReadOnlyList<CommunicationGatewayDto>>> GetGatewaysAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return ApiResponse<IReadOnlyList<CommunicationGatewayDto>>.ErrorResponse("Tenant administrator required.", 403);
        var rows = await _gateways.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId)
            .OrderBy(x => x.Channel).ThenBy(x => x.ProviderCode).Take(200).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<CommunicationGatewayDto>>.SuccessResponse(rows.Select(Map).ToList());
    }
}
