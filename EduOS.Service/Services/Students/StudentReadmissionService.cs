using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;
namespace EduOS.Service.Services.Students;

/// <summary>Creates a new enrollment after a final exit. Never reopens historical enrollments or deletes exits.</summary>
public sealed class StudentReadmissionService : IStudentReadmissionService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentExitRecord> _exits;
    private readonly IGenericRepository<StudentStatusHistory> _history;
    private readonly IGenericRepository<TransferRequest> _transfers;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentReadmissionService> _logger;
    public StudentReadmissionService(IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments, IGenericRepository<StudentExitRecord> exits,
        IGenericRepository<StudentStatusHistory> history, IGenericRepository<TransferRequest> transfers,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicLevel> levels,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicTerm> terms,
        IGenericRepository<AcademicCurriculum> curricula, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<StudentReadmissionService> logger)
    {
        _students=students; _enrollments=enrollments; _exits=exits; _history=history; _transfers=transfers;
        _years=years; _levels=levels; _batches=batches; _terms=terms; _curricula=curricula;
        _uow=uow; _user=user; _clock=clock; _logger=logger;
    }
    private bool Allowed => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static ApiResponse<T> Fail<T>(string message,int code=400)=>ApiResponse<T>.ErrorResponse(message,code);
    private static bool Decode(string? text,out byte[] bytes)
    {
        try { bytes=Convert.FromBase64String(text??""); return bytes.Length>0; }
        catch(FormatException) { bytes=Array.Empty<byte>(); return false; }
    }
    private static bool Matches(byte[] actual,byte[] expected)=>
        actual.Length==expected.Length && CryptographicOperations.FixedTimeEquals(actual,expected);
    private static bool Eligible(Student student,StudentExitRecord exit)=>
        (student.StatusCode=="Dropout" && exit.ExitType==StudentExitType.Dropout) ||
        (student.StatusCode=="TC" && exit.ExitType==StudentExitType.Transfer);
    private static StudentReadmissionResultDto Map(Student student,StudentEnrollment enrollment,bool replay)=>new()
    {
        StudentReference=student.PublicId,EnrollmentReference=enrollment.PublicId,
        AcademicBatchId=enrollment.AcademicBatchId,Roll=enrollment.RollNo,AlreadyProcessed=replay
    };
    private Task<bool> TransferBlocked(long tenant,long student,CancellationToken ct)=>
        _transfers.GetQueryable().AsNoTracking().AnyAsync(x=>x.SourceTenantId==tenant &&
            x.SourceStudentId==student && !x.IsDeleted &&
            (x.State==TransferRequestState.Submitted || x.State==TransferRequestState.SourceApproved ||
             x.State==TransferRequestState.DestinationAccepted || x.State==TransferRequestState.Completed),ct);

    public async Task<ApiResponse<StudentReadmissionOptionsDto>> GetOptionsAsync(Guid reference,CancellationToken ct=default)
    {
        if(!Allowed)return Fail<StudentReadmissionOptionsDto>("Tenant administrator permission required.",403);
        if(reference==Guid.Empty)return Fail<StudentReadmissionOptionsDto>("Student reference required.");
        try
        {
            var tenant=_user.TenantId;
            var student=await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==tenant && x.PublicId==reference && !x.IsDeleted,ct);
            if(student==null)return Fail<StudentReadmissionOptionsDto>("Student not found.",404);
            var exit=await _exits.GetQueryable().AsNoTracking().Where(x=>
                x.TenantId==tenant && x.StudentId==student.Id && !x.IsDeleted)
                .OrderByDescending(x=>x.ProcessedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
            if(exit==null || !Eligible(student,exit))
                return Fail<StudentReadmissionOptionsDto>("Student must have a qualifying final dropout or transfer exit.",409);
            if(await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x=>
                x.TenantId==tenant && x.StudentId==student.Id && x.IsCurrent && !x.IsDeleted,ct))
                return Fail<StudentReadmissionOptionsDto>("Current enrollment already exists.",409);
            if(await TransferBlocked(tenant,student.Id,ct))
                return Fail<StudentReadmissionOptionsDto>("Transfer is pending or completed; resolve transfer first.",409);
            var source=await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==tenant && x.Id==exit.StudentEnrollmentId && x.StudentId==student.Id &&
                !x.IsDeleted && !x.IsCurrent,ct);
            if(source==null)return Fail<StudentReadmissionOptionsDto>("Previous enrollment unavailable.",409);
            var today=DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var years=await _years.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.IsActive && !x.IsDeleted && (x.CampusId==null || x.CampusId==source.CampusId) &&
                x.StartDate<=today && x.EndDate>=today).ToListAsync(ct);
            var yearIds=years.Select(x=>x.Id).ToArray();
            var levels=await _levels.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.IsActive && !x.IsDeleted && x.AcademicProgramId==source.AcademicProgramId).ToListAsync(ct);
            var levelIds=levels.Select(x=>x.Id).ToArray();
            var batches=await _batches.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.IsActive && !x.IsDeleted && x.CampusId==source.CampusId &&
                x.AcademicProgramId==source.AcademicProgramId && x.Capacity>0 &&
                yearIds.Contains(x.AcademicYearId) && levelIds.Contains(x.AcademicLevelId) &&
                (!x.StartDate.HasValue || x.StartDate<=today) && (!x.EndDate.HasValue || x.EndDate>=today))
                .OrderBy(x=>x.Name).Take(200).ToListAsync(ct);
            var terms=await _terms.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.IsActive && !x.IsDeleted && x.StartDate<=today && x.EndDate>=today)
                .Select(x=>x.Id).ToArrayAsync(ct);
            var curricula=await _curricula.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.IsActive && x.IsCurrent && !x.IsDeleted &&
                x.AcademicProgramId==source.AcademicProgramId && x.EffectiveFrom<=today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo>=today)).ToListAsync(ct);
            var yearNames=years.ToDictionary(x=>x.Id,x=>x.Name);
            var levelNames=levels.ToDictionary(x=>x.Id,x=>x.Name);
            var targets=batches.Where(x=>(!x.AcademicTermId.HasValue || terms.Contains(x.AcademicTermId.Value)) &&
                curricula.Count(c=>c.AcademicTrackId==x.AcademicTrackId && c.MediumId==x.MediumId)==1)
                .Select(x=>new StudentReadmissionTargetDto
                {
                    AcademicBatchId=x.Id,Name=yearNames[x.AcademicYearId]+" / "+levelNames[x.AcademicLevelId]+" / "+x.Name
                }).ToArray();
            return ApiResponse<StudentReadmissionOptionsDto>.SuccessResponse(new StudentReadmissionOptionsDto
            {
                StudentReference=student.PublicId,StudentCode=student.StudentCode,PreviousStatus=student.StatusCode,
                PreviousEnrollmentId=source.Id,StudentRowVersion=Convert.ToBase64String(student.RowVersion),
                PreviousEnrollmentRowVersion=Convert.ToBase64String(source.RowVersion),Targets=targets
            });
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            _logger.LogError(ex,"Readmission options failed for tenant {TenantId}",_user.TenantId);
            return Fail<StudentReadmissionOptionsDto>("Readmission options unavailable.",500);
        }
    }

    public async Task<ApiResponse<StudentReadmissionResultDto>> ProcessAsync(Guid reference,
        ProcessStudentReadmissionDto request,CancellationToken ct=default)
    {
        if(!Allowed)return Fail<StudentReadmissionResultDto>("Tenant administrator permission required.",403);
        if(reference==Guid.Empty || request==null || request.ClientRequestId==Guid.Empty ||
            request.PreviousEnrollmentId<=0 || request.TargetAcademicBatchId<=0 ||
            string.IsNullOrWhiteSpace(request.TargetRoll) || request.TargetRoll.Trim().Length>50 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length>450 ||
            !Decode(request.StudentRowVersion,out var studentVersion) ||
            !Decode(request.PreviousEnrollmentRowVersion,out var sourceVersion))
            return Fail<StudentReadmissionResultDto>("Valid request, reason and row versions required.");
        var tenant=_user.TenantId;
        var roll=request.TargetRoll.Trim();
        var note="Readmission: "+request.Reason.Trim();
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token=>
            {
                var student=await _students.GetQueryable().FirstOrDefaultAsync(x=>
                    x.TenantId==tenant && x.PublicId==reference && !x.IsDeleted,token);
                if(student==null)return Fail<StudentReadmissionResultDto>("Student not found.",404);
                var replay=await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>
                    x.TenantId==tenant && x.ClientRequestId==request.ClientRequestId,token);
                if(replay!=null)
                {
                    if(replay.StudentId!=student.Id || replay.AcademicBatchId!=request.TargetAcademicBatchId ||
                        replay.RollNo!=roll || replay.Remarks!=note || replay.IsDeleted)
                        return Fail<StudentReadmissionResultDto>("Request ID belongs to different enrollment details.",409);
                    return ApiResponse<StudentReadmissionResultDto>.SuccessResponse(Map(student,replay,true),"Readmission already processed.");
                }
                if(await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x=>
                    x.TenantId==tenant && x.StudentId==student.Id && x.IsCurrent && !x.IsDeleted,token))
                    return Fail<StudentReadmissionResultDto>("Current enrollment already exists.",409);
                var exit=await _exits.GetQueryable().AsNoTracking().Where(x=>
                    x.TenantId==tenant && x.StudentId==student.Id && !x.IsDeleted)
                    .OrderByDescending(x=>x.ProcessedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(token);
                if(exit==null || !Eligible(student,exit) || exit.StudentEnrollmentId!=request.PreviousEnrollmentId)
                    return Fail<StudentReadmissionResultDto>("Latest final exit is not eligible.",409);
                if(!Matches(student.RowVersion,studentVersion))
                    return Fail<StudentReadmissionResultDto>("Student changed. Reload and retry.",409);
                var source=await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>
                    x.TenantId==tenant && x.Id==request.PreviousEnrollmentId && x.StudentId==student.Id &&
                    !x.IsDeleted && !x.IsCurrent,token);
                if(source==null || !Matches(source.RowVersion,sourceVersion))
                    return Fail<StudentReadmissionResultDto>("Previous enrollment changed. Reload and retry.",409);
                if(await TransferBlocked(tenant,student.Id,token))
                    return Fail<StudentReadmissionResultDto>("Transfer workflow must be resolved first.",409);
                var today=DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var batch=await _batches.GetQueryable().FirstOrDefaultAsync(x=>
                    x.TenantId==tenant && x.Id==request.TargetAcademicBatchId && x.IsActive &&
                    !x.IsDeleted && x.Capacity>0,token);
                if(batch==null || batch.CampusId!=source.CampusId ||
                    batch.AcademicProgramId!=source.AcademicProgramId ||
                    (batch.StartDate.HasValue && batch.StartDate.Value>today) ||
                    (batch.EndDate.HasValue && batch.EndDate.Value<today))
                    return Fail<StudentReadmissionResultDto>("Target batch is not eligible for this campus/program.",409);
                var year=await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>
                    x.TenantId==tenant && x.Id==batch.AcademicYearId && x.IsActive && !x.IsDeleted &&
                    (x.CampusId==null || x.CampusId==batch.CampusId) &&
                    x.StartDate<=today && x.EndDate>=today,token);
                var level=await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x=>
                    x.TenantId==tenant && x.Id==batch.AcademicLevelId && x.IsActive && !x.IsDeleted &&
                    x.AcademicProgramId==batch.AcademicProgramId,token);
                if(year==null || level==null)
                    return Fail<StudentReadmissionResultDto>("Academic year or level unavailable.",409);
                if(batch.AcademicTermId.HasValue && !await _terms.GetQueryable().AsNoTracking().AnyAsync(x=>
                    x.TenantId==tenant && x.Id==batch.AcademicTermId.Value &&
                    x.AcademicYearId==batch.AcademicYearId && x.IsActive && !x.IsDeleted &&
                    x.StartDate<=today && x.EndDate>=today,token))
                    return Fail<StudentReadmissionResultDto>("Academic term unavailable.",409);
                var curricula=await _curricula.GetQueryable().AsNoTracking().Where(x=>
                    x.TenantId==tenant && x.IsActive && x.IsCurrent && !x.IsDeleted &&
                    x.AcademicProgramId==batch.AcademicProgramId && x.AcademicTrackId==batch.AcademicTrackId &&
                    x.MediumId==batch.MediumId && x.EffectiveFrom<=today &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo>=today))
                    .Take(2).Select(x=>x.Id).ToArrayAsync(token);
                if(curricula.Length!=1)return Fail<StudentReadmissionResultDto>("Exactly one curriculum required.",409);
                // Lock the tenant-owned target batch with its rowversion before
                // checking roll and capacity, matching admission/import allocation.
                batch.UpdatedAt=_clock.GetUtcNow().UtcDateTime;
                batch.UpdatedBy=_user.UserId;
                _batches.Update(batch);
                await _uow.SaveChangesAsync(token);
                // Canonical unique batch/roll index also reserves rolls belonging to historic rows.
                if(await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x=>
                    x.TenantId==tenant && x.AcademicBatchId==batch.Id && x.RollNo==roll && !x.IsDeleted,token))
                    return Fail<StudentReadmissionResultDto>("Roll already exists in this batch.",409);
                if(await _enrollments.GetQueryable().AsNoTracking().CountAsync(x=>
                    x.TenantId==tenant && x.AcademicBatchId==batch.Id && x.IsCurrent &&
                    x.State==EnrollmentState.Active && !x.IsDeleted,token)>=batch.Capacity)
                    return Fail<StudentReadmissionResultDto>("Batch capacity reached.",409);
                var now=_clock.GetUtcNow().UtcDateTime;
                var target=new StudentEnrollment
                {
                    TenantId=tenant,PublicId=Guid.NewGuid(),ClientRequestId=request.ClientRequestId,
                    StudentId=student.Id,CampusId=batch.CampusId,AcademicYearId=batch.AcademicYearId,
                    AcademicTermId=batch.AcademicTermId,AcademicProgramId=batch.AcademicProgramId,
                    AcademicLevelId=batch.AcademicLevelId,AcademicBatchId=batch.Id,
                    AcademicCurriculumId=curricula[0],AcademicTrackId=batch.AcademicTrackId,
                    MediumId=batch.MediumId,ShiftId=batch.ShiftId,RollNo=roll,
                    EnrollmentDate=today,IsCurrent=true,State=EnrollmentState.Active,
                    Remarks=note,CreatedAt=now,CreatedBy=_user.UserId
                };
                await _enrollments.AddAsync(target);
                var from=student.StatusCode;
                student.StatusCode="Active"; student.UpdatedAt=now; student.UpdatedBy=_user.UserId;
                await _history.AddAsync(new StudentStatusHistory
                {
                    TenantId=tenant,StudentId=student.Id,FromStatusCode=from,ToStatusCode="Active",
                    EffectiveAt=now,ChangedByUserId=_user.UserId,Reason=note,
                    CreatedAt=now,CreatedBy=_user.UserId
                });
                await _uow.SaveChangesAsync(token);
                return new ApiResponse<StudentReadmissionResultDto>
                {
                    Success=true,StatusCode=201,Message="Student readmitted.",Data=Map(student,target,false)
                };
            },System.Data.IsolationLevel.Serializable,ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex,"Readmission concurrency conflict for tenant {TenantId}",tenant);
            return Fail<StudentReadmissionResultDto>("Student changed. Reload and retry.",409);
        }
        catch(DbUpdateException ex)
        {
            _logger.LogWarning(ex,"Readmission database conflict for tenant {TenantId}",tenant);
            return Fail<StudentReadmissionResultDto>("Readmission conflicts with an existing enrollment.",409);
        }
        catch(TransactionAbortedException ex)
        {
            _logger.LogWarning(ex,"Readmission transaction aborted for tenant {TenantId}",tenant);
            return Fail<StudentReadmissionResultDto>("Concurrent readmission conflict.",409);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex,"Readmission failed for tenant {TenantId}",tenant);
            return Fail<StudentReadmissionResultDto>("Readmission could not be completed.",500);
        }
    }
}
