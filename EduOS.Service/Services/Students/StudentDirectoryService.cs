using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

public sealed class StudentDirectoryService : IStudentDirectoryService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase) { "Active", "Suspended", "TC", "Passed", "Dropout", "Archived" };
    private readonly IGenericRepository<Student> _students;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<StudentDirectoryService> _logger;

    public StudentDirectoryService(IGenericRepository<Student> students, ICurrentUserService currentUser, ILogger<StudentDirectoryService> logger)
    {
        _students = students;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<StudentDirectoryListItemDto>>> GetPageAsync(StudentDirectoryQueryDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<PagedResult<StudentDirectoryListItemDto>>();
        if (request.Page < 1 || request.PageSize is < 1 or > 100) return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.ErrorResponse("Pagination is invalid.");
        var status = request.Status?.Trim();
        if (status != null && !AllowedStatuses.Contains(status)) return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.ErrorResponse("Student status is invalid.");

        try
        {
            var tenantId = _currentUser.TenantId;
            var query = _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId);
            var search = request.Search?.Trim();
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.StudentCode.Contains(search) || x.Roll.Contains(search) || x.FullName.Contains(search));
            if (request.AcademicYearId.HasValue) query = query.Where(x => x.AcademicYearId == request.AcademicYearId.Value);
            if (request.AcademicUnitId.HasValue) query = query.Where(x => x.ClassId == request.AcademicUnitId.Value);
            if (status != null) query = query.Where(x => x.Status == status);

            var total = await query.CountAsync(cancellationToken);
            var items = await query.Include(x => x.AcademicYear).Include(x => x.Class).Include(x => x.Section)
                .OrderBy(x => x.FullName).ThenBy(x => x.Roll).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                .Select(x => new StudentDirectoryListItemDto
                {
                    Reference = x.PublicId, StudentCode = x.StudentCode, Roll = x.Roll, FullName = x.FullName,
                    FullNameBangla = x.FullNameBangla, MaskedMobile = MaskMobile(x.Phone), AcademicYear = x.AcademicYear != null ? x.AcademicYear.Name : string.Empty,
                    AcademicUnit = x.Class != null ? x.Class.Name : string.Empty, Section = x.Section != null ? x.Section.Name : string.Empty, Status = x.Status
                }).ToListAsync(cancellationToken);
            return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.SuccessResponse(new PagedResult<StudentDirectoryListItemDto>
            { Items = items, TotalCount = total, Page = request.Page, PageSize = request.PageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student directory failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.ErrorResponse("Students could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<StudentDirectoryDetailsDto>> GetAsync(Guid reference, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<StudentDirectoryDetailsDto>();
        if (reference == Guid.Empty) return ApiResponse<StudentDirectoryDetailsDto>.ErrorResponse("Student reference is invalid.");
        try
        {
            var student = await _students.GetQueryable().AsNoTracking().AsSplitQuery()
                .Include(x => x.AcademicYear).Include(x => x.Class).Include(x => x.Section)
                .Include(x => x.Guardians)
                .Include(x => x.Enrollments).ThenInclude(x => x.AcademicYear)
                .Include(x => x.Enrollments).ThenInclude(x => x.AcademicTerm)
                .Include(x => x.Enrollments).ThenInclude(x => x.Campus)
                .Include(x => x.Enrollments).ThenInclude(x => x.Class)
                .Include(x => x.Enrollments).ThenInclude(x => x.Section)
                .Include(x => x.Enrollments).ThenInclude(x => x.Group)
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == reference, cancellationToken);
            if (student == null) return ApiResponse<StudentDirectoryDetailsDto>.ErrorResponse("Student not found.", 404);
            return ApiResponse<StudentDirectoryDetailsDto>.SuccessResponse(MapDetails(student));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student details failed for {Reference} in tenant {TenantId}", reference, _currentUser.TenantId);
            return ApiResponse<StudentDirectoryDetailsDto>.ErrorResponse("Student details could not be loaded.", 500);
        }
    }

    private static StudentDirectoryDetailsDto MapDetails(Student x) => new()
    {
        Reference = x.PublicId, StudentCode = x.StudentCode, Roll = x.Roll, FullName = x.FullName, FullNameBangla = x.FullNameBangla,
        MaskedMobile = MaskMobile(x.Phone), AcademicYear = x.AcademicYear?.Name ?? string.Empty, AcademicUnit = x.Class?.Name ?? string.Empty,
        Section = x.Section?.Name ?? string.Empty, Status = x.Status, DateOfBirth = x.DOB.Date, Gender = x.Gender,
        Phone = x.Phone, Email = x.Email, Address = x.Address, PreferredLanguage = x.PreferredLanguage, AdmissionDate = x.AdmissionDate,
        Guardians = x.Guardians.Where(g => !g.IsDeleted).OrderByDescending(g => g.IsPrimary).Select(g => new StudentGuardianDto
        { Reference = g.PublicId, Name = g.Name, NameBangla = g.NameBangla, Relation = g.Relation, Phone = g.Phone,
            Email = g.Email, Address = g.Address, IsPrimary = g.IsPrimary }).ToList(),
        Enrollments = x.Enrollments.Where(e => !e.IsDeleted).OrderByDescending(e => e.EnrollmentDate).Select(e => new StudentEnrollmentDto
        { Id = e.Id, AcademicYear = e.AcademicYear?.Name ?? string.Empty, AcademicTerm = e.AcademicTerm?.Name, Campus = e.Campus?.Name,
            AcademicUnit = e.Class?.Name ?? string.Empty, Section = e.Section?.Name ?? string.Empty, Group = e.Group?.Name,
            Roll = e.Roll, EnrollmentDate = e.EnrollmentDate, IsActive = e.IsActive }).ToList()
    };

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0
                              && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("AdmissionOfficer"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Student directory access is required.", 403);
    private static string MaskMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var visible = Math.Min(4, value.Length);
        return new string('•', value.Length - visible) + value[^visible..];
    }
}
