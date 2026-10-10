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

public sealed class AcademicSetupService : IAcademicSetupService
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
        });
    }

    public Task<ApiResponse<AcademicBatchDto>> CreateBatchAsync(CreateAcademicBatchDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicBatchDto>());
        if (request == null || request.CampusId <= 0 || request.AcademicYearId <= 0 || request.AcademicProgramId <= 0 || request.AcademicLevelId <= 0 || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || request.Capacity <= 0 || request.Capacity > 100000 || !Enum.IsDefined(typeof(DeliveryMode), request.DeliveryMode))
            return Task.FromResult(Error<AcademicBatchDto>("Campus, academic year, programme, level, batch identity, delivery mode and capacity are required."));
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate.Value.Date < request.StartDate.Value.Date)
            return Task.FromResult(Error<AcademicBatchDto>("Batch end date cannot precede its start date."));
        if (!string.IsNullOrWhiteSpace(request.Remarks))
            return Task.FromResult(Error<AcademicBatchDto>("Batch remarks are not supported by the canonical model."));
        return ExecuteWriteAsync("create academic batch", async () =>
        {
            var tenantId = _currentUser.TenantId;
            if (!await _campuses.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.CampusId && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Campus not found.", 404);
            var year = await _academicYears.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicYearId && x.IsActive, cancellationToken);
            if (year == null) return Error<AcademicBatchDto>("Academic year not found.", 404);
            if (request.AcademicTermId.HasValue && !await _academicTerms.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicTermId.Value && x.AcademicYearId == year.Id && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Academic term does not belong to the selected academic year.", 409);
            var program = await _programs.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicProgramId && x.IsActive, cancellationToken);
            if (program == null) return Error<AcademicBatchDto>("Programme not found.", 404);
            var scopedCampuses = await _programCampuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.AcademicProgramId == program.Id && x.IsActive)
                .Select(x => x.CampusId).ToArrayAsync(cancellationToken);
            var hasCampusAssignment = scopedCampuses.Length == 0 || scopedCampuses.Contains(request.CampusId);
            if (!hasCampusAssignment) return Error<AcademicBatchDto>("Programme is unavailable at the selected campus.", 409);
            if (!await _levels.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicLevelId && x.AcademicProgramId == program.Id && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Academic level does not belong to the selected programme.", 409);
            if (request.AcademicTrackId.HasValue && !await _tracks.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicTrackId.Value && x.IsActive && (!x.AcademicProgramId.HasValue || x.AcademicProgramId == program.Id), cancellationToken))
                return Error<AcademicBatchDto>("Academic track is unavailable for this programme.", 409);
            if (request.MediumId.HasValue && !await _mediums.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.MediumId.Value && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Medium not found.", 404);
            if (request.ShiftId.HasValue && !await _shifts.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.ShiftId.Value && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Shift not found.", 404);
            var startDate = request.StartDate.HasValue ? DateOnly.FromDateTime(request.StartDate.Value) : (DateOnly?)null;
            var endDate = request.EndDate.HasValue ? DateOnly.FromDateTime(request.EndDate.Value) : (DateOnly?)null;
            if (startDate.HasValue && startDate.Value < year.StartDate || endDate.HasValue && endDate.Value > year.EndDate)
                return Error<AcademicBatchDto>("Batch dates must fall within the selected academic year.", 409);
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _batches.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicYearId == year.Id && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.CampusId != request.CampusId || existing.AcademicProgramId != program.Id || existing.AcademicLevelId != request.AcademicLevelId || existing.AcademicTermId != request.AcademicTermId || existing.AcademicTrackId != request.AcademicTrackId || existing.MediumId != request.MediumId || existing.ShiftId != request.ShiftId || existing.DeliveryMode.ToString() != request.DeliveryMode.ToString() || existing.Capacity != request.Capacity || existing.StartDate != startDate || existing.EndDate != endDate)
                    return Error<AcademicBatchDto>("Batch code is already in use with different settings.", 409);
                return ApiResponse<AcademicBatchDto>.SuccessResponse(MapBatch(existing), "Academic batch already exists.");
            }
            var row = new AcademicBatch
            {
                TenantId = tenantId,
                CampusId = request.CampusId,
                AcademicYearId = year.Id,
                AcademicTermId = request.AcademicTermId,
                AcademicProgramId = program.Id,
                AcademicLevelId = request.AcademicLevelId,
                AcademicTrackId = request.AcademicTrackId,
                MediumId = request.MediumId,
                ShiftId = request.ShiftId,
                Name = name,
                Code = code,
                DeliveryMode = Enum.Parse<DeliveryModeType>(request.DeliveryMode.ToString()),
                Capacity = request.Capacity,
                StartDate = startDate,
                EndDate = endDate,
                IsActive = true
            };
            await _batches.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapBatch(row), "Academic batch created.");
        });
    }

    public Task<ApiResponse<AcademicRoomDto>> CreateRoomAsync(CreateAcademicRoomDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicRoomDto>());
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || request.Capacity <= 0 || request.Capacity > 100000)
            return Task.FromResult(Error<AcademicRoomDto>("Room name, code and capacity are required."));
        if (!request.CampusId.HasValue || request.CampusId <= 0)
            return Task.FromResult(Error<AcademicRoomDto>("Campus must be selected for a room."));
        if (request.IsLab || !string.IsNullOrWhiteSpace(request.BuildingName) || !string.IsNullOrWhiteSpace(request.Floor))
            return Task.FromResult(Error<AcademicRoomDto>("Lab/building/floor metadata is not supported by the current room model."));
        return ExecuteWriteAsync("create room", async () =>
        {
            var tenantId = _currentUser.TenantId;
            if (request.CampusId.HasValue && !await _campuses.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.CampusId.Value && x.IsActive, cancellationToken))
                return Error<AcademicRoomDto>("Campus not found.", 404);
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _rooms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.CampusId != request.CampusId || existing.Capacity != request.Capacity || false)
                    return Error<AcademicRoomDto>("Room code is already in use with different settings.", 409);
                return ApiResponse<AcademicRoomDto>.SuccessResponse(MapRoom(existing), "Room already exists.");
            }
            var row = new Room { TenantId = tenantId, CampusId = request.CampusId.Value, Name = name, Code = code, Capacity = request.Capacity, IsActive = true };
            await _rooms.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapRoom(row), "Room created.");
        });
    }

    private async Task<AcademicYear?> ResolveYearAsync(long? id, CancellationToken cancellationToken) => id.HasValue
        ? await _academicYears.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id.Value && x.IsActive, cancellationToken)
        : null;

    private async Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = SerializableScope();
                var response = await action();
                if (response.Success) scope.Complete();
                return response;
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting academic setup write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic setup conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized academic setup write aborted during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Academic setup conflicts with another update. Reload and try again.", 409);
        }
    }

    private bool CanRead() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (IsManager() || _currentUser.IsInRole("Teacher"));
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && IsManager();
    private bool IsManager() => _currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal");
    private static TransactionScope SerializableScope() => new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int DecimalToLegacyMark(decimal value) => value > int.MaxValue ? int.MaxValue : decimal.ToInt32(decimal.Truncate(value));

    private static AcademicProgramDto MapProgram(AcademicProgram x) => new() { Id = x.Id, AcademicDepartmentId = x.AcademicDepartmentId, Name = x.Name, Code = x.Code, ShortName = x.ShortName, DurationInMonths = x.DurationInMonths, AwardTitle = x.AwardTitle, Description = x.Description, IsAdmissionOpen = x.IsAdmissionOpen, IsActive = x.IsActive, DisplayOrder = x.DisplayOrder, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static AcademicLevelDto MapLevel(AcademicLevel x) => new() { Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code, LevelNo = x.LevelNo, IsPromotable = x.IsPromotable, IsTerminalLevel = x.IsTerminalLevel, IsActive = x.IsActive };
    private static AcademicTrackDto MapTrack(AcademicTrack x) => new() { Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code, Description = x.Description, IsDefault = x.IsDefault, DisplayOrder = x.DisplayOrder, IsActive = x.IsActive };
    private static AcademicSubjectDto MapSubject(Subject x) => new() { Id = x.Id, Name = x.Name, Code = x.Code, ShortName = x.ShortName, SubjectType = SubjectType.Core, DefaultCreditHours = x.DefaultCreditHours, DefaultFullMarks = 100m, DefaultPassMarks = 33m, HasPractical = false, IsActive = x.IsActive };
    private static AcademicCurriculumDto MapCurriculum(AcademicCurriculum x) => new() { Id = x.Id, AcademicProgramId = x.AcademicProgramId, AcademicTrackId = x.AcademicTrackId, MediumId = x.MediumId, Name = x.Name, Code = x.Code, VersionNo = x.VersionNo, EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo, IsCurrent = x.IsCurrent, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static CurriculumSubjectDto MapCurriculumSubject(CurriculumSubject x) => new() { Id = x.Id, AcademicCurriculumId = x.AcademicCurriculumId, AcademicLevelId = x.AcademicLevelId, SubjectId = x.SubjectId, FullMarks = x.FullMarks, PassMarks = x.PassMarks, CreditHours = x.CreditHours, IsOptional = x.IsOptional, HasPractical = x.HasPractical, IsActive = x.IsActive, DisplayOrder = x.DisplayOrder, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static AcademicBatchDto MapBatch(AcademicBatch x) => new() { Id = x.Id, CampusId = x.CampusId, AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, AcademicProgramId = x.AcademicProgramId, AcademicLevelId = x.AcademicLevelId, AcademicTrackId = x.AcademicTrackId, MediumId = x.MediumId, ShiftId = x.ShiftId, Name = x.Name, Code = x.Code, DeliveryMode = x.DeliveryMode, Capacity = x.Capacity, StartDate = x.StartDate, EndDate = x.EndDate, IsDefault = x.IsDefault, DisplayOrder = x.DisplayOrder, IsActive = x.IsActive, RowVersion = Convert.ToBase64String(x.RowVersion) };
    private static AcademicRoomDto MapRoom(Room x) => new() { Id = x.Id, CampusId = x.CampusId, Name = x.Name, Code = x.Code, Capacity = x.Capacity, IsLab = false, IsActive = x.IsActive };
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic setup access is required.", 403);
}
