using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

public sealed class StudentDirectoryService : IStudentDirectoryService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active", "Suspended", "TC", "Passed", "Dropout", "Archived", "Transferred", "Withdrawn"
    };

    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicTrack> _tracks;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<StudentDirectoryService> _logger;

    public StudentDirectoryService(
        IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<Guardian> guardians,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms,
        IGenericRepository<AcademicLevel> levels,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AcademicTrack> tracks,
        IGenericRepository<Campus> campuses,
        ICurrentUserService currentUser,
        ILogger<StudentDirectoryService> logger)
    {
        _students = students;
        _enrollments = enrollments;
        _studentGuardians = studentGuardians;
        _guardians = guardians;
        _years = years;
        _terms = terms;
        _levels = levels;
        _batches = batches;
        _tracks = tracks;
        _campuses = campuses;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<StudentDirectoryListItemDto>>> GetPageAsync(
        StudentDirectoryQueryDto request, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<PagedResult<StudentDirectoryListItemDto>>();
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.ErrorResponse("Pagination is invalid.");

        var status = request.Status?.Trim();
        if (status != null && !AllowedStatuses.Contains(status))
            return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.ErrorResponse("Student status is invalid.");

        try
        {
            var tenantId = _currentUser.TenantId;
            var enrollmentFilter = _enrollments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive);

            if (request.AcademicYearId.HasValue)
                enrollmentFilter = enrollmentFilter.Where(x => x.AcademicYearId == request.AcademicYearId.Value);
            if (request.AcademicUnitId.HasValue)
                enrollmentFilter = enrollmentFilter.Where(x => x.AcademicLevelId == request.AcademicUnitId.Value);

            var search = request.Search?.Trim();
            List<long>? enrollmentStudentIds = null;
            if (request.AcademicYearId.HasValue || request.AcademicUnitId.HasValue || !string.IsNullOrWhiteSpace(search))
            {
                var filtered = enrollmentFilter;
                if (!string.IsNullOrWhiteSpace(search))
                    filtered = filtered.Where(x => x.RollNo.Contains(search));
                enrollmentStudentIds = await filtered.Select(x => x.StudentId).Distinct().Take(10000).ToListAsync(cancellationToken);
            }

            var query = _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId);
            if (status != null) query = query.Where(x => x.StatusCode == status);
            if (request.AcademicYearId.HasValue || request.AcademicUnitId.HasValue)
                query = query.Where(x => enrollmentStudentIds!.Contains(x.Id));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var rollIds = enrollmentStudentIds ?? [];
                query = query.Where(x =>
                    x.StudentCode.Contains(search) ||
                    x.FullName.Contains(search) ||
                    (x.FullNameBangla != null && x.FullNameBangla.Contains(search)) ||
                    rollIds.Contains(x.Id));
            }

            var total = await query.CountAsync(cancellationToken);
            var students = await query.OrderBy(x => x.FullName).ThenBy(x => x.StudentCode)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(cancellationToken);

            var items = await MapListAsync(students, cancellationToken);
            return ApiResponse<PagedResult<StudentDirectoryListItemDto>>.SuccessResponse(
                new PagedResult<StudentDirectoryListItemDto>
                {
                    Items = items,
                    TotalCount = total,
                    Page = request.Page,
                    PageSize = request.PageSize
                });
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
        if (reference == Guid.Empty)
            return ApiResponse<StudentDirectoryDetailsDto>.ErrorResponse("Student reference is invalid.");

        try
        {
            var tenantId = _currentUser.TenantId;
            var student = await _students.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == reference, cancellationToken);
            if (student == null)
                return ApiResponse<StudentDirectoryDetailsDto>.ErrorResponse("Student not found.", 404);

            var enrollments = await _enrollments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
                .OrderByDescending(x => x.IsCurrent)
                .ThenByDescending(x => x.EnrollmentDate)
                .ThenByDescending(x => x.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            var guardianLinks = await _studentGuardians.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
            var guardianIds = guardianLinks.Select(x => x.GuardianId).Distinct().ToArray();
            var guardians = guardianIds.Length == 0
                ? new Dictionary<long, Guardian>()
                : await _guardians.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenantId && guardianIds.Contains(x.Id) && x.IsActive)
                    .ToDictionaryAsync(x => x.Id, cancellationToken);

            var details = await MapDetailsAsync(student, enrollments, guardianLinks, guardians, cancellationToken);
            return ApiResponse<StudentDirectoryDetailsDto>.SuccessResponse(details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student details failed for {Reference} in tenant {TenantId}", reference, _currentUser.TenantId);
            return ApiResponse<StudentDirectoryDetailsDto>.ErrorResponse("Student details could not be loaded.", 500);
        }
    }

    private async Task<IReadOnlyList<StudentDirectoryListItemDto>> MapListAsync(
        IReadOnlyList<Student> students, CancellationToken cancellationToken)
    {
        if (students.Count == 0) return Array.Empty<StudentDirectoryListItemDto>();
        var tenantId = _currentUser.TenantId;
        var studentIds = students.Select(x => x.Id).ToArray();
        var enrollmentRows = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && studentIds.Contains(x.StudentId) && x.IsActive)
            .OrderByDescending(x => x.IsCurrent)
            .ThenByDescending(x => x.EnrollmentDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var current = enrollmentRows.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.First());
        var lookups = await LoadEnrollmentLookupsAsync(enrollmentRows, cancellationToken);

        return students.Select(x =>
        {
            current.TryGetValue(x.Id, out var e);
            return new StudentDirectoryListItemDto
            {
                Reference = x.PublicId,
                StudentCode = x.StudentCode,
                Roll = e?.RollNo ?? string.Empty,
                FullName = x.FullName,
                FullNameBangla = x.FullNameBangla,
                MaskedMobile = MaskMobile(x.Phone),
                AcademicYear = e != null ? lookups.YearNames.GetValueOrDefault(e.AcademicYearId) ?? string.Empty : string.Empty,
                AcademicUnit = e != null ? lookups.LevelNames.GetValueOrDefault(e.AcademicLevelId) ?? string.Empty : string.Empty,
                Section = e != null ? lookups.BatchNames.GetValueOrDefault(e.AcademicBatchId) ?? string.Empty : string.Empty,
                Status = x.StatusCode
            };
        }).ToList();
    }

    private async Task<StudentDirectoryDetailsDto> MapDetailsAsync(
        Student student,
        IReadOnlyList<StudentEnrollment> enrollments,
        IReadOnlyList<StudentGuardian> guardianLinks,
        IReadOnlyDictionary<long, Guardian> guardians,
        CancellationToken cancellationToken)
    {
        var lookups = await LoadEnrollmentLookupsAsync(enrollments, cancellationToken);
        var current = enrollments.FirstOrDefault(x => x.IsCurrent && x.IsActive) ?? enrollments.FirstOrDefault(x => x.IsActive) ?? enrollments.FirstOrDefault();

        return new StudentDirectoryDetailsDto
        {
            Reference = student.PublicId,
            StudentCode = student.StudentCode,
            Roll = current?.RollNo ?? string.Empty,
            FullName = student.FullName,
            FullNameBangla = student.FullNameBangla,
            MaskedMobile = MaskMobile(student.Phone),
            AcademicYear = current != null ? lookups.YearNames.GetValueOrDefault(current.AcademicYearId) ?? string.Empty : string.Empty,
            AcademicUnit = current != null ? lookups.LevelNames.GetValueOrDefault(current.AcademicLevelId) ?? string.Empty : string.Empty,
            Section = current != null ? lookups.BatchNames.GetValueOrDefault(current.AcademicBatchId) ?? string.Empty : string.Empty,
            Status = student.StatusCode,
            DateOfBirth = student.DateOfBirth?.ToDateTime(TimeOnly.MinValue) ?? default,
            Gender = student.Gender ?? string.Empty,
            Phone = student.Phone,
            Email = student.Email,
            Address = student.Address,
            PreferredLanguage = student.PreferredLanguage,
            AdmissionDate = student.AdmissionDate.ToDateTime(TimeOnly.MinValue),
            Guardians = guardianLinks
                .Where(x => guardians.ContainsKey(x.GuardianId))
                .Select(x =>
                {
                    var guardian = guardians[x.GuardianId];
                    return new StudentGuardianDto
                    {
                        Reference = guardian.PublicId,
                        Name = guardian.FullName,
                        NameBangla = null,
                        Relation = x.RelationCode,
                        Phone = guardian.Phone ?? string.Empty,
                        Email = guardian.Email,
                        Address = guardian.Address,
                        IsPrimary = x.IsPrimary
                    };
                }).ToList(),
            Enrollments = enrollments.Select(e => new EduOS.Core.DTOs.Student.StudentEnrollmentDto
            {
                Id = e.Id,
                AcademicYear = lookups.YearNames.GetValueOrDefault(e.AcademicYearId) ?? string.Empty,
                AcademicTerm = e.AcademicTermId.HasValue ? lookups.TermNames.GetValueOrDefault(e.AcademicTermId.Value) : null,
                Campus = lookups.CampusNames.GetValueOrDefault(e.CampusId),
                AcademicUnit = lookups.LevelNames.GetValueOrDefault(e.AcademicLevelId) ?? string.Empty,
                Section = lookups.BatchNames.GetValueOrDefault(e.AcademicBatchId) ?? string.Empty,
                Group = e.AcademicTrackId.HasValue ? lookups.TrackNames.GetValueOrDefault(e.AcademicTrackId.Value) : null,
                Roll = e.RollNo,
                EnrollmentDate = e.EnrollmentDate.ToDateTime(TimeOnly.MinValue),
                IsActive = e.IsActive && e.State == EnrollmentState.Active
            }).ToList()
        };
    }

    private async Task<EnrollmentLookups> LoadEnrollmentLookupsAsync(
        IReadOnlyList<StudentEnrollment> rows, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var yearIds = rows.Select(x => x.AcademicYearId).Distinct().ToArray();
        var termIds = rows.Where(x => x.AcademicTermId.HasValue).Select(x => x.AcademicTermId!.Value).Distinct().ToArray();
        var levelIds = rows.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var batchIds = rows.Select(x => x.AcademicBatchId).Distinct().ToArray();
        var trackIds = rows.Where(x => x.AcademicTrackId.HasValue).Select(x => x.AcademicTrackId!.Value).Distinct().ToArray();
        var campusIds = rows.Select(x => x.CampusId).Distinct().ToArray();

        var years = yearIds.Length == 0 ? new Dictionary<long, string>() : await _years.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && yearIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var terms = termIds.Length == 0 ? new Dictionary<long, string>() : await _terms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && termIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var levels = levelIds.Length == 0 ? new Dictionary<long, string>() : await _levels.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && levelIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var batches = batchIds.Length == 0 ? new Dictionary<long, string>() : await _batches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && batchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var tracks = trackIds.Length == 0 ? new Dictionary<long, string>() : await _tracks.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && trackIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var campuses = campusIds.Length == 0 ? new Dictionary<long, string>() : await _campuses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && campusIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return new EnrollmentLookups(years, terms, levels, batches, tracks, campuses);
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsTenantAdmin || _currentUser.IsInRole("AdmissionOfficer"));

    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Student directory access is required.", 403);

    private static string MaskMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var visible = Math.Min(4, value.Length);
        return new string('•', value.Length - visible) + value[^visible..];
    }

    private sealed record EnrollmentLookups(
        IReadOnlyDictionary<long, string> YearNames,
        IReadOnlyDictionary<long, string> TermNames,
        IReadOnlyDictionary<long, string> LevelNames,
        IReadOnlyDictionary<long, string> BatchNames,
        IReadOnlyDictionary<long, string> TrackNames,
        IReadOnlyDictionary<long, string> CampusNames);
}
