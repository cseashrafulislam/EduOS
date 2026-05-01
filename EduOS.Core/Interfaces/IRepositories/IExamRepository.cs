using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IExamRepository : IGenericRepository<Exam>
    {
        Task<List<Exam>> GetByYearAsync(int academicYearId);
        Task<List<Exam>> GetPublishedAsync(int academicYearId);
        Task<Exam?> GetWithSchedulesAsync(int id);
    }
}
