using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Omr;

/// <summary>Tenant-scoped OMR evidence and independent human review. Never a grading ledger.</summary>
public sealed partial class OmrWorkflowService(EduOSDbContext db, ICurrentUserService user,
    IFileUploadService files, IAssessmentAdministrationService marks,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<OmrWorkflowService> logger)
{
    private sealed class PostDeclinedException(string message, int statusCode) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }
    private bool InTenant => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        db.CurrentTenantId == user.TenantId;
    private bool IsExamManager => InTenant && (user.IsTenantAdmin ||
        user.IsInRole("Principal") || user.IsInRole("VicePrincipal") || user.IsInRole("ExamController"));
    private bool CanCapture => InTenant && (IsExamManager || user.IsInRole("Teacher"));
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private sealed record Scope(long SubjectId, long AssessmentId, long OfferingId, long CampusId,
        long BatchId, AssessmentState State, decimal SubjectMarks, decimal ComponentMarks);

    private async Task<Scope?> ScopeAsync(long componentId, CancellationToken ct)
    {
        var t = user.TenantId;
        var row = await (from c in db.AssessmentComponents.AsNoTracking()
            join subject in db.AssessmentSubjects.AsNoTracking() on c.AssessmentSubjectId equals subject.Id
            join exam in db.Assessments.AsNoTracking() on subject.AssessmentId equals exam.Id
            join offering in db.SubjectOfferings.AsNoTracking() on subject.SubjectOfferingId equals offering.Id
            join batch in db.AcademicBatches.AsNoTracking() on offering.AcademicBatchId equals batch.Id
            where c.TenantId == t && subject.TenantId == t && exam.TenantId == t &&
                offering.TenantId == t && batch.TenantId == t && c.Id == componentId &&
                !c.IsDeleted && !subject.IsDeleted && !exam.IsDeleted && !offering.IsDeleted &&
                !batch.IsDeleted && offering.IsActive && batch.IsActive &&
                batch.CampusId == exam.CampusId && batch.AcademicYearId == exam.AcademicYearId
            select new Scope(subject.Id, exam.Id, offering.Id, exam.CampusId,
                batch.Id, exam.State, subject.FullMarks, c.FullMarks)).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        if (!user.IsTenantAdmin && (user.IsInRole("Principal") || user.IsInRole("VicePrincipal")))
        {
            if (!await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
                x.TenantId == t && x.UserId == user.UserId && x.CampusId == row.CampusId &&
                x.IsActive && !x.IsDeleted, ct)) return null;
        }
        if (!IsExamManager)
        {
            var teacher = await db.Employees.AsNoTracking().Where(x =>
                x.TenantId == t && x.UserId == user.UserId && x.CanTeach &&
                x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
            if (teacher <= 0 || !await db.InstructorAssignments.AsNoTracking().AnyAsync(x =>
                x.TenantId == t && x.SubjectOfferingId == row.OfferingId &&
                x.EmployeeId == teacher && x.IsActive && !x.IsDeleted, ct)) return null;
        }
        return row;
    }

    private async Task<bool> EligibleAsync(Scope scope, long registrationId, CancellationToken ct)
    {
        var t = user.TenantId;
        return await (from reg in db.StudentSubjectRegistrations.AsNoTracking()
            join enr in db.StudentEnrollments.AsNoTracking() on reg.StudentEnrollmentId equals enr.Id
            join student in db.Students.AsNoTracking() on enr.StudentId equals student.Id
            where reg.TenantId == t && enr.TenantId == t && student.TenantId == t &&
                reg.Id == registrationId && reg.SubjectOfferingId == scope.OfferingId &&
                reg.State == SubjectRegistrationState.Approved &&
                enr.AcademicBatchId == scope.BatchId && enr.CampusId == scope.CampusId &&
                enr.IsCurrent && enr.State == EnrollmentState.Active &&
                student.StatusCode == "Active" && !reg.IsDeleted && !enr.IsDeleted && !student.IsDeleted
            select reg.Id).AnyAsync(ct);
    }

    private async Task<bool> MarksOpenAsync(Scope scope, CancellationToken ct)
    {
        if (scope.State != AssessmentState.MarksEntry) return false;
        var t = user.TenantId;
        // The caller may have read Scope before another manager locked the exam.
        // Re-read within the writer's transaction so a stale snapshot cannot
        // authorize capture/review against an already closed assessment.
        if (!await db.Assessments.AsNoTracking().AnyAsync(x => x.TenantId == t &&
            x.Id == scope.AssessmentId && !x.IsDeleted &&
            x.State == AssessmentState.MarksEntry, ct)) return false;
        if (await db.ResultPublications.AsNoTracking().AnyAsync(x =>
            x.TenantId == t && x.AssessmentId == scope.AssessmentId && x.AcademicBatchId == scope.BatchId &&
            !x.IsDeleted && (x.State == ResultPublicationState.Published ||
                x.State == ResultPublicationState.Withdrawn), ct)) return false;
        var decision = await db.AssessmentMarkReviewEvents.AsNoTracking().Where(x =>
            x.TenantId == t && x.AssessmentSubjectId == scope.SubjectId && !x.IsDeleted)
            .OrderByDescending(x => x.Id).Select(x => (AssessmentMarkReviewDecision?)x.Decision)
            .FirstOrDefaultAsync(ct);
        return decision is not (AssessmentMarkReviewDecision.Submitted or AssessmentMarkReviewDecision.Approved);
    }

    public async Task<ApiResponse<OmrTemplateDto>> CreateTemplateAsync(
        CreateOmrTemplateRequestDto request, CancellationToken ct = default)
    {
        if (!IsExamManager) return Fail<OmrTemplateDto>("Exam management permission required.", 403);
        var key = request?.AnswerKey?.Trim().ToUpperInvariant() ?? "";
        if (request == null || request.AssessmentComponentId <= 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160 ||
            key.Length is < 1 or > OmrImageRecognition.MaxPrintableQuestions ||
            request.OptionsPerQuestion is < 4 or > 5 ||
            key.Any(ch => ch < 'A' || ch >= 'A' + request.OptionsPerQuestion) ||
            request.CorrectAnswerMarks <= 0 || request.IncorrectAnswerMarks < 0 ||
            request.CorrectAnswerMarks > 1000 || request.IncorrectAnswerMarks > 1000 ||
            decimal.Round(request.CorrectAnswerMarks, 2) != request.CorrectAnswerMarks ||
            decimal.Round(request.IncorrectAnswerMarks, 2) != request.IncorrectAnswerMarks)
            return Fail<OmrTemplateDto>("Invalid OMR answer key, options or scoring.");
        var scope = await ScopeAsync(request.AssessmentComponentId, ct);
        if (scope == null) return Fail<OmrTemplateDto>("Assessment component not found.", 404);
        if (scope.State is AssessmentState.Locked or AssessmentState.Published or AssessmentState.Cancelled ||
            request.CorrectAnswerMarks * key.Length > scope.ComponentMarks)
            return Fail<OmrTemplateDto>("Template exceeds component marks or assessment is closed.", 409);
        var row = new OmrTemplate
        {
            TenantId = user.TenantId, AssessmentComponentId = request.AssessmentComponentId,
            Name = request.Name.Trim(), AnswerKey = key,
            OptionsPerQuestion = request.OptionsPerQuestion,
            CorrectAnswerMarks = request.CorrectAnswerMarks,
            IncorrectAnswerMarks = request.IncorrectAnswerMarks,
            SealedAtUtc = DateTime.UtcNow, SealedByUserId = user.UserId, CreatedBy = user.UserId
        };
        db.OmrTemplates.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "OMR template registration conflict for tenant {Tenant}", user.TenantId);
            return Fail<OmrTemplateDto>("OMR template could not be saved.", 409);
        }
        return ApiResponse<OmrTemplateDto>.SuccessResponse(Map(row));
    }

    public async Task<ApiResponse<IReadOnlyList<OmrTemplateDto>>> TemplatesAsync(long componentId,
        CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<IReadOnlyList<OmrTemplateDto>>("Assessment permission required.", 403);
        if (await ScopeAsync(componentId, ct) == null)
            return Fail<IReadOnlyList<OmrTemplateDto>>("Assessment component not found.", 404);
        var items = await db.OmrTemplates.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.AssessmentComponentId == componentId && !x.IsDeleted)
            .OrderByDescending(x => x.SealedAtUtc).Take(100).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<OmrTemplateDto>>.SuccessResponse(items.Select(Map).ToList());
    }

    public async Task<ApiResponse<string>> PrintTemplateAsync(Guid reference, CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<string>("Assessment permission required.", 403);
        var form = await db.OmrTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (form == null || await ScopeAsync(form.AssessmentComponentId, ct) == null)
            return Fail<string>("OMR form not found.", 404);
        return ApiResponse<string>.SuccessResponse(OmrImageRecognition.PrintableSvg(form.PublicId,
            form.AnswerKey.Length, form.OptionsPerQuestion));
    }

    private static OmrTemplateDto Map(OmrTemplate row) => new()
    {
        Reference = row.PublicId, AssessmentComponentId = row.AssessmentComponentId,
        Name = row.Name, QuestionCount = row.AnswerKey.Length,
        OptionsPerQuestion = row.OptionsPerQuestion,
        CorrectAnswerMarks = row.CorrectAnswerMarks,
        IncorrectAnswerMarks = row.IncorrectAnswerMarks, SealedAtUtc = row.SealedAtUtc
    };

    private static decimal Proposed(OmrTemplate form, string answers)
    {
        var correct = 0;
        var incorrect = 0;
        for (var i = 0; i < form.AnswerKey.Length; i++)
        {
            if (answers[i] == '-') continue;
            if (answers[i] == form.AnswerKey[i]) correct++;
            else incorrect++;
        }
        return Math.Max(0, decimal.Round(correct * form.CorrectAnswerMarks -
            incorrect * form.IncorrectAnswerMarks, 2, MidpointRounding.AwayFromZero));
    }

    private static OmrSheetDto Map(OmrSheet row, OmrTemplate form, Guid fileReference) => new()
    {
        Reference = row.PublicId, TemplateReference = form.PublicId,
        FileReference = fileReference, StudentSubjectRegistrationId = row.StudentSubjectRegistrationId,
        SuggestedAnswers = row.SuggestedAnswers, SuggestionStates = row.SuggestionStates,
        ConfirmedAnswers = row.ConfirmedAnswers, State = row.State,
        UncertainCount = row.UncertainCount,
        ProposedMarks = row.State is "Reviewed" or "Posted" ? Proposed(form, row.ConfirmedAnswers) : null,
        CapturedByUserId = row.CapturedByUserId, ReviewedByUserId = row.ReviewedByUserId,
        PostedByUserId = row.PostedByUserId,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<OmrSheetDto>> CaptureAsync(CaptureOmrSheetRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<OmrSheetDto>("Assessment permission required.", 403);
        if (request == null || request.RequestId == Guid.Empty || request.TemplateReference == Guid.Empty ||
            request.FileReference == Guid.Empty || request.StudentSubjectRegistrationId <= 0)
            return Fail<OmrSheetDto>("Invalid OMR capture request.");
        var tenant = user.TenantId;
        var form = await db.OmrTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.TemplateReference && !x.IsDeleted, ct);
        if (form == null) return Fail<OmrSheetDto>("OMR template not found.", 404);
        var scope = await ScopeAsync(form.AssessmentComponentId, ct);
        if (scope == null || !await EligibleAsync(scope, request.StudentSubjectRegistrationId, ct))
            return Fail<OmrSheetDto>("Student or component not found in authorized scope.", 404);
        var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.FileReference && !x.IsDeleted &&
            x.UploadedByUserId == user.UserId && x.IsVerifiedSafe &&
            x.MalwareScanStateCode == "Clean" && x.StorageProvider == "LocalPrivate" &&
            x.Visibility == FileVisibility.Private &&
            (x.ContentType == "image/png" || x.ContentType == "image/jpeg"), ct);
        if (asset == null || !asset.StorageKey.StartsWith($"tenant-{tenant}/omr/", StringComparison.Ordinal) ||
            asset.StorageKey.Contains("..") || asset.StorageKey.Contains('\\') ||
            asset.StorageKey.Contains("//"))
            return Fail<OmrSheetDto>("Private OMR image not found.", 404);

        var previous = await db.OmrSheets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.ClientRequestId == request.RequestId && !x.IsDeleted, ct);
        if (previous != null)
            return previous.OmrTemplateId == form.Id && previous.FileAssetId == asset.Id &&
                previous.StudentSubjectRegistrationId == request.StudentSubjectRegistrationId
                ? ApiResponse<OmrSheetDto>.SuccessResponse(Map(previous, form, asset.PublicId))
                : Fail<OmrSheetDto>("Capture request ID was already used.", 409);

        FileDownloadResult? downloaded;
        try { downloaded = await files.GetPrivateFileAsync(asset.StorageKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Private OMR image unavailable for tenant {Tenant}", tenant);
            return Fail<OmrSheetDto>("OMR image temporarily unavailable.", 503);
        }
        if (downloaded?.Content is not { Length: > 0 } ||
            downloaded.Content.Length != asset.SizeBytes ||
            Convert.ToHexString(SHA256.HashData(downloaded.Content)) != asset.Sha256)
            return Fail<OmrSheetDto>("OMR image failed integrity verification.", 409);
        OmrRecognitionResult recognition;
        try { recognition = OmrImageRecognition.Read(downloaded.Content,
            form.AnswerKey.Length, form.OptionsPerQuestion); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return Fail<OmrSheetDto>("OMR image could not be read reliably. Recapture using the printed template.", 422);
        }
        var suggestion = new string(recognition.Questions.Select(x => x.SuggestedOption ?? '?').ToArray());
        var states = new string(recognition.Questions.Select(x => x.State switch
        {
            OmrSuggestionState.Single => 'S', OmrSuggestionState.Blank => 'B',
            OmrSuggestionState.Ambiguous => 'A', _ => 'L'
        }).ToArray());
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await MarksOpenAsync(scope, token))
                    return Fail<OmrSheetDto>("Assessment marks are locked or published.", 409);
                if (!await db.FileAssets.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == asset.Id && !x.IsDeleted &&
                    x.StorageKey == asset.StorageKey && x.Sha256 == asset.Sha256 &&
                    x.SizeBytes == asset.SizeBytes && x.UploadedByUserId == user.UserId &&
                    x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean", token))
                    return Fail<OmrSheetDto>("OMR image verification changed; upload again.", 409);
                if (await db.OmrSheets.AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                    (x.FileAssetId == asset.Id || (x.OmrTemplateId == form.Id &&
                    x.StudentSubjectRegistrationId == request.StudentSubjectRegistrationId)), token))
                    return Fail<OmrSheetDto>("Duplicate OMR sheet or image.", 409);
                var row = new OmrSheet
                {
                    TenantId = tenant, ClientRequestId = request.RequestId,
                    OmrTemplateId = form.Id, StudentSubjectRegistrationId = request.StudentSubjectRegistrationId,
                    FileAssetId = asset.Id, SuggestedAnswers = suggestion, SuggestionStates = states,
                    UncertainCount = recognition.UncertainCount, CapturedByUserId = user.UserId,
                    CreatedBy = user.UserId
                };
                db.OmrSheets.Add(row);
                await db.SaveChangesAsync(token);
                return ApiResponse<OmrSheetDto>.SuccessResponse(Map(row, form, asset.PublicId));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent OMR sheet capture tenant {Tenant}", tenant);
            return Fail<OmrSheetDto>("OMR capture conflicted with an existing sheet.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "OMR capture database contention tenant {Tenant}", tenant);
            return Fail<OmrSheetDto>("OMR capture was busy; reload and retry with the same request ID.", 409);
        }
    }

    public async Task<ApiResponse<OmrSheetDto>> GetAsync(Guid reference, CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<OmrSheetDto>("Assessment permission required.", 403);
        var row = await (from sheet in db.OmrSheets.AsNoTracking()
            join form in db.OmrTemplates.AsNoTracking() on sheet.OmrTemplateId equals form.Id
            join file in db.FileAssets.AsNoTracking() on sheet.FileAssetId equals file.Id
            where sheet.TenantId == user.TenantId && form.TenantId == user.TenantId &&
                file.TenantId == user.TenantId && sheet.PublicId == reference &&
                !sheet.IsDeleted && !form.IsDeleted && !file.IsDeleted
            select new { sheet, form, file.PublicId }).FirstOrDefaultAsync(ct);
        if (row == null || await ScopeAsync(row.form.AssessmentComponentId, ct) == null)
            return Fail<OmrSheetDto>("OMR sheet not found.", 404);
        return ApiResponse<OmrSheetDto>.SuccessResponse(Map(row.sheet, row.form, row.PublicId));
    }

    public async Task<ApiResponse<OmrSheetPageDto>> SearchAsync(Guid template, int page = 1,
        int pageSize = 30, CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<OmrSheetPageDto>("Assessment permission required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return Fail<OmrSheetPageDto>("Invalid pagination.");
        var form = await db.OmrTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == template && !x.IsDeleted, ct);
        if (form == null || await ScopeAsync(form.AssessmentComponentId, ct) == null)
            return Fail<OmrSheetPageDto>("OMR template not found.", 404);
        var q = db.OmrSheets.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.OmrTemplateId == form.Id && !x.IsDeleted);
        var count = await q.CountAsync(ct);
        var rows = await (from sheet in q.OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
            join file in db.FileAssets.AsNoTracking() on sheet.FileAssetId equals file.Id
            where file.TenantId == user.TenantId && !file.IsDeleted
            select new { sheet, file.PublicId }).ToListAsync(ct);
        return ApiResponse<OmrSheetPageDto>.SuccessResponse(new OmrSheetPageDto
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => Map(x.sheet, form, x.PublicId)).ToList()
        });
    }

    public async Task<ApiResponse<OmrSheetDto>> ReviewAsync(Guid reference,
        ReviewOmrSheetRequestDto request, CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<OmrSheetDto>("Assessment permission required.", 403);
        if (request == null || request.RequestId == Guid.Empty || !request.IdentityConfirmed ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.Reason?.Length > 500)
            return Fail<OmrSheetDto>("Confirm the printed form and student identity before review.");
        var existing = await db.OmrSheets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (existing == null) return Fail<OmrSheetDto>("OMR sheet not found.", 404);
        var form = await db.OmrTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == existing.OmrTemplateId && !x.IsDeleted, ct);
        if (form == null) return Fail<OmrSheetDto>("OMR template not found.", 404);
        var scope = await ScopeAsync(form.AssessmentComponentId, ct);
        if (scope == null) return Fail<OmrSheetDto>("OMR sheet not found.", 404);
        var answers = request.ConfirmedAnswers?.Trim().ToUpperInvariant() ?? "";
        if (answers.Length != form.AnswerKey.Length ||
            answers.Any(ch => ch != '-' && (ch < 'A' || ch >= 'A' + form.OptionsPerQuestion)))
            return Fail<OmrSheetDto>("Verify every answer. Use '-' for a blank choice.");
        if (existing.CapturedByUserId == user.UserId)
            return Fail<OmrSheetDto>("An independent reviewer is required.", 403);
        if (answers != existing.SuggestedAnswers.Replace('?', '-') &&
            string.IsNullOrWhiteSpace(request.Reason))
            return Fail<OmrSheetDto>("A correction reason is required.");
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return Fail<OmrSheetDto>("Invalid row version."); }
        var retry = await db.OmrSheetEvents.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId && !x.IsDeleted, ct);
        if (retry != null)
        {
            var fileId = await db.FileAssets.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.Id == existing.FileAssetId && !x.IsDeleted).Select(x => x.PublicId).FirstOrDefaultAsync(ct);
            return retry.OmrSheetId == existing.Id && retry.Action == "Reviewed" &&
                retry.AfterAnswers == answers && retry.ActorUserId == user.UserId && fileId != Guid.Empty
                ? ApiResponse<OmrSheetDto>.SuccessResponse(Map(existing, form, fileId))
                : Fail<OmrSheetDto>("Review request ID already used.", 409);
        }
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(
            expected.Length == existing.RowVersion.Length ? expected : new byte[existing.RowVersion.Length],
            existing.RowVersion)) return Fail<OmrSheetDto>("Sheet changed. Reload before reviewing.", 409);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var previous = await db.OmrSheetEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId && !x.IsDeleted, token);
                if (previous != null)
                {
                    var duplicateFile = await db.FileAssets.AsNoTracking().Where(x =>
                        x.TenantId == user.TenantId && x.Id == existing.FileAssetId && !x.IsDeleted)
                        .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                    return previous.OmrSheetId == existing.Id && previous.Action == "Reviewed" &&
                        previous.AfterAnswers == answers && previous.ActorUserId == user.UserId &&
                        duplicateFile != Guid.Empty
                        ? ApiResponse<OmrSheetDto>.SuccessResponse(Map(existing, form, duplicateFile))
                        : Fail<OmrSheetDto>("Review request ID already used.", 409);
                }
                if (!await MarksOpenAsync(scope, token))
                    return Fail<OmrSheetDto>("Assessment is locked or published.", 409);
                var row = await db.OmrSheets.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == existing.Id && !x.IsDeleted, token);
                if (row == null || row.State != "Captured" || row.CapturedByUserId == user.UserId)
                    return Fail<OmrSheetDto>("Review transition is not available.", 409);
                db.Entry(row).Property(x => x.RowVersion).OriginalValue = expected;
                var now = DateTime.UtcNow;
                row.State = "Reviewed"; row.ConfirmedAnswers = answers;
                row.ReviewedByUserId = user.UserId; row.ReviewedAtUtc = now;
                row.UpdatedBy = user.UserId; row.UpdatedAt = now;
                db.OmrSheetEvents.Add(new OmrSheetEvent
                {
                    TenantId = user.TenantId, OmrSheetId = row.Id, ClientRequestId = request.RequestId,
                    Action = "Reviewed", BeforeAnswers = row.SuggestedAnswers,
                    AfterAnswers = answers, Reason = request.Reason?.Trim() ?? "",
                    ActorUserId = user.UserId, OccurredAtUtc = now, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                var fileReference = await db.FileAssets.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.FileAssetId && !x.IsDeleted)
                    .Select(x => x.PublicId).FirstAsync(token);
                return ApiResponse<OmrSheetDto>.SuccessResponse(Map(row, form, fileReference));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<OmrSheetDto>("Sheet changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "OMR review conflict tenant {Tenant}", user.TenantId);
            return Fail<OmrSheetDto>("OMR review conflicted; reload and retry.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "OMR review database contention tenant {Tenant}", user.TenantId);
            return Fail<OmrSheetDto>("OMR review was busy; reload and retry with the same request ID.", 409);
        }
    }

    /// <summary>Posts an independently reviewed sheet through the canonical subject and component
    /// marks writers in a single serializable transaction. No master mark or manually entered
    /// component is silently overwritten.</summary>
    public async Task<ApiResponse<OmrSheetDto>> PostReviewedAsync(Guid reference,
        PostOmrSheetRequestDto request, CancellationToken ct = default)
    {
        if (!IsExamManager) return Fail<OmrSheetDto>("Exam management permission required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return Fail<OmrSheetDto>("Invalid posting request.");
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return Fail<OmrSheetDto>("Invalid row version."); }
        if (expected.Length != 8) return Fail<OmrSheetDto>("Invalid row version.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await db.OmrSheets.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.PublicId == reference && !x.IsDeleted, token);
                if (row == null) return Fail<OmrSheetDto>("OMR sheet not found.", 404);
                var form = await db.OmrTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == row.OmrTemplateId && !x.IsDeleted, token);
                var fileReference = await db.FileAssets.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.Id == row.FileAssetId && !x.IsDeleted)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                if (form == null || fileReference == Guid.Empty) return Fail<OmrSheetDto>("OMR evidence missing.", 404);
                var scope = await ScopeAsync(form.AssessmentComponentId, token);
                if (scope == null || !await EligibleAsync(scope, row.StudentSubjectRegistrationId, token))
                    return Fail<OmrSheetDto>("Student or assessment permission denied.", 403);
                var prior = await db.OmrSheetEvents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId && !x.IsDeleted, token);
                if (prior != null)
                    return prior.OmrSheetId == row.Id && prior.Action == "Posted" &&
                        prior.ActorUserId == user.UserId && row.State == "Posted"
                        ? ApiResponse<OmrSheetDto>.SuccessResponse(Map(row, form, fileReference))
                        : Fail<OmrSheetDto>("Posting request ID already used.", 409);
                if (row.State != "Reviewed" || !row.ReviewedByUserId.HasValue ||
                    row.ReviewedByUserId == row.CapturedByUserId ||
                    !CryptographicOperations.FixedTimeEquals(expected, row.RowVersion) ||
                    !await MarksOpenAsync(scope, token))
                    return Fail<OmrSheetDto>("Sheet changed or assessment marks are locked.", 409);
                var component = await db.AssessmentComponents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == form.AssessmentComponentId &&
                    !x.IsDeleted, token);
                if (component == null || row.ConfirmedAnswers.Length != form.AnswerKey.Length)
                    return Fail<OmrSheetDto>("OMR scoring configuration is incomplete.", 409);
                var score = Proposed(form, row.ConfirmedAnswers);
                if (score > component.FullMarks) return Fail<OmrSheetDto>("OMR score exceeds component marks.", 409);
                var master = await db.StudentAssessmentMarks.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.AssessmentSubjectId == scope.SubjectId &&
                    x.StudentSubjectRegistrationId == row.StudentSubjectRegistrationId && !x.IsDeleted, token);
                List<StudentAssessmentComponentMark> previousComponents = master == null ? new() : await db.StudentAssessmentComponentMarks
                    .AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                        x.StudentAssessmentMarkId == master.Id && !x.IsDeleted).ToListAsync(token);
                if (previousComponents.Any(x => x.AssessmentComponentId == component.Id))
                    return Fail<OmrSheetDto>("This component already has marks. Use the audited marks-amendment workflow.", 409);
                if (master != null && (master.IsAbsent || master.IsWithheld ||
                    previousComponents.Any(x => x.IsAbsent || x.IsWithheld) ||
                    master.ObtainedMarks != previousComponents.Sum(x => x.ObtainedMarks)))
                    return Fail<OmrSheetDto>("Manual or unreconciled subject marks exist; do not overwrite them.", 409);
                var total = previousComponents.Sum(x => x.ObtainedMarks) + score;
                if (total > scope.SubjectMarks)
                    return Fail<OmrSheetDto>("Component totals exceed subject full marks.", 409);
                var saved = await marks.SaveMarksRegisterAsync(new SaveMarksRegisterRequestDto
                {
                    AssessmentSubjectId = scope.SubjectId,
                    Marks = [new SaveStudentAssessmentMarkRequestDto
                    {
                        StudentSubjectRegistrationId = row.StudentSubjectRegistrationId,
                        ObtainedMarks = total, RowVersion = master == null ? null :
                            Convert.ToBase64String(master.RowVersion)
                    }]
                }, token);
                if (!saved.Success) throw new PostDeclinedException(saved.Message, saved.StatusCode);
                var masterId = await db.StudentAssessmentMarks.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.AssessmentSubjectId == scope.SubjectId &&
                    x.StudentSubjectRegistrationId == row.StudentSubjectRegistrationId && !x.IsDeleted)
                    .Select(x => x.Id).FirstAsync(token);
                var componentSaved = await marks.SaveComponentMarkAsync(null,
                    new SaveStudentAssessmentComponentMarkRequestDto
                    {
                        AssessmentComponentId = component.Id,
                        StudentAssessmentMarkId = masterId, ObtainedMarks = score
                    }, token);
                if (!componentSaved.Success)
                    throw new PostDeclinedException(componentSaved.Message, componentSaved.StatusCode);
                db.Entry(row).Property(x => x.RowVersion).OriginalValue = expected;
                var now = DateTime.UtcNow;
                row.State = "Posted"; row.PostedAtUtc = now; row.PostedByUserId = user.UserId;
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                db.OmrSheetEvents.Add(new OmrSheetEvent
                {
                    TenantId = user.TenantId, OmrSheetId = row.Id, ClientRequestId = request.RequestId,
                    Action = "Posted", BeforeAnswers = row.ConfirmedAnswers,
                    AfterAnswers = row.ConfirmedAnswers, Reason = "Canonical subject/component marks posting",
                    ActorUserId = user.UserId, OccurredAtUtc = now, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<OmrSheetDto>.SuccessResponse(Map(row, form, fileReference));
            }, IsolationLevel.Serializable, ct);
        }
        catch (PostDeclinedException ex) { return Fail<OmrSheetDto>(ex.Message, ex.StatusCode); }
        catch (DbUpdateConcurrencyException) { return Fail<OmrSheetDto>("OMR sheet changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "OMR canonical marks posting conflict tenant {Tenant}", user.TenantId);
            return Fail<OmrSheetDto>("Marks posting conflicted; reload and retry.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            logger.LogWarning(ex, "OMR posting database contention tenant {Tenant}", user.TenantId);
            return Fail<OmrSheetDto>("Marks posting was busy; reload and retry with the same request ID.", 409);
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid reference,
        CancellationToken ct = default)
    {
        if (!CanCapture) return Fail<DocumentFileDownloadDto>("Assessment permission required.", 403);
        var row = await (from sheet in db.OmrSheets.AsNoTracking()
            join form in db.OmrTemplates.AsNoTracking() on sheet.OmrTemplateId equals form.Id
            join file in db.FileAssets.AsNoTracking() on sheet.FileAssetId equals file.Id
            where sheet.TenantId == user.TenantId && form.TenantId == user.TenantId &&
                file.TenantId == user.TenantId && sheet.PublicId == reference &&
                !sheet.IsDeleted && !form.IsDeleted && !file.IsDeleted &&
                file.IsVerifiedSafe && file.MalwareScanStateCode == "Clean"
            select new { form.AssessmentComponentId, file.StorageKey, file.OriginalFileName,
                file.ContentType, file.SizeBytes, file.Sha256 }).FirstOrDefaultAsync(ct);
        if (row == null || await ScopeAsync(row.AssessmentComponentId, ct) == null ||
            !row.StorageKey.StartsWith($"tenant-{user.TenantId}/omr/", StringComparison.Ordinal) ||
            row.StorageKey.Contains("..") || row.StorageKey.Contains('\\') || row.StorageKey.Contains("//"))
            return Fail<DocumentFileDownloadDto>("OMR image not found.", 404);
        FileDownloadResult? downloadedFile;
        try { downloadedFile = await files.GetPrivateFileAsync(row.StorageKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Private OMR image unavailable tenant {Tenant}", user.TenantId);
            return Fail<DocumentFileDownloadDto>("Private OMR image unavailable.", 503);
        }
        if (downloadedFile == null || downloadedFile.Content.Length != row.SizeBytes ||
            Convert.ToHexString(SHA256.HashData(downloadedFile.Content)) != row.Sha256)
            return Fail<DocumentFileDownloadDto>("OMR image integrity failed.", 409);
        if (!await db.OmrSheets.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.PublicId == reference && !x.IsDeleted && db.FileAssets.Any(f =>
                f.TenantId == user.TenantId && f.Id == x.FileAssetId && !f.IsDeleted &&
                f.IsVerifiedSafe && f.MalwareScanStateCode == "Clean" &&
                f.StorageKey == row.StorageKey && f.Sha256 == row.Sha256 &&
                f.SizeBytes == row.SizeBytes), ct))
            return Fail<DocumentFileDownloadDto>("OMR evidence changed; reload before downloading.", 409);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = downloadedFile.Content, ContentType = row.ContentType, FileName = row.OriginalFileName
        });
    }
}
