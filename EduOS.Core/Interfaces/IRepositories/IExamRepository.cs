using EduOS.Core.Entities.Exams;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IExamRepository : IGenericRepository<Exam>
    {
        Task<List<Exam>> GetByYearAsync(long academicYearId);
        Task<List<Exam>> GetPublishedAsync(long academicYearId);
        Task<Exam?> GetWithSchedulesAsync(long id);
    }
}
