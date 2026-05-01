using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IGuardianRepository : IGenericRepository<Guardian>
    {
        Task<List<Guardian>> GetByStudentIdAsync(int studentId);
        Task<Guardian?> GetPrimaryByStudentIdAsync(int studentId);
        Task<Guardian?> GetByPhoneAsync(string phone);
    }
}
