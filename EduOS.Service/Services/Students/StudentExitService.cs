using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
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

public sealed class StudentExitService : IStudentExitService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentExitRecord> _exits;
    private readonly IGenericRepository<TransferCertificate> _certificates;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentExitService> _logger;

    public StudentExitService(IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments, IGenericRepository<StudentExitRecord> exits,
        IGenericRepository<TransferCertificate> certificates, IGenericRepository<StudentInvoice> invoices,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<StudentExitService> logger)
    {
        _students = students; _enrollments = enrollments; _exits = exits;
        _certificates = certificates; _invoices = invoices; _unitOfWork = unitOfWork;
        _currentUser = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<StudentExitResultDto>> ProcessAsync(ProcessStudentExitDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty ||
            !TryVersion(request.StudentRowVersion, out var expectedVersion))
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit request is invalid.");
        if (!Enum.TryParse<StudentExitType>(request.ExitType, true, out var type) ||
            type is StudentExitType.Withdrawn || !Enum.IsDefined(type))
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Exit type must be Transfer, Completed or Dropout.");
        if (type == StudentExitType.Transfer && string.IsNullOrWhiteSpace(request.Reason))
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Transfer reason is required.");
        if (request.Reason?.Length > 1000 || request.ConductRemark?.Length > 500)
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Exit comments are too long.");

        var tenantId = _currentUser.TenantId;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable },
                TransactionScopeAsyncFlowOption.Enabled);
            var replay = await _exits.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                var oldStudent = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                    x.Id == replay.StudentId, cancellationToken);
                if (oldStudent == null || oldStudent.PublicId != request.StudentReference || replay.ExitType != type ||
                    !string.Equals(replay.Reason, Trim(request.Reason), StringComparison.Ordinal) ||
                    !string.Equals(replay.ConductRemark, Trim(request.ConductRemark), StringComparison.Ordinal))
                    return ApiResponse<StudentExitResultDto>.ErrorResponse("Request ID is already used for a different exit.", 409);
                var oldCertificate = await CertificateNoAsync(replay, cancellationToken);
                scope.Complete();
                return ApiResponse<StudentExitResultDto>.SuccessResponse(Map(replay, oldStudent, oldCertificate), "Exit already processed.");
            }
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                x.PublicId == request.StudentReference, cancellationToken);
            if (student == null) return ApiResponse<StudentExitResultDto>.ErrorResponse("Student not found.", 404);
            if (student.StatusCode != "Active")
                return ApiResponse<StudentExitResultDto>.ErrorResponse("Only an active student can exit.", 409);
            if (!VersionsMatch(student.RowVersion, expectedVersion))
                return ApiResponse<StudentExitResultDto>.ErrorResponse("Student changed. Reload and retry.", 409);
            if (await _exits.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId &&
                x.StudentId == student.Id, cancellationToken))
                return ApiResponse<StudentExitResultDto>.ErrorResponse("Student has an existing final exit.", 409);

            var enrollments = await _enrollments.GetQueryable().Where(x => x.TenantId == tenantId &&
                x.StudentId == student.Id && (x.State == EnrollmentState.Active || x.IsCurrent)).OrderByDescending(x => x.EnrollmentDate)
                .ToListAsync(cancellationToken);
            var current = enrollments.FirstOrDefault(x => x.IsCurrent && x.State == EnrollmentState.Active);
            if (current == null)
                return ApiResponse<StudentExitResultDto>.ErrorResponse("An active current enrollment is required.", 409);
            var due = await (from invoice in _invoices.GetQueryable().AsNoTracking()
                join enrollment in _enrollments.GetQueryable().AsNoTracking() on invoice.StudentEnrollmentId equals enrollment.Id
                where invoice.TenantId == tenantId && enrollment.TenantId == tenantId &&
                    enrollment.StudentId == student.Id && invoice.State != InvoiceState.Cancelled &&
                    invoice.State != InvoiceState.Refunded && invoice.DueAmount > 0
                select invoice.DueAmount).SumAsync(cancellationToken);
            var feesCleared = due <= 0;
            if ((type == StudentExitType.Transfer || type == StudentExitType.Completed) && !feesCleared)
                return ApiResponse<StudentExitResultDto>.ErrorResponse("Outstanding fees must be settled.", 409);

            var now = _clock.GetUtcNow().UtcDateTime;
            var endDate = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var record = new StudentExitRecord
            {
                TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                StudentId = student.Id, StudentEnrollmentId = current.Id, ExitType = type,
                DueAtExit = due, FeesCleared = feesCleared,
                ProcessedAt = now, ProcessedByUserId = _currentUser.UserId,
                Reason = Trim(request.Reason), ConductRemark = Trim(request.ConductRemark),
                CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _exits.AddAsync(record);

            string? certificateNo = null;
            if (type == StudentExitType.Transfer)
            {
                certificateNo = "TC-" + now.ToString("yyyy") + "-" + record.PublicId.ToString("N")[..20].ToUpperInvariant();
                var certificate = new TransferCertificate
                {
                    TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    StudentId = student.Id, StudentEnrollmentId = current.Id,
                    CertificateNumber = certificateNo, IssueDate = endDate,
                    Reason = record.Reason, ConductRemark = record.ConductRemark, FeesCleared = feesCleared,
                    IssuedByUserId = _currentUser.UserId, CreatedAt = now, CreatedBy = _currentUser.UserId
                };
                await _certificates.AddAsync(certificate);
            }

            foreach (var enrollment in enrollments)
            {
                enrollment.IsCurrent = false;
                enrollment.State = type switch
                {
                    StudentExitType.Transfer => EnrollmentState.Transferred,
                    StudentExitType.Completed => EnrollmentState.Completed,
                    _ => EnrollmentState.Dropped
                };
                enrollment.EndDate ??= endDate;
                enrollment.UpdatedAt = now;
                enrollment.UpdatedBy = _currentUser.UserId;
            }
            student.StatusCode = type switch
            {
                StudentExitType.Transfer => "TC",
                StudentExitType.Completed => "Passed",
                _ => "Dropout"
            };
            student.UpdatedAt = now;
            student.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
            return new ApiResponse<StudentExitResultDto>
            {
                Success = true, StatusCode = 201, Message = "Student exit processed.",
                Data = Map(record, student, certificateNo)
            };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent exit of student {Reference} in tenant {TenantId}", request.StudentReference, tenantId);
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Student changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Student exit database conflict for tenant {TenantId}", tenantId);
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Exit conflicts with an existing transaction.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Student exit transaction aborted for tenant {TenantId}", tenantId);
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Exit transaction conflicted with another update.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student exit failed for tenant {TenantId}", tenantId);
            return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit could not be processed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<StudentExitResultDto>>> GetHistoryAsync(Guid studentReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<IReadOnlyList<StudentExitResultDto>>.ErrorResponse("Student exit permission required.", 403);
        var tenantId = _currentUser.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
            x.PublicId == studentReference, cancellationToken);
        if (student == null) return ApiResponse<IReadOnlyList<StudentExitResultDto>>.ErrorResponse("Student not found.", 404);
        var records = await _exits.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StudentId == student.Id).OrderByDescending(x => x.ProcessedAt).ToListAsync(cancellationToken);
        var requestIds = records.Select(x => x.ClientRequestId).ToArray();
        var certificates = await _certificates.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StudentId == student.Id && requestIds.Contains(x.ClientRequestId))
            .ToDictionaryAsync(x => x.ClientRequestId, x => x.CertificateNumber, cancellationToken);
        IReadOnlyList<StudentExitResultDto> result = records.Select(x =>
            Map(x, student, certificates.GetValueOrDefault(x.ClientRequestId))).ToList();
        return ApiResponse<IReadOnlyList<StudentExitResultDto>>.SuccessResponse(result);
    }

    private async Task<string?> CertificateNoAsync(StudentExitRecord record, CancellationToken ct) =>
        await _certificates.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId &&
            x.StudentId == record.StudentId && x.ClientRequestId == record.ClientRequestId)
            .Select(x => x.CertificateNumber).FirstOrDefaultAsync(ct);

    private static StudentExitResultDto Map(StudentExitRecord record, Student student, string? certificateNo) => new()
    {
        Reference = record.PublicId, StudentReference = student.PublicId, StudentCode = student.StudentCode,
        StudentName = student.FullName, ExitType = record.ExitType.ToString(),
        FinalStatus = record.ExitType switch
        {
            StudentExitType.Transfer => "TC",
            StudentExitType.Completed => "Passed",
            _ => "Dropout"
        },
        CertificateNo = certificateNo, DueAtExit = record.DueAtExit, FeesCleared = record.FeesCleared,
        ProcessedAtUtc = record.ProcessedAt
    };
    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("Registrar"));
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? value, out byte[] version)
    {
        try { version = Convert.FromBase64String(value ?? string.Empty); return version.Length > 0; }
        catch (FormatException) { version = Array.Empty<byte>(); return false; }
    }
    private static bool VersionsMatch(byte[] a, byte[] b) => a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(a, b);
}
