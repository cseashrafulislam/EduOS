using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduOS.Service.Services.Library;

/// <summary>
/// Tenant-wide licensed library resources. Reuses the existing private, scanned FileAsset
/// and Document source of truth, rather than introducing a second file table or public URL.
/// A resource is visible only to an active member of a campus in this institution.
/// </summary>
public sealed class LibraryDigitalResourceService(
    EduOSDbContext db, ICurrentUserService user, IDocumentFileService documentFiles,
    IFileUploadService storage, TimeProvider clock, ILogger<LibraryDigitalResourceService> logger)
    : ILibraryDigitalResourceService
{
    private const string TypeCode = "LIBRARY_DIGITAL";
    private const string EntityType = "LibraryBook";
    private const long MaxBytes = 5 * 1024 * 1024;
    private long Tenant => user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Authenticated => user.IsAuthenticated && Tenant > 0 && user.UserId > 0;
    // The library-wide licence is controlled by the institution administrator.
    // Campus-scoped staff cannot publish or revoke access for other campuses.
    private bool CanPublish => Authenticated && user.IsTenantAdmin && db.CurrentTenantId == Tenant;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    private IQueryable<long> ActiveCampusIds() => db.Campuses.AsNoTracking()
        .Where(x => x.TenantId == Tenant && x.IsActive && !x.IsDeleted).Select(x => x.Id);

    private IQueryable<long>? MemberCampusIds()
    {
        var campuses = ActiveCampusIds();
        var today = DateOnly.FromDateTime(Now);
        if (user.IsTenantAdmin) return campuses;
        if (user.IsInRole("Principal"))
            return db.UserCampusAccesses.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId && x.IsActive && !x.IsDeleted &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        if (user.IsInRole("Student"))
        {
            var studentIds = db.Students.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id);
            return db.StudentEnrollments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                x.State == EnrollmentState.Active && studentIds.Contains(x.StudentId) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        if (user.IsInRole("Guardian") || user.IsInRole("Parent"))
        {
            var guardianIds = db.Guardians.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.IsActive && !x.IsDeleted).Select(x => x.Id);
            var linkedStudents = db.StudentGuardians.AsNoTracking().Where(x =>
                x.TenantId == Tenant && guardianIds.Contains(x.GuardianId) && !x.IsDeleted)
                .Select(x => x.StudentId);
            var activeStudents = db.Students.AsNoTracking().Where(x =>
                x.TenantId == Tenant && linkedStudents.Contains(x.Id) &&
                x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id);
            return db.StudentEnrollments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                x.State == EnrollmentState.Active && activeStudents.Contains(x.StudentId) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        if (user.IsInRole("Librarian") || user.IsInRole("Teacher") || user.IsInRole("Employee") ||
            user.IsInRole("Staff"))
        {
            var employeeIds = db.Employees.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id);
            return db.EmployeeCampusAssignments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                employeeIds.Contains(x.EmployeeId) && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        return null;
    }

    private async Task<bool> CanReadAsync(CancellationToken ct)
    {
        if (!Authenticated || db.CurrentTenantId != Tenant) return false;
        var campuses = MemberCampusIds();
        return campuses != null && await campuses.AnyAsync(ct);
    }

    private async Task<bool> ValidCampusScopeAsync(long? campusId, CancellationToken ct) =>
        !campusId.HasValue || (campusId.Value > 0 && await db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && x.Id == campusId.Value && x.IsActive && !x.IsDeleted, ct));

    public async Task<ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>> GetCampusOptionsAsync(CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<IReadOnlyList<LibraryCampusOptionDto>>("Tenant administrator required.", 403);
        var campuses = await db.Campuses.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(200)
            .Select(x => new LibraryCampusOptionDto { Id = x.Id, Name = x.Name, Code = x.Code })
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>.SuccessResponse(campuses);
    }

    public async Task<ApiResponse<PagedResult<LibraryDigitalCurriculumOptionDto>>> GetCurriculumOptionsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<PagedResult<LibraryDigitalCurriculumOptionDto>>("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue ||
            (search != null && search.Length > 100))
            return Fail<PagedResult<LibraryDigitalCurriculumOptionDto>>("Invalid curriculum search.");
        var query = from cs in db.CurriculumSubjects.AsNoTracking()
                    join c in db.AcademicCurriculums.AsNoTracking() on
                        new { cs.TenantId, Id = cs.AcademicCurriculumId } equals new { c.TenantId, c.Id }
                    join l in db.AcademicLevels.AsNoTracking() on
                        new { cs.TenantId, Id = cs.AcademicLevelId } equals new { l.TenantId, l.Id }
                    join s in db.Subjects.AsNoTracking() on
                        new { cs.TenantId, Id = cs.SubjectId } equals new { s.TenantId, s.Id }
                    where cs.TenantId == Tenant && !cs.IsDeleted && cs.IsActive &&
                        !c.IsDeleted && c.IsActive && !l.IsDeleted && l.IsActive &&
                        !s.IsDeleted && s.IsActive
                    select new { cs, c, l, s };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.c.Name.Contains(term) || x.c.Code.Contains(term) ||
                x.l.Name.Contains(term) || x.s.Name.Contains(term) || x.s.Code.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.c.Name).ThenBy(x => x.l.LevelNo)
            .ThenBy(x => x.s.Name).ThenBy(x => x.cs.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LibraryDigitalCurriculumOptionDto
            {
                CurriculumId = x.c.Id, LevelId = x.l.Id, SubjectId = x.s.Id,
                CurriculumName = x.c.Name, LevelName = x.l.Name, SubjectName = x.s.Name
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryDigitalCurriculumOptionDto>>.SuccessResponse(
            new PagedResult<LibraryDigitalCurriculumOptionDto>
            { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    private static bool IsSqlRegistrationConflict(Exception error)
    {
        // Only normalize recognized SQL write-contention codes. Unrelated storage,
        // authentication and programming failures must remain visible to diagnostics.
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number is 1205 or 2601 or 2627 or 3960)
                return true;
        }
        return false;
    }

    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (current.Length == 0 || string.IsNullOrWhiteSpace(supplied)) return false;
        try
        {
            var expected = Convert.FromBase64String(supplied);
            return current.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }

    private static DateTime? Expiry(DateOnly? date) =>
        date?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

    private bool ValidExpiry(DateOnly? date) =>
        !date.HasValue || date.Value >= DateOnly.FromDateTime(Now);

    private static LibraryDigitalResourceDto Map(Document document, FileAsset file,
        Guid bookReference, string bookTitle) => new()
    {
        Reference = document.PublicId, BookReference = bookReference, BookTitle = bookTitle,
        CampusId = document.LibraryCampusId,
        Title = document.Title ?? string.Empty, FileName = file.OriginalFileName,
        ContentType = file.ContentType, SizeBytes = file.SizeBytes, IsActive = document.IsActive,
        ExpiresOn = document.ExpiresAt.HasValue ? DateOnly.FromDateTime(document.ExpiresAt.Value) : null,
        VersionNo = document.VersionNo, CreatedAt = document.CreatedAt,
        RowVersion = Convert.ToBase64String(document.RowVersion),
        CurriculumId = document.ContentCurriculumId, LevelId = document.ContentLevelId,
        SubjectId = document.ContentSubjectId, Chapter = document.ContentChapter,
        Topic = document.ContentTopic, LanguageCode = document.ContentLanguageCode,
        SourceUrl = document.ContentSourceUrl, RightsHolder = document.ContentRightsHolder,
        LicenceCode = document.ContentLicenceCode,
        LicenceEvidence = document.ContentLicenceEvidence,
        ExamCode = document.ContentExamCode, BoardCode = document.ContentBoardCode,
        SyllabusYear = document.ContentSyllabusYear,
        SyllabusVersion = document.ContentSyllabusVersion,
        SyllabusSourceUrl = document.ContentSyllabusSourceUrl,
        PreviousPaperYear = document.ContentPreviousPaperYear,
        StudyExplanation = document.ContentStudyExplanation,
        ReviewStatus = document.VerificationStatusCode
    };

    private static void SetRights(Document document, LibraryDigitalRightsRequestDto rights)
    {
        document.ContentCurriculumId = rights.CurriculumId;
        document.ContentLevelId = rights.LevelId;
        document.ContentSubjectId = rights.SubjectId;
        document.ContentChapter = rights.Chapter?.Trim();
        document.ContentTopic = rights.Topic?.Trim();
        document.ContentLanguageCode = rights.LanguageCode.Trim();
        document.ContentSourceUrl = rights.SourceUrl?.Trim();
        document.ContentRightsHolder = rights.RightsHolder.Trim();
        document.ContentLicenceCode = rights.LicenceCode.Trim();
        document.ContentLicenceEvidence = rights.LicenceEvidence.Trim();
        document.ContentExamCode = rights.ExamCode;
        document.ContentBoardCode = rights.BoardCode?.Trim();
        document.ContentSyllabusYear = rights.SyllabusYear;
        document.ContentSyllabusVersion = rights.SyllabusVersion?.Trim();
        document.ContentSyllabusSourceUrl = rights.SyllabusSourceUrl?.Trim();
        document.ContentPreviousPaperYear = rights.PreviousPaperYear;
        document.ContentStudyExplanation = rights.StudyExplanation?.Trim();
    }

    private static bool RightsMatch(Document document, LibraryDigitalRightsRequestDto rights) =>
        document.ContentCurriculumId == rights.CurriculumId &&
        document.ContentLevelId == rights.LevelId && document.ContentSubjectId == rights.SubjectId &&
        document.ContentChapter == rights.Chapter?.Trim() &&
        document.ContentTopic == rights.Topic?.Trim() &&
        document.ContentLanguageCode == rights.LanguageCode.Trim() &&
        document.ContentSourceUrl == rights.SourceUrl?.Trim() &&
        document.ContentRightsHolder == rights.RightsHolder.Trim() &&
        document.ContentLicenceCode == rights.LicenceCode.Trim() &&
        document.ContentLicenceEvidence == rights.LicenceEvidence.Trim() &&
        document.ContentExamCode == rights.ExamCode &&
        document.ContentBoardCode == rights.BoardCode?.Trim() &&
        document.ContentSyllabusYear == rights.SyllabusYear &&
        document.ContentSyllabusVersion == rights.SyllabusVersion?.Trim() &&
        document.ContentSyllabusSourceUrl == rights.SyllabusSourceUrl?.Trim() &&
        document.ContentPreviousPaperYear == rights.PreviousPaperYear &&
        document.ContentStudyExplanation == rights.StudyExplanation?.Trim();

    private static bool ValidRights(LibraryDigitalRightsRequestDto? rights)
    {
        if (rights == null || rights.LanguageCode is not ("bn-BD" or "en-BD") ||
            rights.LicenceCode is not ("InstitutionOwned" or "PublicDomain" or "PublisherLicensed" or "OpenLicence") ||
            string.IsNullOrWhiteSpace(rights.RightsHolder) || rights.RightsHolder.Length > 200 ||
            string.IsNullOrWhiteSpace(rights.LicenceEvidence) || rights.LicenceEvidence.Length > 500 ||
            rights.SourceUrl?.Length > 1000 ||
            rights.Chapter?.Length > 150 || rights.Topic?.Length > 150 ||
            new[] { rights.RightsHolder, rights.LicenceEvidence, rights.Chapter, rights.Topic }
                .Where(x => x != null).Any(x => x!.Any(char.IsControl))) return false;
        if (rights.LicenceCode != "InstitutionOwned" || !string.IsNullOrWhiteSpace(rights.SourceUrl))
        {
            if (!Uri.TryCreate(rights.SourceUrl, UriKind.Absolute, out var source) ||
                source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(source.UserInfo))
                return false;
        }
        if (rights.ExamCode == null)
        {
            if (rights.BoardCode != null || rights.SyllabusYear != null ||
                rights.SyllabusVersion != null || rights.SyllabusSourceUrl != null ||
                rights.PreviousPaperYear != null || rights.StudyExplanation != null) return false;
        }
        else
        {
            if (rights.ExamCode is not ("SSC" or "HSC" or "UniversityAdmission" or "NTRCAStudy") ||
                rights.SyllabusYear is < 2000 or > 2100 ||
                string.IsNullOrWhiteSpace(rights.SyllabusVersion) || rights.SyllabusVersion.Length > 50 ||
                rights.SyllabusVersion.Any(char.IsControl) ||
                rights.BoardCode?.Length > 40 ||
                (rights.BoardCode != null && (string.IsNullOrWhiteSpace(rights.BoardCode) ||
                    rights.BoardCode.Any(c => !char.IsLetterOrDigit(c) && c != '-'))) ||
                rights.PreviousPaperYear is < 1990 or > 2100 ||
                rights.StudyExplanation?.Length > 2000 ||
                (rights.StudyExplanation?.Any(char.IsControl) ?? false) ||
                !Uri.TryCreate(rights.SyllabusSourceUrl, UriKind.Absolute, out var syllabusSource) ||
                syllabusSource.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(syllabusSource.UserInfo)) return false;
        }
        var selected = rights.CurriculumId.HasValue || rights.LevelId.HasValue || rights.SubjectId.HasValue;
        return !selected || (rights.CurriculumId > 0 && rights.LevelId > 0 && rights.SubjectId > 0);
    }

    private async Task<bool> ValidCurriculumAsync(LibraryDigitalRightsRequestDto rights, CancellationToken ct)
    {
        if (!rights.CurriculumId.HasValue) return true;
        return await db.CurriculumSubjects.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && !x.IsDeleted && x.IsActive &&
            x.AcademicCurriculumId == rights.CurriculumId && x.AcademicLevelId == rights.LevelId &&
            x.SubjectId == rights.SubjectId &&
            db.AcademicCurriculums.Any(c => c.TenantId == Tenant && c.Id == x.AcademicCurriculumId &&
                !c.IsDeleted && c.IsActive) &&
            db.AcademicLevels.Any(l => l.TenantId == Tenant && l.Id == x.AcademicLevelId &&
                !l.IsDeleted && l.IsActive) &&
            db.Subjects.Any(s => s.TenantId == Tenant && s.Id == x.SubjectId &&
                !s.IsDeleted && s.IsActive), ct);
    }

    private IQueryable<LibraryDigitalRow> ResourceQuery()
    {
        // The projection is tenant-filtered on all three source tables before paging.
        var query = from document in db.Documents.AsNoTracking()
               join book in db.Books.AsNoTracking()
                   on new { document.TenantId, Id = document.EntityId }
                   equals new { book.TenantId, Id = (long?)book.Id }
               join file in db.FileAssets.AsNoTracking()
                   on new { document.TenantId, Id = document.FileAssetId }
                   equals new { file.TenantId, file.Id }
               join definition in db.DocumentTypeDefinitions.AsNoTracking()
                   on new { document.TenantId, Code = document.DocumentTypeCode }
                   equals new { definition.TenantId, definition.Code }
               where definition.IsActive && !definition.IsDeleted &&
                   !definition.RequiresVerification && !definition.IsSensitive &&
                   document.TenantId == Tenant && !document.IsDeleted &&
                   document.EntityType == EntityType && document.DocumentTypeCode == TypeCode &&
                   !book.IsDeleted && !file.IsDeleted && file.IsVerifiedSafe &&
                   file.MalwareScanStateCode == "Clean" &&
                   file.Visibility == FileVisibility.Private && file.StorageProvider == "LocalPrivate" &&
                   !db.Documents.AsNoTracking().Any(other =>
                       other.TenantId == Tenant && !other.IsDeleted &&
                       other.FileAssetId == file.Id && other.Id != document.Id)
               select new LibraryDigitalRow { Document = document, Book = book, File = file };
        if (CanPublish) return query;
        var campuses = MemberCampusIds();
        // A campus licence is never widened by revoked membership or inactive campus.
        return campuses == null ? query.Where(x => false) : query.Where(x =>
            !x.Document.LibraryCampusId.HasValue ||
            campuses.Contains(x.Document.LibraryCampusId.Value));
    }

    private IQueryable<LibraryDigitalRow> PublishedResources()
    {
        var activeCampuses = ActiveCampusIds();
        return ResourceQuery().Where(x => x.Document.IsActive && x.Book.IsActive &&
            (x.Document.VerificationStatusCode == "Approved" ||
             x.Document.VerificationStatusCode == "NotRequired") &&
            (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > Now) &&
            (!x.Document.LibraryCampusId.HasValue ||
                activeCampuses.Contains(x.Document.LibraryCampusId.Value)));
    }

    private sealed class LibraryDigitalRow
    {
        public Document Document { get; set; } = null!;
        public EduOS.Core.Entities.Library.Book Book { get; set; } = null!;
        public FileAsset File { get; set; } = null!;
    }

    private static bool ValidCatalogueFilter(string? value) =>
        value == null || (value.Length is > 0 and <= 100 &&
            !string.IsNullOrWhiteSpace(value) &&
            !value.Any(c => char.IsControl(c) ||
                char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format));

    public async Task<ApiResponse<PagedResult<LibraryDigitalResourceDto>>> ListAsync(
        Guid? bookReference, string? search, int page, int pageSize, bool includeInactive,
        CancellationToken ct = default, string? category = null, string? author = null,
        string? publisher = null, string? edition = null, long? curriculumId = null,
        long? subjectId = null, string? chapter = null, string? language = null,
        string? examCode = null, string? boardCode = null, int? syllabusYear = null,
        int? previousPaperYear = null, bool studyOnly = false)
    {
        if (!await CanReadAsync(ct)) return Fail<PagedResult<LibraryDigitalResourceDto>>("Library access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue ||
            search?.Length > 100 || bookReference == Guid.Empty ||
            !ValidCatalogueFilter(category) || !ValidCatalogueFilter(author) ||
            !ValidCatalogueFilter(publisher) || !ValidCatalogueFilter(edition) ||
            !ValidCatalogueFilter(chapter) ||
            (language != null && language is not ("bn-BD" or "en-BD")) ||
            (examCode != null && examCode is not ("SSC" or "HSC" or "UniversityAdmission" or "NTRCAStudy")) ||
            !ValidCatalogueFilter(boardCode) || syllabusYear is < 2000 or > 2100 ||
            previousPaperYear is < 1990 or > 2100 ||
            curriculumId is <= 0 || subjectId is <= 0 ||
            (search != null && (string.IsNullOrWhiteSpace(search) ||
                search.Any(c => char.IsControl(c) ||
                    char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))))
            return Fail<PagedResult<LibraryDigitalResourceDto>>("Invalid digital library filters.");
        var query = ResourceQuery();
        if (!CanPublish || !includeInactive)
            query = PublishedResources();
        if (bookReference.HasValue)
            query = query.Where(x => x.Book.PublicId == bookReference.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Book.Title.Contains(term) ||
                (x.Book.Author != null && x.Book.Author.Contains(term)) ||
                (x.Book.Publisher != null && x.Book.Publisher.Contains(term)) ||
                (x.Book.Edition != null && x.Book.Edition.Contains(term)) ||
                (x.Book.ISBN != null && x.Book.ISBN.Contains(term)) ||
                (x.Document.ContentChapter != null && x.Document.ContentChapter.Contains(term)) ||
                (x.Document.ContentTopic != null && x.Document.ContentTopic.Contains(term)) ||
                db.Subjects.AsNoTracking().Any(subject => subject.TenantId == Tenant &&
                    !subject.IsDeleted && subject.Id == x.Document.ContentSubjectId &&
                    subject.Name.Contains(term)) ||
                db.BookCategories.AsNoTracking().Any(category =>
                    category.TenantId == Tenant && !category.IsDeleted && category.IsActive &&
                    category.Id == x.Book.BookCategoryId && category.Name.Contains(term)) ||
                (x.Document.Title != null && x.Document.Title.Contains(term)));
        }
        if (curriculumId.HasValue)
            query = query.Where(x => x.Document.ContentCurriculumId == curriculumId);
        if (subjectId.HasValue)
            query = query.Where(x => x.Document.ContentSubjectId == subjectId);
        if (chapter != null)
        {
            var term = chapter.Trim();
            query = query.Where(x => x.Document.ContentChapter != null &&
                x.Document.ContentChapter.Contains(term));
        }
        if (language != null)
            query = query.Where(x => x.Document.ContentLanguageCode == language);
        if (examCode != null)
            query = query.Where(x => x.Document.ContentExamCode == examCode);
        if (studyOnly)
            query = query.Where(x => x.Document.ContentExamCode != null);
        if (boardCode != null)
            query = query.Where(x => x.Document.ContentBoardCode == boardCode);
        if (syllabusYear.HasValue)
            query = query.Where(x => x.Document.ContentSyllabusYear == syllabusYear);
        if (previousPaperYear.HasValue)
            query = query.Where(x => x.Document.ContentPreviousPaperYear == previousPaperYear);
        // All facets compose on the same authorized query before counting and paging.
        // Category is resolved from the existing tenant-owned BookCategory, not a shadow index.
        if (category != null)
        {
            var term = category.Trim();
            query = query.Where(x => db.BookCategories.AsNoTracking().Any(c =>
                c.TenantId == Tenant && !c.IsDeleted && c.IsActive &&
                c.Id == x.Book.BookCategoryId && c.Name.Contains(term)));
        }
        if (author != null)
        {
            var term = author.Trim();
            query = query.Where(x => x.Book.Author != null && x.Book.Author.Contains(term));
        }
        if (publisher != null)
        {
            var term = publisher.Trim();
            query = query.Where(x => x.Book.Publisher != null && x.Book.Publisher.Contains(term));
        }
        if (edition != null)
        {
            var term = edition.Trim();
            query = query.Where(x => x.Book.Edition != null && x.Book.Edition.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Document.CreatedAt)
            .ThenByDescending(x => x.Document.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                DocumentReference = x.Document.PublicId, x.Document.Title, x.Document.IsActive,
                x.Document.ExpiresAt, x.Document.CreatedAt, x.Document.RowVersion, x.Document.VersionNo,
                x.Document.LibraryCampusId, x.Document.VerificationStatusCode,
                x.Document.ContentCurriculumId, x.Document.ContentLevelId, x.Document.ContentSubjectId,
                x.Document.ContentChapter, x.Document.ContentTopic, x.Document.ContentLanguageCode,
                x.Document.ContentSourceUrl, x.Document.ContentRightsHolder,
                x.Document.ContentLicenceCode, x.Document.ContentLicenceEvidence,
                x.Document.ContentExamCode, x.Document.ContentBoardCode,
                x.Document.ContentSyllabusYear, x.Document.ContentSyllabusVersion,
                x.Document.ContentSyllabusSourceUrl, x.Document.ContentPreviousPaperYear,
                x.Document.ContentStudyExplanation,
                BookReference = x.Book.PublicId,
                BookTitle = x.Book.Title, BookAuthor = x.Book.Author,
                BookPublisher = x.Book.Publisher, BookEdition = x.Book.Edition,
                BookIsbn = x.Book.ISBN,
                BookCategory = db.BookCategories.AsNoTracking()
                    .Where(category => category.TenantId == Tenant && !category.IsDeleted &&
                        category.IsActive && category.Id == x.Book.BookCategoryId)
                    .Select(category => category.Name).FirstOrDefault(),
                x.File.OriginalFileName, x.File.ContentType, x.File.SizeBytes
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryDigitalResourceDto>>.SuccessResponse(
            new PagedResult<LibraryDigitalResourceDto>
            {
                Items = rows.Select(x => new LibraryDigitalResourceDto
                {
                    Reference = x.DocumentReference, BookReference = x.BookReference,
                    CampusId = x.LibraryCampusId,
                    BookTitle = x.BookTitle, BookAuthor = x.BookAuthor,
                    BookPublisher = x.BookPublisher, BookEdition = x.BookEdition,
                    BookIsbn = x.BookIsbn, BookCategory = x.BookCategory,
                    CurriculumId = x.ContentCurriculumId, LevelId = x.ContentLevelId,
                    SubjectId = x.ContentSubjectId, Chapter = x.ContentChapter, Topic = x.ContentTopic,
                    LanguageCode = x.ContentLanguageCode, SourceUrl = x.ContentSourceUrl,
                    RightsHolder = x.ContentRightsHolder, LicenceCode = x.ContentLicenceCode,
                    LicenceEvidence = CanPublish ? x.ContentLicenceEvidence : null,
                    ExamCode = x.ContentExamCode, BoardCode = x.ContentBoardCode,
                    SyllabusYear = x.ContentSyllabusYear, SyllabusVersion = x.ContentSyllabusVersion,
                    SyllabusSourceUrl = x.ContentSyllabusSourceUrl,
                    PreviousPaperYear = x.ContentPreviousPaperYear,
                    StudyExplanation = x.ContentStudyExplanation,
                    ReviewStatus = x.VerificationStatusCode,
                    Title = x.Title ?? string.Empty,
                    FileName = x.OriginalFileName, ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes, IsActive = x.IsActive,
                    ExpiresOn = x.ExpiresAt.HasValue ? DateOnly.FromDateTime(x.ExpiresAt.Value) : null,
                    VersionNo = x.VersionNo, CreatedAt = x.CreatedAt,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<IReadOnlyList<Guid>>> GetStudyBookmarksAsync(
        IReadOnlyList<Guid> references, CancellationToken ct = default)
    {
        if (!await CanReadAsync(ct)) return Fail<IReadOnlyList<Guid>>("Library access denied.", 403);
        if (references == null || references.Count > 50 || references.Any(x => x == Guid.Empty))
            return Fail<IReadOnlyList<Guid>>("At most 50 valid resource references are allowed.");
        if (references.Count == 0)
            return ApiResponse<IReadOnlyList<Guid>>.SuccessResponse(Array.Empty<Guid>());
        var visible = PublishedResources().Where(x =>
            x.Document.ContentExamCode != null && references.Contains(x.Document.PublicId))
            .Select(x => x.Document.Id);
        var saved = await (from bookmark in db.StudyBookmarks.AsNoTracking()
            join document in db.Documents.AsNoTracking()
                on new { bookmark.TenantId, Id = bookmark.DocumentId }
                equals new { document.TenantId, document.Id }
            where bookmark.TenantId == Tenant && bookmark.UserId == user.UserId &&
                !bookmark.IsDeleted && visible.Contains(bookmark.DocumentId)
            select document.PublicId).Distinct().ToListAsync(ct);
        return ApiResponse<IReadOnlyList<Guid>>.SuccessResponse(saved);
    }

    public async Task<ApiResponse<bool>> SetStudyBookmarkAsync(
        Guid reference, bool bookmarked, CancellationToken ct = default)
    {
        if (!await CanReadAsync(ct)) return Fail<bool>("Library access denied.", 403);
        if (reference == Guid.Empty) return Fail<bool>("A resource reference is required.");
        var readableId = await PublishedResources().Where(x =>
            x.Document.ContentExamCode != null && x.Document.PublicId == reference)
            .Select(x => x.Document.Id).FirstOrDefaultAsync(ct);
        if (bookmarked && readableId == 0)
            return Fail<bool>("Study resource is unavailable.", 404);
        // Removing an old bookmark is allowed after a source is withdrawn, without
        // leaking resource metadata or requiring the expired file to be readable.
        var documentId = readableId != 0 ? readableId : await db.Documents.AsNoTracking()
            .Where(x => x.TenantId == Tenant && x.PublicId == reference &&
                x.DocumentTypeCode == TypeCode && x.EntityType == EntityType)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (documentId == 0) return ApiResponse<bool>.SuccessResponse(false);
        // The global soft-delete filter would otherwise hide a tombstone and make
        // re-bookmarking collide with the unique (tenant, user, document) index.
        var existing = await db.StudyBookmarks.IgnoreQueryFilters().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.UserId == user.UserId && x.DocumentId == documentId, ct);
        if (existing == null && !bookmarked)
            return ApiResponse<bool>.SuccessResponse(false);
        if (existing != null && existing.IsDeleted == !bookmarked)
            return ApiResponse<bool>.SuccessResponse(bookmarked);
        if (existing == null)
            db.StudyBookmarks.Add(new EduOS.Core.Entities.Library.StudyBookmark
            { TenantId = Tenant, UserId = user.UserId, DocumentId = documentId,
                CreatedBy = user.UserId, CreatedAt = Now });
        else
        {
            existing.IsDeleted = !bookmarked;
            existing.UpdatedBy = user.UserId;
            existing.UpdatedAt = Now;
        }
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<bool>.SuccessResponse(bookmarked);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent study bookmark for tenant {TenantId}", Tenant);
            return Fail<bool>("Bookmark changed concurrently; reload and retry.", 409);
        }
    }

    // Metadata-only lineage investigation. Never permit historical bytes to be downloaded:
    // superseded and revoked versions remain inaccessible through DownloadAsync.
    // The bounded, tenant/book-filtered candidate set prevents an unbounded history scan.
    public async Task<ApiResponse<PagedResult<LibraryDigitalVersionDto>>> GetVersionHistoryAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanPublish || db.CurrentTenantId != Tenant)
            return Fail<PagedResult<LibraryDigitalVersionDto>>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            ((long)page - 1) * pageSize > int.MaxValue)
            return Fail<PagedResult<LibraryDigitalVersionDto>>("Invalid version history request.");

        var selected = await db.Documents.AsNoTracking().Where(x =>
            x.TenantId == Tenant && !x.IsDeleted && x.PublicId == reference &&
            x.DocumentTypeCode == TypeCode && x.EntityType == EntityType)
            .Select(x => new { x.Id, x.EntityId }).FirstOrDefaultAsync(ct);
        if (selected == null)
            return Fail<PagedResult<LibraryDigitalVersionDto>>("Resource not found.", 404);

        // All independent publications for the same book may coexist. Resolve only
        // the selected SupersedesDocumentId chain; never mix another publication.
        var candidates = await db.Documents.AsNoTracking().Where(x =>
            x.TenantId == Tenant && !x.IsDeleted && x.EntityId == selected.EntityId &&
            x.DocumentTypeCode == TypeCode && x.EntityType == EntityType)
            .OrderBy(x => x.Id).Take(251)
            .Select(x => new {
                x.Id, x.PublicId, x.SupersedesDocumentId, x.VersionNo, x.Title,
                x.LibraryCampusId, x.ExpiresAt, x.IsActive, x.CreatedAt, x.VerificationStatusCode
            }).ToListAsync(ct);
        if (candidates.Count > 250)
            return Fail<PagedResult<LibraryDigitalVersionDto>>(
                "Book publication history exceeds the safe investigation limit.", 409);
        var byId = candidates.ToDictionary(x => x.Id);
        var seen = new HashSet<long>();
        var rootId = selected.Id;
        while (true)
        {
            if (!seen.Add(rootId) || !byId.TryGetValue(rootId, out var current))
                return Fail<PagedResult<LibraryDigitalVersionDto>>(
                    "Publication lineage is incomplete or cyclic.", 409);
            if (!current.SupersedesDocumentId.HasValue) break;
            rootId = current.SupersedesDocumentId.Value;
        }

        var successors = candidates.Where(x => x.SupersedesDocumentId.HasValue)
            .ToLookup(x => x.SupersedesDocumentId!.Value);
        var lineage = new List<LibraryDigitalVersionDto>();
        seen.Clear();
        var cursor = rootId;
        while (true)
        {
            if (!seen.Add(cursor) || !byId.TryGetValue(cursor, out var current))
                return Fail<PagedResult<LibraryDigitalVersionDto>>(
                    "Publication lineage is incomplete or cyclic.", 409);
            var next = successors[cursor].ToList();
            if (next.Count > 1 || (next.Count == 1 &&
                next[0].VersionNo != current.VersionNo + 1))
                return Fail<PagedResult<LibraryDigitalVersionDto>>(
                    "Conflicting publication successors require investigation.", 409);
            lineage.Add(new LibraryDigitalVersionDto
            {
                Reference = current.PublicId, VersionNo = current.VersionNo,
                Title = current.Title ?? string.Empty, CampusId = current.LibraryCampusId,
                ExpiresOn = current.ExpiresAt.HasValue
                    ? DateOnly.FromDateTime(current.ExpiresAt.Value) : null,
                PublishedAtUtc = current.CreatedAt,
                State = current.VerificationStatusCode is "Pending" or "Rejected"
                    ? current.VerificationStatusCode :
                    current.IsActive ? "Active" : next.Count == 1 &&
                        next[0].VerificationStatusCode is not ("Pending" or "Rejected")
                        ? "Superseded" : "Revoked"
            });
            if (next.Count == 0) break;
            cursor = next[0].Id;
        }
        if (!seen.Contains(selected.Id))
            return Fail<PagedResult<LibraryDigitalVersionDto>>(
                "Selected resource is not in the resolved publication lineage.", 409);

        var total = lineage.Count;
        var items = lineage.OrderByDescending(x => x.VersionNo)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize).ToList();
        return ApiResponse<PagedResult<LibraryDigitalVersionDto>>.SuccessResponse(
            new PagedResult<LibraryDigitalVersionDto>
            { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    // Historical audit events remain immutable. Only tenant admins may investigate actor IDs.
    // Date windows are bounded and private file keys, audit JSON and borrower names never leave SQL.
    public async Task<ApiResponse<PagedResult<LibraryDigitalAuditDto>>> GetAuditTrailAsync(
        DateOnly? from, DateOnly? to, string? action, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanPublish || db.CurrentTenantId != Tenant)
            return Fail<PagedResult<LibraryDigitalAuditDto>>("Tenant administrator required.", 403);
        var today = DateOnly.FromDateTime(Now);
        var first = from ?? today.AddDays(-30);
        var last = to ?? today;
        var filter = (action ?? "all").Trim().ToLowerInvariant();
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue ||
            first > last || last > today || last.DayNumber - first.DayNumber > 31 ||
            filter is not ("all" or "publish" or "replace" or "submit" or "approve" or "reject" or "update" or "revoke" or "download"))
            return Fail<PagedResult<LibraryDigitalAuditDto>>("Invalid audit filters.");
        var begin = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = last.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = from audit in db.AuditLogs.AsNoTracking()
                    join doc in db.Documents.AsNoTracking() on audit.EntityId equals (long?)doc.Id
                    where audit.TenantId == Tenant && audit.IsSuccess &&
                        audit.EntityName == "LibraryDigitalResource" &&
                        audit.OccurredAt >= begin && audit.OccurredAt < end &&
                        doc.TenantId == Tenant && !doc.IsDeleted &&
                        doc.EntityType == EntityType && doc.DocumentTypeCode == TypeCode &&
                        (audit.Action == "LibraryDigitalResource.Publish" ||
                         audit.Action == "LibraryDigitalResource.Replace" ||
                         audit.Action == "LibraryDigitalResource.Submit" ||
                         audit.Action == "LibraryDigitalResource.Approve" ||
                         audit.Action == "LibraryDigitalResource.Reject" ||
                         audit.Action == "LibraryDigitalResource.Update" ||
                         audit.Action == "LibraryDigitalResource.Revoke" ||
                         audit.Action == "LibraryDigitalResource.Download")
                    select new { audit, doc };
        if (filter != "all")
        {
            var code = "LibraryDigitalResource." + char.ToUpperInvariant(filter[0]) + filter[1..];
            query = query.Where(x => x.audit.Action == code);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.audit.OccurredAt)
            .ThenByDescending(x => x.audit.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize)
            .Select(x => new LibraryDigitalAuditDto
            {
                ResourceReference = x.doc.PublicId,
                CurrentTitle = x.doc.Title ?? string.Empty,
                Action = x.audit.Action,
                VersionNo = x.doc.VersionNo,
                OccurredAtUtc = x.audit.OccurredAt,
                ActorUserId = x.audit.UserId
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryDigitalAuditDto>>.SuccessResponse(
            new PagedResult<LibraryDigitalAuditDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    private AuditLog Audit(string action, long id, Guid reference) => new()
    {
        TenantId = Tenant, UserId = user.UserId, Action = action,
        EntityName = "LibraryDigitalResource", EntityId = id,
        CorrelationId = reference.ToString("N"), NewValue = action,
        IsSuccess = true, OccurredAt = Now
    };

    public async Task<ApiResponse<LibraryDigitalResourceDto>> UploadAsync(
        LibraryDigitalUploadRequestDto request, PrivateFileUploadDto file, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (request == null || request.RequestId == Guid.Empty || request.BookReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            file == null || file.Length is < 1 or > MaxBytes || !ValidExpiry(request.ExpiresOn) ||
            !ValidRights(request.Rights))
            return Fail<LibraryDigitalResourceDto>("Invalid book, title, request ID or file.");
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == request.BookReference &&
            x.IsActive && !x.IsDeleted, ct);
        if (book == null) return Fail<LibraryDigitalResourceDto>("Active book not found.", 404);
        if (!await ValidCampusScopeAsync(request.CampusId, ct))
            return Fail<LibraryDigitalResourceDto>("Active campus in this institution required.", 400);
        if (!await ValidCurriculumAsync(request.Rights, ct))
            return Fail<LibraryDigitalResourceDto>("Active curriculum, level and subject mapping required.");
        // The existing document uploader enforces the private tenant namespace, bounded
        // byte stream, quota, malware scan, SHA-256 and idempotent upload receipt.
        var uploaded = await documentFiles.UploadAsync(request.RequestId, file, ct);
        if (!uploaded.Success || uploaded.Data == null)
            return Fail<LibraryDigitalResourceDto>(uploaded.Message, uploaded.StatusCode);
        var title = request.Title.Trim();
        var expiry = Expiry(request.ExpiresOn);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await ValidCampusScopeAsync(request.CampusId, token))
                    return Fail<LibraryDigitalResourceDto>("Campus licence changed. Reload and retry.", 409);
                if (!await ValidCurriculumAsync(request.Rights, token))
                    return Fail<LibraryDigitalResourceDto>("Curriculum mapping changed. Reload and retry.", 409);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == uploaded.Data.Id &&
                    x.PublicId == request.RequestId && x.UploadedByUserId == user.UserId &&
                    !x.IsDeleted && x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (asset == null || !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Verified private file unavailable.", 403);
                var existing = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.FileAssetId != asset.Id || existing.EntityId != book.Id ||
                        existing.EntityType != EntityType || existing.DocumentTypeCode != TypeCode ||
                        existing.Title != title || existing.ExpiresAt != expiry ||
                        existing.LibraryCampusId != request.CampusId ||
                        existing.VerificationStatusCode is not ("Pending" or "Approved") ||
                        !RightsMatch(existing, request.Rights))
                        return Fail<LibraryDigitalResourceDto>("Request ID already used or resource revoked.", 409);
                    return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                        Map(existing, asset, book.PublicId, book.Title));
                }
                if (await db.Documents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.FileAssetId == asset.Id && !x.IsDeleted, token))
                    return Fail<LibraryDigitalResourceDto>("File already attached.", 409);
                var definition = await db.DocumentTypeDefinitions.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Code == TypeCode && !x.IsDeleted, token);
                if (definition == null)
                {
                    definition = new DocumentTypeDefinition
                    {
                        TenantId = Tenant, Code = TypeCode, Name = "Library digital resource",
                        IsActive = true, RequiresVerification = false, IsSensitive = false,
                        CreatedBy = user.UserId, CreatedAt = Now
                    };
                    db.DocumentTypeDefinitions.Add(definition);
                    await db.SaveChangesAsync(token);
                }
                if (!definition.IsActive || definition.RequiresVerification || definition.IsSensitive)
                    return Fail<LibraryDigitalResourceDto>("Library document type requires administrator review.", 409);
                var row = new Document
                {
                    TenantId = Tenant, PublicId = request.RequestId, FileAssetId = asset.Id,
                    DocumentTypeCode = TypeCode, EntityType = EntityType, EntityId = book.Id,
                    Title = title, ExpiresAt = expiry, IsActive = false,
                    LibraryCampusId = request.CampusId,
                    VerificationStatusCode = "Pending",
                    CreatedBy = user.UserId, CreatedAt = Now
                };
                SetRights(row, request.Rights);
                db.Documents.Add(row);
                await db.SaveChangesAsync(token);
                db.AuditLogs.Add(Audit("LibraryDigitalResource.Submit", row.Id, row.PublicId));
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(row, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital registration conflict for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource registration; retry the same request ID.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            // SQL Server can surface a serializable publish collision as SqlException
            // 1205 (deadlock) or 3960 (snapshot update conflict), sometimes wrapped
            // by EF's execution strategy. Never turn a concurrent replay into HTTP 500.
            logger.LogWarning(ex, "Library digital publication contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource registration; retry the same request ID.", 409);
        }
    }


    /// <summary>
    /// Atomically supersede an existing publication while preserving its immutable file
    /// and document history. Scanning runs before the short SQL transaction; an upload
    /// losing a concurrent replacement stays private and unattached for reconciliation.
    /// </summary>
    public async Task<ApiResponse<LibraryDigitalResourceDto>> ReplaceAsync(
        Guid reference, LibraryDigitalReplaceRequestDto request,
        PrivateFileUploadDto file, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.RequestId == reference || string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            file == null || file.Length is < 1 or > MaxBytes || !ValidExpiry(request.ExpiresOn) ||
            !ValidRights(request.Rights))
            return Fail<LibraryDigitalResourceDto>("Valid resource, version, title and replacement file required.");

        var old = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
            x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, ct);
        if (old == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
        var replay = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, ct);
        if (replay == null && (!old.IsActive || !VersionMatches(old.RowVersion, request.RowVersion)))
            return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
        if (replay != null && replay.SupersedesDocumentId != old.Id)
            return Fail<LibraryDigitalResourceDto>("Request ID already used.", 409);
        if (old.VersionNo < 1 || old.VersionNo == int.MaxValue)
            return Fail<LibraryDigitalResourceDto>("Resource version requires reconciliation.", 409);
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == old.EntityId && !x.IsDeleted && x.IsActive, ct);
        if (book == null) return Fail<LibraryDigitalResourceDto>("Active book unavailable.", 409);
        if (!await ValidCurriculumAsync(request.Rights, ct))
            return Fail<LibraryDigitalResourceDto>("Active curriculum, level and subject mapping required.");

        var uploaded = await documentFiles.UploadAsync(request.RequestId, file, ct);
        if (!uploaded.Success || uploaded.Data == null)
            return Fail<LibraryDigitalResourceDto>(uploaded.Message, uploaded.StatusCode);
        var title = request.Title.Trim();
        var expiry = Expiry(request.ExpiresOn);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var previous = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (previous == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                if (!await ValidCurriculumAsync(request.Rights, token))
                    return Fail<LibraryDigitalResourceDto>("Curriculum mapping changed. Reload and retry.", 409);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == uploaded.Data.Id &&
                    x.PublicId == request.RequestId && x.UploadedByUserId == user.UserId &&
                    !x.IsDeleted && x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (asset == null || !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Verified replacement file unavailable.", 403);
                var existing = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.SupersedesDocumentId != previous.Id ||
                        existing.VersionNo != previous.VersionNo + 1 ||
                        existing.EntityId != previous.EntityId || existing.EntityType != EntityType ||
                        existing.DocumentTypeCode != TypeCode || existing.FileAssetId != asset.Id ||
                        existing.Title != title || existing.ExpiresAt != expiry ||
                         existing.LibraryCampusId != previous.LibraryCampusId ||
                         existing.VerificationStatusCode is not ("Pending" or "Approved") ||
                         !RightsMatch(existing, request.Rights))
                        return Fail<LibraryDigitalResourceDto>("Replacement replay differs or was revoked.", 409);
                    return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                        Map(existing, asset, book.PublicId, book.Title));
                }
                if (!previous.IsActive || !VersionMatches(previous.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                if (await db.Documents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && !x.IsDeleted && x.SupersedesDocumentId == previous.Id, token))
                    return Fail<LibraryDigitalResourceDto>("A replacement already exists for this version.", 409);
                if (await db.Documents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.FileAssetId == asset.Id && !x.IsDeleted, token))
                    return Fail<LibraryDigitalResourceDto>("Replacement file already attached.", 409);
                if (!await db.DocumentTypeDefinitions.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.Code == TypeCode && x.IsActive &&
                    !x.IsDeleted && !x.RequiresVerification && !x.IsSensitive, token))
                    return Fail<LibraryDigitalResourceDto>("Library publication policy unavailable.", 409);
                var next = new Document
                {
                    TenantId = Tenant, PublicId = request.RequestId,
                    FileAssetId = asset.Id, DocumentTypeCode = TypeCode,
                    EntityType = EntityType, EntityId = previous.EntityId,
                    SupersedesDocumentId = previous.Id, VersionNo = previous.VersionNo + 1,
                    Title = title, ExpiresAt = expiry, IsActive = false,
                    LibraryCampusId = previous.LibraryCampusId,
                    VerificationStatusCode = "Pending",
                    CreatedBy = user.UserId, CreatedAt = Now
                };
                SetRights(next, request.Rights);
                db.Documents.Add(next);
                await db.SaveChangesAsync(token);
                var audit = Audit("LibraryDigitalResource.Submit", next.Id, next.PublicId);
                audit.OldValue = JsonSerializer.Serialize(new { PreviousReference = reference, previous.VersionNo });
                audit.NewValue = JsonSerializer.Serialize(new { NewReference = next.PublicId, next.VersionNo });
                db.AuditLogs.Add(audit);
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(next, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital replacement contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent replacement. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<LibraryDigitalResourceDto>> UpdateMetadataAsync(
        Guid reference, LibraryDigitalMetadataRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || !ValidExpiry(request.ExpiresOn))
            return Fail<LibraryDigitalResourceDto>("Valid title, future expiry and row version required.");
        if (!await ValidCampusScopeAsync(request.CampusId, ct))
            return Fail<LibraryDigitalResourceDto>("Active campus in this institution required.", 400);
        var title = request.Title.Trim();
        var expiry = Expiry(request.ExpiresOn);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await ValidCampusScopeAsync(request.CampusId, token))
                    return Fail<LibraryDigitalResourceDto>("Campus licence changed. Reload and retry.", 409);
                var document = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (document == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                if (!VersionMatches(document.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                if (!document.IsActive)
                    return Fail<LibraryDigitalResourceDto>("Revoked resources cannot be edited.", 409);
                // Old metadata clients must not silently widen a restricted licence.
                if (document.LibraryCampusId.HasValue && !request.CampusId.HasValue &&
                    !request.ConfirmTenantWideAccess)
                    return Fail<LibraryDigitalResourceDto>("Confirm institute-wide access explicitly.", 409);
                // Extending time or widening campus scope needs a new reviewed
                // publication; the legacy metadata endpoint cannot grant rights.
                if (document.VerificationStatusCode == "Approved" &&
                    ((document.ExpiresAt.HasValue && (!expiry.HasValue || expiry > document.ExpiresAt)) ||
                    (document.LibraryCampusId.HasValue && document.LibraryCampusId != request.CampusId))
                    )
                    return Fail<LibraryDigitalResourceDto>("Expanded access requires a reviewed replacement.", 409);
                var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == document.EntityId && !x.IsDeleted, token);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == document.FileAssetId && !x.IsDeleted &&
                    x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (book == null || asset == null ||
                    !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Verified resource unavailable.", 409);
                if (document.Title == title && document.ExpiresAt == expiry &&
                    document.LibraryCampusId == request.CampusId)
                    return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                        Map(document, asset, book.PublicId, book.Title));
                var before = JsonSerializer.Serialize(new { document.Title, document.ExpiresAt, document.LibraryCampusId });
                document.Title = title;
                document.ExpiresAt = expiry;
                document.LibraryCampusId = request.CampusId;
                document.UpdatedAt = Now;
                document.UpdatedBy = user.UserId;
                var audit = Audit("LibraryDigitalResource.Update", document.Id, reference);
                audit.OldValue = before;
                audit.NewValue = JsonSerializer.Serialize(new { document.Title, document.ExpiresAt, document.LibraryCampusId });
                db.AuditLogs.Add(audit);
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(document, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital write contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource change. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<LibraryDigitalResourceDto>> RevokeAsync(
        Guid reference, LibraryDigitalRevokeRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return Fail<LibraryDigitalResourceDto>("Resource and row version required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (row == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                if (!VersionMatches(row.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.EntityId && !x.IsDeleted, token);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.FileAssetId && !x.IsDeleted, token);
                if (book == null || asset == null)
                    return Fail<LibraryDigitalResourceDto>("Resource references unavailable.", 409);
                if (row.IsActive || row.VerificationStatusCode == "Pending")
                {
                    if (row.VerificationStatusCode == "Pending") row.VerificationStatusCode = "Rejected";
                    row.IsActive = false; row.UpdatedAt = Now; row.UpdatedBy = user.UserId;
                    db.AuditLogs.Add(Audit("LibraryDigitalResource.Revoke", row.Id, row.PublicId));
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(row, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital write contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource change. Reload and retry.", 409);
        }
    }

    /// <summary>Review is a short, serializable state transition. A replacement
    /// supersedes its predecessor only after its rights and file are approved.</summary>
    public async Task<ApiResponse<LibraryDigitalResourceDto>> ReviewAsync(
        Guid reference, LibraryDigitalReviewRequestDto request, bool approve,
        CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length > 500 ||
            request.Note.Any(char.IsControl))
            return Fail<LibraryDigitalResourceDto>("A row version and review note are required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (row == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                if (!VersionMatches(row.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                if (row.VerificationStatusCode != "Pending" || row.IsActive)
                    return Fail<LibraryDigitalResourceDto>("Only pending resources may be reviewed.", 409);
                var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.EntityId && x.IsActive && !x.IsDeleted, token);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.FileAssetId && !x.IsDeleted &&
                    x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (book == null || asset == null ||
                    !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Book or scanned private file unavailable.", 409);
                if (approve)
                {
                    var rights = new LibraryDigitalRightsRequestDto
                    {
                        CurriculumId = row.ContentCurriculumId, LevelId = row.ContentLevelId,
                        SubjectId = row.ContentSubjectId, Chapter = row.ContentChapter,
                        Topic = row.ContentTopic, LanguageCode = row.ContentLanguageCode ?? "",
                        SourceUrl = row.ContentSourceUrl ?? "", RightsHolder = row.ContentRightsHolder ?? "",
                        LicenceCode = row.ContentLicenceCode ?? "",
                        LicenceEvidence = row.ContentLicenceEvidence ?? "",
                        ExamCode = row.ContentExamCode, BoardCode = row.ContentBoardCode,
                        SyllabusYear = row.ContentSyllabusYear,
                        SyllabusVersion = row.ContentSyllabusVersion,
                        SyllabusSourceUrl = row.ContentSyllabusSourceUrl,
                        PreviousPaperYear = row.ContentPreviousPaperYear,
                        StudyExplanation = row.ContentStudyExplanation
                    };
                    if (!ValidRights(rights) || !await ValidCurriculumAsync(rights, token) ||
                        !await ValidCampusScopeAsync(row.LibraryCampusId, token))
                        return Fail<LibraryDigitalResourceDto>("Curriculum, rights or campus licence requires correction.", 409);
                    if (row.SupersedesDocumentId.HasValue)
                    {
                        var previous = await db.Documents.FirstOrDefaultAsync(x =>
                            x.TenantId == Tenant && x.Id == row.SupersedesDocumentId && !x.IsDeleted &&
                            x.EntityType == EntityType && x.DocumentTypeCode == TypeCode, token);
                        if (previous == null || !previous.IsActive ||
                            previous.EntityId != row.EntityId ||
                            previous.VersionNo + 1 != row.VersionNo ||
                            previous.LibraryCampusId != row.LibraryCampusId)
                            return Fail<LibraryDigitalResourceDto>("Predecessor changed; replacement cannot be approved.", 409);
                        previous.IsActive = false;
                        previous.UpdatedAt = Now;
                        previous.UpdatedBy = user.UserId;
                    }
                }
                row.VerificationStatusCode = approve ? "Approved" : "Rejected";
                row.IsActive = approve;
                row.VerifiedByUserId = user.UserId;
                row.VerifiedAt = Now;
                row.VerificationNote = request.Note.Trim();
                row.UpdatedAt = Now;
                row.UpdatedBy = user.UserId;
                var audit = Audit(approve ? "LibraryDigitalResource.Approve" : "LibraryDigitalResource.Reject",
                    row.Id, row.PublicId);
                audit.NewValue = JsonSerializer.Serialize(new { row.VerificationStatusCode, row.VerifiedByUserId,
                    row.VerifiedAt, Note = row.VerificationNote, row.SupersedesDocumentId });
                db.AuditLogs.Add(audit);
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(row, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital review contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent review. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (reference == Guid.Empty || !await CanReadAsync(ct))
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
        var row = await ResourceQuery().Where(x => x.Document.PublicId == reference &&
            x.Document.IsActive && x.Book.IsActive &&
            (x.Document.VerificationStatusCode == "Approved" ||
             x.Document.VerificationStatusCode == "NotRequired") &&
            // A campus licence stops granting downloads when its campus is deactivated.
            (!x.Document.LibraryCampusId.HasValue || db.Campuses.Any(c =>
                c.TenantId == Tenant && c.Id == x.Document.LibraryCampusId.Value &&
                c.IsActive && !c.IsDeleted)) &&
            (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > Now))
            .Select(x => new { DocumentId = x.Document.Id, x.File.StorageKey, x.File.SizeBytes,
                x.File.Sha256, x.File.ContentType, x.File.OriginalFileName })
            .FirstOrDefaultAsync(ct);
        if (row == null || row.SizeBytes is < 1 or > MaxBytes ||
            !DocumentFileService.IsTenantDocumentKey(row.StorageKey, Tenant))
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
        // No public storage URL is returned; every download rechecks live membership,
        // resource state, private namespace and cryptographic file integrity.
        FileDownloadResult? stored;
        try { stored = await storage.GetPrivateFileAsync(row.StorageKey); }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Library private file read unavailable for tenant {TenantId}", Tenant);
            return Fail<DocumentFileDownloadDto>("File temporarily unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Library private file access unavailable for tenant {TenantId}", Tenant);
            return Fail<DocumentFileDownloadDto>("File temporarily unavailable.", 503);
        }
        if (stored == null || stored.Content.LongLength != row.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)), row.Sha256,
                StringComparison.Ordinal))
            return Fail<DocumentFileDownloadDto>("File integrity verification failed.", 409);

        // Private storage is asynchronous. A student may withdraw, lose campus
        // access, or an administrator may revoke the resource while bytes are
        // being read. Recheck the authoritative V1 membership and the complete
        // tenant-bound document/book/file chain before returning those bytes.
        // A successful pre-read authorization is not a durable download grant.
        if (!await CanReadAsync(ct) || !await ResourceQuery().AnyAsync(x =>
                x.Document.Id == row.DocumentId && x.Document.PublicId == reference &&
                // The FileAsset may be replaced while private storage is being read.
                // Never return bytes validated against an obsolete storage fingerprint.
                x.File.StorageKey == row.StorageKey &&
                x.File.SizeBytes == row.SizeBytes &&
                x.File.Sha256 == row.Sha256 &&
                x.File.ContentType == row.ContentType &&
                x.Document.IsActive && x.Book.IsActive &&
                (x.Document.VerificationStatusCode == "Approved" ||
                 x.Document.VerificationStatusCode == "NotRequired") &&
                (!x.Document.LibraryCampusId.HasValue || db.Campuses.Any(c =>
                    c.TenantId == Tenant && c.Id == x.Document.LibraryCampusId.Value &&
                    c.IsActive && !c.IsDeleted)) &&
                (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > Now), ct))
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);

        db.AuditLogs.Add(Audit("LibraryDigitalResource.Download", row.DocumentId, reference));
        await db.SaveChangesAsync(ct);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = stored.Content, ContentType = row.ContentType,
            FileName = Path.GetFileName(row.OriginalFileName.Replace('\\', '/'))
        });
    }
}
