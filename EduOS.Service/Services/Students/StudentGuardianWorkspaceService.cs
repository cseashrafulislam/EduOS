using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Students;

/// <summary>Tenant administrator-only guardian management. Person creation and primary-link changes are atomic.</summary>
public sealed class StudentGuardianWorkspaceService : IStudentGuardianWorkspaceService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _links;
    private readonly IGenericRepository<Person> _persons;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<StudentGuardianWorkspaceService> _logger;

    public StudentGuardianWorkspaceService(IGenericRepository<Student> students,
        IGenericRepository<Guardian> guardians, IGenericRepository<StudentGuardian> links,
        IGenericRepository<Person> persons, IUnitOfWork uow, ICurrentUserService user,
        ILogger<StudentGuardianWorkspaceService> logger)
    {
        _students = students; _guardians = guardians; _links = links; _persons = persons;
        _uow = uow; _user = user; _logger = logger;
    }

    private bool Allowed => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static ApiResponse<T> Fail<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool VersionMatches(byte[] current, string? version)
    {
        try
        {
            var expected = Convert.FromBase64String(version ?? "");
            return expected.Length > 0 && current.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }

    public async Task<ApiResponse<StudentGuardianWorkspaceDto>> GetAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentGuardianWorkspaceDto>("Tenant administrator permission required.", 403);
        if (studentReference == Guid.Empty) return Fail<StudentGuardianWorkspaceDto>("Student reference is required.");
        try { return await LoadAsync(studentReference, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Guardian workspace read failed for tenant {TenantId}", _user.TenantId);
            return Fail<StudentGuardianWorkspaceDto>("Guardian workspace could not be loaded.", 500);
        }
    }

    private async Task<ApiResponse<StudentGuardianWorkspaceDto>> LoadAsync(Guid studentReference, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (student == null) return Fail<StudentGuardianWorkspaceDto>("Student not found.", 404);
        var links = await _links.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
        var ids = links.Select(x => x.GuardianId).Distinct().ToArray();
        var guardians = await _guardians.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids.Contains(x.Id) && !x.IsDeleted && x.IsActive)
            .ToDictionaryAsync(x => x.Id, ct);
        return ApiResponse<StudentGuardianWorkspaceDto>.SuccessResponse(new StudentGuardianWorkspaceDto
        {
            StudentReference = student.PublicId, StudentName = student.FullName,
            StudentCode = student.StudentCode, StudentRowVersion = Convert.ToBase64String(student.RowVersion),
            Guardians = links.Where(x => guardians.ContainsKey(x.GuardianId)).Select(x =>
            {
                var g = guardians[x.GuardianId];
                return new StudentGuardianWorkspaceLinkDto
                {
                    LinkId = x.Id, GuardianReference = g.PublicId, FullName = g.FullName,
                    Phone = g.Phone, Email = g.Email, RelationCode = x.RelationCode,
                    IsPrimary = x.IsPrimary, CanReceiveSms = x.CanReceiveSms,
                    CanReceiveEmail = x.CanReceiveEmail, IsAuthorizedPickup = x.IsAuthorizedPickup,
                    IsFinancialContact = x.IsFinancialContact, IsEmergencyContact = x.IsEmergencyContact,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                };
            }).ToArray()
        });
    }

    public Task<ApiResponse<StudentGuardianWorkspaceDto>> CreateAndLinkAsync(
        CreateStudentGuardianWorkspaceRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Tenant administrator permission required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty || request.StudentReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.RelationCode) || request.RelationCode.Trim().Length > 50 ||
            !ValidVersion(request.StudentRowVersion))
            return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Valid student, guardian, relation and student version required."));
        return WriteAsync(async token =>
        {
            var tenant = _user.TenantId;
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && !x.IsDeleted, token);
            if (student == null) return Fail<StudentGuardianWorkspaceDto>("Student not found.", 404);
            // The client request ID doubles as the tenant-scoped public guardian ID for replay detection.
            var replay = await _guardians.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                var existingLink = await _links.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.StudentId == student.Id && x.GuardianId == replay.Id &&
                    !x.IsDeleted && x.RelationCode == request.RelationCode.Trim(), token);
                if (existingLink == null || replay.FullName != request.FullName.Trim() ||
                    replay.Phone != Clean(request.Phone) || replay.Email != Clean(request.Email) ||
                    existingLink.CanReceiveSms != request.CanReceiveSms ||
                    existingLink.CanReceiveEmail != request.CanReceiveEmail ||
                    existingLink.IsAuthorizedPickup != request.IsAuthorizedPickup ||
                    existingLink.IsFinancialContact != request.IsFinancialContact ||
                    existingLink.IsEmergencyContact != request.IsEmergencyContact)
                    return Fail<StudentGuardianWorkspaceDto>("Request ID already belongs to different guardian details.", 409);
                return await LoadAsync(request.StudentReference, token);
            }
            if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
                return Fail<StudentGuardianWorkspaceDto>("Guardian changes require an active or suspended student.", 409);
            if (!VersionMatches(student.RowVersion, request.StudentRowVersion))
                return Fail<StudentGuardianWorkspaceDto>("Student changed. Reload and retry.", 409);
            var now = DateTime.UtcNow;
            var existingLinks = await _links.GetQueryable().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).ToListAsync(token);
            if (existingLinks.Count >= 100) return Fail<StudentGuardianWorkspaceDto>("Guardian link limit reached.", 409);
            var primary = request.IsPrimary || existingLinks.Count == 0;
            if (primary)
                foreach (var link in existingLinks.Where(x => x.IsPrimary))
                { link.IsPrimary = false; link.UpdatedAt = now; link.UpdatedBy = _user.UserId; }
            var person = new Person
            {
                FullName = request.FullName.Trim(), Phone = Clean(request.Phone), Email = Clean(request.Email),
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _persons.AddAsync(person);
            await _uow.SaveChangesAsync(token);
            var guardian = new Guardian
            {
                TenantId = tenant, PublicId = request.ClientRequestId, PersonId = person.Id,
                FullName = request.FullName.Trim(), Phone = Clean(request.Phone), Email = Clean(request.Email),
                Occupation = Clean(request.Occupation), Address = Clean(request.Address),
                IsActive = true, CreatedAt = now, CreatedBy = _user.UserId
            };
            await _guardians.AddAsync(guardian);
            await _uow.SaveChangesAsync(token);
            await _links.AddAsync(new StudentGuardian
            {
                TenantId = tenant, StudentId = student.Id, GuardianId = guardian.Id,
                RelationCode = request.RelationCode.Trim(), IsPrimary = primary,
                CanReceiveSms = request.CanReceiveSms, CanReceiveEmail = request.CanReceiveEmail,
                IsAuthorizedPickup = request.IsAuthorizedPickup,
                IsFinancialContact = request.IsFinancialContact, IsEmergencyContact = request.IsEmergencyContact,
                CreatedAt = now, CreatedBy = _user.UserId
            });
            await _uow.SaveChangesAsync(token);
            return await LoadAsync(request.StudentReference, token);
        }, ct);
    }

    public Task<ApiResponse<StudentGuardianWorkspaceDto>> UpdateLinkAsync(
        Guid studentReference, long linkId, UpdateStudentGuardianWorkspaceLinkDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Tenant administrator permission required.", 403));
        if (studentReference == Guid.Empty || linkId <= 0 || request == null ||
            string.IsNullOrWhiteSpace(request.RelationCode) || request.RelationCode.Trim().Length > 50 ||
            !ValidVersion(request.RowVersion))
            return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Valid guardian link and row version required."));
        return WriteAsync(async token =>
        {
            var tenant = _user.TenantId;
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, token);
            if (student == null) return Fail<StudentGuardianWorkspaceDto>("Student not found.", 404);
            if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
                return Fail<StudentGuardianWorkspaceDto>("Guardian changes require an active or suspended student.", 409);
            var link = await _links.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.Id == linkId && !x.IsDeleted, token);
            if (link == null) return Fail<StudentGuardianWorkspaceDto>("Guardian relationship not found.", 404);
            if (!VersionMatches(link.RowVersion, request.RowVersion))
                return Fail<StudentGuardianWorkspaceDto>("Guardian relationship changed. Reload and retry.", 409);
            var others = await _links.GetQueryable().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.Id != linkId && !x.IsDeleted).ToListAsync(token);
            if (link.IsPrimary && !request.IsPrimary && !others.Any(x => x.IsPrimary))
                return Fail<StudentGuardianWorkspaceDto>("Assign another primary guardian first.", 409);
            var now = DateTime.UtcNow;
            if (request.IsPrimary)
                foreach (var other in others.Where(x => x.IsPrimary))
                { other.IsPrimary = false; other.UpdatedAt = now; other.UpdatedBy = _user.UserId; }
            link.RelationCode = request.RelationCode.Trim(); link.IsPrimary = request.IsPrimary;
            link.CanReceiveSms = request.CanReceiveSms; link.CanReceiveEmail = request.CanReceiveEmail;
            link.IsAuthorizedPickup = request.IsAuthorizedPickup;
            link.IsFinancialContact = request.IsFinancialContact;
            link.IsEmergencyContact = request.IsEmergencyContact;
            link.UpdatedAt = now; link.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(token);
            return await LoadAsync(studentReference, token);
        }, ct);
    }

    private static bool ValidVersion(string? value)
    {
        try { return Convert.FromBase64String(value ?? "").Length > 0; }
        catch (FormatException) { return false; }
    }

    private async Task<ApiResponse<T>> WriteAsync<T>(Func<CancellationToken, Task<ApiResponse<T>>> action, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(action, System.Data.IsolationLevel.Serializable, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Guardian concurrency conflict in tenant {TenantId}", _user.TenantId);
            return Fail<T>("Guardian data changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Guardian database conflict in tenant {TenantId}", _user.TenantId);
            return Fail<T>("Guardian data conflicts with an existing record.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Guardian transaction aborted in tenant {TenantId}", _user.TenantId);
            return Fail<T>("Guardian update conflicted with another request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Guardian update failed in tenant {TenantId}", _user.TenantId);
            return Fail<T>("Guardian update could not be completed.", 500);
        }
    }
}
