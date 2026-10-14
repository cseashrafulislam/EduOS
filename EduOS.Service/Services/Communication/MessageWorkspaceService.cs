using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

/// <summary>Read models and receipts for the canonical CommunicationService messaging workflow.</summary>
public sealed partial class MessageWorkspaceService(EduOSDbContext db, ICurrentUserService user, IUnitOfWork unit) : IMessageWorkspaceService
{
    private bool SignedIn => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0;
    private static bool PageOk(int page, int size) => page > 0 && size is >= 1 and <= 100 && page <= int.MaxValue / size;
    private IQueryable<EduOS.Core.Entities.Communication.MessageThread> MyThreads() =>
        db.MessageThreads.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            db.MessageThreadParticipants.Any(p => p.TenantId == user.TenantId &&
                p.MessageThreadId == x.Id && p.UserId == user.UserId && p.LeftAt == null));

    public async Task<ApiResponse<PagedResult<MessageThreadSummaryDto>>> GetMyThreadsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<PagedResult<MessageThreadSummaryDto>>.ErrorResponse("Sign in required.", 403);
        if (!PageOk(page, pageSize)) return ApiResponse<PagedResult<MessageThreadSummaryDto>>.ErrorResponse("Invalid paging.");
        var query = MyThreads();
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.LastMessageAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var peers = await db.MessageThreadParticipants.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            ids.Contains(x.MessageThreadId) && x.UserId != user.UserId && x.LeftAt == null)
            .Select(x => new { x.MessageThreadId, x.UserId }).ToListAsync(ct);
        var peerIds = peers.Select(x => x.UserId).Distinct().ToArray();
        var names = await db.ApplicationUsers.AsNoTracking().Where(x => peerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var unread = await db.Messages.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            ids.Contains(x.MessageThreadId) && x.RecipientUserId == user.UserId && x.ReadAt == null)
            .GroupBy(x => x.MessageThreadId).Select(x => new { ThreadId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ThreadId, x => x.Count, ct);
        return ApiResponse<PagedResult<MessageThreadSummaryDto>>.SuccessResponse(new()
        {
            Items = rows.Select(x =>
            {
                var peer = peers.FirstOrDefault(p => p.MessageThreadId == x.Id);
                return new MessageThreadSummaryDto
                {
                    Reference = x.PublicId, Subject = x.Subject, LastMessageAt = x.LastMessageAt,
                    PeerUserId = peer?.UserId ?? 0,
                    PeerName = peer == null ? "Unknown member" : names.GetValueOrDefault(peer.UserId) ?? "User",
                    UnreadCount = unread.GetValueOrDefault(x.Id)
                };
            }).ToList(), TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<IReadOnlyList<MessageRecipientDto>>> SearchRecipientsAsync(string query, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<IReadOnlyList<MessageRecipientDto>>.ErrorResponse("Sign in required.", 403);
        var term = query?.Trim() ?? "";
        if (term.Length < 2 || term.Length > 80)
            return ApiResponse<IReadOnlyList<MessageRecipientDto>>.ErrorResponse("Enter 2 to 80 characters.");
        var tenant = user.TenantId;
        var rows = await db.TenantMemberships.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.Status == MembershipStatus.Active && x.UserId != user.UserId)
            .Join(db.ApplicationUsers.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted && x.FullName.Contains(term)),
                member => member.UserId, account => account.Id,
                (member, account) => new MessageRecipientDto { UserId = account.Id, Name = account.FullName })
            .Distinct().OrderBy(x => x.Name).ThenBy(x => x.UserId).Take(20).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<MessageRecipientDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<MessageThreadDto>> GetThreadForMessageAsync(long messageId, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<MessageThreadDto>.ErrorResponse("Sign in required.", 403);
        if (messageId <= 0) return ApiResponse<MessageThreadDto>.ErrorResponse("Invalid message.");
        var threadId = await db.Messages.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.Id == messageId && (x.SenderUserId == user.UserId || x.RecipientUserId == user.UserId))
            .Select(x => x.MessageThreadId).FirstOrDefaultAsync(ct);
        if (threadId == 0) return ApiResponse<MessageThreadDto>.ErrorResponse("Message not found.", 404);
        var thread = await MyThreads().FirstOrDefaultAsync(x => x.Id == threadId, ct);
        return thread == null ? ApiResponse<MessageThreadDto>.ErrorResponse("Thread not found.", 404) :
            ApiResponse<MessageThreadDto>.SuccessResponse(new MessageThreadDto
            { Id = thread.Id, Reference = thread.PublicId, Subject = thread.Subject, LastMessageAt = thread.LastMessageAt });
    }
}
