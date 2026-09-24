using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

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
    private readonly IGenericRepository<Department> _departments;
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
        IGenericRepository<Department> departments,
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

    public async Task<ApiResponse<AcademicSetupCatalogDto>> GetCatalogAsync(long? academicYearId, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<AcademicSetupCatalogDto>();
        if (academicYearId.HasValue && academicYearId.Value <= 0) return Error<AcademicSetupCatalogDto>("Academic year is invalid.");
        var tenantId = _currentUser.TenantId;
        if (academicYearId.HasValue && !await _academicYears.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == academicYearId.Value, cancellationToken))
            return Error<AcademicSetupCatalogDto>("Academic year not found.", 404);

        var programRows = await _programs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var programs = programRows.Select(MapProgram).ToList();
        var levelRows = await _levels.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.AcademicProgramId).ThenBy(x => x.LevelNo).ThenBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
        var levels = levelRows.Select(MapLevel).ToList();
        var trackRows = await _tracks.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.AcademicProgramId).ThenByDescending(x => x.IsDefault).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var tracks = trackRows.Select(MapTrack).ToList();
        var subjectRows = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ClassId == null && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var subjects = subjectRows.Select(MapSubject).ToList();
        var curriculumRows = await _curricula.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.IsCurrent).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var curricula = curriculumRows.Select(MapCurriculum).ToList();
        var curriculumIds = curricula.Select(x => x.Id).ToList();
        var curriculumSubjectRows = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && curriculumIds.Contains(x.AcademicCurriculumId))
            .OrderBy(x => x.AcademicCurriculumId).ThenBy(x => x.AcademicLevelId).ThenBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
        var curriculumSubjects = curriculumSubjectRows.Select(MapCurriculumSubject).ToList();
        var batchQuery = _batches.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive);
        if (academicYearId.HasValue) batchQuery = batchQuery.Where(x => x.AcademicYearId == academicYearId.Value);
        var batchRows = await batchQuery.OrderBy(x => x.AcademicYearId).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var batches = batchRows.Select(MapBatch).ToList();
        var roomRows = await _rooms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.CampusId).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var rooms = roomRows.Select(MapRoom).ToList();

        return ApiResponse<AcademicSetupCatalogDto>.SuccessResponse(new AcademicSetupCatalogDto
        {
            Programs = programs,
            Levels = levels,
            Tracks = tracks,
            Subjects = subjects,
            Curricula = curricula,
            CurriculumSubjects = curriculumSubjects,
            Batches = batches,
            Rooms = rooms
        });
    }

    public Task<ApiResponse<AcademicTrackDto>> CreateTrackAsync(CreateAcademicTrackDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicTrackDto>());
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || request.DisplayOrder <= 0 || request.DisplayOrder > 10000 || (request.AcademicProgramId.HasValue && request.AcademicProgramId.Value <= 0))
            return Task.FromResult(Error<AcademicTrackDto>("Track name, code, display order and optional programme are invalid."));
        return ExecuteWriteAsync("create academic track", async () =>
        {
            var tenantId = _currentUser.TenantId;
            AcademicProgram? program = null;
            if (request.AcademicProgramId.HasValue)
            {
                program = await _programs.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicProgramId.Value && x.IsActive, cancellationToken);
                if (program == null) return Error<AcademicTrackDto>("Programme not found.", 404);
            }
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _tracks.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.AcademicProgramId != request.AcademicProgramId || existing.IsDefault != request.IsDefault || existing.DisplayOrder != request.DisplayOrder)
                    return Error<AcademicTrackDto>("Track code is already in use with different settings.", 409);
                return ApiResponse<AcademicTrackDto>.SuccessResponse(MapTrack(existing), "Academic track already exists.");
            }
            if (request.IsDefault && await _tracks.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.AcademicProgramId == request.AcademicProgramId && x.IsDefault && x.IsActive, cancellationToken))
                return Error<AcademicTrackDto>("A default track already exists for this programme scope.", 409);
            var row = new AcademicTrack
            {
                TenantId = tenantId,
                AcademicProgramId = program?.Id,
                Name = name,
                Code = code,
                Description = Trim(request.Description),
                IsDefault = request.IsDefault,
                DisplayOrder = request.DisplayOrder,
                IsActive = true
            };
            await _tracks.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapTrack(row), "Academic track created.");
        });
    }

    public Task<ApiResponse<AcademicProgramDto>> CreateProgramAsync(CreateAcademicProgramDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicProgramDto>());
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || request.DurationInMonths <= 0 || request.DurationInMonths > 600)
            return Task.FromResult(Error<AcademicProgramDto>("Programme name, code and a valid duration are required."));
        return ExecuteWriteAsync("create programme", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            Campus? campus = null;
            if (request.CampusId.HasValue)
            {
                campus = await _campuses.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.CampusId.Value && x.IsActive, cancellationToken);
                if (campus == null) return Error<AcademicProgramDto>("Campus not found.", 404);
            }
            Department? department = null;
            if (request.DepartmentId.HasValue)
            {
                department = await _departments.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.DepartmentId.Value && x.IsActive, cancellationToken);
                if (department == null) return Error<AcademicProgramDto>("Department not found.", 404);
                if (campus != null && department.CampusId.HasValue && department.CampusId != campus.Id)
                    return Error<AcademicProgramDto>("Department belongs to a different campus.", 409);
            }
            var existing = await _programs.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.CampusId != request.CampusId || existing.DepartmentId != request.DepartmentId || existing.DurationInMonths != request.DurationInMonths)
                    return Error<AcademicProgramDto>("Programme code is already in use with different settings.", 409);
                return ApiResponse<AcademicProgramDto>.SuccessResponse(MapProgram(existing), "Programme already exists.");
            }
            var row = new AcademicProgram
            {
                TenantId = tenantId,
                CampusId = campus?.Id,
                DepartmentId = department?.Id,
                Name = name,
                Code = code,
                ShortName = Trim(request.ShortName),
                DurationInMonths = request.DurationInMonths,
                AwardTitle = Trim(request.AwardTitle),
                Description = Trim(request.Description),
                IsAdmissionOpen = request.IsAdmissionOpen,
                IsActive = true
            };
            await _programs.AddAsync(row);
            if (campus != null)
            {
                await _programCampuses.AddAsync(new ProgramCampus
                {
                    TenantId = tenantId,
                    AcademicProgram = row,
                    CampusId = campus.Id,
                    IsAdmissionOpen = request.IsAdmissionOpen,
                    IsActive = true
                });
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapProgram(row), "Programme created.");
        });
    }

    public Task<ApiResponse<AcademicLevelDto>> CreateLevelAsync(long academicProgramId, CreateAcademicLevelDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicLevelDto>());
        if (academicProgramId <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || request.LevelNo <= 0 || request.LevelNo > 100)
            return Task.FromResult(Error<AcademicLevelDto>("Programme, level name, code and level number are required."));
        return ExecuteWriteAsync("create academic level", async () =>
        {
            var tenantId = _currentUser.TenantId;
            if (!await _programs.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == academicProgramId && x.IsActive, cancellationToken))
                return Error<AcademicLevelDto>("Programme not found.", 404);
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _levels.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicProgramId == academicProgramId && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.LevelNo != request.LevelNo || existing.IsPromotable != request.IsPromotable || existing.IsTerminalLevel != request.IsTerminalLevel)
                    return Error<AcademicLevelDto>("Level code is already in use with different settings.", 409);
                return ApiResponse<AcademicLevelDto>.SuccessResponse(MapLevel(existing), "Academic level already exists.");
            }
            var row = new AcademicLevel { TenantId = tenantId, AcademicProgramId = academicProgramId, Name = name, Code = code, LevelNo = request.LevelNo, IsPromotable = request.IsPromotable, IsTerminalLevel = request.IsTerminalLevel, IsActive = true };
            await _levels.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapLevel(row), "Academic level created.");
        });
    }

    public Task<ApiResponse<AcademicSubjectDto>> CreateSubjectAsync(CreateAcademicSubjectDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicSubjectDto>());
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || !Enum.IsDefined(typeof(SubjectType), request.SubjectType) || request.DefaultFullMarks <= 0 || request.DefaultPassMarks < 0 || request.DefaultPassMarks > request.DefaultFullMarks || request.DefaultCreditHours < 0)
            return Task.FromResult(Error<AcademicSubjectDto>("Subject name, code, type and valid mark settings are required."));
        return ExecuteWriteAsync("create subject", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _subjects.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClassId == null && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.SubjectType != request.SubjectType || existing.DefaultFullMarks != request.DefaultFullMarks || existing.DefaultPassMarks != request.DefaultPassMarks || existing.DefaultCreditHours != request.DefaultCreditHours || existing.HasPractical != request.HasPractical)
                    return Error<AcademicSubjectDto>("Subject code is already in use with different settings.", 409);
                return ApiResponse<AcademicSubjectDto>.SuccessResponse(MapSubject(existing), "Subject already exists.");
            }
            var row = new Subject
            {
                TenantId = tenantId,
                ClassId = null,
                Name = name,
                Code = code,
                ShortName = Trim(request.ShortName),
                SubjectType = request.SubjectType,
                DefaultCreditHours = request.DefaultCreditHours,
                DefaultFullMarks = request.DefaultFullMarks,
                DefaultPassMarks = request.DefaultPassMarks,
                FullMark = DecimalToLegacyMark(request.DefaultFullMarks),
                PassMark = DecimalToLegacyMark(request.DefaultPassMarks),
                IsOptional = request.SubjectType == SubjectType.Optional,
                HasPractical = request.HasPractical,
                IsActive = true
            };
            await _subjects.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapSubject(row), "Subject created.");
        });
    }

    public Task<ApiResponse<AcademicCurriculumDto>> CreateCurriculumAsync(CreateAcademicCurriculumDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicCurriculumDto>());
        if (request == null || request.AcademicProgramId <= 0 || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            return Task.FromResult(Error<AcademicCurriculumDto>("Programme, curriculum name and code are required."));
        return ExecuteWriteAsync("create curriculum", async () =>
        {
            var tenantId = _currentUser.TenantId;
            if (!await _programs.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicProgramId && x.IsActive, cancellationToken))
                return Error<AcademicCurriculumDto>("Programme not found.", 404);
            var fromYear = await ResolveYearAsync(request.EffectiveFromAcademicYearId, cancellationToken);
            if (request.EffectiveFromAcademicYearId.HasValue && fromYear == null) return Error<AcademicCurriculumDto>("Effective-from academic year not found.", 404);
            var toYear = await ResolveYearAsync(request.EffectiveToAcademicYearId, cancellationToken);
            if (request.EffectiveToAcademicYearId.HasValue && toYear == null) return Error<AcademicCurriculumDto>("Effective-to academic year not found.", 404);
            if (fromYear != null && toYear != null && toYear.EndDate.Date < fromYear.StartDate.Date)
                return Error<AcademicCurriculumDto>("Curriculum effective years are out of order.");
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _curricula.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.AcademicProgramId != request.AcademicProgramId || existing.Name != name || existing.EffectiveFromAcademicYearId != request.EffectiveFromAcademicYearId || existing.EffectiveToAcademicYearId != request.EffectiveToAcademicYearId || existing.IsCurrent != request.IsCurrent)
                    return Error<AcademicCurriculumDto>("Curriculum code is already in use with different settings.", 409);
                return ApiResponse<AcademicCurriculumDto>.SuccessResponse(MapCurriculum(existing), "Curriculum already exists.");
            }
            if (request.IsCurrent && await _curricula.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.AcademicProgramId == request.AcademicProgramId && x.IsCurrent && x.IsActive, cancellationToken))
                return Error<AcademicCurriculumDto>("The programme already has a current curriculum.", 409);
            var row = new AcademicCurriculum
            {
                TenantId = tenantId,
                AcademicProgramId = request.AcademicProgramId,
                Name = name,
                Code = code,
                EffectiveFromAcademicYearId = request.EffectiveFromAcademicYearId,
                EffectiveToAcademicYearId = request.EffectiveToAcademicYearId,
                IsCurrent = request.IsCurrent,
                IsActive = true,
                Remarks = Trim(request.Remarks)
            };
            await _curricula.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapCurriculum(row), "Curriculum created.");
        });
    }

    public Task<ApiResponse<CurriculumSubjectDto>> RegisterCurriculumSubjectAsync(long academicCurriculumId, RegisterCurriculumSubjectDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<CurriculumSubjectDto>());
        if (academicCurriculumId <= 0 || request == null || request.AcademicLevelId <= 0 || request.SubjectId <= 0 || request.FullMarks <= 0 || request.PassMarks < 0 || request.PassMarks > request.FullMarks || request.CreditHours < 0)
            return Task.FromResult(Error<CurriculumSubjectDto>("Curriculum, level, subject and valid mark settings are required."));
        return ExecuteWriteAsync("register curriculum subject", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var curriculum = await _curricula.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == academicCurriculumId && x.IsActive, cancellationToken);
            if (curriculum == null) return Error<CurriculumSubjectDto>("Curriculum not found.", 404);
            var level = await _levels.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AcademicLevelId && x.IsActive, cancellationToken);
            if (level == null) return Error<CurriculumSubjectDto>("Academic level not found.", 404);
            if (level.AcademicProgramId != curriculum.AcademicProgramId) return Error<CurriculumSubjectDto>("Academic level belongs to a different programme.", 409);
            if (!await _subjects.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.SubjectId && x.IsActive, cancellationToken))
                return Error<CurriculumSubjectDto>("Subject not found.", 404);
            if (request.AcademicTrackId.HasValue && !await _tracks.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicTrackId.Value && x.IsActive && (!x.AcademicProgramId.HasValue || x.AcademicProgramId == curriculum.AcademicProgramId), cancellationToken))
                return Error<CurriculumSubjectDto>("Academic track is unavailable for this programme.", 409);
            if (request.MediumId.HasValue && !await _mediums.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.MediumId.Value && x.IsActive, cancellationToken))
                return Error<CurriculumSubjectDto>("Medium not found.", 404);
            var existing = await _curriculumSubjects.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicCurriculumId == academicCurriculumId && x.AcademicLevelId == request.AcademicLevelId && x.SubjectId == request.SubjectId && x.AcademicTrackId == request.AcademicTrackId && x.MediumId == request.MediumId && x.IsActive, cancellationToken);
            if (existing != null)
            {
                if (existing.FullMarks != request.FullMarks || existing.PassMarks != request.PassMarks || existing.CreditHours != request.CreditHours || existing.IsOptional != request.IsOptional || existing.HasPractical != request.HasPractical)
                    return Error<CurriculumSubjectDto>("Subject is already registered with different curriculum settings.", 409);
                return ApiResponse<CurriculumSubjectDto>.SuccessResponse(MapCurriculumSubject(existing), "Curriculum subject already registered.");
            }
            var row = new CurriculumSubject
            {
                TenantId = tenantId,
                AcademicCurriculumId = academicCurriculumId,
                AcademicLevelId = request.AcademicLevelId,
                SubjectId = request.SubjectId,
                AcademicTrackId = request.AcademicTrackId,
                MediumId = request.MediumId,
                FullMarks = request.FullMarks,
                PassMarks = request.PassMarks,
                CreditHours = request.CreditHours,
                IsOptional = request.IsOptional,
                HasPractical = request.HasPractical,
                IsActive = true
            };
            await _curriculumSubjects.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapCurriculumSubject(row), "Subject registered in curriculum.");
        });
    }

    public Task<ApiResponse<AcademicBatchDto>> CreateBatchAsync(CreateAcademicBatchDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AcademicBatchDto>());
        if (request == null || request.CampusId <= 0 || request.AcademicYearId <= 0 || request.AcademicProgramId <= 0 || request.AcademicLevelId <= 0 || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code) || request.Capacity <= 0 || request.Capacity > 100000 || !Enum.IsDefined(typeof(DeliveryMode), request.DeliveryMode))
            return Task.FromResult(Error<AcademicBatchDto>("Campus, academic year, programme, level, batch identity, delivery mode and capacity are required."));
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate.Value.Date < request.StartDate.Value.Date)
            return Task.FromResult(Error<AcademicBatchDto>("Batch end date cannot precede its start date."));
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
            var hasCampusAssignment = program.CampusId == null || program.CampusId == request.CampusId || await _programCampuses.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.AcademicProgramId == program.Id && x.CampusId == request.CampusId && x.IsActive, cancellationToken);
            if (!hasCampusAssignment) return Error<AcademicBatchDto>("Programme is unavailable at the selected campus.", 409);
            if (!await _levels.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicLevelId && x.AcademicProgramId == program.Id && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Academic level does not belong to the selected programme.", 409);
            if (request.AcademicTrackId.HasValue && !await _tracks.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicTrackId.Value && x.IsActive && (!x.AcademicProgramId.HasValue || x.AcademicProgramId == program.Id), cancellationToken))
                return Error<AcademicBatchDto>("Academic track is unavailable for this programme.", 409);
            if (request.MediumId.HasValue && !await _mediums.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.MediumId.Value && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Medium not found.", 404);
            if (request.ShiftId.HasValue && !await _shifts.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Id == request.ShiftId.Value && x.IsActive, cancellationToken))
                return Error<AcademicBatchDto>("Shift not found.", 404);
            var startDate = request.StartDate?.Date;
            var endDate = request.EndDate?.Date;
            if (startDate.HasValue && startDate.Value < year.StartDate.Date || endDate.HasValue && endDate.Value > year.EndDate.Date)
                return Error<AcademicBatchDto>("Batch dates must fall within the selected academic year.", 409);
            var name = request.Name.Trim();
            var code = NormalizeCode(request.Code);
            var existing = await _batches.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AcademicYearId == year.Id && x.Code == code, cancellationToken);
            if (existing != null)
            {
                if (existing.Name != name || existing.CampusId != request.CampusId || existing.AcademicProgramId != program.Id || existing.AcademicLevelId != request.AcademicLevelId || existing.AcademicTermId != request.AcademicTermId || existing.AcademicTrackId != request.AcademicTrackId || existing.MediumId != request.MediumId || existing.ShiftId != request.ShiftId || existing.DeliveryMode != request.DeliveryMode || existing.Capacity != request.Capacity || existing.StartDate?.Date != startDate || existing.EndDate?.Date != endDate)
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
                DeliveryMode = request.DeliveryMode,
                Capacity = request.Capacity,
                StartDate = startDate,
                EndDate = endDate,
                Remarks = Trim(request.Remarks),
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
                if (existing.Name != name || existing.CampusId != request.CampusId || existing.Capacity != request.Capacity || existing.IsLab != request.IsLab || existing.BuildingName != Trim(request.BuildingName) || existing.Floor != Trim(request.Floor))
                    return Error<AcademicRoomDto>("Room code is already in use with different settings.", 409);
                return ApiResponse<AcademicRoomDto>.SuccessResponse(MapRoom(existing), "Room already exists.");
            }
            var row = new Room { TenantId = tenantId, CampusId = request.CampusId, Name = name, Code = code, BuildingName = Trim(request.BuildingName), Floor = Trim(request.Floor), Capacity = request.Capacity, IsLab = request.IsLab, IsActive = true };
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

    private static AcademicProgramDto MapProgram(AcademicProgram x) => new() { Id = x.Id, CampusId = x.CampusId, DepartmentId = x.DepartmentId, Name = x.Name, Code = x.Code, ShortName = x.ShortName, DurationInMonths = x.DurationInMonths, AwardTitle = x.AwardTitle, IsAdmissionOpen = x.IsAdmissionOpen, IsActive = x.IsActive };
    private static AcademicLevelDto MapLevel(AcademicLevel x) => new() { Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code, LevelNo = x.LevelNo, IsPromotable = x.IsPromotable, IsTerminalLevel = x.IsTerminalLevel, IsActive = x.IsActive };
    private static AcademicTrackDto MapTrack(AcademicTrack x) => new() { Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code, Description = x.Description, IsDefault = x.IsDefault, DisplayOrder = x.DisplayOrder, IsActive = x.IsActive };
    private static AcademicSubjectDto MapSubject(Subject x) => new() { Id = x.Id, Name = x.Name, Code = x.Code, ShortName = x.ShortName, SubjectType = x.SubjectType, DefaultCreditHours = x.DefaultCreditHours, DefaultFullMarks = x.DefaultFullMarks, DefaultPassMarks = x.DefaultPassMarks, HasPractical = x.HasPractical, IsActive = x.IsActive };
    private static AcademicCurriculumDto MapCurriculum(AcademicCurriculum x) => new() { Id = x.Id, AcademicProgramId = x.AcademicProgramId, Name = x.Name, Code = x.Code, EffectiveFromAcademicYearId = x.EffectiveFromAcademicYearId, EffectiveToAcademicYearId = x.EffectiveToAcademicYearId, IsCurrent = x.IsCurrent, IsActive = x.IsActive };
    private static CurriculumSubjectDto MapCurriculumSubject(CurriculumSubject x) => new() { Id = x.Id, AcademicCurriculumId = x.AcademicCurriculumId, AcademicLevelId = x.AcademicLevelId, SubjectId = x.SubjectId, AcademicTrackId = x.AcademicTrackId, MediumId = x.MediumId, FullMarks = x.FullMarks, PassMarks = x.PassMarks, CreditHours = x.CreditHours, IsOptional = x.IsOptional, HasPractical = x.HasPractical, IsActive = x.IsActive };
    private static AcademicBatchDto MapBatch(AcademicBatch x) => new() { Id = x.Id, CampusId = x.CampusId, AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, AcademicProgramId = x.AcademicProgramId, AcademicLevelId = x.AcademicLevelId, AcademicTrackId = x.AcademicTrackId, MediumId = x.MediumId, ShiftId = x.ShiftId, Name = x.Name, Code = x.Code, DeliveryMode = x.DeliveryMode, Capacity = x.Capacity, StartDate = x.StartDate, EndDate = x.EndDate, IsActive = x.IsActive };
    private static AcademicRoomDto MapRoom(Room x) => new() { Id = x.Id, CampusId = x.CampusId, Name = x.Name, Code = x.Code, BuildingName = x.BuildingName, Floor = x.Floor, Capacity = x.Capacity, IsLab = x.IsLab, IsActive = x.IsActive };
    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, StatusCode = 201, Message = message, Data = data };
    private static ApiResponse<T> Error<T>(string message, int statusCode = 400) => ApiResponse<T>.ErrorResponse(message, statusCode);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Academic setup access is required.", 403);
}
