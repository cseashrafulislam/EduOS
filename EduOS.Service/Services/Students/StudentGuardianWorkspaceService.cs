using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Transactions;

namespace EduOS.Service.Services.Students;

/// <summary>Tenant administrator-only guardian management. Person creation and primary-link changes are atomic.</summary>
public sealed class StudentGuardianWorkspaceService : IStudentGuardianWorkspaceService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _links;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<AuditLog> _auditLogs;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly ILogger<StudentGuardianWorkspaceService> _logger;

    public StudentGuardianWorkspaceService(IGenericRepository<Student> students,
        IGenericRepository<Guardian> guardians, IGenericRepository<StudentGuardian> links,
        IGenericRepository<Person> persons, IGenericRepository<AuditLog> auditLogs,
        IUnitOfWork uow, ICurrentUserService user,
        ILogger<StudentGuardianWorkspaceService> logger)
    {
        _students = students; _guardians = guardians; _links = links; _persons = persons;
        _auditLogs = auditLogs;
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

    public async Task<ApiResponse<PagedResult<StudentGuardianRelationshipHistoryDto>>> GetHistoryAsync(
        Guid studentReference, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!Allowed)
            return Fail<PagedResult<StudentGuardianRelationshipHistoryDto>>("Tenant administrator permission required.", 403);
        if (studentReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            return Fail<PagedResult<StudentGuardianRelationshipHistoryDto>>("Valid student reference and paging required.");
        try
        {
            var tenant = _user.TenantId;
            var studentId = await _students.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted)
                .Select(x => x.Id).SingleOrDefaultAsync(ct);
            if (studentId <= 0)
                return Fail<PagedResult<StudentGuardianRelationshipHistoryDto>>("Student not found.", 404);

            // Include revoked links without exposing another tenant's soft-deleted records.
            var linkIds = _links.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.StudentId == studentId)
                .Select(x => x.Id);
            var query = _auditLogs.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.EntityName == "StudentGuardian" &&
                x.EntityId.HasValue && linkIds.Contains(x.EntityId.Value));
            var total = await query.CountAsync(ct);
            var offset = ((long)page - 1) * pageSize;
            var entries = offset > int.MaxValue ? new List<GuardianAuditRow>() : await query
                .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
                .Skip((int)offset).Take(pageSize)
                .Select(x => new GuardianAuditRow
                {
                    Id = x.Id, LinkId = x.EntityId!.Value, ActorUserId = x.UserId,
                    Action = x.Action, NewValue = x.NewValue, IsSuccess = x.IsSuccess,
                    OccurredAt = x.OccurredAt
                }).ToListAsync(ct);
            return ApiResponse<PagedResult<StudentGuardianRelationshipHistoryDto>>.SuccessResponse(
                new PagedResult<StudentGuardianRelationshipHistoryDto>
                {
                    Items = entries.Select(x => new StudentGuardianRelationshipHistoryDto
                    {
                        Id = x.Id, LinkId = x.LinkId, ActorUserId = x.ActorUserId,
                        Action = x.Action, Reason = ReadAuditReason(x.NewValue),
                        IsSuccess = x.IsSuccess, OccurredAt = x.OccurredAt
                    }).ToList(),
                    TotalCount = total, Page = page, PageSize = pageSize
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Guardian relationship audit read failed for tenant {TenantId}", _user.TenantId);
            return Fail<PagedResult<StudentGuardianRelationshipHistoryDto>>("Guardian history could not be loaded.", 500);
        }
    }

    private sealed class GuardianAuditRow
    {
        public long Id { get; set; }
        public long LinkId { get; set; }
        public long? ActorUserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? NewValue { get; set; }
        public bool IsSuccess { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    private static string? ReadAuditReason(string? newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue)) return null;
        try
        {
            using var json = JsonDocument.Parse(newValue);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("Reason", out var reason) ||
                reason.ValueKind != JsonValueKind.String)
                return null;
            var value = reason.GetString();
            return value?.Length > 500 ? value[..500] : value;
        }
        catch (JsonException) { return null; }
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
        // Only tenant administrators can access this workspace. Include a bounded set of
        // revoked relationships for explicit reactivation; portal reads still exclude them.
        var revokedLinks = await _links.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.StudentId == student.Id && x.IsDeleted)
            .OrderByDescending(x => x.DeletedAt).ThenByDescending(x => x.Id).Take(50).ToListAsync(ct);
        var ids = links.Concat(revokedLinks).Select(x => x.GuardianId).Distinct().ToArray();
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
            }).ToArray(),
            RevokedGuardians = revokedLinks.Where(x => guardians.ContainsKey(x.GuardianId)).Select(x =>
            {
                var g = guardians[x.GuardianId];
                return new StudentGuardianWorkspaceLinkDto
                {
                    LinkId = x.Id, GuardianReference = g.PublicId, FullName = g.FullName,
                    RelationCode = x.RelationCode, IsPrimary = x.IsPrimary,
                    CanReceiveSms = x.CanReceiveSms, CanReceiveEmail = x.CanReceiveEmail,
                    IsAuthorizedPickup = x.IsAuthorizedPickup,
                    IsFinancialContact = x.IsFinancialContact, IsEmergencyContact = x.IsEmergencyContact,
                    RowVersion = Convert.ToBase64String(x.RowVersion), RevokedAt = x.DeletedAt
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


    /// <summary>Link an already registered tenant guardian to another student (e.g. a sibling).
    /// A matching existing link is a safe retry; conflicting permissions require explicit edit.</summary>
    public Task<ApiResponse<StudentGuardianWorkspaceDto>> LinkExistingAsync(
        LinkExistingStudentGuardianRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Tenant administrator permission required.", 403));
        if (request == null || request.StudentReference == Guid.Empty || request.GuardianReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RelationCode) || request.RelationCode.Trim().Length > 50 ||
            !ValidVersion(request.StudentRowVersion))
            return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Valid student, existing guardian, relation and student version required."));
        return WriteAsync(async token =>
        {
            var tenant = _user.TenantId;
            var student = await _students.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && !x.IsDeleted, token);
            if (student == null) return Fail<StudentGuardianWorkspaceDto>("Student not found.", 404);
            var guardian = await _guardians.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.GuardianReference && x.IsActive && !x.IsDeleted, token);
            if (guardian == null) return Fail<StudentGuardianWorkspaceDto>("Guardian not found in this institution.", 404);
            // The tenant/student/guardian pair has a unique database index. Never create a second Person.
            var existing = await _links.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.GuardianId == guardian.Id, token);
            if (existing != null)
            {
                if (!existing.IsDeleted && existing.RelationCode == request.RelationCode.Trim() &&
                    (existing.IsPrimary == request.IsPrimary ||
                    (existing.IsPrimary && !request.IsPrimary &&
                     !await _links.GetQueryable().AsNoTracking().AnyAsync(x =>
                         x.TenantId == tenant && x.StudentId == student.Id &&
                         x.Id != existing.Id && !x.IsDeleted, token))) &&
                    existing.CanReceiveSms == request.CanReceiveSms &&
                    existing.CanReceiveEmail == request.CanReceiveEmail &&
                    existing.IsAuthorizedPickup == request.IsAuthorizedPickup &&
                    existing.IsFinancialContact == request.IsFinancialContact &&
                    existing.IsEmergencyContact == request.IsEmergencyContact)
                    return await LoadAsync(request.StudentReference, token);
                return Fail<StudentGuardianWorkspaceDto>(
                    "Guardian is already linked or was previously removed. Review the existing relationship.", 409);
            }
            if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
                return Fail<StudentGuardianWorkspaceDto>("Guardian changes require an active or suspended student.", 409);
            if (!VersionMatches(student.RowVersion, request.StudentRowVersion))
                return Fail<StudentGuardianWorkspaceDto>("Student changed. Reload and retry.", 409);
            var links = await _links.GetQueryable().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).ToListAsync(token);
            if (links.Count >= 100) return Fail<StudentGuardianWorkspaceDto>("Guardian link limit reached.", 409);
            var primary = request.IsPrimary || links.Count == 0;
            var now = DateTime.UtcNow;
            if (primary)
                foreach (var link in links.Where(x => x.IsPrimary))
                { link.IsPrimary = false; link.UpdatedAt = now; link.UpdatedBy = _user.UserId; }
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
            _logger.LogInformation("Existing guardian {GuardianId} linked to student {StudentId} in tenant {TenantId}",
                guardian.Id, student.Id, tenant);
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


    /// <summary>Revoke one relationship, preserving its historical row and guardian identity.
    /// Live portal authorization filters soft-deleted links, so access ends at commit.</summary>
    public Task<ApiResponse<StudentGuardianWorkspaceDto>> UnlinkAsync(
        Guid studentReference, long linkId, UnlinkStudentGuardianWorkspaceRequestDto request,
        CancellationToken ct = default)
    {
        if (!Allowed) return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Tenant administrator permission required.", 403));
        if (studentReference == Guid.Empty || linkId <= 0 || request == null ||
            !ValidVersion(request.RowVersion) || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length < 10 || request.Reason.Trim().Length > 500)
            return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Valid relationship version and reason (10-500 characters) required."));
        return WriteAsync(async token =>
        {
            var tenant = _user.TenantId;
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, token);
            if (student == null) return Fail<StudentGuardianWorkspaceDto>("Student not found.", 404);
            // Explicit tenant and student predicates are mandatory when bypassing the soft-delete filter.
            var link = await _links.GetQueryable().IgnoreQueryFilters().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.Id == linkId, token);
            if (link == null) return Fail<StudentGuardianWorkspaceDto>("Guardian relationship not found.", 404);
            var reason = request.Reason.Trim();
            var auditValue = JsonSerializer.Serialize(new { Reason = reason, IsDeleted = true });
            if (link.IsDeleted)
            {
                // Only an identical retry by the original administrator is idempotent.
                var prior = await _auditLogs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.EntityName == "StudentGuardian" &&
                    x.EntityId == link.Id && x.Action == "StudentGuardian.Unlinked" &&
                    x.UserId == _user.UserId && !x.IsDeleted, token);
                return prior?.NewValue == auditValue
                    ? await LoadAsync(studentReference, token)
                    : Fail<StudentGuardianWorkspaceDto>("Relationship was already revoked. Refresh the student.", 409);
            }
            if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
                return Fail<StudentGuardianWorkspaceDto>("Guardian changes require an active or suspended student.", 409);
            if (!VersionMatches(link.RowVersion, request.RowVersion))
                return Fail<StudentGuardianWorkspaceDto>("Guardian relationship changed. Reload and retry.", 409);
            var others = await _links.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id &&
                x.Id != linkId && !x.IsDeleted).ToListAsync(token);
            if (link.IsPrimary && !others.Any(x => x.IsPrimary))
                return Fail<StudentGuardianWorkspaceDto>("Assign another primary guardian before unlinking this one.", 409);
            var now = DateTime.UtcNow;
            link.IsDeleted = true; link.DeletedAt = now; link.DeletedBy = _user.UserId;
            link.UpdatedAt = now; link.UpdatedBy = _user.UserId;
            await _auditLogs.AddAsync(new AuditLog
            {
                TenantId = tenant, UserId = _user.UserId,
                Action = "StudentGuardian.Unlinked", EntityName = "StudentGuardian",
                EntityId = link.Id,
                OldValue = JsonSerializer.Serialize(new
                {
                    StudentReference = student.PublicId, GuardianId = link.GuardianId,
                    link.RelationCode, link.IsPrimary, link.CanReceiveSms,
                    link.CanReceiveEmail, link.IsAuthorizedPickup,
                    link.IsFinancialContact, link.IsEmergencyContact
                }),
                NewValue = auditValue, IsSuccess = true, OccurredAt = now
            });
            await _uow.SaveChangesAsync(token);
            _logger.LogInformation("Guardian relationship {LinkId} revoked for tenant {TenantId} student {StudentId}",
                link.Id, tenant, student.Id);
            return await LoadAsync(studentReference, token);
        }, ct);
    }


    /// <summary>Re-enable an existing soft-deleted relationship. The guardian identity and
    /// relationship ID are preserved; permissions must be explicitly re-authorized.</summary>
    public Task<ApiResponse<StudentGuardianWorkspaceDto>> ReactivateAsync(
        Guid studentReference, long linkId, ReactivateStudentGuardianWorkspaceRequestDto request,
        CancellationToken ct = default)
    {
        if (!Allowed) return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Tenant administrator permission required.", 403));
        var permissions = request?.Relationship;
        if (studentReference == Guid.Empty || linkId <= 0 || permissions == null ||
            !ValidVersion(permissions.RowVersion) || string.IsNullOrWhiteSpace(permissions.RelationCode) ||
            permissions.RelationCode.Trim().Length > 50 || string.IsNullOrWhiteSpace(request!.Reason) ||
            request.Reason.Trim().Length < 10 || request.Reason.Trim().Length > 500)
            return Task.FromResult(Fail<StudentGuardianWorkspaceDto>("Valid relationship, version and reactivation reason (10-500 characters) required."));
        return WriteAsync(async token =>
        {
            var tenant = _user.TenantId;
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, token);
            if (student == null) return Fail<StudentGuardianWorkspaceDto>("Student not found.", 404);
            if (student.StatusCode != "Active" && student.StatusCode != "Suspended")
                return Fail<StudentGuardianWorkspaceDto>("Guardian changes require an active or suspended student.", 409);
            // Bypassing soft-delete filters must never bypass tenant/student ownership.
            var link = await _links.GetQueryable().IgnoreQueryFilters().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id && x.Id == linkId, token);
            if (link == null) return Fail<StudentGuardianWorkspaceDto>("Guardian relationship not found.", 404);
            var guardianExists = await _guardians.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == link.GuardianId && x.IsActive && !x.IsDeleted, token);
            if (!guardianExists) return Fail<StudentGuardianWorkspaceDto>("Guardian is no longer active in this institution.", 409);
            var activeLinks = await _links.GetQueryable().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).ToListAsync(token);
            var primary = permissions.IsPrimary || activeLinks.Count == 0;
            var reason = request!.Reason.Trim();
            var relation = permissions.RelationCode.Trim();
            var auditValue = JsonSerializer.Serialize(new
            {
                Reason = reason, RelationCode = relation, IsPrimary = primary,
                permissions.CanReceiveSms, permissions.CanReceiveEmail, permissions.IsAuthorizedPickup,
                permissions.IsFinancialContact, permissions.IsEmergencyContact, IsDeleted = false
            });
            if (!link.IsDeleted)
            {
                // Retry only if this administrator already restored this exact relationship
                // and the current permissions have not changed since that restoration.
                var prior = await _auditLogs.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.EntityName == "StudentGuardian" &&
                    x.EntityId == link.Id && x.Action == "StudentGuardian.Reactivated" &&
                    x.UserId == _user.UserId && !x.IsDeleted)
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(token);
                if (prior?.NewValue == auditValue && link.RelationCode == relation &&
                    link.IsPrimary == primary && link.CanReceiveSms == permissions.CanReceiveSms &&
                    link.CanReceiveEmail == permissions.CanReceiveEmail &&
                    link.IsAuthorizedPickup == permissions.IsAuthorizedPickup &&
                    link.IsFinancialContact == permissions.IsFinancialContact &&
                    link.IsEmergencyContact == permissions.IsEmergencyContact)
                    return await LoadAsync(studentReference, token);
                return Fail<StudentGuardianWorkspaceDto>("Relationship is already active or has changed. Refresh the student.", 409);
            }
            if (!VersionMatches(link.RowVersion, permissions.RowVersion))
                return Fail<StudentGuardianWorkspaceDto>("Guardian relationship changed. Reload and retry.", 409);
            if (activeLinks.Count >= 100)
                return Fail<StudentGuardianWorkspaceDto>("Guardian link limit reached.", 409);
            var now = DateTime.UtcNow;
            var oldValue = JsonSerializer.Serialize(new
            {
                link.RelationCode, link.IsPrimary, link.CanReceiveSms, link.CanReceiveEmail,
                link.IsAuthorizedPickup, link.IsFinancialContact, link.IsEmergencyContact,
                link.IsDeleted, link.DeletedAt, link.DeletedBy
            });
            if (primary)
                foreach (var other in activeLinks.Where(x => x.IsPrimary))
                { other.IsPrimary = false; other.UpdatedAt = now; other.UpdatedBy = _user.UserId; }
            link.IsDeleted = false; link.DeletedAt = null; link.DeletedBy = null;
            link.RelationCode = relation; link.IsPrimary = primary;
            link.CanReceiveSms = permissions.CanReceiveSms;
            link.CanReceiveEmail = permissions.CanReceiveEmail;
            link.IsAuthorizedPickup = permissions.IsAuthorizedPickup;
            link.IsFinancialContact = permissions.IsFinancialContact;
            link.IsEmergencyContact = permissions.IsEmergencyContact;
            link.UpdatedAt = now; link.UpdatedBy = _user.UserId;
            await _auditLogs.AddAsync(new AuditLog
            {
                TenantId = tenant, UserId = _user.UserId,
                Action = "StudentGuardian.Reactivated", EntityName = "StudentGuardian",
                EntityId = link.Id, OldValue = oldValue, NewValue = auditValue,
                IsSuccess = true, OccurredAt = now
            });
            await _uow.SaveChangesAsync(token);
            _logger.LogInformation("Guardian relationship {LinkId} reactivated in tenant {TenantId} for student {StudentId}",
                link.Id, tenant, student.Id);
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
