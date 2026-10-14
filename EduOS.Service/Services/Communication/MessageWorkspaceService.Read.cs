using System.Data;
using EduOS.Core.Common;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Communication;

public sealed partial class MessageWorkspaceService
{
    public async Task<ApiResponse<bool>> MarkThreadReadAsync(Guid threadReference, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<bool>.ErrorResponse("Sign in required.", 403);
        if (threadReference == Guid.Empty) return ApiResponse<bool>.ErrorResponse("Invalid thread reference.");
        var threadId = await MyThreads().Where(x => x.PublicId == threadReference)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (threadId == 0) return ApiResponse<bool>.ErrorResponse("Thread not found.", 404);
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var participant = await db.MessageThreadParticipants.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.MessageThreadId == threadId &&
                    x.UserId == user.UserId && x.LeftAt == null, token);
                if (participant == null) return ApiResponse<bool>.ErrorResponse("Thread not found.", 404);
                var now = DateTime.UtcNow;
                await db.Messages.Where(x => x.TenantId == user.TenantId && x.MessageThreadId == threadId &&
                    x.RecipientUserId == user.UserId && x.ReadAt == null && x.SentAt <= now)
                    .ExecuteUpdateAsync(x => x.SetProperty(m => m.ReadAt, now)
                        .SetProperty(m => m.UpdatedAt, now)
                        .SetProperty(m => m.UpdatedBy, user.UserId), token);
                participant.LastReadAt = now; participant.UpdatedAt = now; participant.UpdatedBy = user.UserId;
                await db.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true);
            }, IsolationLevel.ReadCommitted, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<bool>.ErrorResponse("Thread changed; reload.", 409); }
    }
}
