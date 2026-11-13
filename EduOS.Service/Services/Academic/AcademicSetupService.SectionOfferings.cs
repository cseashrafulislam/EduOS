using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<SubjectOfferingDto>>> GetSectionOfferingsAsync(
        long sectionId, long yearId, string? search, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!CanManageOfferings()) return Denied<PagedResult<SubjectOfferingDto>>();
        if (sectionId <= 0 || yearId <= 0 || _sections == null || page < 1 ||
            pageSize is < 1 or > 100 || search?.Length > 80 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<SubjectOfferingDto>>("Invalid section, year or page.");
        var tenant = _currentUser.TenantId;
        if (!await _sections.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == sectionId && !x.IsDeleted, ct))
            return Error<PagedResult<SubjectOfferingDto>>("Academic section not found.", 404);
        var query = from offering in _offerings.GetQueryable().AsNoTracking()
                    join mapping in _curriculumSubjects.GetQueryable().AsNoTracking()
                        on offering.CurriculumSubjectId equals mapping.Id
                    join subject in _subjects.GetQueryable().AsNoTracking()
                        on mapping.SubjectId equals subject.Id
                    where offering.TenantId == tenant && mapping.TenantId == tenant &&
                        subject.TenantId == tenant && offering.AcademicBatchId == null &&
                        offering.AcademicSectionId == sectionId && offering.AcademicYearId == yearId &&
                        !offering.IsDeleted && !mapping.IsDeleted && !subject.IsDeleted
                    select new { offering, mapping, subject };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.offering.Code.StartsWith(term) ||
                x.subject.Code.StartsWith(term) || x.subject.Name.StartsWith(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.offering.Code).ThenBy(x => x.offering.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.offering.Id).ToArray();
        var usage = await _registrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids.Contains(x.SubjectOfferingId) && !x.IsDeleted &&
            x.State != SubjectRegistrationState.Rejected && x.State != SubjectRegistrationState.Dropped)
            .GroupBy(x => x.SubjectOfferingId)
            .Select(g => new
            {
                Id = g.Key,
                Reserved = g.Count(),
                Pending = g.Count(x => x.State == SubjectRegistrationState.Pending),
                Allocated = g.Count(x => x.State == SubjectRegistrationState.Approved ||
                    x.State == SubjectRegistrationState.Completed)
            }).ToDictionaryAsync(x => x.Id, ct);
        return ApiResponse<PagedResult<SubjectOfferingDto>>.SuccessResponse(new PagedResult<SubjectOfferingDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = rows.Select(x =>
            {
                var dto = MapOffering(x.offering, "", x.mapping.SubjectId,
                    x.subject.Name, x.subject.Code);
                if (usage.TryGetValue(x.offering.Id, out var count))
                {
                    dto.ReservedSeats = count.Reserved;
                    dto.PendingSeats = count.Pending;
                    dto.AllocatedSeats = count.Allocated;
                }
                dto.SeatsRemaining = Math.Max(0, dto.Capacity - dto.ReservedSeats);
                dto.IsOverCapacity = dto.ReservedSeats > dto.Capacity;
                return dto;
            }).ToList()
        });
    }

    private Task<ApiResponse<SubjectOfferingDto>> CreateSectionOfferingAsync(
        SaveSubjectOfferingRequestDto request, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || request.AcademicBatchId.HasValue ||
            request.AcademicYearId <= 0 || request.AcademicSectionId is null or <= 0 ||
            request.CurriculumSubjectId <= 0 || request.AcademicTermId is <= 0 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            request.Capacity is < 1 or > 100000 || !Enum.IsDefined(request.DeliveryMode) ||
            _sections == null)
            return Task.FromResult(Error<SubjectOfferingDto>("Provide a valid year, section, subject and capacity."));
        return ExecuteWriteAsync("create section subject offering", async () =>
        {
            var tenant = _currentUser.TenantId;
            var section = await _sections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicSectionId!.Value &&
                x.IsActive && !x.IsDeleted, ct);
            if (section == null || section.Capacity <= 0 || request.Capacity > section.Capacity)
                return Error<SubjectOfferingDto>("Offering capacity exceeds its active academic section.", 409);
            var level = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == section.AcademicLevelId &&
                x.IsActive && !x.IsDeleted, ct);
            var year = await _academicYears.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
            if (level == null || year == null ||
                year.CampusId.HasValue && year.CampusId != section.CampusId ||
                !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == section.CampusId &&
                    x.IsActive && !x.IsDeleted, ct))
                return Error<SubjectOfferingDto>("Class, year or campus is unavailable.", 409);
            var datesStart = year.StartDate;
            var datesEnd = year.EndDate;
            if (request.AcademicTermId.HasValue)
            {
                var term = await _academicTerms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicTermId.Value &&
                    x.AcademicYearId == year.Id && x.IsActive && !x.IsDeleted, ct);
                if (term == null || term.StartDate < year.StartDate ||
                    term.EndDate > year.EndDate || term.EndDate < term.StartDate)
                    return Error<SubjectOfferingDto>("Term is not within its academic year.", 409);
                datesStart = term.StartDate;
                datesEnd = term.EndDate;
            }
            var program = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == level.AcademicProgramId && x.IsActive && !x.IsDeleted, ct);
            if (program == null)
                return Error<SubjectOfferingDto>("Class programme is unavailable.", 409);
            var permitted = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == program.Id &&
                x.IsActive && !x.IsDeleted).Select(x => x.CampusId).ToListAsync(ct);
            if (permitted.Count > 0 && !permitted.Contains(section.CampusId))
                return Error<SubjectOfferingDto>("Programme not offered at this campus.", 403);
            var mapping = await _curriculumSubjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CurriculumSubjectId &&
                x.AcademicLevelId == section.AcademicLevelId && x.IsActive && !x.IsDeleted, ct);
            if (mapping == null) return Error<SubjectOfferingDto>("Curriculum subject not in this class.", 409);
            var curriculum = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == mapping.AcademicCurriculumId &&
                x.AcademicProgramId == level.AcademicProgramId && x.IsCurrent &&
                x.IsActive && !x.IsDeleted, ct);
            if (curriculum == null || curriculum.EffectiveFrom > datesStart ||
                curriculum.EffectiveTo.HasValue && curriculum.EffectiveTo.Value < datesEnd)
                return Error<SubjectOfferingDto>("Current curriculum is not valid for this period.", 409);
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == mapping.SubjectId && x.IsActive && !x.IsDeleted, ct);
            if (subject == null) return Error<SubjectOfferingDto>("Subject is unavailable.", 409);
            var code = NormalizeCode(request.Code);
            var found = await _offerings.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicBatchId == null && x.AcademicYearId == year.Id &&
                x.AcademicSectionId == section.Id && !x.IsDeleted &&
                (x.Code == code || x.CurriculumSubjectId == mapping.Id))
                .ToListAsync(ct);
            if (found.Count > 0)
            {
                if (found.Count == 1 && found[0].Code == code &&
                    found[0].CurriculumSubjectId == mapping.Id &&
                    found[0].AcademicTermId == request.AcademicTermId &&
                    found[0].Capacity == request.Capacity &&
                    found[0].DeliveryMode == request.DeliveryMode &&
                    found[0].IsActive == request.IsActive)
                    return ApiResponse<SubjectOfferingDto>.SuccessResponse(
                        MapOffering(found[0], "", subject.Id, subject.Name, subject.Code),
                        "Matching section offering already exists.");
                return Error<SubjectOfferingDto>("Duplicate subject offering in this year and section.", 409);
            }
            var row = new SubjectOffering
            {
                TenantId = tenant, PublicId = Guid.NewGuid(),
                AcademicBatchId = null, AcademicSectionId = section.Id,
                AcademicYearId = year.Id, AcademicTermId = request.AcademicTermId,
                CurriculumSubjectId = mapping.Id, Code = code,
                Capacity = request.Capacity, DeliveryMode = request.DeliveryMode,
                IsActive = request.IsActive, CreatedBy = _currentUser.UserId
            };
            await _offerings.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapOffering(row, "", subject.Id, subject.Name, subject.Code),
                "Year/section subject offering created.");
        }, serializable: true);
    }
}
