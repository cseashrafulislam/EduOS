using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Service.Helpers.Storage;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

/// <summary>Section and resource operations on the existing Course/Lesson aggregate, never a second lesson CRUD.</summary>
public sealed partial class LmsCourseContentService : ILmsCourseContentService
{
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<CourseSection> _sections;
    private readonly IGenericRepository<Lesson> _lessons;
    private readonly IGenericRepository<LessonResource> _resources;
    private readonly IGenericRepository<LmsLessonRevision>? _revisions;
    private readonly IGenericRepository<Assignment> _assignments;
    private readonly IGenericRepository<AssignmentAttachment> _attachments;
    private readonly IGenericRepository<FileAsset> _files;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<CourseEnrollment> _enrollments;
    private readonly IGenericRepository<StudentEnrollment> _academicEnrollments;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _subjects;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<InstructorAssignment> _instructors;
    private readonly IGenericRepository<EmployeeCampusAssignment> _campuses;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<LmsCourseContentService> _logger;
    private readonly IFileUploadService? _storage;
    private readonly IDocumentFileService? _documentFiles;

    public LmsCourseContentService(IGenericRepository<Course> courses,
        IGenericRepository<CourseSection> sections, IGenericRepository<Lesson> lessons,
        IGenericRepository<LessonResource> resources, IGenericRepository<FileAsset> files,
        IGenericRepository<Employee> employees, IGenericRepository<Student> students,
        IGenericRepository<CourseEnrollment> enrollments, IGenericRepository<StudentEnrollment> academicEnrollments,
        IGenericRepository<SubjectOffering> offerings, IGenericRepository<CurriculumSubject> subjects,
        IGenericRepository<AcademicCurriculum> curricula, IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicTerm> terms, IGenericRepository<InstructorAssignment> instructors,
        IGenericRepository<EmployeeCampusAssignment> campuses, IUnitOfWork uow,
        ICurrentUserService user, TimeProvider clock, ILogger<LmsCourseContentService> logger,
        IGenericRepository<Assignment>? assignments = null,
        IGenericRepository<AssignmentAttachment>? attachments = null,
        IFileUploadService? storage = null,
        IDocumentFileService? documentFiles = null,
        IGenericRepository<LmsLessonRevision>? revisions = null)
    {
        _courses = courses; _sections = sections; _lessons = lessons; _resources = resources;
        _assignments = assignments!; _attachments = attachments!; _files = files;
        _employees = employees; _students = students; _enrollments = enrollments;
        _academicEnrollments = academicEnrollments; _offerings = offerings; _subjects = subjects;
        _curricula = curricula; _batches = batches; _years = years; _instructors = instructors;
        _campuses = campuses; _terms = terms; _uow = uow; _user = user; _clock = clock; _logger = logger;
        _storage = storage; _documentFiles = documentFiles; _revisions = revisions;
    }

    private long Tenant => _user.TenantId;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka")));
    private bool Authenticated => _user.IsAuthenticated && Tenant > 0;
    private bool Manager => Authenticated && (_user.IsTenantAdmin || _user.IsInRole("Principal"));
    private bool Author => Manager || (Authenticated && _user.IsInRole("Teacher"));
    private static ApiResponse<T> Fail<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Version(byte[] actual, string? encoded)
    {
        if (actual.Length == 0 || string.IsNullOrWhiteSpace(encoded)) return false;
        try { var expected = Convert.FromBase64String(encoded);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected); }
        catch (FormatException) { return false; }
    }
    private static bool Https(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }
    private Task<Course?> CourseAsync(Guid reference, CancellationToken ct) =>
        _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted, ct);

    // A published academic course must have a currently valid, same-campus offering.
    private async Task<bool> HasOfferingAsync(Course course, long? instructorId, long? batchId, CancellationToken ct)
    {
        if (!course.AcademicProgramId.HasValue && !course.SubjectId.HasValue) return true;
        if (!course.AcademicProgramId.HasValue || !course.SubjectId.HasValue) return false;
        var today = Today;
        var query =
            from offering in _offerings.GetQueryable().AsNoTracking()
            join subject in _subjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals subject.Id
            join curriculum in _curricula.GetQueryable().AsNoTracking() on subject.AcademicCurriculumId equals curriculum.Id
            join batch in _batches.GetQueryable().AsNoTracking() on offering.AcademicBatchId equals batch.Id
            join year in _years.GetQueryable().AsNoTracking() on offering.AcademicYearId equals year.Id
            where offering.TenantId == Tenant && subject.TenantId == Tenant && curriculum.TenantId == Tenant &&
                batch.TenantId == Tenant && year.TenantId == Tenant &&
                !offering.IsDeleted && !subject.IsDeleted && !curriculum.IsDeleted && !batch.IsDeleted && !year.IsDeleted &&
                offering.IsActive && subject.IsActive && curriculum.IsActive && batch.IsActive && year.IsActive &&
                curriculum.AcademicProgramId == course.AcademicProgramId &&
                batch.AcademicProgramId == course.AcademicProgramId && subject.SubjectId == course.SubjectId &&
                subject.AcademicLevelId == batch.AcademicLevelId &&
                batch.AcademicYearId == year.Id && offering.AcademicYearId == year.Id &&
                (!batch.AcademicTermId.HasValue || batch.AcademicTermId == offering.AcademicTermId) &&
                (!offering.AcademicTermId.HasValue || _terms.GetQueryable().Any(term =>
                    term.TenantId == Tenant && term.Id == offering.AcademicTermId &&
                    term.AcademicYearId == year.Id && term.IsActive && !term.IsDeleted &&
                    term.StartDate <= today && term.EndDate >= today)) &&
                (!batchId.HasValue || batch.Id == batchId.Value) &&
                year.StartDate <= today && year.EndDate >= today &&
                (!year.CampusId.HasValue || year.CampusId == batch.CampusId) &&
                curriculum.EffectiveFrom <= today && (!curriculum.EffectiveTo.HasValue || curriculum.EffectiveTo >= today) &&
                (!batch.StartDate.HasValue || batch.StartDate <= today) &&
                (!batch.EndDate.HasValue || batch.EndDate >= today)
            select new { offering.Id, batch.CampusId };
        if (!instructorId.HasValue) return await query.AnyAsync(ct);
        return await (from row in query
            join assignment in _instructors.GetQueryable().AsNoTracking() on row.Id equals assignment.SubjectOfferingId
            join campus in _campuses.GetQueryable().AsNoTracking() on assignment.EmployeeId equals campus.EmployeeId
            where assignment.TenantId == Tenant && campus.TenantId == Tenant &&
                !assignment.IsDeleted && !campus.IsDeleted && assignment.IsActive && campus.IsCurrent &&
                assignment.EmployeeId == instructorId.Value && campus.CampusId == row.CampusId &&
                assignment.EffectiveFrom <= today && (!assignment.EffectiveTo.HasValue || assignment.EffectiveTo >= today) &&
                campus.EffectiveFrom <= today && (!campus.EffectiveTo.HasValue || campus.EffectiveTo >= today)
            select assignment.Id).AnyAsync(ct);
    }
    private async Task<bool> CanEditAsync(Course course, CancellationToken ct)
    {
        if (!Author) return false;
        if (Manager) return true;
        var employeeId = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.UserId == _user.UserId && x.CanTeach &&
            x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
        return employeeId > 0 && course.PrimaryInstructorEmployeeId == employeeId &&
            await HasOfferingAsync(course, employeeId, null, ct);
    }
    private async Task<bool> CanViewAsync(Course course, CancellationToken ct)
    {
        if (await CanEditAsync(course, ct)) return true;
        if (!Authenticated || !_user.IsInRole("Student")) return false;
        var studentId = await _students.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.UserId == _user.UserId &&
            x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (studentId == 0) return false;
        if (!course.IsActive) return await CanViewHistoricalAsync(course, studentId, ct);
        var enrollments = _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.CourseId == course.Id && x.StudentId == studentId &&
            !x.IsDeleted && (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed));
        if (!course.AcademicProgramId.HasValue && !course.SubjectId.HasValue)
            return await enrollments.AnyAsync(ct);
        var academic = await _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.StudentId == studentId && x.IsCurrent &&
            x.State == EnrollmentState.Active && !x.IsDeleted &&
            x.AcademicProgramId == course.AcademicProgramId)
            .Select(x => new { x.Id, x.AcademicBatchId }).Take(20).ToListAsync(ct);
        foreach (var row in academic)
            if (await enrollments.AnyAsync(x => !x.StudentEnrollmentId.HasValue || x.StudentEnrollmentId == row.Id, ct) &&
                await HasOfferingAsync(course, null, row.AcademicBatchId, ct)) return true;
        return false;
    }
    private static CourseSectionDto Section(CourseSection row) => new()
    {
        Id = row.Id, Title = row.Title, DisplayOrder = row.DisplayOrder,
        IsPublished = row.IsPublished, RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private static ContentLessonDto LessonRow(Lesson row) => new()
    {
        Reference = row.PublicId, Title = row.Title, CourseSectionId = row.CourseSectionId,
        DisplayOrder = row.DisplayOrder, IsPublished = row.IsPublished,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
    private static LessonResourceDto Resource(LessonResource row, Guid lessonReference, FileAsset? file) => new()
    {
        Id = row.Id, LessonReference = lessonReference, Title = row.Title,
        ExternalUrl = row.ExternalUrl, FileAssetReference = file?.PublicId,
        FileName = file?.OriginalFileName, FileDownloadable = file != null && IsLmsPrivateAsset(file, row.TenantId),
        DisplayOrder = row.DisplayOrder,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<CourseContentDto>> GetCourseContentAsync(Guid courseReference, CancellationToken ct = default)
    {
        if (!Authenticated || courseReference == Guid.Empty)
            return Fail<CourseContentDto>("Course not found.", 404);
        try
        {
            // The same SQL read boundary must cover learner enrollment, lesson
            // publication and the bounded resource projection used by HTML/CSV.
            // A withdrawal must not split authorization from private readback.
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
        if (!Authenticated || courseReference == Guid.Empty) return Fail<CourseContentDto>("Course not found.", 404);
        var course = await CourseAsync(courseReference, token);
        if (course == null || !await CanViewAsync(course, token)) return Fail<CourseContentDto>("Course not found.", 404);
        var edit = await CanEditAsync(course, token);
        var sections = await _sections.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.CourseId == course.Id && !x.IsDeleted && (edit || x.IsPublished))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Take(201).ToListAsync(token);
        var lessons = await _lessons.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.CourseId == course.Id && !x.IsDeleted && (edit || x.IsPublished))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Take(501).ToListAsync(token);
        if (sections.Count > 200 || lessons.Count > 500)
            return Fail<CourseContentDto>("Course content exceeds the 200-section/500-lesson view limit.", 409);
        var visibleSectionIds = sections.Select(x => x.Id).ToHashSet();
        lessons = lessons.Where(x => !x.CourseSectionId.HasValue || visibleSectionIds.Contains(x.CourseSectionId.Value)).ToList();
        var lessonIds = lessons.Select(x => x.Id).ToArray();
        var resources = await _resources.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && lessonIds.Contains(x.LessonId) && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Take(1001).ToListAsync(token);
        if (resources.Count > 1000) return Fail<CourseContentDto>("Course resources exceed the 1000-item view limit.", 409);
        var fileIds = resources.Where(x => x.FileAssetId.HasValue).Select(x => x.FileAssetId!.Value).Distinct().ToArray();
        var files = await _files.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && fileIds.Contains(x.Id) && !x.IsDeleted && x.IsVerifiedSafe)
            .ToDictionaryAsync(x => x.Id, token);
        files = files.Where(x => IsLmsPrivateAsset(x.Value, Tenant))
            .ToDictionary(x => x.Key, x => x.Value);
        var lessonMap = lessons.ToDictionary(x => x.Id, x => LessonRow(x));
        foreach (var row in lessons)
        {
            var visibleResources = resources.Where(x => x.LessonId == row.Id)
                .Where(x => !x.FileAssetId.HasValue || files.ContainsKey(x.FileAssetId.Value))
                .Select(x => Resource(x, row.PublicId, x.FileAssetId.HasValue ? files[x.FileAssetId.Value] : null))
                .ToList();
            lessonMap[row.Id].Resources = visibleResources;
        }
        var sectionDtos = sections.Select(x =>
        {
            var dto = Section(x);
            dto.Lessons = lessons.Where(l => l.CourseSectionId == x.Id).Select(l => lessonMap[l.Id]).ToList();
            return dto;
        }).ToList();
        return ApiResponse<CourseContentDto>.SuccessResponse(new CourseContentDto
        {
            CourseReference = course.PublicId, CourseTitle = course.Title, CanEdit = edit,
            IsArchived = !course.IsActive,
            Sections = sectionDtos,
            UnsectionedLessons = lessons.Where(x => !x.CourseSectionId.HasValue).Select(x => lessonMap[x.Id]).ToList()
        });
            }, IsolationLevel.Serializable, ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
        {
            _logger.LogWarning(ex, "LMS content read SQL conflict for tenant {TenantId}", Tenant);
            return Fail<CourseContentDto>("Course content changed concurrently. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<CourseSectionDto>> SaveSectionAsync(long? sectionId,
        SaveCourseSectionRequestDto request, CancellationToken ct = default)
    {
        if (!Author) return Fail<CourseSectionDto>("Author permission required.", 403);
        if (request == null || request.CourseReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 ||
            request.DisplayOrder is < 0 or > 10000 || sectionId is <= 0)
            return Fail<CourseSectionDto>("Invalid course section fields.");
        var course = await CourseAsync(request.CourseReference, ct);
        if (course == null || !await CanEditAsync(course, ct))
            return Fail<CourseSectionDto>("Course author access denied.", 403);
        if (request.IsPublished && (!course.IsActive || !await HasOfferingAsync(course, null, null, ct)))
            return Fail<CourseSectionDto>("Active academic offering required before publication.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                if (request.IsPublished && (!course.IsActive || !await HasOfferingAsync(course, null, null, token)))
                    return Fail<CourseSectionDto>("Active academic offering required before publication.", 409);
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, token))
                    return Fail<CourseSectionDto>(CourseCompletionPublicationGuard.Message, 409);
                var row = sectionId.HasValue ? await _sections.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == sectionId && x.CourseId == course.Id && !x.IsDeleted, token) : null;
                if (sectionId.HasValue && row == null) return Fail<CourseSectionDto>("Section not found.", 404);
                var title = request.Title.Trim();
                var conflict = await _sections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.CourseId == course.Id && !x.IsDeleted &&
                    (x.Title == title || x.DisplayOrder == request.DisplayOrder) &&
                    (row == null || x.Id != row.Id), token);
                if (conflict != null)
                {
                    if (row == null && conflict.Title == title && conflict.DisplayOrder == request.DisplayOrder &&
                        conflict.IsPublished == request.IsPublished)
                        return ApiResponse<CourseSectionDto>.SuccessResponse(Section(conflict), "Section already exists.");
                    return Fail<CourseSectionDto>("Section title or display order already exists.", 409);
                }
                if (row != null && !Version(row.RowVersion, request.RowVersion))
                    return Fail<CourseSectionDto>("Section changed. Reload before editing.", 409);
                var now = Now;
                if (row == null)
                {
                    row = new CourseSection { TenantId = Tenant, CourseId = course.Id,
                        CreatedAt = now, CreatedBy = _user.UserId };
                    await _sections.AddAsync(row);
                }
                else { row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _sections.Update(row); }
                row.Title = title; row.DisplayOrder = request.DisplayOrder; row.IsPublished = request.IsPublished;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<CourseSectionDto>.SuccessResponse(Section(row), "Section saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<CourseSectionDto>("Section changed concurrently.", 409); }
        catch (DbUpdateException ex) { _logger.LogWarning(ex, "Section conflict for tenant {Tenant}", Tenant);
            return Fail<CourseSectionDto>("Section save conflicts with another request.", 409); }
    }

    public async Task<ApiResponse<ContentLessonDto>> AssignLessonSectionAsync(
        AssignLessonSectionRequestDto request, CancellationToken ct = default)
    {
        if (!Author) return Fail<ContentLessonDto>("Author permission required.", 403);
        if (request == null || request.CourseReference == Guid.Empty || request.LessonReference == Guid.Empty ||
            request.CourseSectionId is <= 0) return Fail<ContentLessonDto>("Invalid lesson or section.");
        var course = await CourseAsync(request.CourseReference, ct);
        if (course == null || !await CanEditAsync(course, ct))
            return Fail<ContentLessonDto>("Course author access denied.", 403);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _lessons.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.CourseId == course.Id &&
                    x.PublicId == request.LessonReference && !x.IsDeleted, token);
                if (row == null) return Fail<ContentLessonDto>("Lesson not found.", 404);
                if (request.CourseSectionId.HasValue)
                {
                    var section = await _sections.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == Tenant && x.CourseId == course.Id && x.Id == request.CourseSectionId &&
                        !x.IsDeleted, token);
                    if (section == null) return Fail<ContentLessonDto>("Section not found.", 404);
                    if (row.IsPublished && !section.IsPublished)
                        return Fail<ContentLessonDto>("Publish the section before moving a published lesson.", 409);
                }
                if (row.CourseSectionId == request.CourseSectionId)
                    return ApiResponse<ContentLessonDto>.SuccessResponse(LessonRow(row), "Lesson already assigned.");
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, token))
                    return Fail<ContentLessonDto>(CourseCompletionPublicationGuard.Message, 409);
                if (!Version(row.RowVersion, request.RowVersion))
                    return Fail<ContentLessonDto>("Lesson changed. Reload before assigning.", 409);
                await LmsLessonRevisionWriter.BaselineAsync(_revisions, _lessons, _resources, Tenant, row.Id, _user.UserId, Now, token);
                row.CourseSectionId = request.CourseSectionId; row.UpdatedAt = Now; row.UpdatedBy = _user.UserId;
                _lessons.Update(row);
                await _uow.SaveChangesAsync(token);
                await LmsLessonRevisionWriter.AppendAsync(_revisions, _lessons, _resources, Tenant, row.Id, "Section", _user.UserId, Now, token);
                return ApiResponse<ContentLessonDto>.SuccessResponse(LessonRow(row), "Lesson assigned.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<ContentLessonDto>("Lesson changed concurrently.", 409); }
        catch (DbUpdateException) { return Fail<ContentLessonDto>("Lesson section assignment conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<LessonResourceDto>> SaveResourceAsync(long? resourceId,
        SaveLessonResourceRequestDto request, CancellationToken ct = default)
    {
        if (!Author) return Fail<LessonResourceDto>("Author permission required.", 403);
        if (request == null || request.LessonReference == Guid.Empty || resourceId is <= 0 ||
            request.DisplayOrder is < 0 or > 10000 || request.Title?.Length > 200 ||
            request.ExternalUrl?.Length > 1000 ||
            (request.FileAssetReference.HasValue == !string.IsNullOrWhiteSpace(request.ExternalUrl)) ||
            (!request.FileAssetReference.HasValue && !Https(request.ExternalUrl)))
            return Fail<LessonResourceDto>("Provide exactly one verified file or HTTPS resource link.");
        var lesson = await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == request.LessonReference && !x.IsDeleted, ct);
        if (lesson == null) return Fail<LessonResourceDto>("Lesson not found.", 404);
        var course = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == lesson.CourseId && !x.IsDeleted, ct);
        if (course == null || !await CanEditAsync(course, ct))
            return Fail<LessonResourceDto>("Course author access denied.", 403);
        FileAsset? file = null;
        if (request.FileAssetReference.HasValue)
        {
            file = await _files.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.PublicId == request.FileAssetReference &&
                x.IsVerifiedSafe && !x.IsDeleted, ct);
            if (!IsLmsPrivateAsset(file, Tenant, _user.UserId)) return Fail<LessonResourceDto>("A clean LMS file uploaded by this author is required.", 409);
        }
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                if (request.FileAssetReference.HasValue)
                {
                    file = await _files.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == Tenant && x.PublicId == request.FileAssetReference.Value &&
                        x.IsVerifiedSafe && !x.IsDeleted, token);
                    if (!IsLmsPrivateAsset(file, Tenant, _user.UserId)) return Fail<LessonResourceDto>("A clean LMS file uploaded by this author is required.", 409);
                }
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, token))
                    return Fail<LessonResourceDto>(CourseCompletionPublicationGuard.Message, 409);
                var row = resourceId.HasValue ? await _resources.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == resourceId && x.LessonId == lesson.Id && !x.IsDeleted, token) : null;
                if (resourceId.HasValue && row == null) return Fail<LessonResourceDto>("Resource not found.", 404);
                var title = Trim(request.Title); var url = Trim(request.ExternalUrl);
                var conflict = await _resources.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.LessonId == lesson.Id && !x.IsDeleted &&
                    (x.DisplayOrder == request.DisplayOrder ||
                     (x.ExternalUrl == url && x.FileAssetId == (file == null ? (long?)null : file.Id))) &&
                    (row == null || x.Id != row.Id), token);
                if (conflict != null)
                {
                    if (row == null && conflict.DisplayOrder == request.DisplayOrder &&
                        conflict.Title == title && conflict.ExternalUrl == url &&
                        conflict.FileAssetId == (file == null ? (long?)null : file.Id))
                        return ApiResponse<LessonResourceDto>.SuccessResponse(Resource(conflict, lesson.PublicId, file), "Resource already exists.");
                    return Fail<LessonResourceDto>("Resource link or display order already exists.", 409);
                }
                if (row != null && !Version(row.RowVersion, request.RowVersion))
                    return Fail<LessonResourceDto>("Resource changed. Reload before editing.", 409);
                await LmsLessonRevisionWriter.BaselineAsync(_revisions, _lessons, _resources, Tenant, lesson.Id, _user.UserId, Now, token);
                var now = Now;
                if (row == null)
                {
                    row = new LessonResource { TenantId = Tenant, LessonId = lesson.Id,
                        CreatedAt = now, CreatedBy = _user.UserId };
                    await _resources.AddAsync(row);
                }
                else { row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _resources.Update(row); }
                row.Title = title; row.ExternalUrl = url; row.FileAssetId = file?.Id;
                row.DisplayOrder = request.DisplayOrder;
                await _uow.SaveChangesAsync(token);
                await LmsLessonRevisionWriter.AppendAsync(_revisions, _lessons, _resources, Tenant, lesson.Id, "Resource", _user.UserId, Now, token);
                return ApiResponse<LessonResourceDto>.SuccessResponse(Resource(row, lesson.PublicId, file), "Resource saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<LessonResourceDto>("Resource changed concurrently.", 409); }
        catch (DbUpdateException ex) { _logger.LogWarning(ex, "Resource conflict for tenant {Tenant}", Tenant);
            return Fail<LessonResourceDto>("Resource save conflicts with another request.", 409); }
    }

    public async Task<ApiResponse<bool>> RemoveResourceAsync(long resourceId, string rowVersion, CancellationToken ct = default)
    {
        if (!Author) return Fail<bool>("Author permission required.", 403);
        if (resourceId <= 0) return Fail<bool>("Invalid resource.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _resources.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == resourceId, token);
                if (row == null) return Fail<bool>("Resource not found.", 404);
                var lesson = await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.LessonId && !x.IsDeleted, token);
                var course = lesson == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == lesson.CourseId && !x.IsDeleted, token);
                if (course == null || !await CanEditAsync(course, token))
                    return Fail<bool>("Course author access denied.", 403);
                if (row.IsDeleted) return ApiResponse<bool>.SuccessResponse(true, "Resource already removed.");
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, token))
                    return Fail<bool>(CourseCompletionPublicationGuard.Message, 409);
                if (!Version(row.RowVersion, rowVersion))
                    return Fail<bool>("Resource changed. Reload before removing.", 409);
                await LmsLessonRevisionWriter.BaselineAsync(_revisions, _lessons, _resources, Tenant, lesson!.Id, _user.UserId, Now, token);
                row.IsDeleted = true; row.DeletedAt = Now; row.DeletedBy = _user.UserId;
                row.UpdatedAt = Now; row.UpdatedBy = _user.UserId; _resources.Update(row);
                await _uow.SaveChangesAsync(token);
                await LmsLessonRevisionWriter.AppendAsync(_revisions, _lessons, _resources, Tenant, lesson!.Id, "ResourceRemoved", _user.UserId, Now, token);
                return ApiResponse<bool>.SuccessResponse(true, "Resource removed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<bool>("Resource changed concurrently.", 409); }
        catch (DbUpdateException) { return Fail<bool>("Resource removal conflicts with existing data.", 409); }
    }
}
