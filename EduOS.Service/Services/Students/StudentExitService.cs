using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Students;

public sealed class StudentExitService : IStudentExitService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Enrollment> _enrollments;
    private readonly IGenericRepository<StudentExitRecord> _exits;
    private readonly IGenericRepository<TransferCertificate> _certificates;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentExitService> _logger;

    public StudentExitService(IGenericRepository<Student> students, IGenericRepository<Enrollment> enrollments,
        IGenericRepository<StudentExitRecord> exits, IGenericRepository<TransferCertificate> certificates,
        IGenericRepository<StudentInvoice> invoices, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        TimeProvider clock, ILogger<StudentExitService> logger)
    {
        _students = students; _enrollments = enrollments; _exits = exits; _certificates = certificates;
        _invoices = invoices; _unitOfWork = unitOfWork; _currentUser = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<StudentExitResultDto>> ProcessAsync(ProcessStudentExitDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit access is required.", 403);
        if (request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty || !TryVersion(request.StudentRowVersion, out var version)) return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit request is invalid.");
        var exitType = NormalizeExitType(request.ExitType);
        if (exitType == null) return ApiResponse<StudentExitResultDto>.ErrorResponse("Exit type is invalid.");
        if (exitType == "Transfer" && string.IsNullOrWhiteSpace(request.Reason)) return ApiResponse<StudentExitResultDto>.ErrorResponse("Transfer reason is required.");
        var tenantId = _currentUser.TenantId;
        var existing = await _exits.GetQueryable().AsNoTracking().Include(x => x.Student).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (existing != null)
        {
            if (existing.Student?.PublicId != request.StudentReference || !string.Equals(existing.ExitType, exitType, StringComparison.OrdinalIgnoreCase)) return ApiResponse<StudentExitResultDto>.ErrorResponse("Client request reference was already used for a different exit.", 409);
            return ApiResponse<StudentExitResultDto>.SuccessResponse(Map(existing, existing.Student!), "Student exit was already processed.");
        }
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.StudentReference, cancellationToken);
            if (student == null) return await RollbackError("Student not found.", 404);
            if (!student.IsActive || !string.Equals(student.Status, "Active", StringComparison.OrdinalIgnoreCase)) return await RollbackError("Only an active student can be processed.", 409);
            if (!VersionsMatch(student.RowVersion, version)) return await RollbackError("Student changed by another user. Reload and try again.", 409);
            if (await _exits.AnyAsync(x => x.TenantId == tenantId && x.StudentId == student.Id)) return await RollbackError("Student already has a final exit record.", 409);
            var active = await _enrollments.GetQueryable().Where(x => x.TenantId == tenantId && x.StudentId == student.Id && x.IsActive).OrderByDescending(x => x.EnrollmentDate).ToListAsync(cancellationToken);
            var placement = active.FirstOrDefault();
            var due = await _invoices.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.StudentId == student.Id).SumAsync(x => (decimal?)x.DueAmount, cancellationToken) ?? 0m;
            var feesCleared = due <= 0m;
            if (exitType is "Transfer" or "Completed" && !feesCleared) return await RollbackError("Outstanding fees must be cleared before transfer or completion.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var publicId = Guid.NewGuid();
            var certificateNo = exitType == "Dropout" ? null : $"{(exitType == "Transfer" ? "TC" : "COMP")}-{now:yyyy}-{publicId:N}"[..18].ToUpperInvariant();
            var record = new StudentExitRecord
            {
                TenantId = tenantId, PublicId = publicId, ClientRequestId = request.ClientRequestId, StudentId = student.Id, Student = student,
                EnrollmentId = placement?.Id, Enrollment = placement, ExitType = exitType, CertificateNo = certificateNo,
                AcademicYearId = placement?.AcademicYearId ?? student.AcademicYearId, ClassId = placement?.ClassId ?? student.ClassId,
                SectionId = placement?.SectionId ?? student.SectionId, GroupId = placement?.GroupId ?? student.GroupId,
                Roll = placement?.Roll ?? student.Roll, DueAtExit = due, FeesCleared = feesCleared, ProcessedAtUtc = now,
                ProcessedByUserId = _currentUser.UserId, Reason = Trim(request.Reason), ConductRemark = Trim(request.ConductRemark), CreatedAt = now, CreatedBy = _currentUser.UserId
            };
            await _exits.AddAsync(record);
            if (exitType == "Transfer")
            {
                await _certificates.AddAsync(new TransferCertificate
                {
                    TenantId = tenantId, PublicId = publicId, ClientRequestId = request.ClientRequestId, StudentId = student.Id, Student = student,
                    TcNo = certificateNo!, IssueDate = now, Reason = record.Reason, LastAcademicYearId = record.AcademicYearId,
                    LastClassId = record.ClassId, LastSectionId = record.SectionId, LastRoll = record.Roll,
                    ConductRemark = record.ConductRemark, FeesCleared = feesCleared, IssuedBy = _currentUser.UserId, CreatedAt = now, CreatedBy = _currentUser.UserId
                });
            }
            foreach (var enrollment in active) { enrollment.IsActive = false; enrollment.UpdatedAt = now; enrollment.UpdatedBy = _currentUser.UserId; }
            student.IsActive = false; student.Status = exitType switch { "Transfer" => "TC", "Completed" => "Passed", _ => "Dropout" }; student.UpdatedAt = now; student.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken); await _unitOfWork.CommitTransactionAsync();
            return new ApiResponse<StudentExitResultDto> { Success = true, StatusCode = 201, Message = "Student exit processed.", Data = Map(record, student) };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollbackAsync(); _logger.LogWarning(ex, "Concurrent student exit {Reference}", request.StudentReference); return ApiResponse<StudentExitResultDto>.ErrorResponse("Student changed by another user. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            await SafeRollbackAsync(); _logger.LogWarning(ex, "Student exit conflict {Reference}", request.StudentReference); return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit conflicts with an existing record.", 409);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(); _logger.LogError(ex, "Student exit failed {Reference}", request.StudentReference); return ApiResponse<StudentExitResultDto>.ErrorResponse("Student exit could not be processed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<StudentExitResultDto>>> GetHistoryAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<IReadOnlyList<StudentExitResultDto>>.ErrorResponse("Student exit access is required.", 403);
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == studentReference, cancellationToken);
        if (student == null) return ApiResponse<IReadOnlyList<StudentExitResultDto>>.ErrorResponse("Student not found.", 404);
        IReadOnlyList<StudentExitResultDto> rows = await _exits.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id).OrderByDescending(x => x.ProcessedAtUtc).Select(x => new StudentExitResultDto { Reference = x.PublicId, StudentReference = student.PublicId, StudentCode = student.StudentCode, StudentName = student.FullName, ExitType = x.ExitType, FinalStatus = x.ExitType == "Transfer" ? "TC" : x.ExitType == "Completed" ? "Passed" : "Dropout", CertificateNo = x.CertificateNo, DueAtExit = x.DueAtExit, FeesCleared = x.FeesCleared, ProcessedAtUtc = x.ProcessedAtUtc }).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<StudentExitResultDto>>.SuccessResponse(rows);
    }

    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("Registrar"));
    private static string? NormalizeExitType(string? value) => value?.Trim().ToLowerInvariant() switch { "transfer" => "Transfer", "completed" => "Completed", "dropout" => "Dropout", _ => null };
    private static StudentExitResultDto Map(StudentExitRecord x, Student s) => new() { Reference = x.PublicId, StudentReference = s.PublicId, StudentCode = s.StudentCode, StudentName = s.FullName, ExitType = x.ExitType, FinalStatus = s.Status, CertificateNo = x.CertificateNo, DueAtExit = x.DueAtExit, FeesCleared = x.FeesCleared, ProcessedAtUtc = x.ProcessedAtUtc };
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? value, out byte[] version) { try { version = Convert.FromBase64String(value ?? string.Empty); return version.Length > 0; } catch (FormatException) { version = []; return false; } }
    private static bool VersionsMatch(byte[] a, byte[] b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    private async Task<ApiResponse<StudentExitResultDto>> RollbackError(string message, int code) { await SafeRollbackAsync(); return ApiResponse<StudentExitResultDto>.ErrorResponse(message, code); }
    private async Task SafeRollbackAsync() { try { await _unitOfWork.RollbackTransactionAsync(); } catch (Exception ex) { _logger.LogError(ex, "Student exit rollback failed."); } }
}
