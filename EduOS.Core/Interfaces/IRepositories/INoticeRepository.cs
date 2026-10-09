using EduOS.Core.Entities.Communication;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Notice visibility must join NoticeAudience and verify effective campus/program/batch and user scope.</summary>
public interface INoticeRepository : IGenericRepository<Notice>
{
    Task<(List<Notice> Items, int TotalCount)> GetActiveAsync(long tenantId, DateTime utcNow, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<Notice> Items, int TotalCount)> GetVisibleToUserAsync(long tenantId, long userId, long? campusId, long? academicProgramId, long? academicBatchId, DateTime utcNow, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> HasUserReadAsync(long tenantId, long userId, long noticeId, CancellationToken cancellationToken = default);
    Task<bool> RecordReadAsync(long tenantId, long userId, long noticeId, DateTime readAtUtc, CancellationToken cancellationToken = default);
}
