using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

// Read-only reporting over receipts written by the canonical CommunicationService.
public sealed partial class MessageWorkspaceService
{
    public async Task<ApiResponse<PagedResult<NoticeReadReceiptDto>>> GetNoticeReadReceiptsAsync(
        Guid noticeReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!SignedIn || !user.IsTenantAdmin)
            return ApiResponse<PagedResult<NoticeReadReceiptDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (noticeReference == Guid.Empty || !PageOk(page, pageSize))
            return ApiResponse<PagedResult<NoticeReadReceiptDto>>.ErrorResponse("Invalid notice or paging.");
        var tenant = user.TenantId;
        var noticeId = await db.Notices.AsNoTracking()
            .Where(n => n.TenantId == tenant && n.PublicId == noticeReference)
            .Select(n => (long?)n.Id).FirstOrDefaultAsync(ct);
        if (!noticeId.HasValue)
            return ApiResponse<PagedResult<NoticeReadReceiptDto>>.ErrorResponse("Notice not found.", 404);
        // Read acknowledgement is not proof of delivery. Exclude former and other-tenant members.
        var receipts = db.NoticeReadReceipts.AsNoTracking().Where(r =>
            r.TenantId == tenant && r.NoticeId == noticeId.Value &&
            db.TenantMemberships.AsNoTracking().Any(m => m.TenantId == tenant &&
                m.UserId == r.UserId && m.Status == MembershipStatus.Active) &&
            db.ApplicationUsers.AsNoTracking().Any(u => u.Id == r.UserId && u.IsActive && !u.IsDeleted));
        var total = await receipts.CountAsync(ct);
        var rows = await (from r in receipts
                          join u in db.ApplicationUsers.AsNoTracking() on r.UserId equals u.Id
                          orderby r.ReadAt descending, r.Id descending
                          select new NoticeReadReceiptDto
                          {
                              UserId = r.UserId, UserName = u.FullName, ReadAt = r.ReadAt
                          }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<NoticeReadReceiptDto>>.SuccessResponse(new PagedResult<NoticeReadReceiptDto>
        {
            Items = rows, TotalCount = total, Page = page, PageSize = pageSize
        });
    }
}
