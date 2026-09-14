using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGuardianRepository : IGenericRepository<Guardian>
    {
        Task<List<Guardian>> GetByStudentIdAsync(long studentId);
        Task<Guardian?> GetPrimaryByStudentIdAsync(long studentId);
        Task<Guardian?> GetByPhoneAsync(string phone);
    }
}
