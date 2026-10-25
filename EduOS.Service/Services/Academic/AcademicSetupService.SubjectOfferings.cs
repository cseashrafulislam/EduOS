using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    // Academic setup is tenant-wide. Campus-scoped managers must not edit offerings across campuses.
    private bool CanManageOfferings() => _currentUser.IsAuthenticated &&
        _currentUser.TenantId > 0 && _currentUser.IsTenantAdmin;

    public async Task<ApiResponse<PagedResult<SubjectOfferingDto>>> GetSubjectOfferingsAsync(
        long academicBatchId, string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageOfferings()) return Denied<PagedResult<SubjectOfferingDto>>();
        if (academicBatchId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            search?.Length > 80 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<SubjectOfferingDto>>("Invalid batch, search or page.");
        var tenant = _currentUser.TenantId;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academicBatchId && !x.IsDeleted, ct);
        if (batch == null) return Error<PagedResult<SubjectOfferingDto>>("Batch not found.", 404);
        var query = from offering in _offerings.GetQueryable().AsNoTracking()
                    join mapping in _curriculumSubjects.GetQueryable().AsNoTracking()
                        on offering.CurriculumSubjectId equals mapping.Id
                    join subject in _subjects.GetQueryable().AsNoTracking()
                        on mapping.SubjectId equals subject.Id
                    where offering.TenantId == tenant && mapping.TenantId == tenant &&
                        subject.TenantId == tenant && offering.AcademicBatchId == academicBatchId &&
                        !offering.IsDeleted && !mapping.IsDeleted && !subject.IsDeleted
                    select new { offering, mapping, subject };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.offering.Code.StartsWith(term) ||
                x.subject.Code.StartsWith(term) || x.subject.Name.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.offering.Code).ThenBy(x => x.offering.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<SubjectOfferingDto>>.SuccessResponse(new PagedResult<SubjectOfferingDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => MapOffering(x.offering, batch.Name, x.mapping.SubjectId,
                x.subject.Name, x.subject.Code)).ToList()
        });
    }

    public Task<ApiResponse<SubjectOfferingDto>> CreateSubjectOfferingAsync(
        SaveSubjectOfferingRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageOfferings()) return Task.FromResult(Denied<SubjectOfferingDto>());
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AcademicBatchId <= 0 || request.CurriculumSubjectId <= 0 ||
            request.AcademicYearId <= 0 || request.AcademicTermId is <= 0 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            request.Capacity is < 1 or > 100000 || !Enum.IsDefined(request.DeliveryMode))
            return Task.FromResult(Error<SubjectOfferingDto>("Invalid offering, capacity or request reference."));
        return ExecuteWriteAsync("create subject offering", async () =>
        {
            var tenant = _currentUser.TenantId;
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicBatchId && x.IsActive && !x.IsDeleted, ct);
            if (batch == null) return Error<SubjectOfferingDto>("Active academic batch not found.", 404);
            if (batch.AcademicYearId != request.AcademicYearId ||
                batch.AcademicTermId != request.AcademicTermId ||
                batch.Capacity <= 0 || request.Capacity > batch.Capacity)
                return Error<SubjectOfferingDto>("Offering year, term or capacity does not match its batch.", 409);
            var year = await _academicYears.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
            if (year == null || year.CampusId.HasValue && year.CampusId != batch.CampusId)
                return Error<SubjectOfferingDto>("Academic year is unavailable at the batch campus.", 409);
            var start = batch.StartDate ?? year.StartDate;
            var end = batch.EndDate ?? year.EndDate;
            if (start < year.StartDate || end > year.EndDate || end < start)
                return Error<SubjectOfferingDto>("Batch dates are outside the academic year.", 409);
            if (batch.AcademicTermId.HasValue)
            {
                var term = await _academicTerms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == batch.AcademicTermId.Value &&
                    x.AcademicYearId == year.Id && x.IsActive && !x.IsDeleted, ct);
                if (term == null || start < term.StartDate || end > term.EndDate)
                    return Error<SubjectOfferingDto>("Batch dates are outside the academic term.", 409);
            }
            var program = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == batch.AcademicProgramId && x.IsActive && !x.IsDeleted, ct);
            if (program == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == batch.CampusId && x.IsActive && !x.IsDeleted, ct))
                return Error<SubjectOfferingDto>("Batch programme or campus is unavailable.", 409);
            var permitted = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == program.Id && x.IsActive && !x.IsDeleted)
                .Select(x => x.CampusId).ToListAsync(ct);
            if (permitted.Count > 0 && !permitted.Contains(batch.CampusId))
                return Error<SubjectOfferingDto>("Programme is not offered at this campus.", 409);
            var mapping = await _curriculumSubjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CurriculumSubjectId &&
                x.AcademicLevelId == batch.AcademicLevelId && x.IsActive && !x.IsDeleted, ct);
            if (mapping == null) return Error<SubjectOfferingDto>("Active curriculum subject not found for batch level.", 409);
            var curriculum = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == mapping.AcademicCurriculumId &&
                x.AcademicProgramId == batch.AcademicProgramId &&
                x.AcademicTrackId == batch.AcademicTrackId && x.MediumId == batch.MediumId &&
                x.IsActive && !x.IsDeleted, ct);
            if (curriculum == null || curriculum.EffectiveFrom > start ||
                curriculum.EffectiveTo.HasValue && curriculum.EffectiveTo.Value < end)
                return Error<SubjectOfferingDto>("Curriculum is not effective for the full batch period.", 409);
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == mapping.SubjectId && x.IsActive && !x.IsDeleted, ct);
            if (subject == null) return Error<SubjectOfferingDto>("Subject is inactive or unavailable.", 409);
            var code = NormalizeCode(request.Code);
            var existing = await _offerings.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicBatchId == batch.Id && !x.IsDeleted &&
                (x.CurriculumSubjectId == mapping.Id || x.Code == code)).ToListAsync(ct);
            if (existing.Count > 0)
            {
                var same = existing.Count == 1 && existing[0].CurriculumSubjectId == mapping.Id &&
                    existing[0].Code == code && existing[0].Capacity == request.Capacity &&
                    existing[0].DeliveryMode == request.DeliveryMode && existing[0].IsActive == request.IsActive;
                if (!same) return Error<SubjectOfferingDto>("Batch already has this curriculum subject or offering code with different settings.", 409);
                return ApiResponse<SubjectOfferingDto>.SuccessResponse(
                    MapOffering(existing[0], batch.Name, subject.Id, subject.Name, subject.Code),
                    "Offering already exists with identical settings.");
            }
            var row = new SubjectOffering
            {
                TenantId = tenant, PublicId = Guid.NewGuid(), AcademicBatchId = batch.Id,
                CurriculumSubjectId = mapping.Id, AcademicYearId = year.Id,
                AcademicTermId = batch.AcademicTermId, Code = code,
                Capacity = request.Capacity, DeliveryMode = request.DeliveryMode,
                IsActive = request.IsActive, CreatedBy = _currentUser.UserId
            };
            await _offerings.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapOffering(row, batch.Name, subject.Id, subject.Name, subject.Code),
                "Subject offering created.");
        }, serializable: true);
    }

    public Task<ApiResponse<SubjectOfferingDto>> UpdateSubjectOfferingAsync(
        Guid reference, UpdateSubjectOfferingRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageOfferings()) return Task.FromResult(Denied<SubjectOfferingDto>());
        if (reference == Guid.Empty || request == null || request.Capacity is < 1 or > 100000)
            return Task.FromResult(Error<SubjectOfferingDto>("Valid offering reference and capacity are required."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<SubjectOfferingDto>("Invalid offering version.")); }
        if (expected.Length != 8) return Task.FromResult(Error<SubjectOfferingDto>("Valid offering version is required."));
        return ExecuteWriteAsync("update subject offering", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _offerings.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, ct);
            if (row == null) return Error<SubjectOfferingDto>("Subject offering not found.", 404);
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.AcademicBatchId && !x.IsDeleted, ct);
            var mapping = await _curriculumSubjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.CurriculumSubjectId && !x.IsDeleted, ct);
            if (batch == null || mapping == null)
                return Error<SubjectOfferingDto>("Offering academic mapping is unavailable.", 409);
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == mapping.SubjectId && !x.IsDeleted, ct);
            if (subject == null) return Error<SubjectOfferingDto>("Offering subject not found.", 409);
            var dto = MapOffering(row, batch.Name, subject.Id, subject.Name, subject.Code);
            // A successful identical retry is safe even if the first write advanced RowVersion.
            if (row.Capacity == request.Capacity && row.IsActive == request.IsActive)
                return ApiResponse<SubjectOfferingDto>.SuccessResponse(dto, "Offering already has these settings.");
            if (!row.RowVersion.SequenceEqual(expected))
                return Error<SubjectOfferingDto>("Offering changed concurrently. Reload and retry.", 409);
            if (!batch.IsActive || batch.Capacity <= 0 || request.Capacity > batch.Capacity)
                return Error<SubjectOfferingDto>("Capacity exceeds active batch capacity.", 409);
            var registered = await _registrations.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && x.SubjectOfferingId == row.Id && !x.IsDeleted &&
                x.State != SubjectRegistrationState.Rejected && x.State != SubjectRegistrationState.Dropped, ct);
            if (request.Capacity < registered || !request.IsActive && registered > 0)
                return Error<SubjectOfferingDto>("Existing subject registrations prevent this capacity or deactivation.", 409);
            row.Capacity = request.Capacity;
            row.IsActive = request.IsActive;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _offerings.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<SubjectOfferingDto>.SuccessResponse(
                MapOffering(row, batch.Name, subject.Id, subject.Name, subject.Code), "Offering updated.");
        }, serializable: true);
    }

    private static SubjectOfferingDto MapOffering(SubjectOffering row, string batchName,
        long subjectId, string subjectName, string subjectCode) => new()
    {
        Id = row.Id, Reference = row.PublicId, AcademicBatchId = row.AcademicBatchId,
        AcademicBatchName = batchName, CurriculumSubjectId = row.CurriculumSubjectId,
        SubjectId = subjectId, SubjectName = subjectName, SubjectCode = subjectCode,
        AcademicYearId = row.AcademicYearId, AcademicTermId = row.AcademicTermId,
        Code = row.Code, Capacity = row.Capacity, DeliveryMode = row.DeliveryMode,
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
}
