using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Enums.Domain;
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
    private readonly IGenericRepository<AdmissionApplicant> _applicants;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionAssessmentService> _logger;

    public AdmissionAssessmentService(IGenericRepository<AdmissionTest> tests, IGenericRepository<AdmissionResult> results,
        IGenericRepository<AdmissionApplicant> applicants, IGenericRepository<AdmissionIntakeForm> forms,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<AdmissionAssessmentService> logger)
    {
        _tests = tests;
        _results = results;
        _applicants = applicants;
        _forms = forms;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<AdmissionTestDto>>> GetTestsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AdmissionTestDto>>();
        var tenant = _currentUser.TenantId;
        try
        {
            var rows = await (from test in _tests.GetQueryable().AsNoTracking()
                join form in _forms.GetQueryable().AsNoTracking() on test.AdmissionIntakeFormId equals form.Id
                where test.TenantId == tenant && form.TenantId == tenant
                orderby test.TestDate descending, test.Id descending
                select new { Test = test, FormReference = form.PublicId }).ToListAsync(cancellationToken);
            IReadOnlyList<AdmissionTestDto> result = rows.Select(x => MapTest(x.Test, x.FormReference)).ToList();
            return ApiResponse<IReadOnlyList<AdmissionTestDto>>.SuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission test listing failed for tenant {TenantId}", tenant);
            return ApiResponse<IReadOnlyList<AdmissionTestDto>>.ErrorResponse("Admission tests could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionTestDto>> CreateTestAsync(SaveAdmissionTestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionTestDto>();
        var tenant = _currentUser.TenantId;
        try
        {
            var selection = await ResolveFormAsync(request, cancellationToken);
            if (selection.Error != null) return ApiResponse<AdmissionTestDto>.ErrorResponse(selection.Error, 409);
            var form = selection.Form!;
            if (await _tests.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.AdmissionIntakeFormId == form.Id && x.Name == request.Name.Trim(), cancellationToken))
                return ApiResponse<AdmissionTestDto>.ErrorResponse("An admission test with this name already exists.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var test = new AdmissionTest
            {
                TenantId = tenant, AdmissionIntakeFormId = form.Id, Name = request.Name.Trim(),
                TestDate = DateOnly.FromDateTime(request.TestDate), DurationMinutes = request.DurationMinutes,
                TotalMarks = request.TotalMarks, PassMarks = request.PassMarks,
                Venue = Trim(request.Venue), IsPublished = false,
                CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _tests.AddAsync(test);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new ApiResponse<AdmissionTestDto>
            {
                Success = true, StatusCode = 201, Message = "Admission test created.",
                Data = MapTest(test, form.PublicId)
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission test creation conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test already exists or contains invalid references.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission test creation failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test could not be created.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionTestDto>> UpdateTestAsync(long id, SaveAdmissionTestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionTestDto>();
        if (id <= 0 || request == null) return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test is invalid.");
        var tenant = _currentUser.TenantId;
        try
        {
            var test = await _tests.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == id,
                cancellationToken);
            if (test == null) return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test not found.", 404);
            if (test.IsPublished) return ApiResponse<AdmissionTestDto>.ErrorResponse("Published admission tests cannot be edited.", 409);
            if (string.IsNullOrWhiteSpace(request.RowVersion) || !MatchesVersion(test.RowVersion, request.RowVersion))
                return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test was changed or RowVersion was not supplied.", 409);
            var selection = await ResolveFormAsync(request, cancellationToken);
            if (selection.Error != null) return ApiResponse<AdmissionTestDto>.ErrorResponse(selection.Error, 409);
            if (test.AdmissionIntakeFormId != selection.Form!.Id)
                return ApiResponse<AdmissionTestDto>.ErrorResponse("A test cannot be transferred to another admission form.", 409);
            test.Name = request.Name.Trim();
            test.TestDate = DateOnly.FromDateTime(request.TestDate);
            test.TotalMarks = request.TotalMarks;
            test.PassMarks = request.PassMarks;
            test.DurationMinutes = request.DurationMinutes;
            test.Venue = Trim(request.Venue);
            test.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            test.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionTestDto>.SuccessResponse(MapTest(test, selection.Form.PublicId), "Admission test updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test changed. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission test update failed for tenant {TenantId} test {Id}", tenant, id);
            return ApiResponse<AdmissionTestDto>.ErrorResponse("Admission test could not be updated.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<AdmissionResultDto>>> SaveResultsAsync(long testId,
        SaveAdmissionResultsDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AdmissionResultDto>>();
        if (testId <= 0 || request?.Results == null || request.Results.Count == 0)
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Assessment results are required.");
        if (request.Results.Any(x => x == null || x.ApplicantId <= 0) ||
            request.Results.Select(x => x.ApplicantId).Distinct().Count() != request.Results.Count)
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Applicant references must be valid and unique.");
        var tenant = _currentUser.TenantId;
        try
        {
            var test = await _tests.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == testId, cancellationToken);
            if (test == null) return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Admission test not found.", 404);
            if (test.IsPublished) return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Published marks cannot be changed.", 409);
            if (test.TotalMarks <= 0 || request.Results.Any(x => x.ObtainedMarks < 0 || x.ObtainedMarks > test.TotalMarks))
                return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Marks are outside the valid range.");
            var ids = request.Results.Select(x => x.ApplicantId).ToArray();
            var validCount = await _applicants.GetQueryable().AsNoTracking().CountAsync(x => x.TenantId == tenant &&
                x.AdmissionIntakeFormId == test.AdmissionIntakeFormId && ids.Contains(x.Id), cancellationToken);
            if (validCount != ids.Length)
                return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Every applicant must belong to the same admission form.", 409);
            var existing = await _results.GetQueryable().Where(x => x.TenantId == tenant &&
                x.AdmissionTestId == testId && ids.Contains(x.AdmissionApplicantId))
                .ToDictionaryAsync(x => x.AdmissionApplicantId, cancellationToken);
            var now = _clock.GetUtcNow().UtcDateTime;
            foreach (var item in request.Results)
            {
                if (!existing.TryGetValue(item.ApplicantId, out var row))
                {
                    row = new AdmissionResult
                    {
                        TenantId = tenant, AdmissionTestId = testId, AdmissionApplicantId = item.ApplicantId,
                        CreatedAt = now, CreatedBy = _currentUser.UserId
                    };
                    await _results.AddAsync(row);
                }
                row.ObtainedMarks = item.ObtainedMarks;
                row.IsPassed = row.ObtainedMarks >= test.PassMarks;
                row.MeritPosition = null;
                row.Grade = Trim(item.Grade);
                row.Remarks = Trim(item.Remarks);
                row.UpdatedAt = now;
                row.UpdatedBy = _currentUser.UserId;
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var rows = await LoadResultsAsync(testId, tenant, cancellationToken);
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.SuccessResponse(rows, "Marks saved.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission marks conflict for tenant {TenantId}", tenant);
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Marks conflict with another update. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission marks save failed for tenant {TenantId}", tenant);
            return ApiResponse<IReadOnlyList<AdmissionResultDto>>.ErrorResponse("Marks could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionMeritListDto>> GetMeritListAsync(long testId,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionMeritListDto>();
        var tenant = _currentUser.TenantId;
        try
        {
            var test = await _tests.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == testId, cancellationToken);
            if (test == null) return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Admission test not found.", 404);
            var formReference = await _forms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.Id == test.AdmissionIntakeFormId).Select(x => x.PublicId).FirstOrDefaultAsync(cancellationToken);
            if (formReference == Guid.Empty) return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Admission form not found.", 404);
            var rows = await LoadResultsAsync(testId, tenant, cancellationToken);
            return ApiResponse<AdmissionMeritListDto>.SuccessResponse(new AdmissionMeritListDto
            {
                Test = MapTest(test, formReference), Results = rows.ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merit list failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Merit list could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionMeritListDto>> PublishMeritListAsync(long testId,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionMeritListDto>();
        if (testId <= 0) return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Admission test is required.");
        var tenant = _currentUser.TenantId;
        try
        {
            var result = await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var test = await _tests.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == testId && !x.IsDeleted, token);
                if (test == null) return ApiResponse<bool>.ErrorResponse("Admission test not found.", 404);
                if (test.IsPublished) return ApiResponse<bool>.SuccessResponse(true);
                var applicants = _applicants.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.AdmissionIntakeFormId == test.AdmissionIntakeFormId && !x.IsDeleted);
                var records = await (from entry in _results.GetQueryable()
                    join applicant in applicants on entry.AdmissionApplicantId equals applicant.Id
                    where entry.TenantId == tenant && entry.AdmissionTestId == testId && !entry.IsDeleted
                    select new { Result = entry, applicant.SubmittedAt }).ToListAsync(token);
                if (records.Count == 0)
                    return ApiResponse<bool>.ErrorResponse("At least one result is required.", 409);
                var ranked = records.Where(x => x.Result.IsPassed)
                    .OrderByDescending(x => x.Result.ObtainedMarks)
                    .ThenBy(x => x.SubmittedAt).ThenBy(x => x.Result.AdmissionApplicantId).ToArray();
                for (var i = 0; i < ranked.Length; i++) ranked[i].Result.MeritPosition = i + 1;
                foreach (var failed in records.Where(x => !x.Result.IsPassed))
                    failed.Result.MeritPosition = null;
                test.IsPublished = true;
                test.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                test.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true);
            }, cancellationToken);
            if (!result.Success)
                return ApiResponse<AdmissionMeritListDto>.ErrorResponse(result.Message, result.StatusCode);
            return await GetMeritListAsync(testId, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Merit list changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Merit publish conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Merit publish conflicts with another update.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merit publish failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionMeritListDto>.ErrorResponse("Merit list could not be published.", 500);
        }
    }

    private async Task<(AdmissionIntakeForm? Form, string? Error)> ResolveFormAsync(
        SaveAdmissionTestDto request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            request.TotalMarks <= 0 || request.PassMarks < 0 || request.PassMarks > request.TotalMarks ||
            request.DurationMinutes is < 1 or > 1440 || request.AcademicYearId <= 0 ||
            request.CampusId <= 0 || request.AcademicLevelId <= 0)
            return (null, "Admission test request is invalid.");
        var date = DateOnly.FromDateTime(request.TestDate);
        var formQuery = _forms.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId &&
            x.AcademicYearId == request.AcademicYearId && x.CampusId == request.CampusId &&
            x.AcademicLevelId == request.AcademicLevelId &&
            x.State != AdmissionFormState.Archived);
        if (request.AdmissionIntakeFormReference.HasValue)
            formQuery = formQuery.Where(x => x.PublicId == request.AdmissionIntakeFormReference.Value);
        var matches = await formQuery.Take(2).ToListAsync(ct);
        if (matches.Count != 1)
            return (null, matches.Count == 0 ? "Matching admission form was not found." :
                "Multiple admission forms match; supply AdmissionIntakeFormReference.");
        var form = matches[0];
        if (date.Year < 2000) return (null, "Test date is invalid.");
        if (request.TotalMarks > 1_000_000m) return (null, "Test total marks exceed the maximum.");
        return (form, null);
    }

    private async Task<IReadOnlyList<AdmissionResultDto>> LoadResultsAsync(long testId, long tenant, CancellationToken ct)
    {
        var query = from result in _results.GetQueryable().AsNoTracking()
            join applicant in _applicants.GetQueryable().AsNoTracking()
                on result.AdmissionApplicantId equals applicant.Id
            where result.TenantId == tenant && applicant.TenantId == tenant && result.AdmissionTestId == testId
            orderby result.MeritPosition == null, result.MeritPosition, result.ObtainedMarks descending, result.AdmissionApplicantId
            select new { Result = result, applicant.PublicId, applicant.FullName };
        var rows = await query.ToListAsync(ct);
        return rows.Select(x => new AdmissionResultDto
        {
            Id = x.Result.Id, AdmissionTestId = x.Result.AdmissionTestId,
            AdmissionApplicantReference = x.PublicId, ApplicantName = x.FullName,
            ObtainedMarks = x.Result.ObtainedMarks, IsPassed = x.Result.IsPassed,
            MeritPosition = x.Result.MeritPosition, Grade = x.Result.Grade, Remarks = x.Result.Remarks,
            RowVersion = Convert.ToBase64String(x.Result.RowVersion)
        }).ToList();
    }

    private static AdmissionTestDto MapTest(AdmissionTest row, Guid formReference) => new()
    {
        Id = row.Id, AdmissionIntakeFormReference = formReference,
        Name = row.Name, TestDate = row.TestDate, StartTime = row.StartTime,
        DurationMinutes = row.DurationMinutes, TotalMarks = row.TotalMarks,
        PassMarks = row.PassMarks, Venue = row.Venue,
        IsPublished = row.IsPublished, RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private static bool MatchesVersion(byte[] current, string supplied)
    {
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsTenantAdmin || _currentUser.IsInRole("AdmissionOfficer"));
    private static ApiResponse<T> Denied<T>() =>
        ApiResponse<T>.ErrorResponse("Admission assessment management permission is required.", 403);
}
