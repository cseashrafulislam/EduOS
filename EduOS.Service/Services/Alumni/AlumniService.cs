using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Alumni;

/// <summary>
/// Tenant-owned alumni opt-in; completion is verified against the immutable V1 exit/enrollment
/// record. No global Person data, grades, guardians or contact fields are imported.
/// The alumni tables are managed by an explicit SQL migration and queried via the canonical
/// EduOSDbContext; they are intentionally not duplicate EF entities or a second database.
/// </summary>
public sealed partial class AlumniService : IAlumniService
{
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ILogger<AlumniService> _logger;
    private readonly IAlumniInvitationTransport _invitationTransport;

    public AlumniService(EduOSDbContext db, ICurrentUserService user, ILogger<AlumniService> logger,
        IAlumniInvitationTransport invitationTransport)
    {
        _db = db;
        _user = user;
        _logger = logger;
        _invitationTransport = invitationTransport;
    }

    public async Task<ApiResponse<IReadOnlyList<AlumniSelfDto>>> MineAsync(CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<IReadOnlyList<AlumniSelfDto>>.ErrorResponse("Institution membership required.", 403);
        try
        {
            var tenantId = _user.TenantId;
            var userId = _user.UserId;
            // Limit distinct verified student identities, not exit-history rows. Otherwise
            // one graduate with many completed exits can hide another graduate of this login.
            var candidates = await (from student in _db.Students.AsNoTracking()
                join exit in _db.StudentExitRecords.AsNoTracking() on student.Id equals exit.StudentId
                join enrollment in _db.StudentEnrollments.AsNoTracking() on exit.StudentEnrollmentId equals enrollment.Id
                where student.TenantId == tenantId && student.UserId == userId && !student.IsDeleted
                    && exit.TenantId == tenantId && !exit.IsDeleted
                    && exit.ExitType == StudentExitType.Completed && exit.FeesCleared
                    && enrollment.TenantId == tenantId && enrollment.StudentId == student.Id
                    && enrollment.State == EnrollmentState.Completed && !enrollment.IsDeleted
                group exit.ProcessedAt by new { student.Id, student.PublicId,
                    student.StudentCode, student.FullName } into completed
                select new { completed.Key.Id, completed.Key.PublicId,
                    completed.Key.StudentCode, completed.Key.FullName,
                    ProcessedAt = completed.Max() })
                .OrderByDescending(x => x.ProcessedAt).ThenBy(x => x.Id)
                .Take(100).ToListAsync(ct);

            // One bounded, tenant-filtered lookup avoids a separate SQL query per graduate.
            var profilesByStudent = new Dictionary<long, AlumniProfileSqlRow>();
            if (candidates.Count > 0)
            {
                var parameters = new object[candidates.Count + 1];
                parameters[0] = tenantId;
                for (var i = 0; i < candidates.Count; i++)
                    parameters[i + 1] = candidates[i].Id;
                var placeholders = string.Join(", ", Enumerable.Range(1, candidates.Count)
                    .Select(i => "{" + i + "}"));
                var profiles = await _db.Database.SqlQueryRaw<AlumniProfileSqlRow>(
                    "SELECT Id, StudentId, DirectoryOptIn, InvitationEmailOptIn, InvitationSmsOptIn, ContactEmail, ContactPhone, RowVersion " +
                    "FROM dbo.AlumniProfiles WHERE TenantId = {0} AND StudentId IN (" + placeholders + ")",
                    parameters).ToListAsync(ct);
                profilesByStudent = profiles.ToDictionary(x => x.StudentId);
            }

            var result = new List<AlumniSelfDto>();
            foreach (var candidate in candidates)
            {
                profilesByStudent.TryGetValue(candidate.Id, out var profile);
                result.Add(new AlumniSelfDto
                {
                    StudentReference = candidate.PublicId,
                    StudentCode = candidate.StudentCode,
                    FullName = candidate.FullName,
                    CompletedAtUtc = candidate.ProcessedAt,
                    Registered = profile != null,
                    DirectoryOptIn = profile?.DirectoryOptIn == true,
                    InvitationEmailOptIn = profile?.InvitationEmailOptIn == true,
                    InvitationSmsOptIn = profile?.InvitationSmsOptIn == true,
                    ContactEmail = profile?.ContactEmail,
                    ContactPhone = profile?.ContactPhone,
                    RowVersion = profile == null ? null : Convert.ToBase64String(profile.RowVersion)
                });
            }
            return ApiResponse<IReadOnlyList<AlumniSelfDto>>.SuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni self-service read failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<IReadOnlyList<AlumniSelfDto>>.ErrorResponse("Alumni records could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AlumniSelfDto>> SaveConsentAsync(AlumniConsentRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return ApiResponse<AlumniSelfDto>.ErrorResponse("Institution membership required.", 403);
        if (request == null || request.StudentReference == Guid.Empty || request.ClientRequestId == Guid.Empty)
            return ApiResponse<AlumniSelfDto>.ErrorResponse("Student and unique request references are required.");

        // Public directory consent and private invitations are separate decisions.
        var email = request.DirectoryOptIn || request.InvitationEmailOptIn
            ? Normalize(request.ContactEmail)?.ToLowerInvariant() : null;
        var phone = request.DirectoryOptIn || request.InvitationSmsOptIn
            ? Normalize(request.ContactPhone) : null;
        if (email?.Length > 200 || phone?.Length > 30)
            return ApiResponse<AlumniSelfDto>.ErrorResponse("Contact information exceeds the permitted length.");
        if (email != null && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return ApiResponse<AlumniSelfDto>.ErrorResponse("A valid email address is required.");
        if ((request.InvitationEmailOptIn && email == null) ||
            (request.InvitationSmsOptIn && (phone == null ||
                !Regex.IsMatch(phone, @"^\+?[0-9]{9,15}$", RegexOptions.CultureInvariant))))
            return ApiResponse<AlumniSelfDto>.ErrorResponse("Invitation consent requires a valid destination for each selected channel.");

        var tenantId = _user.TenantId;
        var userId = _user.UserId;
        // Preserve fingerprints for pre-migration requests with both invitation channels off.
        var requestSignature = $"{request.StudentReference:D}|{request.DirectoryOptIn}|{email}|{phone}";
        if (request.InvitationEmailOptIn || request.InvitationSmsOptIn)
            requestSignature += $"|{request.InvitationEmailOptIn}|{request.InvitationSmsOptIn}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestSignature)));
        try
        {
            return await _db.ExecuteInTransactionAsync(async token =>
            {
                var completion = await (from student in _db.Students.AsNoTracking()
                    join exit in _db.StudentExitRecords.AsNoTracking() on student.Id equals exit.StudentId
                    join enrollment in _db.StudentEnrollments.AsNoTracking() on exit.StudentEnrollmentId equals enrollment.Id
                    where student.TenantId == tenantId && student.UserId == userId
                        && student.PublicId == request.StudentReference && !student.IsDeleted
                        && exit.TenantId == tenantId && exit.ExitType == StudentExitType.Completed
                        && exit.FeesCleared && !exit.IsDeleted
                        && enrollment.TenantId == tenantId && enrollment.StudentId == student.Id
                        && enrollment.State == EnrollmentState.Completed && !enrollment.IsDeleted
                    orderby exit.ProcessedAt descending
                    select new { student.Id, student.StudentCode, student.FullName,
                        ExitId = exit.Id, exit.ProcessedAt, enrollment.CampusId })
                    .FirstOrDefaultAsync(token);
                if (completion == null)
                    return ApiResponse<AlumniSelfDto>.ErrorResponse("Verified completion for this account was not found.", 404);

                var prior = await _db.Database.SqlQueryRaw<AlumniCommandSqlRow>(
                    @"SELECT c.RequestHash, c.ActorUserId, c.AlumniProfileId,
                        CAST(CASE WHEN EXISTS (
                            SELECT 1 FROM dbo.AlumniConsentCommands later
                            WHERE later.TenantId = c.TenantId
                              AND later.AlumniProfileId = c.AlumniProfileId
                              AND later.Id > c.Id
                        ) THEN 0 ELSE 1 END AS bit) AS IsLatest
                      FROM dbo.AlumniConsentCommands c
                      WHERE c.TenantId = {0} AND c.ClientRequestId = {1}",
                    tenantId, request.ClientRequestId).ToListAsync(token);
                if (prior.Count > 0)
                {
                    if (prior[0].ActorUserId != userId ||
                        !string.Equals(prior[0].RequestHash, fingerprint, StringComparison.Ordinal))
                        return ApiResponse<AlumniSelfDto>.ErrorResponse("Request reference belongs to another operation.", 409);
                    var replay = await ReadProfileAsync(tenantId, completion.Id, token);
                    // A retry is safe only while this is the most recent consent command
                    // and its persisted privacy state still matches the original request.
                    // An old revoke must not report success after a later opt-in.
                    if (replay == null || replay.Id != prior[0].AlumniProfileId ||
                        !prior[0].IsLatest || replay.DirectoryOptIn != request.DirectoryOptIn ||
                        replay.InvitationEmailOptIn != request.InvitationEmailOptIn ||
                        replay.InvitationSmsOptIn != request.InvitationSmsOptIn ||
                        !string.Equals(replay.ContactEmail, email, StringComparison.Ordinal) ||
                        !string.Equals(replay.ContactPhone, phone, StringComparison.Ordinal))
                        return ApiResponse<AlumniSelfDto>.ErrorResponse(
                            "Consent changed since this request; submit a new request.", 409);
                    return ApiResponse<AlumniSelfDto>.SuccessResponse(ToSelf(completion.StudentCode,
                        completion.FullName, request.StudentReference, completion.ProcessedAt, replay), "Request already processed.");
                }

                var profile = await ReadProfileAsync(tenantId, completion.Id, token);
                var now = DateTime.UtcNow;
                string action;
                if (profile == null)
                {
                    if (!string.IsNullOrWhiteSpace(request.RowVersion))
                        return ApiResponse<AlumniSelfDto>.ErrorResponse("Profile changed. Reload and retry.", 409);
                    await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO dbo.AlumniProfiles
                        (TenantId, PublicId, StudentId, StudentExitRecordId, CampusId,
                         DirectoryOptIn, InvitationEmailOptIn, InvitationSmsOptIn, ContactEmail, ContactPhone, ConsentRecordedAtUtc,
                         CreatedAtUtc, UpdatedAtUtc, UpdatedByUserId)
                        VALUES ({tenantId}, {Guid.NewGuid()}, {completion.Id}, {completion.ExitId},
                                {completion.CampusId}, {request.DirectoryOptIn},
                                {request.InvitationEmailOptIn}, {request.InvitationSmsOptIn}, {email}, {phone},
                                {now}, {now}, {now}, {userId})", token);
                    action = request.DirectoryOptIn ? "RegisteredOptIn" :
                        request.InvitationEmailOptIn || request.InvitationSmsOptIn ? "RegisteredInvitationsOnly" : "RegisteredPrivate";
                }
                else
                {
                    byte[] supplied;
                    try { supplied = Convert.FromBase64String(request.RowVersion ?? ""); }
                    catch (FormatException) { supplied = Array.Empty<byte>(); }
                    if (supplied.Length != 8 ||
                        !CryptographicOperations.FixedTimeEquals(supplied, profile.RowVersion))
                        return ApiResponse<AlumniSelfDto>.ErrorResponse("Profile changed. Reload and retry.", 409);
                    var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE dbo.AlumniProfiles SET StudentExitRecordId = {completion.ExitId},
                            CampusId = {completion.CampusId}, DirectoryOptIn = {request.DirectoryOptIn},
                            InvitationEmailOptIn = {request.InvitationEmailOptIn},
                            InvitationSmsOptIn = {request.InvitationSmsOptIn},
                            ContactEmail = {email}, ContactPhone = {phone},
                            ConsentRecordedAtUtc = {now}, UpdatedAtUtc = {now},
                            UpdatedByUserId = {userId}
                        WHERE TenantId = {tenantId} AND StudentId = {completion.Id}
                            AND RowVersion = {supplied}", token);
                    if (updated != 1)
                        return ApiResponse<AlumniSelfDto>.ErrorResponse("Profile changed. Reload and retry.", 409);
                    if (profile.DirectoryOptIn && !request.DirectoryOptIn)
                        await _db.Database.ExecuteSqlInterpolatedAsync($@"
                            DELETE FROM dbo.AlumniEventRsvps
                            WHERE TenantId = {tenantId} AND AlumniProfileId = {profile.Id}", token);
                    action = profile.DirectoryOptIn && !request.DirectoryOptIn ? "ConsentRevoked"
                        : !profile.DirectoryOptIn && request.DirectoryOptIn ? "ConsentGranted" : "ConsentUpdated";
                }

                var saved = await ReadProfileAsync(tenantId, completion.Id, token);
                if (saved == null) throw new InvalidOperationException("Alumni profile write did not persist.");
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.AlumniConsentCommands
                        (TenantId, ClientRequestId, AlumniProfileId, RequestHash, CreatedAtUtc, ActorUserId)
                    VALUES ({tenantId}, {request.ClientRequestId}, {saved.Id},
                            {fingerprint}, {now}, {userId})", token);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.AlumniProfileAudits
                        (TenantId, AlumniProfileId, ActionCode, ActorUserId, OccurredAtUtc)
                    VALUES ({tenantId}, {saved.Id}, {action}, {userId}, {now})", token);
                return ApiResponse<AlumniSelfDto>.SuccessResponse(ToSelf(completion.StudentCode,
                    completion.FullName, request.StudentReference, completion.ProcessedAt, saved),
                    "Alumni privacy preferences saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205)
        {
            _logger.LogWarning(ex, "Alumni consent concurrency conflict in tenant {TenantId}", tenantId);
            return ApiResponse<AlumniSelfDto>.ErrorResponse("A concurrent update occurred. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni consent save failed in tenant {TenantId}", tenantId);
            return ApiResponse<AlumniSelfDto>.ErrorResponse("Alumni privacy preferences could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<AlumniDirectoryItemDto>>> DirectoryAsync(
        AlumniDirectoryQueryDto query, CancellationToken ct = default)
    {
        if (!CanRead() || !_user.IsTenantAdmin)
            return ApiResponse<PagedResult<AlumniDirectoryItemDto>>.ErrorResponse("Alumni directory administration required.", 403);
        if (query == null || query.Page < 1 || query.PageSize is < 1 or > 50 ||
            query.Search?.Length > 100)
            return ApiResponse<PagedResult<AlumniDirectoryItemDto>>.ErrorResponse("Directory filter is invalid.");

        try
        {
            var tenantId = _user.TenantId;
            var userId = _user.UserId;
            var search = Normalize(query.Search) ?? "";
            var pattern = "%" + search.Replace("~", "~~").Replace("%", "~%")
                .Replace("_", "~_").Replace("[", "~[") + "%";
            const string from = @"
                FROM dbo.AlumniProfiles p
                INNER JOIN dbo.Students s ON s.Id = p.StudentId AND s.TenantId = p.TenantId
                INNER JOIN dbo.StudentExitRecords e ON e.Id = p.StudentExitRecordId
                    AND e.TenantId = p.TenantId AND e.StudentId = s.Id
                INNER JOIN dbo.StudentEnrollments en ON en.Id = e.StudentEnrollmentId
                    AND en.TenantId = p.TenantId AND en.StudentId = s.Id
                WHERE p.TenantId = {0} AND p.DirectoryOptIn = 1
                    AND p.ConsentRecordedAtUtc IS NOT NULL AND s.IsDeleted = 0
                    AND e.IsDeleted = 0 AND e.ExitType = 2 AND e.FeesCleared = 1
                    AND en.IsDeleted = 0 AND en.State = 2 AND p.CampusId = en.CampusId
                    AND EXISTS (
                        SELECT 1 FROM dbo.UserCampusAccesses ca
                        WHERE ca.TenantId = {0} AND ca.UserId = {1} AND ca.CampusId = p.CampusId
                            AND ca.IsActive = 1 AND ca.IsDeleted = 0)
                    AND ({2} = N'' OR s.FullName LIKE {3} ESCAPE N'~'
                        OR s.StudentCode LIKE {3} ESCAPE N'~')";
            var count = await _db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] " + from, tenantId, userId, search, pattern).ToListAsync(ct);
            var offset = (int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue);
            var items = await _db.Database.SqlQueryRaw<AlumniDirectoryItemDto>(
                @"SELECT p.PublicId AS Reference, s.StudentCode, s.FullName,
                    p.ContactEmail, p.ContactPhone, e.ProcessedAt AS CompletedAtUtc, p.CampusId "
                + from + " ORDER BY p.CreatedAtUtc DESC, p.Id DESC OFFSET {4} ROWS FETCH NEXT {5} ROWS ONLY",
                tenantId, userId, search, pattern, offset, query.PageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<AlumniDirectoryItemDto>>.SuccessResponse(
                new PagedResult<AlumniDirectoryItemDto>
                {
                    Items = items,
                    TotalCount = count.FirstOrDefault(),
                    Page = query.Page,
                    PageSize = query.PageSize
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alumni directory failed in tenant {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<AlumniDirectoryItemDto>>.ErrorResponse("Directory could not be loaded.", 500);
        }
    }

    private async Task<AlumniProfileSqlRow?> ReadProfileAsync(long tenantId, long studentId, CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<AlumniProfileSqlRow>(
            @"SELECT Id, StudentId, DirectoryOptIn, InvitationEmailOptIn, InvitationSmsOptIn, ContactEmail, ContactPhone, RowVersion
              FROM dbo.AlumniProfiles WHERE TenantId = {0} AND StudentId = {1}",
            tenantId, studentId).ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    private static AlumniSelfDto ToSelf(string code, string name, Guid reference, DateTime completed,
        AlumniProfileSqlRow profile) => new()
    {
        StudentReference = reference, StudentCode = code, FullName = name,
        CompletedAtUtc = completed, Registered = true,
        DirectoryOptIn = profile.DirectoryOptIn,
        InvitationEmailOptIn = profile.InvitationEmailOptIn,
        InvitationSmsOptIn = profile.InvitationSmsOptIn,
        ContactEmail = profile.ContactEmail,
        ContactPhone = profile.ContactPhone, RowVersion = Convert.ToBase64String(profile.RowVersion)
    };

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0 && _user.UserId > 0;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AlumniProfileSqlRow
{
    public long Id { get; set; }
    public long StudentId { get; set; }
    public bool DirectoryOptIn { get; set; }
    public bool InvitationEmailOptIn { get; set; }
    public bool InvitationSmsOptIn { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class AlumniCommandSqlRow
{
    public string RequestHash { get; set; } = string.Empty;
    public long ActorUserId { get; set; }
    public long AlumniProfileId { get; set; }
    public bool IsLatest { get; set; }
}
