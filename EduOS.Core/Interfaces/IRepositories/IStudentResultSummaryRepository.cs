using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Published results are historical snapshots keyed by publication version and enrollment, not mutable assessment state.</summary>
public interface IStudentResultSummaryRepository : IGenericRepository<StudentResultSummary>
{
    Task<StudentResultSummary?> GetByPublicationAndEnrollmentAsync(long resultPublicationId, long studentEnrollmentId, CancellationToken cancellationToken = default);
    Task<(List<StudentResultSummary> Items, int TotalCount)> GetByPublicationAsync(long resultPublicationId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<List<StudentResultSummary>> GetTopRankersAsync(long resultPublicationId, int top = 10, CancellationToken cancellationToken = default);
}
