using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Guardian-to-student membership is held in StudentGuardian; primary guardian selection is link-scoped.</summary>
public interface IGuardianRepository : IGenericRepository<Guardian>
{
    Task<List<Guardian>> GetByStudentIdAsync(long studentId, CancellationToken cancellationToken = default);
    Task<Guardian?> GetPrimaryByStudentIdAsync(long studentId, CancellationToken cancellationToken = default);
    Task<Guardian?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
}
