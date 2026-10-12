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

/// <summary>Source-institution transfer request and approval only. Does not move learners across tenants.</summary>
public sealed partial class StudentTransferRequestService : IStudentTransferRequestService
{
    private readonly IGenericRepository<TransferRequest> _requests;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<StudentExitRecord> _exits;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentTransferRequestService> _logger;

    public StudentTransferRequestService(IGenericRepository<TransferRequest> requests,
        IGenericRepository<Student> students, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<StudentExitRecord> exits, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, TimeProvider clock, ILogger<StudentTransferRequestService> logger)
    {
        _requests = requests; _students = students; _enrollments = enrollments; _exits = exits;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    private bool CanManage() => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static bool TryVersion(string? text, out byte[] bytes)
    {
        try { bytes = Convert.FromBase64String(text ?? ""); return bytes.Length > 0; }
        catch (FormatException) { bytes = Array.Empty<byte>(); return false; }
    }
    private static bool Matches(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    private static TransferRequestDto Map(TransferRequest request, Student student, StudentEnrollment enrollment) => new()
    {
        Id = request.Id, Reference = request.PublicId, SourceTenantId = request.SourceTenantId,
        SourceStudentReference = student.PublicId, SourceEnrollmentReference = enrollment.PublicId,
        DestinationTenantId = request.DestinationTenantId, State = request.State,
        Reason = request.Reason, RequestedAt = request.RequestedAt,
        RequestedByUserId = request.RequestedByUserId, CompletedAt = request.CompletedAt,
        RowVersion = Convert.ToBase64String(request.RowVersion)
    };

    private async Task<ApiResponse<T>> WriteAsync<T>(Func<CancellationToken, Task<ApiResponse<T>>> action, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(action, System.Data.IsolationLevel.Serializable, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Transfer request concurrency conflict for tenant {TenantId}", _user.TenantId);
            return Error<T>("Transfer request changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transfer request database conflict for tenant {TenantId}", _user.TenantId);
            return Error<T>("Transfer request conflicts with existing data.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Transfer request transaction aborted for tenant {TenantId}", _user.TenantId);
            return Error<T>("Transfer request conflicted with another update.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer request operation failed for tenant {TenantId}", _user.TenantId);
            return Error<T>("Transfer request could not be processed.", 500);
        }
    }
}
