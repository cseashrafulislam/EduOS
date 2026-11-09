using EduOS.AI;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Library;

/// <summary>One bounded, model-free capability over the reviewed digital-library publication.
/// Every call reconstructs rights and campus membership from the database; no prompt can
/// select another tenant, chapter, campus, provider or SQL operation.</summary>
public sealed class GroundedStudyAssistantService(
    EduOSDbContext db, ICurrentUserService user, ReviewedCapabilityRegistry capabilities,
    TimeProvider clock)
{
    private long Tenant => user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Active => user.IsAuthenticated && user.UserId > 0 && Tenant > 0 && db.CurrentTenantId == Tenant &&
        (user.IsTenantAdmin || user.IsInRole("Student") || user.IsInRole("Teacher") || user.IsInRole("Principal"));
    private bool CanReview => Active && (user.IsTenantAdmin || user.IsInRole("Teacher"));
    private bool CapabilityOn => capabilities.TryGet("education.study.chapter", out var c) &&
        c!.Version == 1 && c.RequiredPermission == "Study.Read";
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    private IQueryable<long> ActiveCampuses() => db.Campuses.AsNoTracking()
        .Where(c => c.TenantId == Tenant && !c.IsDeleted && c.IsActive).Select(c => c.Id);

    private IQueryable<long>? MemberCampuses()
    {
        var campuses = ActiveCampuses();
        if (user.IsTenantAdmin) return campuses;
        if (user.IsInRole("Principal"))
            return db.UserCampusAccesses.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId && x.IsActive && !x.IsDeleted &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        if (user.IsInRole("Student"))
        {
            var students = db.Students.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id);
            return db.StudentEnrollments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                x.State == EnrollmentState.Active && students.Contains(x.StudentId) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        if (user.IsInRole("Teacher"))
        {
            var today = DateOnly.FromDateTime(Now);
            var employees = db.Employees.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id);
            return db.EmployeeCampusAssignments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                employees.Contains(x.EmployeeId) && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        return null;
    }

    private sealed class Source
    {
        public EduOS.Core.Entities.Files.Document Document { get; set; } = null!;
        public Book Book { get; set; } = null!;
    }

    private IQueryable<Source> Sources(bool review)
    {
        var members = MemberCampuses();
        var activeCampuses = ActiveCampuses();
        var query = from d in db.Documents.AsNoTracking()
                    join b in db.Books.AsNoTracking() on
                        new { d.TenantId, Id = d.EntityId } equals
                        new { b.TenantId, Id = (long?)b.Id }
                    join file in db.FileAssets.AsNoTracking() on
                        new { d.TenantId, Id = d.FileAssetId } equals
                        new { file.TenantId, file.Id }
                    join definition in db.DocumentTypeDefinitions.AsNoTracking() on
                        new { d.TenantId, Code = d.DocumentTypeCode } equals
                        new { definition.TenantId, definition.Code }
                    where d.TenantId == Tenant && !d.IsDeleted && d.IsActive &&
                        d.DocumentTypeCode == "LIBRARY_DIGITAL" && d.EntityType == "LibraryBook" &&
                        (d.VerificationStatusCode == "Approved" || d.VerificationStatusCode == "NotRequired") &&
                        (!d.ExpiresAt.HasValue || d.ExpiresAt > Now) &&
                        d.ContentChapter != null && d.ContentChapter != "" &&
                        (d.ContentLanguageCode == "bn-BD" || d.ContentLanguageCode == "en-BD") &&
                        (d.ContentLicenceCode == "InstitutionOwned" ||
                         d.ContentLicenceCode == "PublicDomain" ||
                         d.ContentLicenceCode == "PublisherLicensed" ||
                         d.ContentLicenceCode == "OpenLicence") &&
                        d.ContentLicenceEvidence != null && d.ContentLicenceEvidence != "" &&
                        d.ContentRightsHolder != null && d.ContentRightsHolder != "" &&
                        !b.IsDeleted && b.IsActive && !file.IsDeleted && file.IsVerifiedSafe &&
                        file.MalwareScanStateCode == "Clean" &&
                        file.Visibility == FileVisibility.Private && file.StorageProvider == "LocalPrivate" &&
                        definition.IsActive && !definition.IsDeleted &&
                        !definition.RequiresVerification && !definition.IsSensitive &&
                        !db.Documents.AsNoTracking().Any(other =>
                            other.TenantId == Tenant && !other.IsDeleted &&
                            other.FileAssetId == file.Id && other.Id != d.Id) &&
                        !db.Documents.AsNoTracking().Any(newer =>
                            newer.TenantId == Tenant && !newer.IsDeleted && newer.IsActive &&
                            newer.SupersedesDocumentId == d.Id) &&
                        (!d.LibraryCampusId.HasValue || activeCampuses.Contains(d.LibraryCampusId.Value))
                    select new Source { Document = d, Book = b };
        if (members == null) return query.Where(_ => false);
        // Institution-wide resources are readable by active members, but only the
        // tenant administrator may author/review institution-wide passages.
        if (review && !user.IsTenantAdmin)
            return query.Where(x => members.Any() && x.Document.LibraryCampusId.HasValue &&
                members.Contains(x.Document.LibraryCampusId.Value));
        if (!user.IsTenantAdmin)
            return query.Where(x => members.Any() && (!x.Document.LibraryCampusId.HasValue ||
                members.Contains(x.Document.LibraryCampusId.Value)));
        return query;
    }

    private static bool ValidPage(int page, int size) => page >= 1 && size is >= 1 and <= 40 &&
        ((long)page - 1) * size <= int.MaxValue;
    private static bool SafeText(string? value, int min, int max, bool forbidMarkup = true) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= min && value.Length <= max &&
        !value.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
            char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ||
            (forbidMarkup && c is '<' or '>' or '&'));

    private static string CitationPart(string? value, int max) => new string((value ?? "")
        .Where(c => !char.IsControl(c) && !char.IsSurrogate(c) &&
            char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format &&
            c is not ('<' or '>' or '&')).Take(max).ToArray()).Trim();

    private AuditLog Audit(string action, StudyEvidenceExcerpt row) => new()
    {
        TenantId = Tenant, UserId = user.UserId, EntityName = "StudyEvidenceExcerpt",
        EntityId = row.Id > 0 ? row.Id : null, Action = action, CorrelationId = row.PublicId.ToString("N"),
        NewValue = row.StateCode, IsSuccess = true, OccurredAt = Now
    };

    public async Task<ApiResponse<PagedResult<GroundedStudySourceDto>>> ListSourcesAsync(
        string? search, int page, int pageSize, bool forReview, CancellationToken ct)
    {
        if (!Active || !CapabilityOn || (forReview && !CanReview))
            return Fail<PagedResult<GroundedStudySourceDto>>("Study access denied.", 403);
        if (!ValidPage(page, pageSize) || search?.Length > 100 ||
            (search != null && !SafeText(search, 1, 100, false)))
            return Fail<PagedResult<GroundedStudySourceDto>>("Invalid search or pagination.");
        var query = Sources(forReview);
        if (!forReview)
            query = query.Where(x => db.StudyEvidenceExcerpts.Any(e =>
                e.TenantId == Tenant && e.DocumentId == x.Document.Id &&
                e.StateCode == "Approved" && !e.IsDeleted));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Book.Title.Contains(term) ||
                (x.Document.ContentChapter != null && x.Document.ContentChapter.Contains(term)) ||
                (x.Document.Title != null && x.Document.Title.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Book.Title).ThenBy(x => x.Document.ContentChapter)
            .ThenBy(x => x.Document.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GroundedStudySourceDto
            {
                Reference = x.Document.PublicId, Title = x.Document.Title ?? x.Book.Title,
                BookTitle = x.Book.Title, Edition = x.Book.Edition,
                Chapter = x.Document.ContentChapter!, Language = x.Document.ContentLanguageCode!,
                ApprovedPassages = db.StudyEvidenceExcerpts.Count(e => e.TenantId == Tenant &&
                    e.DocumentId == x.Document.Id && !e.IsDeleted && e.StateCode == "Approved")
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<GroundedStudySourceDto>>.SuccessResponse(new()
        { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<GroundedStudyAnswerDto>> AskAsync(GroundedStudyQuestionDto? request,
        CancellationToken ct)
    {
        if (!Active || !CapabilityOn || MemberCampuses() == null)
            return Fail<GroundedStudyAnswerDto>("Study access denied.", 403);
        if (request == null || request.SourceReference == Guid.Empty ||
            !SafeText(request.Question, 2, GroundedChapterStudy.MaxQuestionLength, false))
            return Fail<GroundedStudyAnswerDto>("Invalid question.");
        var source = await Sources(false).Where(x => x.Document.PublicId == request.SourceReference)
            .Select(x => new { x.Document.Id, x.Document.LibraryCampusId, x.Document.PublicId,
                x.Document.ContentChapter, x.Document.ContentLanguageCode, x.Document.VersionNo,
                BookId = x.Book.Id, x.Book.Title, x.Book.Edition }).SingleOrDefaultAsync(ct);
        if (source == null) return Fail<GroundedStudyAnswerDto>("Study source unavailable.", 404);
        var excerpts = await db.StudyEvidenceExcerpts.AsNoTracking()
            .Where(e => e.TenantId == Tenant && e.DocumentId == source.Id &&
                !e.IsDeleted && e.StateCode == "Approved" &&
                e.LanguageCode == source.ContentLanguageCode)
            .OrderBy(e => e.Id).Take(GroundedChapterStudy.MaxEvidenceRows)
            .Select(e => new { e.SourceLocator, e.ExcerptText }).ToListAsync(ct);
        var scope = source.LibraryCampusId?.ToString() ?? "institution";
        var authorized = excerpts.Select(e => new GroundedChapterStudy.AuthorizedChapterExcerpt(
            Tenant.ToString(), scope, source.BookId.ToString(), source.Id.ToString(),
            source.Id.ToString(),
            $"{CitationPart(source.Title, 48)} · {CitationPart(source.Edition, 18)} v{source.VersionNo} · {CitationPart(source.ContentChapter, 28)} · {CitationPart(e.SourceLocator, 40)}",
            e.ExcerptText, true, true)).ToArray();
        var answer = GroundedChapterStudy.Answer(new GroundedChapterStudy.StudyRequest(
            request.Question, Tenant.ToString(), scope, source.BookId.ToString(),
            source.Id.ToString(), source.Id.ToString(), user.UserId.ToString()), authorized);
        // No learner name, prompt or source passage is persisted or sent externally.
        return ApiResponse<GroundedStudyAnswerDto>.SuccessResponse(new()
        {
            Outcome = answer.Status.ToString(), Message = answer.Message, Citation = answer.Citation,
            StudySuggestion = answer.Status == GroundedChapterStudy.Outcome.NoEvidence
                ? "Review the selected chapter and ask a more specific question supported by its text." : null
        });
    }

    public async Task<ApiResponse<PagedResult<GroundedStudyExcerptDto>>> ReviewQueueAsync(
        Guid sourceReference, int page, int pageSize, CancellationToken ct)
    {
        if (!CanReview || !CapabilityOn) return Fail<PagedResult<GroundedStudyExcerptDto>>("Review access denied.", 403);
        if (sourceReference == Guid.Empty || !ValidPage(page, pageSize))
            return Fail<PagedResult<GroundedStudyExcerptDto>>("Invalid source or pagination.");
        var sourceId = await Sources(true).Where(x => x.Document.PublicId == sourceReference)
            .Select(x => x.Document.Id).SingleOrDefaultAsync(ct);
        if (sourceId == 0) return Fail<PagedResult<GroundedStudyExcerptDto>>("Source unavailable.", 404);
        var query = db.StudyEvidenceExcerpts.AsNoTracking().Where(e => e.TenantId == Tenant &&
            e.DocumentId == sourceId && !e.IsDeleted);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new GroundedStudyExcerptDto
            {
                Reference = e.PublicId, SourceReference = sourceReference,
                SourceLocator = e.SourceLocator, ExcerptText = e.ExcerptText,
                LanguageCode = e.LanguageCode, State = e.StateCode,
                RowVersion = Convert.ToBase64String(e.RowVersion), CreatedAt = e.CreatedAt,
                ReviewReason = e.ReviewReason
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<GroundedStudyExcerptDto>>.SuccessResponse(new()
        { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<GroundedStudyExcerptDto>> SubmitAsync(
        CreateGroundedStudyExcerptDto? request, CancellationToken ct)
    {
        if (!CanReview || !CapabilityOn) return Fail<GroundedStudyExcerptDto>("Authoring access denied.", 403);
        if (request == null || request.SourceReference == Guid.Empty ||
            !SafeText(request.SourceLocator, 2, 160) ||
            !SafeText(request.ExcerptText, 10, GroundedChapterStudy.MaxExcerptLength) ||
            request.LanguageCode is not ("bn-BD" or "en-BD"))
            return Fail<GroundedStudyExcerptDto>("Invalid passage, language or source locator.");
        var source = await Sources(true).Where(x => x.Document.PublicId == request.SourceReference)
            .Select(x => new { x.Document.Id, x.Document.ContentLanguageCode }).SingleOrDefaultAsync(ct);
        if (source == null) return Fail<GroundedStudyExcerptDto>("Source unavailable.", 404);
        if (source.ContentLanguageCode != request.LanguageCode)
            return Fail<GroundedStudyExcerptDto>("Passage language must match the reviewed publication.");
        if (await db.StudyEvidenceExcerpts.AsNoTracking().AnyAsync(e =>
            e.TenantId == Tenant && e.DocumentId == source.Id && !e.IsDeleted &&
            e.SourceLocator == request.SourceLocator.Trim() &&
            (e.StateCode == "Pending" || e.StateCode == "Approved"), ct))
            return Fail<GroundedStudyExcerptDto>("Another pending or approved passage already uses that locator.", 409);
        var row = new StudyEvidenceExcerpt
        {
            TenantId = Tenant, DocumentId = source.Id,
            SourceLocator = request.SourceLocator.Trim(), ExcerptText = request.ExcerptText.Trim(),
            LanguageCode = request.LanguageCode, SubmittedByUserId = user.UserId,
            CreatedAt = Now, CreatedBy = user.UserId
        };
        db.StudyEvidenceExcerpts.Add(row);
        db.AuditLogs.Add(Audit("GroundedStudy.Submit", row));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsConflict(ex))
        {
            db.ChangeTracker.Clear();
            return Fail<GroundedStudyExcerptDto>("Another pending or approved passage already uses that locator.", 409);
        }
        return ApiResponse<GroundedStudyExcerptDto>.SuccessResponse(new()
        {
            Reference = row.PublicId, SourceReference = request.SourceReference,
            SourceLocator = row.SourceLocator, ExcerptText = row.ExcerptText,
            LanguageCode = row.LanguageCode, State = row.StateCode,
            RowVersion = Convert.ToBase64String(row.RowVersion), CreatedAt = row.CreatedAt
        });
    }

    public async Task<ApiResponse<GroundedStudyExcerptDto>> ReviewAsync(
        Guid reference, ReviewGroundedStudyExcerptDto? request, CancellationToken ct)
    {
        if (!CanReview || !CapabilityOn) return Fail<GroundedStudyExcerptDto>("Review access denied.", 403);
        if (reference == Guid.Empty || request == null ||
            request.Decision is not ("Approve" or "Reject" or "Revoke") ||
            request.Reason?.Length > 300 ||
            (request.Reason != null && !SafeText(request.Reason, 1, 300)))
            return Fail<GroundedStudyExcerptDto>("Invalid review decision.");
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return Fail<GroundedStudyExcerptDto>("Invalid concurrency token."); }
        if (expected.Length != 8) return Fail<GroundedStudyExcerptDto>("Invalid concurrency token.");
        var row = await db.StudyEvidenceExcerpts.SingleOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted, ct);
        if (row == null) return Fail<GroundedStudyExcerptDto>("Passage unavailable.", 404);
        var source = await Sources(true).Where(x => x.Document.Id == row.DocumentId)
            .Select(x => x.Document.PublicId).SingleOrDefaultAsync(ct);
        if (source == Guid.Empty) return Fail<GroundedStudyExcerptDto>("Source unavailable.", 404);
        if (row.SubmittedByUserId == user.UserId && (request.Decision is "Approve" or "Reject"))
            return Fail<GroundedStudyExcerptDto>("A different reviewer must decide this passage.", 403);
        if (request.Decision == "Revoke" && !user.IsTenantAdmin)
            return Fail<GroundedStudyExcerptDto>("Only an administrator can revoke an approved passage.", 403);
        if (request.Decision == "Revoke" ? row.StateCode != "Approved" : row.StateCode != "Pending")
            return Fail<GroundedStudyExcerptDto>("This passage is no longer in the required state.", 409);
        if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
            return Fail<GroundedStudyExcerptDto>("Passage changed; reload before reviewing.", 409);
        db.Entry(row).Property(x => x.RowVersion).OriginalValue = expected;
        row.StateCode = request.Decision switch { "Approve" => "Approved", "Reject" => "Rejected", _ => "Revoked" };
        row.ReviewedByUserId = user.UserId;
        row.ReviewedAtUtc = Now;
        row.ReviewReason = request.Reason?.Trim();
        row.UpdatedBy = user.UserId;
        row.UpdatedAt = Now;
        db.AuditLogs.Add(Audit("GroundedStudy." + request.Decision, row));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Fail<GroundedStudyExcerptDto>("Passage changed; reload before reviewing.", 409);
        }
        return ApiResponse<GroundedStudyExcerptDto>.SuccessResponse(new()
        {
            Reference = row.PublicId, SourceReference = source,
            SourceLocator = row.SourceLocator, ExcerptText = row.ExcerptText,
            LanguageCode = row.LanguageCode, State = row.StateCode,
            RowVersion = Convert.ToBase64String(row.RowVersion), CreatedAt = row.CreatedAt,
            ReviewReason = row.ReviewReason
        });
    }

    private static bool IsConflict(Exception error)
    {
        for (Exception? e = error; e != null; e = e.InnerException)
            if (e is SqlException sql && sql.Number is 2601 or 2627 or 1205 or 3960) return true;
        return false;
    }
}
