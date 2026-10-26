using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService : IAcademicSetupService
{
    private readonly IGenericRepository<AcademicProgram> _programs;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IGenericRepository<ProgramCampus> _programCampuses;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<AcademicDepartment> _departments;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<AcademicTerm> _academicTerms;
    private readonly IGenericRepository<AcademicTrack> _tracks;
    private readonly IGenericRepository<Medium> _mediums;
    private readonly IGenericRepository<Shift> _shifts;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AcademicSetupService> _logger;

    public AcademicSetupService(
        IGenericRepository<AcademicProgram> programs,
        IGenericRepository<AcademicLevel> levels,
        IGenericRepository<Subject> subjects,
        IGenericRepository<AcademicCurriculum> curricula,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<Room> rooms,
        IGenericRepository<ProgramCampus> programCampuses,
        IGenericRepository<Campus> campuses,
        IGenericRepository<AcademicDepartment> departments,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<AcademicTerm> academicTerms,
        IGenericRepository<AcademicTrack> tracks,
        IGenericRepository<Medium> mediums,
        IGenericRepository<Shift> shifts,
        IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<StudentEnrollment> enrollments,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ILogger<AcademicSetupService> logger)
    {
        _programs = programs;
        _levels = levels;
        _subjects = subjects;
        _curricula = curricula;
        _curriculumSubjects = curriculumSubjects;
        _batches = batches;
        _rooms = rooms;
        _programCampuses = programCampuses;
        _campuses = campuses;
        _departments = departments;
        _academicYears = academicYears;
        _academicTerms = academicTerms;
        _tracks = tracks;
        _mediums = mediums;
        _shifts = shifts;
        _offerings = offerings;
        _registrations = registrations;
        _enrollments = enrollments;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<AcademicSetupCatalogDto>> GetSetupOptionsAsync(
        long? academicYearId, string? search, int take = 50, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<AcademicSetupCatalogDto>();
        if (academicYearId is <= 0 || take < 1 || take > 100 || search?.Length > 100)
            return Error<AcademicSetupCatalogDto>("Invalid academic year, search, or result limit.");
        var tenant = _currentUser.TenantId;
        if (academicYearId.HasValue && !await _academicYears.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant && x.Id == academicYearId.Value && x.IsActive && !x.IsDeleted, ct))
            return Error<AcademicSetupCatalogDto>("Academic year not found.", 404);
        var term = search?.Trim();
        var programs = _programs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var levels = _levels.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var tracks = _tracks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var subjects = _subjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var curricula = _curricula.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var curriculaSubjects = _curriculumSubjects.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var batches = _batches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        var rooms = _rooms.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && !x.IsDeleted);
        if (academicYearId.HasValue)
        {
            batches = batches.Where(x => x.AcademicYearId == academicYearId.Value);
            var years = _academicYears.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.Id == academicYearId.Value)
                .Select(x => new { x.StartDate, x.EndDate });
            var year = await years.FirstAsync(ct);
            curricula = curricula.Where(x => x.EffectiveFrom <= year.EndDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= year.StartDate));
        }
        if (!string.IsNullOrWhiteSpace(term))
        {
            programs = programs.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            levels = levels.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            tracks = tracks.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            subjects = subjects.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            curricula = curricula.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            batches = batches.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            rooms = rooms.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
            var curriculumIds = curricula.Select(x => x.Id);
            var subjectIds = subjects.Select(x => x.Id);
            curriculaSubjects = curriculaSubjects.Where(x =>
                curriculumIds.Contains(x.AcademicCurriculumId) || subjectIds.Contains(x.SubjectId));
        }
        var programRows = await programs.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
        var programIds = programRows.Select(x => x.Id).ToArray();
        var links = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.IsActive && programIds.Contains(x.AcademicProgramId))
            .Select(x => new { x.AcademicProgramId, x.CampusId }).ToListAsync(ct);
        var programDtos = programRows.Select(MapProgram).ToList();
        foreach (var program in programDtos)
            program.CampusIds = links.Where(x => x.AcademicProgramId == program.Id)
                .Select(x => x.CampusId).ToArray();
        return ApiResponse<AcademicSetupCatalogDto>.SuccessResponse(new AcademicSetupCatalogDto
        {
            Programs = programDtos,
            Levels = (await levels.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapLevel).ToList(),
            Tracks = (await tracks.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapTrack).ToList(),
            Subjects = (await subjects.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapSubject).ToList(),
            Curricula = (await curricula.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapCurriculum).ToList(),
            CurriculumSubjects = (await curriculaSubjects.OrderBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapCurriculumSubject).ToList(),
            Batches = (await batches.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapBatch).ToList(),
            Rooms = (await rooms.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(take).ToListAsync(ct)).Select(MapRoom).ToList()
        });
    }

    public Task<ApiResponse<AcademicTrackDto>> CreateTrackAsync(SaveAcademicTrackRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicTrackDto>());
        if (request == null || !ValidNameCode(request.Name, request.Code, 150) ||
            request.AcademicProgramId is <= 0 || request.DisplayOrder < 0 || request.Description?.Length > 500)
            return Task.FromResult(Error<AcademicTrackDto>("Invalid academic track."));
        return ExecuteWriteAsync("create track", async () =>
        {
            var tenant = _currentUser.TenantId;
            if (request.AcademicProgramId.HasValue && !await _programs.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicProgramId.Value && x.IsActive, ct))
                return Error<AcademicTrackDto>("Academic program not found.", 404);
            var code = NormalizeCode(request.Code);
            var existing = await _tracks.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Code == code, ct);
            if (existing != null)
            {
                if (existing.Name != request.Name.Trim() || existing.AcademicProgramId != request.AcademicProgramId ||
                    existing.IsDefault != request.IsDefault || existing.DisplayOrder != request.DisplayOrder ||
                    existing.Description != Trim(request.Description) || existing.IsActive != request.IsActive)
                    return Error<AcademicTrackDto>("Track code already exists with other settings.", 409);
                return ApiResponse<AcademicTrackDto>.SuccessResponse(MapTrack(existing));
            }
            if (request.IsDefault && request.IsActive && await _tracks.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == request.AcademicProgramId &&
                x.IsDefault && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicTrackDto>("A default track already exists.", 409);
            var row = new AcademicTrack
            {
                TenantId = tenant, Name = request.Name.Trim(), Code = code,
                AcademicProgramId = request.AcademicProgramId, Description = Trim(request.Description),
                IsDefault = request.IsDefault, IsActive = request.IsActive,
                DisplayOrder = request.DisplayOrder
            };
            await _tracks.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapTrack(row), "Academic track created.");
        });
    }

    public Task<ApiResponse<AcademicProgramDto>> CreateProgramAsync(SaveAcademicProgramRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicProgramDto>());
        if (request == null || !ValidNameCode(request.Name, request.Code, 200) ||
            request.DurationInMonths < 0 || request.DurationInMonths > 1200 ||
            request.AcademicDepartmentId is <= 0 || request.CampusIds == null ||
            request.CampusIds.Any(x => x <= 0) || request.CampusIds.Count > 100 ||
            request.CampusIds.Distinct().Count() != request.CampusIds.Count ||
            request.ShortName?.Length > 100 || request.AwardTitle?.Length > 150 ||
            request.Description?.Length > 1000 || request.DisplayOrder < 0)
            return Task.FromResult(Error<AcademicProgramDto>("Invalid academic program or campus assignments."));
        return ExecuteWriteAsync("create academic program", async () =>
        {
            var tenant = _currentUser.TenantId;
            if (request.AcademicDepartmentId.HasValue && !await _departments.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicDepartmentId.Value && x.IsActive, ct))
                return Error<AcademicProgramDto>("Academic department not found.", 404);
            var ids = request.CampusIds.ToArray();
            var valid = await _campuses.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && ids.Contains(x.Id) && x.IsActive && !x.IsDeleted, ct);
            if (valid != ids.Length) return Error<AcademicProgramDto>("One or more campuses are unavailable.", 404);
            var code = NormalizeCode(request.Code);
            var existing = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Code == code, ct);
            if (existing != null)
            {
                var linked = await _programCampuses.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.AcademicProgramId == existing.Id && x.IsActive)
                    .Select(x => x.CampusId).ToArrayAsync(ct);
                if (existing.Name != request.Name.Trim() ||
                    existing.AcademicDepartmentId != request.AcademicDepartmentId ||
                    existing.DurationInMonths != request.DurationInMonths ||
                    existing.ShortName != Trim(request.ShortName) ||
                    existing.AwardTitle != Trim(request.AwardTitle) ||
                    existing.Description != Trim(request.Description) ||
                    existing.IsAdmissionOpen != request.IsAdmissionOpen ||
                    existing.DisplayOrder != request.DisplayOrder || existing.IsActive != request.IsActive ||
                    !linked.OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)))
                    return Error<AcademicProgramDto>("Program code already exists with different settings.", 409);
                var dto = MapProgram(existing); dto.CampusIds = linked;
                return ApiResponse<AcademicProgramDto>.SuccessResponse(dto);
            }
            var row = new AcademicProgram
            {
                TenantId = tenant, AcademicDepartmentId = request.AcademicDepartmentId,
                Name = request.Name.Trim(), Code = code, ShortName = Trim(request.ShortName),
                DurationInMonths = request.DurationInMonths, AwardTitle = Trim(request.AwardTitle),
                Description = Trim(request.Description), IsAdmissionOpen = request.IsAdmissionOpen,
                IsActive = request.IsActive, DisplayOrder = request.DisplayOrder
            };
            await _programs.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            var first = true;
            foreach (var campusId in ids)
            {
                await _programCampuses.AddAsync(new ProgramCampus
                {
                    TenantId = tenant, AcademicProgramId = row.Id, CampusId = campusId,
                    IsPrimary = first, IsActive = true
                });
                first = false;
            }
            await _unitOfWork.SaveChangesAsync(ct);
            var result = MapProgram(row); result.CampusIds = ids;
            return Created(result, "Academic program created.");
        });
    }


    public async Task<ApiResponse<ProgramCampusAllocationDto>> GetProgramCampusAllocationAsync(
        long academicProgramId, CancellationToken ct = default)
    {
        if (!CanRead()) return Denied<ProgramCampusAllocationDto>();
        if (academicProgramId <= 0) return Error<ProgramCampusAllocationDto>("Valid programme ID is required.");
        var tenant = _currentUser.TenantId;
        var program = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == academicProgramId && x.IsActive && !x.IsDeleted, ct);
        if (program == null) return Error<ProgramCampusAllocationDto>("Programme not found.", 404);
        var links = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicProgramId == academicProgramId && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.CampusId).ToListAsync(ct);
        return ApiResponse<ProgramCampusAllocationDto>.SuccessResponse(MapAllocation(program, links));
    }

    public Task<ApiResponse<ProgramCampusAllocationDto>> UpdateProgramCampusAllocationAsync(
        long academicProgramId, UpdateProgramCampusAllocationRequestDto request, CancellationToken ct = default)
    {
        // Programme allocation is tenant-wide configuration; campus-scoped managers cannot edit it.
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId <= 0 || !_currentUser.IsTenantAdmin)
            return Task.FromResult(Denied<ProgramCampusAllocationDto>());
        if (academicProgramId <= 0 || request == null || request.CampusIds == null ||
            request.CampusIds.Count > 100 || request.CampusIds.Any(x => x <= 0) ||
            request.CampusIds.Distinct().Count() != request.CampusIds.Count ||
            request.AllowAllCampuses != (request.CampusIds.Count == 0) ||
            (request.CampusIds.Count > 0 && (!request.PrimaryCampusId.HasValue ||
                !request.CampusIds.Contains(request.PrimaryCampusId.Value))) ||
            (request.CampusIds.Count == 0 && request.PrimaryCampusId.HasValue))
            return Task.FromResult(Error<ProgramCampusAllocationDto>("Select valid campuses and a primary campus, or explicitly allow all campuses."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<ProgramCampusAllocationDto>("Invalid programme version.")); }
        if (expected.Length != 8)
            return Task.FromResult(Error<ProgramCampusAllocationDto>("Valid programme version is required."));
        var campusIds = request.CampusIds.ToArray();
        return ExecuteWriteAsync("update programme campus allocation", async () =>
        {
            var tenant = _currentUser.TenantId;
            var program = await _programs.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == academicProgramId && x.IsActive && !x.IsDeleted, ct);
            if (program == null) return Error<ProgramCampusAllocationDto>("Programme not found.", 404);
            var links = await _programCampuses.GetQueryable().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == academicProgramId && !x.IsDeleted).ToListAsync(ct);
            var current = links.Where(x => x.IsActive).Select(x => x.CampusId).OrderBy(x => x).ToArray();
            var primary = links.FirstOrDefault(x => x.IsActive && x.IsPrimary)?.CampusId;
            // Identical retries are safe even when the original successful write advanced RowVersion.
            if (current.SequenceEqual(campusIds.OrderBy(x => x)) &&
                (campusIds.Length == 0 || primary == request.PrimaryCampusId))
                return ApiResponse<ProgramCampusAllocationDto>.SuccessResponse(
                    MapAllocation(program, links), "Programme campus allocation already applied.");
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(program.RowVersion, expected))
                return Error<ProgramCampusAllocationDto>("Programme changed. Reload before saving.", 409);
            var available = await _campuses.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && campusIds.Contains(x.Id) && x.IsActive && !x.IsDeleted, ct);
            if (available != campusIds.Length)
                return Error<ProgramCampusAllocationDto>("One or more campuses are unavailable.", 404);
            // Never strand an existing historical or active batch outside a newly restricted programme.
            if (campusIds.Length > 0 && await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == academicProgramId && !x.IsDeleted &&
                !campusIds.Contains(x.CampusId), ct))
                return Error<ProgramCampusAllocationDto>("Existing academic batches use a campus outside this allocation.", 409);
            var now = DateTime.UtcNow;
            foreach (var link in links)
            {
                var active = campusIds.Contains(link.CampusId);
                var isPrimary = active && request.PrimaryCampusId == link.CampusId;
                if (link.IsActive == active && link.IsPrimary == isPrimary) continue;
                link.IsActive = active; link.IsPrimary = isPrimary;
                link.UpdatedAt = now; link.UpdatedBy = _currentUser.UserId;
                _programCampuses.Update(link);
            }
            foreach (var campusId in campusIds.Where(id => links.All(x => x.CampusId != id)))
                await _programCampuses.AddAsync(new ProgramCampus
                {
                    TenantId = tenant, AcademicProgramId = academicProgramId, CampusId = campusId,
                    IsPrimary = request.PrimaryCampusId == campusId, IsActive = true,
                    CreatedAt = now, CreatedBy = _currentUser.UserId
                });
            program.UpdatedAt = now; program.UpdatedBy = _currentUser.UserId;
            _programs.Update(program);
            await _unitOfWork.SaveChangesAsync(ct);
            var activeLinks = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == academicProgramId && x.IsActive && !x.IsDeleted)
                .ToListAsync(ct);
            return ApiResponse<ProgramCampusAllocationDto>.SuccessResponse(
                MapAllocation(program, activeLinks), "Programme campus allocation saved.");
        }, serializable: true);
    }

    public Task<ApiResponse<AcademicLevelDto>> CreateLevelAsync(SaveAcademicLevelRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicLevelDto>());
        if (request == null || request.AcademicProgramId <= 0 ||
            !ValidNameCode(request.Name, request.Code, 150) ||
            request.LevelNo is < 1 or > 1000 || request.DisplayOrder < 0 ||
            request.IsTerminalLevel && request.IsPromotable)
            return Task.FromResult(Error<AcademicLevelDto>("Invalid program, level or progression flags."));
        return ExecuteWriteAsync("create level", async () =>
        {
            var tenant = _currentUser.TenantId;
            if (!await _programs.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicProgramId && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicLevelDto>("Program not found.", 404);
            var code = NormalizeCode(request.Code);
            var existing = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == request.AcademicProgramId && x.Code == code, ct);
            if (existing != null)
            {
                if (existing.Name != request.Name.Trim() || existing.LevelNo != request.LevelNo ||
                    existing.IsPromotable != request.IsPromotable ||
                    existing.IsTerminalLevel != request.IsTerminalLevel ||
                    existing.DisplayOrder != request.DisplayOrder || existing.IsActive != request.IsActive)
                    return Error<AcademicLevelDto>("Level code already exists with different settings.", 409);
                return ApiResponse<AcademicLevelDto>.SuccessResponse(MapLevel(existing));
            }
            var row = new AcademicLevel
            {
                TenantId = tenant, AcademicProgramId = request.AcademicProgramId,
                Name = request.Name.Trim(), Code = code, LevelNo = request.LevelNo,
                IsPromotable = request.IsPromotable, IsTerminalLevel = request.IsTerminalLevel,
                IsActive = request.IsActive, DisplayOrder = request.DisplayOrder
            };
            await _levels.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapLevel(row), "Academic level created.");
        });
    }

    public Task<ApiResponse<SubjectDto>> CreateSubjectAsync(SaveSubjectRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<SubjectDto>());
        if (request == null || !ValidNameCode(request.Name, request.Code, 200) ||
            request.ShortName?.Length > 50 || request.DefaultCreditHours is < 0 or > 1000)
            return Task.FromResult(Error<SubjectDto>("Invalid subject name, code or credit hours."));
        return ExecuteWriteAsync("create subject", async () =>
        {
            var tenant = _currentUser.TenantId;
            var code = NormalizeCode(request.Code);
            var existing = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Code == code, ct);
            if (existing != null)
            {
                if (existing.Name != request.Name.Trim() ||
                    existing.ShortName != Trim(request.ShortName) ||
                    existing.DefaultCreditHours != request.DefaultCreditHours ||
                    existing.IsActive != request.IsActive)
                    return Error<SubjectDto>("Subject code already exists with other values.", 409);
                return ApiResponse<SubjectDto>.SuccessResponse(MapSubject(existing));
            }
            var row = new Subject
            {
                TenantId = tenant, Name = request.Name.Trim(), Code = code,
                ShortName = Trim(request.ShortName), DefaultCreditHours = request.DefaultCreditHours,
                IsActive = request.IsActive
            };
            await _subjects.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapSubject(row), "Subject created.");
        });
    }

    public Task<ApiResponse<AcademicCurriculumDto>> CreateCurriculumAsync(
        SaveAcademicCurriculumRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCurriculumDto>());
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AcademicProgramId <= 0 || !ValidNameCode(request.Name, request.Code, 150) ||
            request.VersionNo < 1 || request.EffectiveFrom == default ||
            request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom ||
            request.AcademicTrackId is <= 0 || request.MediumId is <= 0 ||
            request.Subjects == null || request.Subjects.Count > 500 ||
            request.Subjects.Any(x => !ValidSubject(x)) ||
            request.Subjects.Select(x => new { x.AcademicLevelId, x.SubjectId }).Distinct().Count() != request.Subjects.Count)
            return Task.FromResult(Error<AcademicCurriculumDto>("Invalid curriculum, dates or subject lines."));
        return ExecuteWriteAsync("create curriculum", async () =>
        {
            var tenant = _currentUser.TenantId;
            var program = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicProgramId && x.IsActive && !x.IsDeleted, ct);
            if (program == null) return Error<AcademicCurriculumDto>("Academic program unavailable.", 404);
            if (request.AcademicTrackId.HasValue && !await _tracks.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicTrackId.Value &&
                    x.IsActive && (!x.AcademicProgramId.HasValue || x.AcademicProgramId == program.Id), ct))
                return Error<AcademicCurriculumDto>("Academic track invalid for program.", 409);
            if (request.MediumId.HasValue && !await _mediums.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.MediumId.Value && x.IsActive, ct))
                return Error<AcademicCurriculumDto>("Medium not found.", 404);
            var code = NormalizeCode(request.Code);
            var existing = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Code == code, ct);
            if (existing != null)
            {
                if (existing.AcademicProgramId != request.AcademicProgramId ||
                    existing.AcademicTrackId != request.AcademicTrackId ||
                    existing.MediumId != request.MediumId ||
                    existing.Name != request.Name.Trim() || existing.VersionNo != request.VersionNo ||
                    existing.EffectiveFrom != request.EffectiveFrom ||
                    existing.EffectiveTo != request.EffectiveTo || existing.IsCurrent != request.IsCurrent ||
                    existing.IsActive != request.IsActive)
                    return Error<AcademicCurriculumDto>("Curriculum code already exists with other settings.", 409);
                var registered = await _curriculumSubjects.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.AcademicCurriculumId == existing.Id && !x.IsDeleted)
                    .ToListAsync(ct);
                if (registered.Count != request.Subjects.Count || request.Subjects.Any(x => !registered.Any(y =>
                    y.AcademicLevelId == x.AcademicLevelId && y.SubjectId == x.SubjectId &&
                    y.FullMarks == x.FullMarks && y.PassMarks == x.PassMarks &&
                    y.CreditHours == x.CreditHours && y.IsOptional == x.IsOptional &&
                    y.HasPractical == x.HasPractical && y.IsActive == x.IsActive &&
                    y.DisplayOrder == x.DisplayOrder)))
                    return Error<AcademicCurriculumDto>("Existing curriculum subjects differ from this request.", 409);
                var dto = MapCurriculum(existing);
                dto.Subjects = registered.Select(MapCurriculumSubject).ToList();
                return ApiResponse<AcademicCurriculumDto>.SuccessResponse(dto, "Curriculum already exists.");
            }
            if (request.IsCurrent && request.IsActive && await _curricula.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.AcademicProgramId == program.Id &&
                    x.AcademicTrackId == request.AcademicTrackId &&
                    x.MediumId == request.MediumId && x.IsCurrent && !x.IsDeleted, ct))
                return Error<AcademicCurriculumDto>("A current curriculum exists for this program, track and medium.", 409);
            if (request.Subjects.Count > 0)
            {
                var levelIds = request.Subjects.Select(x => x.AcademicLevelId).Distinct().ToArray();
                var subjectIds = request.Subjects.Select(x => x.SubjectId).Distinct().ToArray();
                var levelCount = await _levels.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.AcademicProgramId == program.Id &&
                    x.IsActive && levelIds.Contains(x.Id), ct);
                var subjectCount = await _subjects.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.IsActive && subjectIds.Contains(x.Id), ct);
                if (levelCount != levelIds.Length || subjectCount != subjectIds.Length)
                    return Error<AcademicCurriculumDto>("Curriculum subject or level is not valid for this program.", 409);
            }
            var row = new AcademicCurriculum
            {
                TenantId = tenant, AcademicProgramId = program.Id, AcademicTrackId = request.AcademicTrackId,
                MediumId = request.MediumId, Name = request.Name.Trim(), Code = code,
                VersionNo = request.VersionNo, EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo, IsCurrent = request.IsCurrent,
                IsActive = request.IsActive
            };
            await _curricula.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            var items = new List<CurriculumSubject>();
            foreach (var requestItem in request.Subjects)
            {
                var item = new CurriculumSubject
                {
                    TenantId = tenant, AcademicCurriculumId = row.Id,
                    AcademicLevelId = requestItem.AcademicLevelId, SubjectId = requestItem.SubjectId,
                    FullMarks = requestItem.FullMarks, PassMarks = requestItem.PassMarks,
                    CreditHours = requestItem.CreditHours, IsOptional = requestItem.IsOptional,
                    HasPractical = requestItem.HasPractical, IsActive = requestItem.IsActive,
                    DisplayOrder = requestItem.DisplayOrder
                };
                items.Add(item);
                await _curriculumSubjects.AddAsync(item);
            }
            await _unitOfWork.SaveChangesAsync(ct);
            var result = MapCurriculum(row);
            result.Subjects = items.Select(MapCurriculumSubject).ToList();
            return Created(result, "Curriculum and subjects created.");
        });
    }

    public Task<ApiResponse<AcademicCurriculumDto>> ActivateCurriculumAsync(
        long curriculumId, string rowVersion, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCurriculumDto>());
        if (curriculumId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
            return Task.FromResult(Error<AcademicCurriculumDto>("Curriculum ID and version are required."));
        byte[] expected;
        try { expected = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { return Task.FromResult(Error<AcademicCurriculumDto>("Invalid curriculum version.")); }
        if (expected.Length != 8) return Task.FromResult(Error<AcademicCurriculumDto>("Invalid curriculum version."));
        return ExecuteWriteAsync("activate curriculum", async () =>
        {
            var tenant = _currentUser.TenantId;
            var target = await _curricula.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == curriculumId && x.IsActive && !x.IsDeleted, ct);
            if (target == null) return Error<AcademicCurriculumDto>("Curriculum not found.", 404);
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                target.RowVersion, expected))
                return Error<AcademicCurriculumDto>("Curriculum has changed. Reload before activation.", 409);
            if (target.IsCurrent) return ApiResponse<AcademicCurriculumDto>.SuccessResponse(MapCurriculum(target));
            var old = await _curricula.GetQueryable().Where(x =>
                x.TenantId == tenant && x.AcademicProgramId == target.AcademicProgramId &&
                x.AcademicTrackId == target.AcademicTrackId && x.MediumId == target.MediumId &&
                x.IsCurrent && !x.IsDeleted && x.Id != curriculumId).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var previous in old)
            {
                previous.IsCurrent = false;
                previous.UpdatedAt = now; previous.UpdatedBy = _currentUser.UserId;
                _curricula.Update(previous);
            }
            // Two flushes in ONE transaction avoid SQL Server's filtered unique
            // index collision while switching the canonical current curriculum.
            await _unitOfWork.SaveChangesAsync(ct);
            target.IsCurrent = true;
            target.UpdatedAt = now; target.UpdatedBy = _currentUser.UserId;
            _curricula.Update(target);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<AcademicCurriculumDto>.SuccessResponse(MapCurriculum(target),
                "Current curriculum activated.");
        });
    }

    public Task<ApiResponse<CurriculumSubjectDto>> RegisterCurriculumSubjectAsync(
        long academicCurriculumId, SaveCurriculumSubjectRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<CurriculumSubjectDto>());
        if (academicCurriculumId <= 0 || !ValidSubject(request) || request!.Id.HasValue)
            return Task.FromResult(Error<CurriculumSubjectDto>("Valid curriculum subject without existing ID is required."));
        return ExecuteWriteAsync("register curriculum subject", async () =>
        {
            var tenant = _currentUser.TenantId;
            var curriculum = await _curricula.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == academicCurriculumId && x.IsActive && !x.IsDeleted, ct);
            if (curriculum == null) return Error<CurriculumSubjectDto>("Curriculum not found.", 404);
            if (!await _levels.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicLevelId &&
                    x.AcademicProgramId == curriculum.AcademicProgramId && x.IsActive, ct) ||
                !await _subjects.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == request.SubjectId && x.IsActive, ct))
                return Error<CurriculumSubjectDto>("Academic level or subject is unavailable.", 409);
            var existing = await _curriculumSubjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.AcademicCurriculumId == academicCurriculumId &&
                x.AcademicLevelId == request.AcademicLevelId && x.SubjectId == request.SubjectId && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.FullMarks != request.FullMarks || existing.PassMarks != request.PassMarks ||
                    existing.CreditHours != request.CreditHours || existing.IsOptional != request.IsOptional ||
                    existing.HasPractical != request.HasPractical || existing.IsActive != request.IsActive ||
                    existing.DisplayOrder != request.DisplayOrder)
                    return Error<CurriculumSubjectDto>("Curriculum subject already exists with different settings.", 409);
                return ApiResponse<CurriculumSubjectDto>.SuccessResponse(MapCurriculumSubject(existing));
            }
            // Preserve enrollment history even if the original offerings were removed.
            // Identical mapping retries above remain safe and do not mutate the curriculum.
            if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                x.AcademicCurriculumId == academicCurriculumId, ct))
                return Error<CurriculumSubjectDto>(
                    "Student enrollments already use this curriculum. Create a new version.", 409);
            var mappingIds = await _curriculumSubjects.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                    x.AcademicCurriculumId == academicCurriculumId)
                .Select(x => x.Id).ToArrayAsync(ct);
            if (mappingIds.Length > 0 && await _offerings.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                    mappingIds.Contains(x.CurriculumSubjectId), ct))
                return Error<CurriculumSubjectDto>(
                    "Subject offerings already use this curriculum. Create a new version.", 409);
            if (curriculum.EffectiveTo.HasValue &&
                curriculum.EffectiveTo.Value < DateOnly.FromDateTime(DateTime.UtcNow))
                return Error<CurriculumSubjectDto>(
                    "The curriculum effective period has ended. Create a new version.", 409);
            if (!curriculum.IsCurrent && await _curricula.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                    x.IsCurrent && x.AcademicProgramId == curriculum.AcademicProgramId &&
                    x.AcademicTrackId == curriculum.AcademicTrackId &&
                    x.MediumId == curriculum.MediumId && x.VersionNo > curriculum.VersionNo, ct))
                return Error<CurriculumSubjectDto>(
                    "A newer current curriculum supersedes this version.", 409);
            var row = new CurriculumSubject
            {
                TenantId = tenant, AcademicCurriculumId = academicCurriculumId,
                AcademicLevelId = request.AcademicLevelId, SubjectId = request.SubjectId,
                FullMarks = request.FullMarks, PassMarks = request.PassMarks,
                CreditHours = request.CreditHours, IsOptional = request.IsOptional,
                HasPractical = request.HasPractical, DisplayOrder = request.DisplayOrder,
                IsActive = request.IsActive
            };
            await _curriculumSubjects.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapCurriculumSubject(row), "Curriculum subject registered.");
        }, serializable: true);
    }

    public Task<ApiResponse<AcademicBatchDto>> CreateBatchAsync(SaveAcademicBatchRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicBatchDto>());
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            request.AcademicProgramId <= 0 || request.AcademicYearId <= 0 || request.AcademicLevelId <= 0 ||
            !ValidNameCode(request.Name, request.Code, 150) || !Enum.IsDefined(request.DeliveryMode) ||
            request.Capacity is < 0 or > 100000 || request.DisplayOrder < 0 ||
            request.AcademicTermId is <= 0 || request.AcademicTrackId is <= 0 ||
            request.MediumId is <= 0 || request.ShiftId is <= 0 ||
            request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate > request.EndDate)
            return Task.FromResult(Error<AcademicBatchDto>("Invalid academic batch, date range or references."));
        return ExecuteWriteAsync("create batch", async () =>
        {
            var tenant = _currentUser.TenantId;
            if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicBatchDto>("Campus not found.", 404);
            var year = await _academicYears.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId && x.IsActive && !x.IsDeleted, ct);
            if (year == null) return Error<AcademicBatchDto>("Academic year not found.", 404);
            var program = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicProgramId && x.IsActive && !x.IsDeleted, ct);
            if (program == null) return Error<AcademicBatchDto>("Academic program not found.", 404);
            if (!await _levels.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicLevelId &&
                x.AcademicProgramId == program.Id && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicBatchDto>("Academic level does not belong to selected program.", 409);
            var linked = await _programCampuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.AcademicProgramId == program.Id &&
                    x.IsActive && !x.IsDeleted).Select(x => x.CampusId).ToListAsync(ct);
            if (linked.Count > 0 && !linked.Contains(request.CampusId))
                return Error<AcademicBatchDto>("Academic program is not available at this campus.", 409);
            AcademicTerm? term = null;
            if (request.AcademicTermId.HasValue)
            {
                term = await _academicTerms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicTermId.Value &&
                    x.AcademicYearId == year.Id && x.IsActive && !x.IsDeleted, ct);
                if (term == null) return Error<AcademicBatchDto>("Academic term not found for selected year.", 409);
            }
            if (request.AcademicTrackId.HasValue && !await _tracks.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicTrackId.Value &&
                    x.IsActive && (!x.AcademicProgramId.HasValue || x.AcademicProgramId == program.Id), ct))
                return Error<AcademicBatchDto>("Academic track not valid for selected program.", 409);
            if (request.MediumId.HasValue && !await _mediums.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.MediumId.Value && x.IsActive, ct))
                return Error<AcademicBatchDto>("Medium unavailable.", 404);
            if (request.ShiftId.HasValue && !await _shifts.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.ShiftId.Value && x.IsActive, ct))
                return Error<AcademicBatchDto>("Shift unavailable.", 404);
            if (request.StartDate < year.StartDate || request.EndDate > year.EndDate ||
                term != null && (request.StartDate < term.StartDate || request.EndDate > term.EndDate))
                return Error<AcademicBatchDto>("Academic batch dates exceed the academic year or term.", 409);
            var code = NormalizeCode(request.Code);
            var existing = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.CampusId == request.CampusId &&
                x.AcademicYearId == year.Id && x.Code == code, ct);
            if (existing != null)
            {
                if (existing.Name != request.Name.Trim() || existing.AcademicProgramId != program.Id ||
                    existing.AcademicLevelId != request.AcademicLevelId ||
                    existing.AcademicTermId != request.AcademicTermId ||
                    existing.AcademicTrackId != request.AcademicTrackId ||
                    existing.MediumId != request.MediumId || existing.ShiftId != request.ShiftId ||
                    existing.Capacity != request.Capacity || existing.DeliveryMode != request.DeliveryMode ||
                    existing.StartDate != request.StartDate || existing.EndDate != request.EndDate ||
                    existing.IsDefault != request.IsDefault ||
                    existing.IsActive != request.IsActive || existing.DisplayOrder != request.DisplayOrder)
                    return Error<AcademicBatchDto>("Batch code exists with different settings.", 409);
                return ApiResponse<AcademicBatchDto>.SuccessResponse(MapBatch(existing));
            }
            if (request.IsDefault && request.IsActive && await _batches.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.CampusId == request.CampusId &&
                    x.AcademicYearId == year.Id && x.AcademicProgramId == program.Id &&
                    x.AcademicLevelId == request.AcademicLevelId && x.IsDefault && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicBatchDto>("A default batch exists for this campus, year and level.", 409);
            var row = new AcademicBatch
            {
                TenantId = tenant, CampusId = request.CampusId, AcademicYearId = year.Id,
                AcademicProgramId = program.Id, AcademicLevelId = request.AcademicLevelId,
                AcademicTermId = request.AcademicTermId, AcademicTrackId = request.AcademicTrackId,
                MediumId = request.MediumId, ShiftId = request.ShiftId,
                Name = request.Name.Trim(), Code = code, DeliveryMode = request.DeliveryMode,
                Capacity = request.Capacity, StartDate = request.StartDate, EndDate = request.EndDate,
                IsDefault = request.IsDefault, IsActive = request.IsActive, DisplayOrder = request.DisplayOrder
            };
            await _batches.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapBatch(row), "Academic batch created.");
        });
    }

    public Task<ApiResponse<RoomDto>> CreateRoomAsync(SaveRoomRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<RoomDto>());
        if (request == null || request.CampusId <= 0 ||
            !ValidNameCode(request.Name, request.Code, 100) ||
            request.Capacity is < 0 or > 100000 || request.RoomType?.Length > 100)
            return Task.FromResult(Error<RoomDto>("Invalid room, campus or capacity."));
        return ExecuteWriteAsync("create room", async () =>
        {
            var tenant = _currentUser.TenantId;
            if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, ct))
                return Error<RoomDto>("Campus not found.", 404);
            var code = NormalizeCode(request.Code);
            var existing = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.CampusId == request.CampusId && x.Code == code, ct);
            if (existing != null)
            {
                if (existing.Name != request.Name.Trim() || existing.Capacity != request.Capacity ||
                    existing.RoomType != Trim(request.RoomType) || existing.IsActive != request.IsActive)
                    return Error<RoomDto>("Room code exists with different details.", 409);
                return ApiResponse<RoomDto>.SuccessResponse(MapRoom(existing));
            }
            var row = new Room
            {
                TenantId = tenant, CampusId = request.CampusId,
                Name = request.Name.Trim(), Code = code, Capacity = request.Capacity,
                RoomType = Trim(request.RoomType), IsActive = request.IsActive
            };
            await _rooms.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapRoom(row), "Academic room created.");
        });
    }

    private async Task<ApiResponse<T>> ExecuteWriteAsync<T>(
        string operation, Func<Task<ApiResponse<T>>> action, bool serializable = false)
    {
        try
        {
            return serializable
                ? await _unitOfWork.ExecuteInTransactionAsync(_ => action(), System.Data.IsolationLevel.Serializable)
                : await _unitOfWork.ExecuteInTransactionAsync(_ => action());
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent academic setup write {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic setup was changed concurrently. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting academic setup write {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic setup conflicts with existing data.", 409);
        }
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (IsManager() || _currentUser.IsInRole("Teacher"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && IsManager();
    private bool IsManager() => _currentUser.IsTenantAdmin ||
        _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal");
    private static bool ValidNameCode(string? name, string? code, int maxName) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= maxName &&
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 50;
    private static bool ValidSubject(SaveCurriculumSubjectRequestDto? x) =>
        x != null && !x.Id.HasValue && x.AcademicLevelId > 0 && x.SubjectId > 0 &&
        x.FullMarks is > 0 and <= 100000 && x.PassMarks >= 0 && x.PassMarks <= x.FullMarks &&
        x.CreditHours is >= 0 and <= 1000 && x.DisplayOrder >= 0;
    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static ProgramCampusAllocationDto MapAllocation(AcademicProgram program, IEnumerable<ProgramCampus> links)
    {
        var active = links.Where(x => x.IsActive && !x.IsDeleted).OrderBy(x => x.CampusId).ToArray();
        return new ProgramCampusAllocationDto
        {
            AcademicProgramId = program.Id, ProgramName = program.Name, ProgramCode = program.Code,
            CampusIds = active.Select(x => x.CampusId).ToArray(),
            PrimaryCampusId = active.FirstOrDefault(x => x.IsPrimary)?.CampusId,
            AllowAllCampuses = active.Length == 0, RowVersion = Version(program.RowVersion)
        };
    }

    private static AcademicProgramDto MapProgram(AcademicProgram x) => new()
    {
        Id = x.Id, AcademicDepartmentId = x.AcademicDepartmentId, Name = x.Name, Code = x.Code,
        ShortName = x.ShortName, DurationInMonths = x.DurationInMonths, AwardTitle = x.AwardTitle,
        Description = x.Description, IsAdmissionOpen = x.IsAdmissionOpen, IsActive = x.IsActive,
        DisplayOrder = x.DisplayOrder, RowVersion = Version(x.RowVersion)
    };
    private static AcademicLevelDto MapLevel(AcademicLevel x) => new()
    {
        Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code,
        LevelNo = x.LevelNo, IsPromotable = x.IsPromotable, IsTerminalLevel = x.IsTerminalLevel,
        IsActive = x.IsActive, DisplayOrder = x.DisplayOrder, RowVersion = Version(x.RowVersion)
    };
    private static AcademicTrackDto MapTrack(AcademicTrack x) => new()
    {
        Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code,
        Description = x.Description, IsDefault = x.IsDefault, IsActive = x.IsActive,
        DisplayOrder = x.DisplayOrder, RowVersion = Version(x.RowVersion)
    };
    private static SubjectDto MapSubject(Subject x) => new()
    {
        Id = x.Id, Name = x.Name, Code = x.Code, ShortName = x.ShortName,
        DefaultCreditHours = x.DefaultCreditHours, IsActive = x.IsActive,
        RowVersion = Version(x.RowVersion)
    };
    private static AcademicCurriculumDto MapCurriculum(AcademicCurriculum x) => new()
    {
        Id = x.Id, AcademicProgramId = x.AcademicProgramId, AcademicTrackId = x.AcademicTrackId,
        MediumId = x.MediumId, Name = x.Name, Code = x.Code, VersionNo = x.VersionNo,
        EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo, IsCurrent = x.IsCurrent,
        IsActive = x.IsActive, RowVersion = Version(x.RowVersion)
    };
    private static CurriculumSubjectDto MapCurriculumSubject(CurriculumSubject x) => new()
    {
        Id = x.Id, AcademicCurriculumId = x.AcademicCurriculumId, AcademicLevelId = x.AcademicLevelId,
        SubjectId = x.SubjectId, FullMarks = x.FullMarks, PassMarks = x.PassMarks,
        CreditHours = x.CreditHours, IsOptional = x.IsOptional, HasPractical = x.HasPractical,
        IsActive = x.IsActive, DisplayOrder = x.DisplayOrder, RowVersion = Version(x.RowVersion)
    };
    private static AcademicBatchDto MapBatch(AcademicBatch x) => new()
    {
        Id = x.Id, CampusId = x.CampusId, AcademicYearId = x.AcademicYearId,
        AcademicTermId = x.AcademicTermId, AcademicProgramId = x.AcademicProgramId,
        AcademicLevelId = x.AcademicLevelId, AcademicTrackId = x.AcademicTrackId,
        MediumId = x.MediumId, ShiftId = x.ShiftId, Name = x.Name, Code = x.Code,
        DeliveryMode = x.DeliveryMode, Capacity = x.Capacity,
        StartDate = x.StartDate, EndDate = x.EndDate, IsDefault = x.IsDefault,
        DisplayOrder = x.DisplayOrder, IsActive = x.IsActive, RowVersion = Version(x.RowVersion)
    };
    private static RoomDto MapRoom(Room x) => new()
    {
        Id = x.Id, CampusId = x.CampusId, Name = x.Name, Code = x.Code,
        Capacity = x.Capacity, RoomType = x.RoomType, IsActive = x.IsActive,
        RowVersion = Version(x.RowVersion)
    };


    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic setup access is required.", 403);
}
