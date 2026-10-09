using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class NoticeRepository : GenericRepository<Notice>, INoticeRepository
{
    public NoticeRepository(EduOSDbContext context) : base(context) { }

    public Task<List<Notice>> GetActiveAsync(long tenantId)
    {
        var now = DateTime.UtcNow;
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished
                && x.PublishAt <= now && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value >= now))
            .OrderByDescending(x => x.PublishAt).ToListAsync();
    }

    public async Task<List<Notice>> GetByAudienceAsync(string audience, long tenantId)
    {
        var now = DateTime.UtcNow;
        var noticeIds = _context.Set<NoticeAudience>()
            .Where(x => x.TenantId == tenantId
                && (x.AudienceTypeCode == "All" || x.AudienceTypeCode == audience))
            .Select(x => x.NoticeId);
        return await _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished
                && x.PublishAt <= now && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value >= now)
                && noticeIds.Contains(x.Id))
            .OrderByDescending(x => x.PublishAt).ToListAsync();
    }

    public Task<List<Notice>> GetRecentAsync(long tenantId, int count = 10)
    {
        var take = Math.Clamp(count, 1, 100);
        return _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished)
            .OrderByDescending(x => x.PublishAt).Take(take).ToListAsync();
    }

    public Task<(List<Notice> Items, int TotalCount)> GetActiveAsync(long tenantId, DateTime utcNow, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished
            && x.PublishAt <= utcNow && (x.ExpiresAt == null || x.ExpiresAt >= utcNow))
            .OrderByDescending(x => x.PublishAt).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<Notice> Items, int TotalCount)> GetVisibleToUserAsync(long tenantId, long userId, long? campusId,
        long? academicProgramId, long? academicBatchId, DateTime utcNow, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (userId <= 0 || tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var noticeIds = _context.Set<NoticeAudience>().Where(x => x.TenantId == tenantId
            && (x.AudienceTypeCode == "All"
                || (x.AudienceTypeCode == "User" && x.UserId == userId)
                || (x.AudienceTypeCode == "Campus" && campusId.HasValue && x.CampusId == campusId)
                || (x.AudienceTypeCode == "Program" && academicProgramId.HasValue && x.AcademicProgramId == academicProgramId)
                || (x.AudienceTypeCode == "Batch" && academicBatchId.HasValue && x.AcademicBatchId == academicBatchId)))
            .Select(x => x.NoticeId);
        return PageAsync(_dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsPublished
            && x.PublishAt <= utcNow && (x.ExpiresAt == null || x.ExpiresAt >= utcNow) && noticeIds.Contains(x.Id))
            .OrderByDescending(x => x.PublishAt).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    }
    public Task<bool> HasUserReadAsync(long tenantId, long userId, long noticeId, CancellationToken cancellationToken) =>
        _context.Set<NoticeReadReceipt>().AsNoTracking().AnyAsync(x => x.TenantId == tenantId
            && x.UserId == userId && x.NoticeId == noticeId, cancellationToken);
    public Task<bool> RecordReadAsync(long tenantId, long userId, long noticeId, DateTime readAtUtc, CancellationToken cancellationToken)
    {
        if (readAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Read timestamp must be UTC.", nameof(readAtUtc));
        if (tenantId <= 0 || userId <= 0 || noticeId <= 0) throw new ArgumentOutOfRangeException(nameof(noticeId));
        return _context.ExecuteInTransactionAsync(async ct =>
        {
            var receipts = _context.Set<NoticeReadReceipt>();
            if (await receipts.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId && x.NoticeId == noticeId, ct))
                return false;
            if (!await _dbSet.AnyAsync(x => x.TenantId == tenantId && x.Id == noticeId, ct))
                return false;
            await receipts.AddAsync(new NoticeReadReceipt { TenantId = tenantId, UserId = userId,
                NoticeId = noticeId, ReadAt = readAtUtc }, ct);
            await _context.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
    }
}
