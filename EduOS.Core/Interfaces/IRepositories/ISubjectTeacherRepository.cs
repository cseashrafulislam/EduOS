using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IInstructorAssignmentRepository : IGenericRepository<InstructorAssignment>
    {
        Task<List<InstructorAssignment>> GetByEmployeeAsync(long employeeId, long academicYearId);
        Task<List<InstructorAssignment>> GetByBatchAsync(long academicBatchId);
        Task<InstructorAssignment?> GetAdvisorAsync(long academicBatchId, long academicYearId);
    }
}
