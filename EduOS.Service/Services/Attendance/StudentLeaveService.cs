using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Attendance;

/// <summary>One authoritative student leave application per idempotent request; approval never edits attendance history.</summary>
public sealed class StudentLeaveService : IStudentLeaveService
{
    private readonly IGenericRepository<StudentLeaveApplication> _leaves;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentGuardian> _links;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IUnitOfWork _uow;
    private readonly EduOSDbContext _db;
    private readonly IDocumentFileService _fileService;
    private readonly IFileUploadService _storage;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentLeaveService> _logger;

    public StudentLeaveService(IGenericRepository<StudentLeaveApplication> leaves,
        IGenericRepository<StudentEnrollment> enrollments, IGenericRepository<Student> students,
        IGenericRepository<StudentGuardian> links, IGenericRepository<Guardian> guardians,
        IGenericRepository<AcademicYear> years, IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<StudentLeaveService> logger,
        EduOSDbContext db, IDocumentFileService fileService, IFileUploadService storage)
    {
        _leaves = leaves; _enrollments = enrollments; _students = students;
        _links = links; _guardians = guardians; _years = years;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
        _db = db; _fileService = fileService; _storage = storage;
    }

    public async Task<ApiResponse<PagedResult<StudentLeaveApplicationDto>>> GetApplicationsAsync(
        Guid? enrollmentReference, LeaveState? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<PagedResult<StudentLeaveApplicationDto>>("Student leave access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize ||
            enrollmentReference == Guid.Empty || state.HasValue && !Enum.IsDefined(state.Value))
            return Error<PagedResult<StudentLeaveApplicationDto>>("Invalid leave filters.");
        var tenant = _user.TenantId;
        var query = _leaves.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (enrollmentReference.HasValue)
        {
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == enrollmentReference.Value && !x.IsDeleted, ct);
            if (enrollment == null) return Error<PagedResult<StudentLeaveApplicationDto>>("Enrollment not found.", 404);
            if (!CanManage() && !await OwnsEnrollmentAsync(enrollment, ct))
                return Error<PagedResult<StudentLeaveApplicationDto>>("Enrollment not accessible.", 404);
            query = query.Where(x => x.StudentEnrollmentId == enrollment.Id);
        }
        else if (!CanManage())
        {
            var ownedIds = from enrollment in _enrollments.GetQueryable().AsNoTracking()
                join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                where enrollment.TenantId == tenant && student.TenantId == tenant && !enrollment.IsDeleted &&
                    !student.IsDeleted && (student.UserId == _user.UserId ||
                    (from link in _links.GetQueryable().AsNoTracking()
                     join guardian in _guardians.GetQueryable().AsNoTracking() on link.GuardianId equals guardian.Id
                     where link.TenantId == tenant && guardian.TenantId == tenant && !link.IsDeleted &&
                         !guardian.IsDeleted && guardian.IsActive &&
                         link.StudentId == student.Id && guardian.UserId == _user.UserId
                     select link.Id).Any())
                select enrollment.Id;
            query = query.Where(x => ownedIds.Contains(x.StudentEnrollmentId));
        }
        if (state.HasValue) query = query.Where(x => x.State == state.Value);
        var total = await query.CountAsync(ct);
        var data = await query.OrderByDescending(x => x.FromDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = data.Select(x => x.StudentEnrollmentId).Distinct().ToArray();
        var refs = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId }).ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        return ApiResponse<PagedResult<StudentLeaveApplicationDto>>.SuccessResponse(new PagedResult<StudentLeaveApplicationDto>
        {
            Items = data.Select(x => Map(x, refs.GetValueOrDefault(x.StudentEnrollmentId))).ToList(),
            TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    /// <summary>Stage a malware-scanned private file for a specific, authorized enrollment.</summary>
    public async Task<ApiResponse<FileAssetDto>> UploadAttachmentAsync(
        Guid enrollmentReference, Guid uploadRequestId, PrivateFileUploadDto upload,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Error<FileAssetDto>("Student leave access denied.", 403);
        if (enrollmentReference == Guid.Empty || uploadRequestId == Guid.Empty || upload == null)
            return Error<FileAssetDto>("Enrollment, upload request and file are required.");
        var tenant = _user.TenantId;
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == enrollmentReference &&
            x.IsCurrent && x.State == EnrollmentState.Active && !x.IsDeleted, ct);
        if (enrollment == null || (!CanManage() && !await OwnsEnrollmentAsync(enrollment, ct)))
            return Error<FileAssetDto>("Active enrollment not found.", 404);
        if (CanManage()) return await _fileService.UploadForLeaveAsync(uploadRequestId, upload, ct);
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == enrollment.StudentId && !x.IsDeleted, ct);
        if (student == null) return Error<FileAssetDto>("Student not found.", 404);
        return await _fileService.UploadPortalAsync(student.PublicId, uploadRequestId, upload, ct);
    }

    /// <summary>Only a live student/guardian link or authorized reviewer can read this private file.</summary>
    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAttachmentAsync(
        long applicationId, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<DocumentFileDownloadDto>("Student leave access denied.", 403);
        if (applicationId <= 0) return Error<DocumentFileDownloadDto>("Invalid leave application.");
        var tenant = _user.TenantId;
        var leave = await _leaves.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == applicationId && !x.IsDeleted, ct);
        if (leave?.AttachmentFileId is not long fileId)
            return Error<DocumentFileDownloadDto>("Attachment not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == leave.StudentEnrollmentId && !x.IsDeleted, ct);
        if (enrollment == null || (!CanManage() && !await OwnsEnrollmentAsync(enrollment, ct)))
            return Error<DocumentFileDownloadDto>("Attachment not found.", 404);
        var file = await _db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == fileId && !x.IsDeleted &&
            x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
            x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate" &&
            x.SizeBytes > 0 && x.SizeBytes <= 5242880, ct);
        if (file == null || !DocumentFileService.IsTenantDocumentKey(file.StorageKey, tenant) ||
            await _db.Documents.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.FileAssetId == fileId && !x.IsDeleted, ct) ||
            await _leaves.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && x.AttachmentFileId == fileId && !x.IsDeleted, ct) != 1)
            return Error<DocumentFileDownloadDto>("Attachment not found.", 404);
        var stored = await _storage.GetPrivateFileAsync(file.StorageKey);
        if (stored == null || stored.Content.LongLength != file.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)), file.Sha256,
                StringComparison.OrdinalIgnoreCase))
            return Error<DocumentFileDownloadDto>("Attachment integrity check failed.", 409);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = stored.Content, ContentType = file.ContentType,
            FileName = Path.GetFileName(file.OriginalFileName.Replace('\\', '/'))
        });
    }

    public async Task<ApiResponse<StudentLeaveApplicationDto>> ApplyAsync(
        CreateStudentLeaveApplicationRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<StudentLeaveApplicationDto>("Student leave access denied.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentEnrollmentReference == Guid.Empty ||
            request.FromDate == default || request.ToDate < request.FromDate ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 2000 ||
            request.AttachmentFileId is <= 0)
            return Error<StudentLeaveApplicationDto>("Valid dates, reason, request ID and optional attachment are required.");
        var tenant = _user.TenantId;
        var reason = request.Reason.Trim();
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var replay = await _leaves.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (replay != null)
                {
                    var old = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == replay.StudentEnrollmentId, token);
                    if (old == null || old.PublicId != request.StudentEnrollmentReference ||
                        replay.FromDate != request.FromDate || replay.ToDate != request.ToDate ||
                        replay.Reason != reason || replay.AttachmentFileId != request.AttachmentFileId)
                        return Error<StudentLeaveApplicationDto>("Request ID already used for different leave details.", 409);
                    if (!CanManage() && !await OwnsEnrollmentAsync(old, token))
                        return Error<StudentLeaveApplicationDto>("Enrollment not accessible.", 404);
                    return ApiResponse<StudentLeaveApplicationDto>.SuccessResponse(Map(replay, old.PublicId), "Leave already submitted.");
                }
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == request.StudentEnrollmentReference &&
                    x.IsCurrent && x.State == EnrollmentState.Active && !x.IsDeleted, token);
                if (enrollment == null) return Error<StudentLeaveApplicationDto>("Active current enrollment not found.", 404);
                if (!CanManage() && !await OwnsEnrollmentAsync(enrollment, token))
                    return Error<StudentLeaveApplicationDto>("Enrollment not accessible.", 404);
                var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == enrollment.AcademicYearId && !x.IsDeleted, token);
                if (year == null || request.FromDate < year.StartDate || request.ToDate > year.EndDate ||
                    request.FromDate < enrollment.EnrollmentDate ||
                    enrollment.EndDate.HasValue && request.ToDate > enrollment.EndDate.Value)
                    return Error<StudentLeaveApplicationDto>("Leave dates must fall within the student's enrollment and academic year.", 409);
                var duplicate = await _leaves.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.StudentEnrollmentId == enrollment.Id && !x.IsDeleted &&
                    (x.State == LeaveState.Submitted || x.State == LeaveState.Approved) &&
                    x.FromDate <= request.ToDate && x.ToDate >= request.FromDate, token);
                if (duplicate) return Error<StudentLeaveApplicationDto>("Overlapping pending or approved leave exists.", 409);
                if (request.AttachmentFileId is long attachmentId)
                {
                    // A file can belong to exactly one leave and no other document.
                    // The caller must be its uploader; a guessed file ID is never sufficient.
                    var file = await _db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == attachmentId && !x.IsDeleted &&
                        x.UploadedByUserId == _user.UserId && x.IsVerifiedSafe &&
                        x.MalwareScanStateCode == "Clean" && x.Visibility == FileVisibility.Private &&
                        x.StorageProvider == "LocalPrivate" && x.SizeBytes > 0 && x.SizeBytes <= 5242880, token);
                    if (file == null || !DocumentFileService.IsTenantDocumentKey(file.StorageKey, tenant))
                        return Error<StudentLeaveApplicationDto>("Verified private attachment not found.", 404);
                    if (await _db.Documents.AsNoTracking().AnyAsync(x =>
                            x.TenantId == tenant && x.FileAssetId == attachmentId && !x.IsDeleted, token) ||
                        await _leaves.GetQueryable().AsNoTracking().AnyAsync(x =>
                            x.TenantId == tenant && x.AttachmentFileId == attachmentId && !x.IsDeleted, token))
                        return Error<StudentLeaveApplicationDto>("Attachment is already in use.", 409);
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                var leave = new StudentLeaveApplication
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId, StudentEnrollmentId = enrollment.Id,
                    FromDate = request.FromDate, ToDate = request.ToDate, Reason = reason,
                    AttachmentFileId = request.AttachmentFileId, State = LeaveState.Submitted, CreatedAt = now, CreatedBy = _user.UserId
                };
                await _leaves.AddAsync(leave);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<StudentLeaveApplicationDto>.SuccessResponse(Map(leave, enrollment.PublicId), "Leave submitted.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<StudentLeaveApplicationDto>("Leave changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Student leave conflict in tenant {TenantId}", tenant);
            return Error<StudentLeaveApplicationDto>("Leave request conflicts with an existing request.", 409);
        }
    }

    /// <summary>Cancel only a submitted request; never rewrite approved attendance history.</summary>
    public async Task<ApiResponse<StudentLeaveApplicationDto>> CancelAsync(
        long applicationId, CancelStudentLeaveRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<StudentLeaveApplicationDto>("Student leave access denied.", 403);
        if (applicationId <= 0 || request == null ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 ||
            !TryVersion(request.RowVersion, out var expected))
            return Error<StudentLeaveApplicationDto>("Cancellation reason and row version are required.");
        var tenant = _user.TenantId;
        var note = "Cancelled: " + request.Reason.Trim();
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _leaves.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == applicationId && !x.IsDeleted, token);
                if (row == null) return Error<StudentLeaveApplicationDto>("Leave application not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.StudentEnrollmentId && !x.IsDeleted, token);
                if (enrollment == null || (!CanManage() && !await OwnsEnrollmentAsync(enrollment, token)))
                    return Error<StudentLeaveApplicationDto>("Leave application not found.", 404);
                if (row.State == LeaveState.Cancelled)
                    return row.ReviewNote == note
                        ? ApiResponse<StudentLeaveApplicationDto>.SuccessResponse(
                            Map(row, enrollment.PublicId), "Leave already cancelled.")
                        : Error<StudentLeaveApplicationDto>("Leave already cancelled with a different reason.", 409);
                if (row.State != LeaveState.Submitted)
                    return Error<StudentLeaveApplicationDto>("Only submitted leave can be cancelled.", 409);
                if (!VersionsMatch(row.RowVersion, expected))
                    return Error<StudentLeaveApplicationDto>("Leave changed. Reload before cancelling.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                row.State = LeaveState.Cancelled;
                row.ReviewNote = note;
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                _leaves.Update(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<StudentLeaveApplicationDto>.SuccessResponse(
                    Map(row, enrollment.PublicId), "Leave cancelled.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<StudentLeaveApplicationDto>("Leave changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Student leave cancellation conflict in tenant {TenantId}", tenant);
            return Error<StudentLeaveApplicationDto>("Concurrent leave cancellation conflict.", 409);
        }
    }

    public async Task<ApiResponse<StudentLeaveApplicationDto>> ReviewAsync(
        long applicationId, ReviewStudentLeaveRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<StudentLeaveApplicationDto>("Student leave review permission denied.", 403);
        if (applicationId <= 0 || request == null ||
            request.State is not (LeaveState.Approved or LeaveState.Rejected) ||
            !TryVersion(request.RowVersion, out var version) || request.ReviewNote?.Length > 1000)
            return Error<StudentLeaveApplicationDto>("Review decision and row version are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _leaves.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == applicationId && !x.IsDeleted, token);
                if (row == null) return Error<StudentLeaveApplicationDto>("Leave application not found.", 404);
                if (!VersionsMatch(row.RowVersion, version))
                    return Error<StudentLeaveApplicationDto>("Leave changed. Reload before approval.", 409);
                if (row.State != LeaveState.Submitted)
                    return Error<StudentLeaveApplicationDto>("Only submitted leave can be reviewed.", 409);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == row.StudentEnrollmentId && !x.IsDeleted, token);
                if (enrollment == null) return Error<StudentLeaveApplicationDto>("Enrollment no longer exists.", 409);
                if (request.State == LeaveState.Approved)
                {
                    if (!enrollment.IsCurrent || enrollment.State != EnrollmentState.Active)
                        return Error<StudentLeaveApplicationDto>("Enrollment is no longer active.", 409);
                    if (await _leaves.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id != row.Id &&
                        x.StudentEnrollmentId == row.StudentEnrollmentId && !x.IsDeleted &&
                        x.State == LeaveState.Approved &&
                        x.FromDate <= row.ToDate && x.ToDate >= row.FromDate, token))
                        return Error<StudentLeaveApplicationDto>("Another approved leave overlaps these dates.", 409);
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                row.State = request.State; row.ReviewNote = Normalize(request.ReviewNote);
                row.ReviewedByUserId = _user.UserId; row.ReviewedAt = now;
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                _leaves.Update(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<StudentLeaveApplicationDto>.SuccessResponse(Map(row, enrollment.PublicId), "Leave reviewed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<StudentLeaveApplicationDto>("Leave changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Student leave review conflict in tenant {TenantId}", tenant);
            return Error<StudentLeaveApplicationDto>("Leave review conflicts with another update.", 409);
        }
    }

    private async Task<bool> OwnsEnrollmentAsync(StudentEnrollment enrollment, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == enrollment.StudentId && !x.IsDeleted, ct);
        if (student == null) return false;
        if (student.UserId == _user.UserId) return true;
        return await (from link in _links.GetQueryable().AsNoTracking()
            join guardian in _guardians.GetQueryable().AsNoTracking() on link.GuardianId equals guardian.Id
            where link.TenantId == tenant && guardian.TenantId == tenant &&
                !link.IsDeleted && !guardian.IsDeleted && guardian.IsActive &&
                link.StudentId == student.Id && guardian.UserId == _user.UserId
            select link.Id).AnyAsync(ct);
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (CanManage() || _user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent"));
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("VicePrincipal"));
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static StudentLeaveApplicationDto Map(StudentLeaveApplication row, Guid reference) => new()
    {
        Id = row.Id, StudentEnrollmentReference = reference,
        FromDate = row.FromDate, ToDate = row.ToDate, Reason = row.Reason,
        AttachmentFileId = row.AttachmentFileId, State = row.State,
        ReviewedByUserId = row.ReviewedByUserId, ReviewedAt = row.ReviewedAt,
        ReviewNote = row.ReviewNote, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private static bool TryVersion(string? text, out byte[] data)
    {
        data = [];
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { data = Convert.FromBase64String(text); return data.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] a, byte[] b) => a.Length == b.Length && a.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(a, b);
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
