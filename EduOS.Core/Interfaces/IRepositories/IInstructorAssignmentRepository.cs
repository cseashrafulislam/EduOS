using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>InstructorAssignment is related to SubjectOffering, not an academic-year-wide advisor record.</summary>
public interface IInstructorAssignmentRepository : IGenericRepository<InstructorAssignment>
{
    Task<List<InstructorAssignment>> GetByEmployeeAsync(long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
    Task<List<InstructorAssignment>> GetByAcademicBatchAsync(long academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
    Task<InstructorAssignment?> GetPrimaryBySubjectOfferingAsync(long subjectOfferingId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
}
