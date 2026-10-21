using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Communication;

/// <summary>Canonical tenant communication owner. No gateway delivery occurs here.</summary>
public sealed class CommunicationService(
    EduOSDbContext db, ICurrentUserService user, INoticeRepository notices,
    INotificationRepository notifications, IUnitOfWork unit,
    IDataProtectionProvider protector, ILogger<CommunicationService> logger) : ICommunicationService
{
    private const string MessageScope = "communication.message.send";
    private bool SignedIn => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0;
    private bool Admin => SignedIn && user.IsTenantAdmin;
    private static bool PageOk(int page, int size) => page > 0 && size is >= 1 and <= 100 && page <= int.MaxValue / size;
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static bool SameVersion(byte[] current, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(value)); }
        catch (FormatException) { return false; }
    }
    private static NoticeAudienceDto Map(NoticeAudience a) => new()
    {
        Id = a.Id, AudienceTypeCode = a.AudienceTypeCode, CampusId = a.CampusId,
        AcademicProgramId = a.AcademicProgramId, AcademicBatchId = a.AcademicBatchId, UserId = a.UserId
    };
    private static NoticeDto Map(Notice n, string? category, IReadOnlyList<NoticeAudienceDto> audience) => new()
    {
        Id = n.Id, Reference = n.PublicId, NoticeCategoryId = n.NoticeCategoryId, NoticeCategoryName = category,
        Title = n.Title, Body = n.Body, PublishAt = n.PublishAt, ExpiresAt = n.ExpiresAt,
        IsPublished = n.IsPublished, Audiences = audience, RowVersion = Version(n.RowVersion)
    };
    private static NotificationDto Map(Notification n) => new()
    {
        Id = n.Id, Title = n.Title, Body = n.Body, TypeCode = n.TypeCode,
        ActionUrl = n.ActionUrl, IsRead = n.IsRead, ReadAt = n.ReadAt, CreatedAt = n.CreatedAt
    };
    private static MessageDto Map(Message m, IReadOnlyDictionary<long, string> names) => new()
    {
        Id = m.Id, SenderUserId = m.SenderUserId, RecipientUserId = m.RecipientUserId,
        SenderName = names.GetValueOrDefault(m.SenderUserId) ?? "User",
        RecipientName = names.GetValueOrDefault(m.RecipientUserId) ?? "User",
        Body = m.Body, SentAt = m.SentAt, ReadAt = m.ReadAt
    };
    private static NotificationPreferenceDto Map(NotificationPreference p) => new()
    {
        Id = p.Id, Channel = p.Channel, EventCode = p.EventCode,
        IsEnabled = p.IsEnabled, RowVersion = Version(p.RowVersion)
    };
    private static DeviceTokenDto Map(DeviceToken d) => new()
    { Id = d.Id, Platform = d.Platform, DeviceId = d.DeviceId, IsActive = d.IsActive, LastSeenAt = d.LastSeenAt };

    // Derive visibility exclusively from authenticated identity and persisted tenant/campus/enrollment membership.
    // Never accept campus, program or batch identifiers from the viewer's request.
    private IQueryable<Notice> VisibleNotices(DateTime now)
    {
        var tenant = user.TenantId;
        var uid = user.UserId;
        var campuses = db.UserCampusAccesses.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.UserId == uid && x.IsActive).Select(x => x.CampusId);
        var enrollments = db.StudentEnrollments.AsNoTracking().Where(e => e.TenantId == tenant &&
            e.IsCurrent && e.State == EnrollmentState.Active &&
            (db.Students.Any(s => s.TenantId == tenant && s.Id == e.StudentId && s.UserId == uid) ||
             db.StudentGuardians.Any(g => g.TenantId == tenant && g.StudentId == e.StudentId &&
                 db.Guardians.Any(z => z.TenantId == tenant && z.Id == g.GuardianId && z.UserId == uid))));
        return db.Notices.AsNoTracking().Where(n => n.TenantId == tenant && n.IsPublished &&
            db.TenantMemberships.Any(m => m.TenantId == tenant && m.UserId == uid && m.Status == MembershipStatus.Active) &&
            n.PublishAt <= now && (!n.ExpiresAt.HasValue || n.ExpiresAt > now) &&
            db.NoticeAudiences.Any(a => a.TenantId == tenant && a.NoticeId == n.Id &&
                (a.AudienceTypeCode == "All" ||
                 (a.AudienceTypeCode == "User" && a.UserId == uid) ||
                 (a.AudienceTypeCode == "Campus" && a.CampusId.HasValue &&
                     (campuses.Contains(a.CampusId.Value) || enrollments.Any(e => e.CampusId == a.CampusId.Value))) ||
                 (a.AudienceTypeCode == "Program" && a.AcademicProgramId.HasValue &&
                     enrollments.Any(e => e.AcademicProgramId == a.AcademicProgramId.Value)) ||
                 (a.AudienceTypeCode == "Batch" && a.AcademicBatchId.HasValue &&
                     enrollments.Any(e => e.AcademicBatchId == a.AcademicBatchId.Value)))));
    }

    public async Task<ApiResponse<PagedResult<NoticeDto>>> GetNoticesAsync(int page, int pageSize, bool? published, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<PagedResult<NoticeDto>>.ErrorResponse("Sign in to view notices.", 403);
        if (!PageOk(page, pageSize)) return ApiResponse<PagedResult<NoticeDto>>.ErrorResponse("Invalid paging.");
        if (!Admin && published == false) return ApiResponse<PagedResult<NoticeDto>>.ErrorResponse("Drafts are restricted.", 403);
        IQueryable<Notice> query = Admin ? db.Notices.AsNoTracking().Where(x => x.TenantId == user.TenantId) :
            VisibleNotices(DateTime.UtcNow);
        if (Admin && published.HasValue) query = query.Where(x => x.IsPublished == published.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.PublishAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var categoryIds = rows.Where(x => x.NoticeCategoryId.HasValue).Select(x => x.NoticeCategoryId!.Value).Distinct().ToArray();
        var categories = await db.NoticeCategories.AsNoTracking().Where(x => x.TenantId == user.TenantId && categoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var audiences = Admin ? await db.NoticeAudiences.AsNoTracking().Where(x => x.TenantId == user.TenantId && ids.Contains(x.NoticeId))
            .ToListAsync(ct) : new List<NoticeAudience>();
        return ApiResponse<PagedResult<NoticeDto>>.SuccessResponse(new PagedResult<NoticeDto>
        {
            Items = rows.Select(n => Map(n, n.NoticeCategoryId.HasValue ? categories.GetValueOrDefault(n.NoticeCategoryId.Value) : null,
                audiences.Where(a => a.NoticeId == n.Id).Select(Map).ToArray())).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    private async Task<bool> ValidAudienceAsync(SaveNoticeAudienceRequestDto a, long tenant, CancellationToken ct)
    {
        if (a == null) return false;
        var code = a.AudienceTypeCode?.Trim();
        return code switch
        {
            "All" => a.CampusId == null && a.AcademicProgramId == null && a.AcademicBatchId == null && a.UserId == null,
            "User" => a.UserId > 0 && a.CampusId == null && a.AcademicProgramId == null && a.AcademicBatchId == null &&
                await db.TenantMemberships.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.UserId == a.UserId &&
                    x.Status == MembershipStatus.Active && db.ApplicationUsers.Any(u => u.Id == x.UserId && u.IsActive), ct),
            "Campus" => a.CampusId > 0 && a.UserId == null && a.AcademicProgramId == null && a.AcademicBatchId == null &&
                await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Id == a.CampusId && x.IsActive, ct),
            "Program" => a.AcademicProgramId > 0 && a.UserId == null && a.CampusId == null && a.AcademicBatchId == null &&
                await db.AcademicPrograms.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Id == a.AcademicProgramId && x.IsActive, ct),
            "Batch" => a.AcademicBatchId > 0 && a.UserId == null && a.CampusId == null && a.AcademicProgramId == null &&
                await db.AcademicBatches.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Id == a.AcademicBatchId && x.IsActive, ct),
            _ => false
        };
    }
    private static string AudienceKey(string code, long? campus, long? program, long? batch, long? uid) =>
        $"{code}|{campus}|{program}|{batch}|{uid}";

    public async Task<ApiResponse<NoticeDto>> SaveNoticeAsync(Guid? noticeReference, SaveNoticeRequestDto request, CancellationToken ct = default)
    {
        if (!Admin) return ApiResponse<NoticeDto>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 20000 ||
            request.PublishAt.Kind != DateTimeKind.Utc || request.ExpiresAt?.Kind == DateTimeKind.Unspecified ||
            (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= request.PublishAt) ||
            request.Audiences == null || request.Audiences.Count is < 1 or > 25 ||
            noticeReference == Guid.Empty)
            return ApiResponse<NoticeDto>.ErrorResponse("Invalid notice, schedule or audience.");
        var tenant = user.TenantId;
        var selected = request.Audiences.Select(a => new SaveNoticeAudienceRequestDto
        {
            AudienceTypeCode = a?.AudienceTypeCode?.Trim() ?? "", CampusId = a?.CampusId,
            AcademicProgramId = a?.AcademicProgramId, AcademicBatchId = a?.AcademicBatchId, UserId = a?.UserId
        }).ToList();
        if ((selected.Count > 1 && selected.Any(a => a.AudienceTypeCode == "All")) ||
            selected.Select(a => AudienceKey(a.AudienceTypeCode, a.CampusId, a.AcademicProgramId, a.AcademicBatchId, a.UserId))
            .Distinct(StringComparer.Ordinal).Count() != selected.Count)
            return ApiResponse<NoticeDto>.ErrorResponse("Duplicate notice audience.");
        foreach (var a in selected)
            if (!await ValidAudienceAsync(a, tenant, ct)) return ApiResponse<NoticeDto>.ErrorResponse("Invalid or inaccessible notice audience.");
        if (request.NoticeCategoryId.HasValue && !await db.NoticeCategories.AsNoTracking().AnyAsync(
            x => x.TenantId == tenant && x.Id == request.NoticeCategoryId.Value && x.IsActive, ct))
            return ApiResponse<NoticeDto>.ErrorResponse("Notice category not found.");
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                Notice row;
                if (noticeReference.HasValue)
                {
                    row = await db.Notices.FirstOrDefaultAsync(x => x.TenantId == tenant && x.PublicId == noticeReference.Value, token)
                        ?? null!;
                    if (row == null) return ApiResponse<NoticeDto>.ErrorResponse("Notice not found.", 404);
                    if (!SameVersion(row.RowVersion, request.RowVersion))
                        return ApiResponse<NoticeDto>.ErrorResponse("Notice changed; reload before editing.", 409);
                }
                else
                {
                    row = new Notice { TenantId = tenant, CreatedBy = user.UserId };
                    db.Notices.Add(row);
                }
                row.Title = request.Title.Trim(); row.Body = request.Body.Trim();
                row.NoticeCategoryId = request.NoticeCategoryId; row.PublishAt = request.PublishAt;
                row.ExpiresAt = request.ExpiresAt; row.IsPublished = request.IsPublished;
                if (noticeReference.HasValue) { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
                await db.SaveChangesAsync(token);
                // Reuse archived audience rows: the existing tenant-scoped unique index includes IsDeleted rows.
                var existing = await db.NoticeAudiences.IgnoreQueryFilters().Where(x => x.TenantId == tenant && x.NoticeId == row.Id)
                    .ToListAsync(token);
                foreach (var old in existing)
                {
                    var keep = selected.Any(a => AudienceKey(a.AudienceTypeCode, a.CampusId, a.AcademicProgramId, a.AcademicBatchId, a.UserId) ==
                        AudienceKey(old.AudienceTypeCode, old.CampusId, old.AcademicProgramId, old.AcademicBatchId, old.UserId));
                    old.IsDeleted = !keep;
                    if (!keep) { old.DeletedAt = DateTime.UtcNow; old.DeletedBy = user.UserId; }
                    else { old.DeletedAt = null; old.DeletedBy = null; }
                }
                foreach (var a in selected)
                    if (!existing.Any(old => AudienceKey(old.AudienceTypeCode, old.CampusId, old.AcademicProgramId, old.AcademicBatchId, old.UserId) ==
                        AudienceKey(a.AudienceTypeCode, a.CampusId, a.AcademicProgramId, a.AcademicBatchId, a.UserId)))
                        db.NoticeAudiences.Add(new NoticeAudience
                        {
                            TenantId = tenant, NoticeId = row.Id, AudienceTypeCode = a.AudienceTypeCode,
                            CampusId = a.CampusId, AcademicProgramId = a.AcademicProgramId,
                            AcademicBatchId = a.AcademicBatchId, UserId = a.UserId, CreatedBy = user.UserId
                        });
                await db.SaveChangesAsync(token);
                var mapped = existing.Where(x => !x.IsDeleted).Select(Map).ToList();
                mapped.AddRange(db.ChangeTracker.Entries<NoticeAudience>().Where(x => x.State != EntityState.Detached &&
                    x.Entity.NoticeId == row.Id && !x.Entity.IsDeleted && !existing.Contains(x.Entity)).Select(x => Map(x.Entity)));
                return ApiResponse<NoticeDto>.SuccessResponse(Map(row, null, mapped));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<NoticeDto>.ErrorResponse("Notice changed; reload before editing.", 409); }
        catch (DbUpdateException) { return ApiResponse<NoticeDto>.ErrorResponse("Notice could not be saved due to a data conflict.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Notice save failed for tenant {TenantId}", tenant);
            return ApiResponse<NoticeDto>.ErrorResponse("Notice save failed.", 500);
        }
    }

    /// <summary>
    /// Tenant-scoped, idempotent notice archival. Missing/previously archived identifiers
    /// return success without revealing whether another tenant owns the reference.
    /// No global query filter bypass is permitted in this tenant-admin workflow.
    /// </summary>
    public async Task<ApiResponse<bool>> ArchiveNoticeAsync(Guid noticeReference, string? rowVersion, CancellationToken ct = default)
    {
        if (!Admin) return ApiResponse<bool>.ErrorResponse("Tenant administrator required.", 403);
        if (noticeReference == Guid.Empty) return ApiResponse<bool>.ErrorResponse("Invalid notice reference.");
        var tenant = user.TenantId;
        try
        {
            var row = await db.Notices.FirstOrDefaultAsync(
                x => x.TenantId == tenant && x.PublicId == noticeReference, ct);
            // A repeated DELETE is safe, and an inaccessible tenant reference is indistinguishable
            // from a missing one. Never query soft-deleted rows with IgnoreQueryFilters().
            if (row == null) return ApiResponse<bool>.SuccessResponse(true);
            if (!SameVersion(row.RowVersion, rowVersion))
                return ApiResponse<bool>.ErrorResponse("Notice changed; reload before archiving.", 409);
            var now = DateTime.UtcNow;
            row.IsDeleted = true;
            row.DeletedAt = now;
            row.DeletedBy = user.UserId;
            row.UpdatedAt = now;
            row.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("Notice changed; reload before archiving.", 409);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<bool>.ErrorResponse("Notice could not be archived due to a data conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Notice archive failed for tenant {TenantId}", tenant);
            return ApiResponse<bool>.ErrorResponse("Notice archive failed.", 500);
        }
    }

    public async Task<ApiResponse<bool>> MarkNoticeReadAsync(Guid noticeReference, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<bool>.ErrorResponse("Sign in required.", 403);
        var noticeId = await VisibleNotices(DateTime.UtcNow).Where(x => x.PublicId == noticeReference)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (noticeId <= 0) return ApiResponse<bool>.ErrorResponse("Notice not found.", 404);
        try
        {
            await notices.RecordReadAsync(user.TenantId, user.UserId, noticeId, DateTime.UtcNow, ct);
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (DbUpdateException)
        {
            if (await notices.HasUserReadAsync(user.TenantId, user.UserId, noticeId, ct))
                return ApiResponse<bool>.SuccessResponse(true);
            return ApiResponse<bool>.ErrorResponse("Notice receipt conflict.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<NotificationDto>>> GetMyNotificationsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<PagedResult<NotificationDto>>.ErrorResponse("Sign in required.", 403);
        if (!PageOk(page, pageSize)) return ApiResponse<PagedResult<NotificationDto>>.ErrorResponse("Invalid paging.");
        var (rows, total) = await notifications.GetByUserAsync(user.TenantId, user.UserId, page, pageSize, null, ct);
        return ApiResponse<PagedResult<NotificationDto>>.SuccessResponse(new()
        { Items = rows.Select(Map).ToList(), TotalCount = total, Page = page, PageSize = pageSize });
    }
    public async Task<ApiResponse<bool>> MarkNotificationReadAsync(long notificationId, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<bool>.ErrorResponse("Sign in required.", 403);
        if (notificationId <= 0) return ApiResponse<bool>.ErrorResponse("Invalid notification.");
        if (!await db.Notifications.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId && x.Id == notificationId, ct))
            return ApiResponse<bool>.ErrorResponse("Notification not found.", 404);
        await notifications.MarkAsReadAsync(user.TenantId, user.UserId, notificationId, DateTime.UtcNow, ct);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<int>> MarkAllMyNotificationsReadAsync(CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<int>.ErrorResponse("Sign in required.", 403);
        // Repository uses a single tenant/user-scoped conditional update: safe to retry and idempotent.
        var count = await notifications.MarkAllAsReadAsync(user.TenantId, user.UserId, DateTime.UtcNow, ct);
        return ApiResponse<int>.SuccessResponse(count);
    }

    public async Task<ApiResponse<MessageDto>> SendMessageAsync(Guid clientRequestId, SendMessageRequestDto request, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<MessageDto>.ErrorResponse("Sign in required.", 403);
        if (clientRequestId == Guid.Empty || request == null || request.RecipientUserId <= 0 ||
            request.RecipientUserId == user.UserId || string.IsNullOrWhiteSpace(request.Body) ||
            request.Body.Trim().Length > 4000 || request.Subject?.Length > 250 || request.ThreadReference == Guid.Empty)
            return ApiResponse<MessageDto>.ErrorResponse("Invalid message request.");
        var tenant = user.TenantId;
        var sender = user.UserId;
        var body = request.Body.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{request.ThreadReference}|{request.RecipientUserId}|{request.Subject?.Trim()}|{body}")));
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var prior = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                    x.Scope == MessageScope && x.RequestId == clientRequestId, token);
                if (prior != null)
                {
                    if (prior.RequestHash != hash || !long.TryParse(prior.ResultHash, out var messageId))
                        return ApiResponse<MessageDto>.ErrorResponse("Request ID already used for another message.", 409);
                    var old = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                        x.Id == messageId && x.SenderUserId == sender, token);
                    if (old == null) return ApiResponse<MessageDto>.ErrorResponse("Original message unavailable.", 409);
                    var names = await NamesAsync(new[] { sender, old.RecipientUserId }, token);
                    return ApiResponse<MessageDto>.SuccessResponse(Map(old, names));
                }
                if (!await db.TenantMemberships.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.UserId == request.RecipientUserId && x.Status == MembershipStatus.Active &&
                    db.ApplicationUsers.Any(u => u.Id == x.UserId && u.IsActive), token))
                    return ApiResponse<MessageDto>.ErrorResponse("Recipient is not an active tenant member.", 404);
                MessageThread thread;
                if (request.ThreadReference.HasValue)
                {
                    thread = await db.MessageThreads.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                        x.PublicId == request.ThreadReference.Value, token) ?? null!;
                    if (thread == null) return ApiResponse<MessageDto>.ErrorResponse("Thread not found.", 404);
                    var both = await db.MessageThreadParticipants.AsNoTracking().Where(x => x.TenantId == tenant &&
                        x.MessageThreadId == thread.Id && x.LeftAt == null &&
                        (x.UserId == sender || x.UserId == request.RecipientUserId)).Select(x => x.UserId).Distinct().CountAsync(token);
                    if (both != 2) return ApiResponse<MessageDto>.ErrorResponse("Thread participants do not match.", 403);
                }
                else
                {
                    thread = new MessageThread { TenantId = tenant, Subject = request.Subject?.Trim(),
                        LastMessageAt = DateTime.UtcNow, CreatedBy = sender };
                    db.MessageThreads.Add(thread);
                    await db.SaveChangesAsync(token);
                    db.MessageThreadParticipants.AddRange(
                        new MessageThreadParticipant { TenantId = tenant, MessageThreadId = thread.Id, UserId = sender, CreatedBy = sender },
                        new MessageThreadParticipant { TenantId = tenant, MessageThreadId = thread.Id, UserId = request.RecipientUserId, CreatedBy = sender });
                }
                var msg = new Message { TenantId = tenant, MessageThreadId = thread.Id,
                    SenderUserId = sender, RecipientUserId = request.RecipientUserId,
                    Body = body, SentAt = DateTime.UtcNow, CreatedBy = sender };
                db.Messages.Add(msg);
                thread.LastMessageAt = msg.SentAt; thread.UpdatedAt = msg.SentAt; thread.UpdatedBy = sender;
                await db.SaveChangesAsync(token);
                db.IdempotencyRecords.Add(new IdempotencyRecord { TenantId = tenant, Scope = MessageScope,
                    RequestId = clientRequestId, RequestHash = hash, ResultHash = msg.Id.ToString(),
                    ExpiresAt = DateTime.UtcNow.AddDays(90), CreatedBy = sender });
                await db.SaveChangesAsync(token);
                var namesNew = await NamesAsync(new[] { sender, request.RecipientUserId }, token);
                return ApiResponse<MessageDto>.SuccessResponse(Map(msg, namesNew));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<MessageDto>.ErrorResponse("Thread changed; retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<MessageDto>.ErrorResponse("Duplicate or conflicting message request.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Message send failed for tenant {TenantId}", tenant);
            return ApiResponse<MessageDto>.ErrorResponse("Message could not be sent.", 500);
        }
    }
    private Task<Dictionary<long, string>> NamesAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var values = ids.Distinct().ToArray();
        return db.ApplicationUsers.AsNoTracking().Where(x => values.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }
    private IQueryable<MessageThread> MyThreads() => db.MessageThreads.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
        db.MessageThreadParticipants.Any(p => p.TenantId == user.TenantId && p.MessageThreadId == x.Id &&
            p.UserId == user.UserId && p.LeftAt == null));
    public async Task<ApiResponse<MessageThreadDto>> GetThreadAsync(Guid threadReference, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<MessageThreadDto>.ErrorResponse("Sign in required.", 403);
        var row = await MyThreads().FirstOrDefaultAsync(x => x.PublicId == threadReference, ct);
        return row == null ? ApiResponse<MessageThreadDto>.ErrorResponse("Thread not found.", 404) :
            ApiResponse<MessageThreadDto>.SuccessResponse(new MessageThreadDto
            { Id = row.Id, Reference = row.PublicId, Subject = row.Subject, LastMessageAt = row.LastMessageAt });
    }
    public async Task<ApiResponse<PagedResult<MessageDto>>> GetThreadMessagesAsync(Guid threadReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<PagedResult<MessageDto>>.ErrorResponse("Sign in required.", 403);
        if (!PageOk(page, pageSize)) return ApiResponse<PagedResult<MessageDto>>.ErrorResponse("Invalid paging.");
        var threadId = await MyThreads().Where(x => x.PublicId == threadReference).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (threadId == 0) return ApiResponse<PagedResult<MessageDto>>.ErrorResponse("Thread not found.", 404);
        var query = db.Messages.AsNoTracking().Where(x => x.TenantId == user.TenantId && x.MessageThreadId == threadId);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.SentAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await NamesAsync(rows.SelectMany(x => new[] { x.SenderUserId, x.RecipientUserId }), ct);
        return ApiResponse<PagedResult<MessageDto>>.SuccessResponse(new()
        { Items = rows.Select(x => Map(x, names)).ToList(), TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<IReadOnlyList<NotificationPreferenceDto>>> GetMyPreferencesAsync(CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<IReadOnlyList<NotificationPreferenceDto>>.ErrorResponse("Sign in required.", 403);
        var rows = await db.NotificationPreferences.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId).OrderBy(x => x.Channel).ThenBy(x => x.EventCode).Take(200).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<NotificationPreferenceDto>>.SuccessResponse(rows.Select(Map).ToArray());
    }
    public async Task<ApiResponse<NotificationPreferenceDto>> SaveMyPreferenceAsync(SaveNotificationPreferenceRequestDto request, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<NotificationPreferenceDto>.ErrorResponse("Sign in required.", 403);
        if (request == null || !Enum.IsDefined(request.Channel) || string.IsNullOrWhiteSpace(request.EventCode) ||
            request.EventCode.Length > 100 || !Regex.IsMatch(request.EventCode.Trim(), @"^[A-Za-z][A-Za-z0-9_.-]+$"))
            return ApiResponse<NotificationPreferenceDto>.ErrorResponse("Invalid notification preference.");
        var code = request.EventCode.Trim().ToUpperInvariant();
        try
        {
            var row = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.UserId == user.UserId && x.Channel == request.Channel && x.EventCode == code, ct);
            if (row == null)
            {
                row = new NotificationPreference { TenantId = user.TenantId, UserId = user.UserId,
                    Channel = request.Channel, EventCode = code, CreatedBy = user.UserId };
                db.NotificationPreferences.Add(row);
            }
            else if (!SameVersion(row.RowVersion, request.RowVersion))
                return ApiResponse<NotificationPreferenceDto>.ErrorResponse("Preference changed; reload.", 409);
            else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
            row.IsEnabled = request.IsEnabled;
            await db.SaveChangesAsync(ct);
            return ApiResponse<NotificationPreferenceDto>.SuccessResponse(Map(row));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<NotificationPreferenceDto>.ErrorResponse("Preference changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<NotificationPreferenceDto>.ErrorResponse("Preference already exists; reload.", 409); }
    }
    public async Task<ApiResponse<DeviceTokenDto>> RegisterDeviceTokenAsync(RegisterDeviceTokenRequestDto request, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<DeviceTokenDto>.ErrorResponse("Sign in required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 512 ||
            string.IsNullOrWhiteSpace(request.Platform) || request.Platform.Length > 50 ||
            string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 100)
            return ApiResponse<DeviceTokenDto>.ErrorResponse("Valid token, platform and device ID required.");
        var platform = request.Platform.Trim().ToLowerInvariant();
        var device = request.DeviceId.Trim();
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var row = await db.DeviceTokens.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.UserId == user.UserId && x.Platform == platform && x.DeviceId == device, token);
                if (row == null)
                {
                    row = new DeviceToken { TenantId = user.TenantId, UserId = user.UserId,
                        Platform = platform, DeviceId = device, CreatedBy = user.UserId };
                    db.DeviceTokens.Add(row);
                }
                else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
                var encrypted = protector.CreateProtector("EduOS.Communication.DeviceTokens.v1").Protect(request.Token.Trim());
                if (encrypted.Length > 1000) return ApiResponse<DeviceTokenDto>.ErrorResponse("Device token is too long.");
                row.Token = encrypted; row.IsActive = true; row.LastSeenAt = DateTime.UtcNow;
                await db.SaveChangesAsync(token);
                return ApiResponse<DeviceTokenDto>.SuccessResponse(Map(row));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException) { return ApiResponse<DeviceTokenDto>.ErrorResponse("Device token conflict.", 409); }
    }
    public async Task<ApiResponse<bool>> DeactivateDeviceTokenAsync(long tokenId, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<bool>.ErrorResponse("Sign in required.", 403);
        var row = await db.DeviceTokens.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId && x.Id == tokenId, ct);
        if (row == null) return ApiResponse<bool>.ErrorResponse("Device not found.", 404);
        if (!row.IsActive) return ApiResponse<bool>.SuccessResponse(true);
        row.IsActive = false; row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId;
        try { await db.SaveChangesAsync(ct); return ApiResponse<bool>.SuccessResponse(true); }
        catch (DbUpdateConcurrencyException) { return ApiResponse<bool>.ErrorResponse("Device changed; reload.", 409); }
    }
}
