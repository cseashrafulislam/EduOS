using System.Data;
using System.Security.Cryptography;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Communication;

/// <summary>Private, scanned attachments for the existing tenant message aggregate.</summary>
public sealed class MessageAttachmentService(
    EduOSDbContext db, ICurrentUserService user, IFileUploadService storage,
    IUnitOfWork unit, ILogger<MessageAttachmentService> logger) : IMessageAttachmentService
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private const int MaxAttachments = 3;
    private bool SignedIn => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0;
    internal static bool TenantMessageKey(string? key, long tenant) =>
        tenant > 0 && !string.IsNullOrWhiteSpace(key) &&
        key.StartsWith($"tenant-{tenant}/message-attachments/", StringComparison.Ordinal) &&
        !key.Contains('\\') && !key.Split('/').Any(x => x is "" or "." or "..");

    private IQueryable<Message> AccessibleMessages() => db.Messages.AsNoTracking().Where(m =>
        m.TenantId == user.TenantId &&
        (m.SenderUserId == user.UserId || m.RecipientUserId == user.UserId) &&
        db.MessageThreadParticipants.Any(p => p.TenantId == user.TenantId &&
            p.MessageThreadId == m.MessageThreadId && p.UserId == user.UserId && p.LeftAt == null) &&
        db.TenantMemberships.Any(p => p.TenantId == user.TenantId &&
            p.UserId == user.UserId && p.Status == MembershipStatus.Active));

    private static bool Supported(string fileName, string mime) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
            ".png" => mime.Equals("image/png", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".webp" => mime.Equals("image/webp", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static MessageAttachmentDto Map(MessageAttachment link, FileAsset asset) => new()
    {
        Reference = asset.PublicId, MessageId = link.MessageId,
        FileName = asset.OriginalFileName, ContentType = asset.ContentType,
        SizeBytes = asset.SizeBytes, UploadedAt = asset.UploadedAt
    };

    public async Task<ApiResponse<MessageAttachmentListingDto>> GetForMessageAsync(long messageId, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<MessageAttachmentListingDto>.ErrorResponse("Sign in required.", 403);
        if (messageId <= 0) return ApiResponse<MessageAttachmentListingDto>.ErrorResponse("Invalid message.");
        var message = await AccessibleMessages().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message == null) return ApiResponse<MessageAttachmentListingDto>.ErrorResponse("Message not found.", 404);
        var links = await (from a in db.MessageAttachments.AsNoTracking()
            join f in db.FileAssets.AsNoTracking() on new { a.TenantId, Id = a.FileAssetId }
                equals new { f.TenantId, Id = f.Id }
            where a.TenantId == user.TenantId && a.MessageId == messageId &&
                !a.IsDeleted && !f.IsDeleted && f.IsVerifiedSafe &&
                f.MalwareScanStateCode == "Clean" && f.Visibility == FileVisibility.Private &&
                f.StorageProvider == "LocalPrivate"
            orderby a.Id
            select new { Link = a, Asset = f }).Take(MaxAttachments + 1).ToListAsync(ct);
        var items = links.Where(x => TenantMessageKey(x.Asset.StorageKey, user.TenantId))
            .Select(x => Map(x.Link, x.Asset)).ToArray();
        return ApiResponse<MessageAttachmentListingDto>.SuccessResponse(new MessageAttachmentListingDto
        {
            CanAttach = message.SenderUserId == user.UserId && links.Count < MaxAttachments,
            Items = items
        });
    }

    public async Task<ApiResponse<MessageAttachmentDto>> UploadAsync(long messageId, Guid requestId,
        PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<MessageAttachmentDto>.ErrorResponse("Sign in required.", 403);
        if (messageId <= 0 || requestId == Guid.Empty || upload == null || upload.Content == null ||
            upload.Length is < 1 or > MaxBytes || string.IsNullOrWhiteSpace(upload.FileName) ||
            upload.FileName.Length > 255 || string.IsNullOrWhiteSpace(upload.ContentType) ||
            upload.ContentType.Length > 150)
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Invalid attachment request.");
        var tenant = user.TenantId;
        var sender = user.UserId;
        var message = await AccessibleMessages().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message == null || message.SenderUserId != sender)
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Message not found.", 404);
        if (!await db.TenantMemberships.AsNoTracking().AnyAsync(m => m.TenantId == tenant &&
            m.UserId == message.RecipientUserId && m.Status == MembershipStatus.Active, ct))
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Recipient is not an active member.", 409);
        var fileName = Path.GetFileName(upload.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || !Supported(fileName, upload.ContentType))
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Only PDF, PNG, JPEG and WebP attachments are supported.");

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await upload.Content.ReadAsync(chunk.AsMemory(), ct)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                    return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment exceeds 5 MB.", 413);
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
        }
        catch (IOException) { return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment could not be read."); }
        if (buffer.Length != upload.Length)
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment length mismatch.");
        var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        var existing = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(f =>
            f.TenantId == tenant && f.PublicId == requestId, ct);
        if (existing != null)
        {
            var link = await db.MessageAttachments.AsNoTracking().FirstOrDefaultAsync(a =>
                a.TenantId == tenant && a.FileAssetId == existing.Id && !a.IsDeleted, ct);
            return link != null && link.MessageId == messageId && existing.UploadedByUserId == sender &&
                existing.IsVerifiedSafe && existing.MalwareScanStateCode == "Clean" &&
                TenantMessageKey(existing.StorageKey, tenant) && existing.Sha256 == hash &&
                existing.SizeBytes == buffer.Length && existing.OriginalFileName == fileName &&
                existing.ContentType == upload.ContentType
                ? ApiResponse<MessageAttachmentDto>.SuccessResponse(Map(link, existing))
                : ApiResponse<MessageAttachmentDto>.ErrorResponse("Upload request ID already used.", 409);
        }

        // The same tenant plan covers stored documents and private message attachments.
        // Keep the existing idempotent replay path available even at capacity.
        var quota = await SubscriptionStorageQuotaPolicy.GetAsync(db, tenant, DateTime.UtcNow, ct);
        if (!quota.CanAdd(buffer.Length))
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Subscription storage limit reached.", 409);

        buffer.Position = 0;
        var file = new FormFile(buffer, 0, buffer.Length, "file", fileName)
        { Headers = new HeaderDictionary(), ContentType = upload.ContentType };
        if (!storage.ValidateFile(file))
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment signature or format is invalid.");
        FileUploadResult stored;
        try { stored = await storage.UploadPrivateAsync(file, "message-attachments"); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Private message upload failed for tenant {TenantId}", tenant);
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Private attachment storage unavailable.", 503);
        }
        if (!stored.Success || !TenantMessageKey(stored.FileUrl, tenant))
        {
            // Never delete an unexpected storage key; it could belong to another tenant.
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Private attachment storage unavailable.", 503);
        }
        var key = stored.FileUrl;
        var committed = false;
        FileAsset? asset = null;
        MessageAttachment? attachment = null;
        try
        {
            var scanned = await storage.ScanPrivateForMalwareAsync(key, ct);
            if (!string.Equals(scanned, hash, StringComparison.Ordinal))
                return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment security scan unavailable.", 503);
            asset = new FileAsset
            {
                TenantId = tenant, PublicId = requestId, StorageProvider = "LocalPrivate",
                StorageKey = key, OriginalFileName = fileName, ContentType = upload.ContentType,
                SizeBytes = buffer.Length, Sha256 = hash, UploadedByUserId = sender,
                MalwareScanStateCode = "Clean", MalwareScanCompletedAt = DateTime.UtcNow,
                Visibility = FileVisibility.Private, IsVerifiedSafe = true, CreatedBy = sender
            };
            var result = await unit.ExecuteInTransactionAsync(async token =>
            {
                // Serializable isolation prevents competing requests exceeding the per-message cap.
                if (!await AccessibleMessages().AnyAsync(m => m.Id == messageId && m.SenderUserId == sender, token))
                    return ApiResponse<MessageAttachmentDto>.ErrorResponse("Message no longer accessible.", 404);
                if (!await db.TenantMemberships.AsNoTracking().AnyAsync(m => m.TenantId == tenant && m.UserId == message.RecipientUserId && m.Status == MembershipStatus.Active, token))
                    return ApiResponse<MessageAttachmentDto>.ErrorResponse("Recipient no longer active.", 409);
                if (await db.MessageAttachments.CountAsync(a => a.TenantId == tenant &&
                    a.MessageId == messageId && !a.IsDeleted, token) >= MaxAttachments)
                    return ApiResponse<MessageAttachmentDto>.ErrorResponse("Maximum three attachments per message.", 409);
                // A serializable registration check arbitrates competing document
                // and message uploads against the same committed tenant file total.
                var currentQuota = await SubscriptionStorageQuotaPolicy.GetAsync(
                    db, tenant, DateTime.UtcNow, token);
                if (!currentQuota.CanAdd(asset.SizeBytes))
                    return ApiResponse<MessageAttachmentDto>.ErrorResponse("Subscription storage limit reached.", 409);
                db.FileAssets.Add(asset);
                await db.SaveChangesAsync(token);
                attachment = new MessageAttachment
                {
                    TenantId = tenant, MessageId = messageId, FileAssetId = asset.Id, CreatedBy = sender
                };
                db.MessageAttachments.Add(attachment);
                await db.SaveChangesAsync(token);
                return ApiResponse<MessageAttachmentDto>.SuccessResponse(Map(attachment, asset));
            }, IsolationLevel.Serializable, ct);
            committed = result.Success;
            return result;
        }
        catch (DbUpdateException)
        {
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment request conflicted; reload files.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Message attachment failed for tenant {TenantId}, message {MessageId}", tenant, messageId);
            return ApiResponse<MessageAttachmentDto>.ErrorResponse("Attachment could not be saved.", 503);
        }
        finally
        {
            if (!committed)
            {
                if (attachment != null) db.Entry(attachment).State = EntityState.Detached;
                if (asset != null) db.Entry(asset).State = EntityState.Detached;
                try
                {
                    // A commit could have succeeded despite a lost response. Never delete its file.
                    var persisted = await db.FileAssets.AsNoTracking().AnyAsync(f =>
                        f.TenantId == tenant && f.PublicId == requestId && f.StorageKey == key, CancellationToken.None);
                    if (!persisted && !await storage.DeletePrivateAsync(key))
                        logger.LogWarning("Orphan attachment cleanup failed for tenant {TenantId}", tenant);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Attachment cleanup deferred for tenant {TenantId}", tenant);
                }
            }
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(long messageId,
        Guid reference, CancellationToken ct = default)
    {
        if (!SignedIn) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Sign in required.", 403);
        if (messageId <= 0 || reference == Guid.Empty)
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Invalid attachment.");
        if (!await AccessibleMessages().AnyAsync(m => m.Id == messageId, ct))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Attachment not found.", 404);
        var asset = await (from a in db.MessageAttachments.AsNoTracking()
            join f in db.FileAssets.AsNoTracking() on new { a.TenantId, Id = a.FileAssetId }
                equals new { f.TenantId, Id = f.Id }
            where a.TenantId == user.TenantId && a.MessageId == messageId && !a.IsDeleted &&
                f.TenantId == user.TenantId && f.PublicId == reference && !f.IsDeleted &&
                f.IsVerifiedSafe && f.MalwareScanStateCode == "Clean" &&
                f.Visibility == FileVisibility.Private && f.StorageProvider == "LocalPrivate"
            select f).FirstOrDefaultAsync(ct);
        if (asset == null || !TenantMessageKey(asset.StorageKey, user.TenantId))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Attachment not found.", 404);
        FileDownloadResult? file;
        try { file = await storage.GetPrivateFileAsync(asset.StorageKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Attachment storage read failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Attachment unavailable.", 503);
        }
        if (file == null || file.Content.LongLength != asset.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(file.Content)), asset.Sha256, StringComparison.Ordinal))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Attachment integrity check failed.", 409);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = file.Content, ContentType = asset.ContentType,
            FileName = Path.GetFileName(asset.OriginalFileName.Replace('\\', '/'))
        });
    }
}
