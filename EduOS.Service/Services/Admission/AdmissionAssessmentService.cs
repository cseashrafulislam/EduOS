using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionAssessmentService : IAdmissionAssessmentService
{
    private readonly IGenericRepository<AdmissionTest> _tests;
    private readonly IGenericRepository<AdmissionResult> _results;
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Class> _academicUnits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionAssessmentService> _logger;

    public AdmissionAssessmentService(
        IGenericRepository<AdmissionTest> tests,
        IGenericRepository<AdmissionResult> results,
        IGenericRepository<AdmissionApplicant> applications,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<Campus> campuses,
        IGenericRepository<Class> academicUnits,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<AdmissionAssessmentService> logger)
    {
        _tests = tests;
        _results = results;
        _applications = applications;
        _academicYears = academicYears;
        _campuses = campuses;
        _academicUnits = academicUnits;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<AdmissionTestDto>>> GetTestsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AdmissionTestDto>>();

        var tenantId = _currentUser.TenantId;
        try
        {
            var rows = await _tests.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.TestDate)
                .ThenByDescending(x => x.Id)
                .Select(x => MapTest(x))
                .ToListAsync(cancellationToken);

            return ApiResponse<IReadOnlyList<AdmissionTestDto>>.SuccessResponse(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission test list failed for tenant {TenantId}", tenantId);
            return ApiResponse<IReadOnlyList<AdmissionTestDto>>.ErrorResponse("Admission tests could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionTestDto>> CreateTestAsync(SaveAdmissionTestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionTestDto>();

        var tenantId = _currentUser.TenantId;
        var validation = await ValidateTestRequestAsync(request, tenantId, cancellationToken);
        if (validation != null) return ApiResponse<AdmissionTestDto>.ErrorResponse(validation, 409);

        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var entity = new AdmissionTest
            {
                TenantId = tenantId,
                Name = request.Name.Trim(),
                AcademicYearId = request.AcademicYearId,
                CampusId = request.CampusId,
                AcademicUnitId = request.AcademicUnitId,
                TestDate = request.TestDate,
                TotalMarks = request.TotalMarks,
                PassMarks = request.PassMarks,
                Venue = TrimToNull(request.Venue),
                DurationMinutes = request.DurationMinutes,
                IsPublished = false,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };

            await _tests.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new ApiResponse<AdmissionTestDto>
            {
                Success = true,
                Message = "Admission test created.",
                Data = MapTest(entity),
                StatusCode = 201
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission test creation failed for tenant {TenantId}", tenantId);
            return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test could not be created.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionTestDto>> UpdateTestAsync(long id, SaveAdmissionTestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionTestDto>();
        if (id <= 0) return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test is invalid.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var entity = await _tests.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
            if (entity == null) return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test not found.", 404);
            if (entity.IsPublished) return ApiResponse<AdmissionTestDto>.ErrorResponse("Published admission tests cannot be edited.", 409);

            var validation = await ValidateTestRequestAsync(request, tenantId, cancellationToken);
            if (validation != null) return ApiResponse<AdmissionTestDto>.ErrorResponse(validation, 409);

            entity.Name = request.Name.Trim();
            entity.AcademicYearId = request.AcademicYearId;
            entity.CampusId = request.CampusId;
            entity.AcademicUnitId = request.AcademicUnitId;
            entity.TestDate = request.TestDate;
            entity.TotalMarks = request.TotalMarks;
            entity.PassMarks = request.PassMarks;
            entity.Venue = TrimToNull(request.Venue);
            entity.DurationMinutes = request.DurationMinutes;
            entity.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            entity.UpdatedBy = _currentUser.UserId;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionTestDto>.SuccessResponse(MapTest(entity), "Admission test updated.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission test update failed for {TestId} tenant {TenantId}", id, tenantId);
            return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<AdmissionResultDto>>> SaveResultsAsync(long testId, SaveAdmissionResultsDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AdmissionResultDto>>();
        if (testId <= 0 || request.Results.Count == 0)
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Assessment results are required.");
        if (request.Results.GroupBy(x => x.ApplicantId).Any(x => x.Count() > 1))
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("The same applicant cannot appear more than once.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var test = await _tests.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == testId, cancellationToken);
            if (test == null) return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Admission test not found.", 404);
            if (test.IsPublished) return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Published merit results cannot be changed.", 409);

            if (request.Results.Any(x => x.ObtainedMarks < 0 || x.ObtainedMarks > test.TotalMarks))
                return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Obtained marks must be between zero and total marks.");

            var applicantIds = request.Results.Select(x => x.ApplicantId).Distinct().ToArray();
            var applicants = await _applications.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && applicantIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

            if (applicants.Count != applicantIds.Length)
                return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("One or more applicants were not found.", 404);

            var invalidApplicant = applicants.FirstOrDefault(x =>
                x.AcademicYearId != test.AcademicYearId ||
                x.CampusId != test.CampusId ||
                x.AcademicUnitId != test.AcademicUnitId);
            if (invalidApplicant != null)
                return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("All applicants must belong to the test academic year, campus and academic unit.", 409);

            var existing = await _results.GetQueryable()
                .Where(x => x.TenantId == tenantId && x.AdmissionTestId == testId && applicantIds.Contains(x.ApplicantId))
                .ToListAsync(cancellationToken);
            var existingByApplicant = existing.ToDictionary(x => x.ApplicantId);
            var now = _clock.GetUtcNow().UtcDateTime;

            foreach (var item in request.Results)
            {
                if (!existingByApplicant.TryGetValue(item.ApplicantId, out var result))
                {
                    result = new AdmissionResult
                    {
                        TenantId = tenantId,
                        AdmissionTestId = testId,
                        ApplicantId = item.ApplicantId,
                        CreatedAt = now,
                        CreatedBy = _currentUser.UserId
                    };
                    await _results.AddAsync(result);
                    existingByApplicant[item.ApplicantId] = result;
                }
                else
                {
                    result.UpdatedAt = now;
                    result.UpdatedBy = _currentUser.UserId;
                }

                result.ObtainedMarks = item.ObtainedMarks;
                result.Percentage = test.TotalMarks == 0 ? 0 : Math.Round(item.ObtainedMarks / test.TotalMarks * 100m, 2);
                result.IsPassed = item.ObtainedMarks >= test.PassMarks;
                result.ResultStatus = result.IsPassed ? "Passed" : "Failed";
                result.MeritPosition = null;
                result.Grade = TrimToNull(item.Grade);
                result.Remarks = TrimToNull(item.Remarks);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var response = await LoadResultsAsync(testId, tenantId, cancellationToken);
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.SuccessResponse(response, "Assessment results saved.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission result conflict for test {TestId} tenant {TenantId}", testId, tenantId);
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Assessment results conflict with another update. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission result save failed for test {TestId} tenant {TenantId}", testId, tenantId);
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Assessment results could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionMeritListDto>> GetMeritListAsync(long testId, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionMeritListDto>();

        var tenantId = _currentUser.TenantId;
        try
        {
            var test = await _tests.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == testId, cancellationToken);
            if (test == null) return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Admission test not found.", 404);

            var results = await LoadResultsAsync(testId, tenantId, cancellationToken);
            return ApiResponse<AdmissionMeritListDto>.SuccessResponse(new AdmissionMeritListDto
            {
                Test = MapTest(test),
                Results = results.ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission merit list load failed for test {TestId} tenant {TenantId}", testId, tenantId);
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Merit list could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionMeritListDto>> PublishMeritListAsync(long testId, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionMeritListDto>();

        var tenantId = _currentUser.TenantId;
        try
        {
            var test = await _tests.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == testId, cancellationToken);
            if (test == null) return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Admission test not found.", 404);
            if (test.IsPublished) return await GetMeritListAsync(testId, cancellationToken);

            var results = await _results.GetQueryable()
                .Include(x => x.Applicant)
                .Where(x => x.TenantId == tenantId && x.AdmissionTestId == testId)
                .ToListAsync(cancellationToken);
            if (results.Count == 0) return ApiResponse<AdmissionMeritListDto>.ErrorResponse("At least one assessment result is required before publishing.", 409);

            var ranked = results
                .Where(x => x.IsPassed)
                .OrderByDescending(x => x.ObtainedMarks)
                .ThenBy(x => x.Applicant!.SubmittedAtUtc)
                .ThenBy(x => x.ApplicantId)
                .ToList();
            for (var i = 0; i < ranked.Count; i++) ranked[i].MeritPosition = i + 1;
            foreach (var failed in results.Where(x => !x.IsPassed)) failed.MeritPosition = null;

            var now = _clock.GetUtcNow().UtcDateTime;
            test.IsPublished = true;
            test.PublishedAtUtc = now;
            test.PublishedByUserId = _currentUser.UserId;
            test.UpdatedAt = now;
            test.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var published = await LoadResultsAsync(testId, tenantId, cancellationToken);
            return ApiResponse<AdmissionMeritListDto>.SuccessResponse(new AdmissionMeritListDto
            {
                Test = MapTest(test),
                Results = published.ToList()
            }, "Merit list published.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Admission merit publish conflict for test {TestId} tenant {TenantId}", testId, tenantId);
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("The merit list changed during publishing. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission merit publish failed for test {TestId} tenant {TenantId}", testId, tenantId);
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Merit list could not be published.", 500);
        }
    }

    private async Task<string?> ValidateTestRequestAsync(SaveAdmissionTestDto request, long tenantId, CancellationToken cancellationToken)
    {
        var name = TrimToNull(request.Name);
        if (name == null || name.Length > 200) return "Test name is required.";
        if (request.TotalMarks <= 0) return "Total marks must be greater than zero.";
        if (request.PassMarks < 0 || request.PassMarks > request.TotalMarks) return "Pass marks must be between zero and total marks.";
        if (request.DurationMinutes is < 1 or > 1440) return "Test duration is invalid.";

        var year = await _academicYears.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicYearId && x.IsActive, cancellationToken);
        if (year == null) return "Academic year is invalid.";
        if (request.TestDate.Date < year.StartDate.Date || request.TestDate.Date > year.EndDate.Date)
            return "Test date must be inside the academic year.";
        if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == request.CampusId && x.IsActive, cancellationToken))
            return "Campus is invalid.";
        if (!await _academicUnits.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicUnitId && x.IsActive, cancellationToken))
            return "Academic unit is invalid.";

        return null;
    }

    private async Task<IReadOnlyList<AdmissionResultDto>> LoadResultsAsync(long testId, long tenantId, CancellationToken cancellationToken)
    {
        return await _results.GetQueryable().AsNoTracking()
            .Include(x => x.Applicant)
            .Where(x => x.TenantId == tenantId && x.AdmissionTestId == testId)
            .OrderBy(x => x.MeritPosition == null)
            .ThenBy(x => x.MeritPosition)
            .ThenByDescending(x => x.ObtainedMarks)
            .ThenBy(x => x.ApplicantId)
            .Select(x => new AdmissionResultDto
            {
                Id = x.Id,
                AdmissionTestId = x.AdmissionTestId,
                ApplicantId = x.ApplicantId,
                ApplicantReference = x.Applicant!.PublicId,
                ApplicationNumber = x.Applicant.ApplicationNumber,
                ApplicantName = x.Applicant.ApplicantName,
                ObtainedMarks = x.ObtainedMarks,
                Percentage = x.Percentage,
                IsPassed = x.IsPassed,
                MeritPosition = x.MeritPosition,
                ResultStatus = x.ResultStatus,
                Grade = x.Grade,
                Remarks = x.Remarks
            })
            .ToListAsync(cancellationToken);
    }

    private bool CanManage() => _currentUser.IsAuthenticated
        && (_currentUser.IsInRole("TenantAdmin") || _currentUser.IsInRole("AdmissionOfficer"));

    private static AdmissionTestDto MapTest(AdmissionTest x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        AcademicYearId = x.AcademicYearId,
        CampusId = x.CampusId,
        AcademicUnitId = x.AcademicUnitId,
        TestDate = x.TestDate,
        TotalMarks = x.TotalMarks,
        PassMarks = x.PassMarks,
        Venue = x.Venue,
        DurationMinutes = x.DurationMinutes,
        IsPublished = x.IsPublished,
        PublishedAtUtc = x.PublishedAtUtc
    };

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("You do not have permission to manage admission assessments.", 403);
}
