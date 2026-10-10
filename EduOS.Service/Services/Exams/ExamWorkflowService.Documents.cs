using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EduOS.Service.Services.Exams;

public sealed partial class ExamWorkflowService
{
    public Task<ApiResponse<AssessmentComponentDto>> SaveAssessmentComponentAsync(
        long? componentId, SaveAssessmentComponentRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<AssessmentComponentDto>("Assessment management permission required.", 403));
        if (componentId is <= 0 || request == null || request.AssessmentSubjectId <= 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            request.FullMarks <= 0 || request.PassMarks < 0 ||
            request.PassMarks > request.FullMarks ||
            request.Weightage is <= 0 or > 100 || request.DisplayOrder < 0 ||
            !Exact2(request.FullMarks) || !Exact2(request.PassMarks) || !Exact2(request.Weightage))
            return Task.FromResult(Error<AssessmentComponentDto>("Assessment component marks or weight invalid."));
        return WriteAsync("assessment component", async token =>
        {
            var tenant = _user.TenantId;
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AssessmentSubjectId && !x.IsDeleted, token);
            var exam = subject == null ? null : await _assessments.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == subject.AssessmentId &&
                    !x.IsDeleted, token);
            if (exam == null || exam.State != AssessmentState.Draft)
                return Error<AssessmentComponentDto>("Components can only be edited in Draft state.", 409);
            var old = componentId.HasValue ? await _components.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == componentId.Value &&
                x.AssessmentSubjectId == subject!.Id && !x.IsDeleted, token) : null;
            if (componentId.HasValue && old == null)
                return Error<AssessmentComponentDto>("Assessment component not found.", 404);
            if (old != null && !Matches(old.RowVersion, request.RowVersion))
                return Error<AssessmentComponentDto>("Component was changed; reload.", 409);
            if (old != null && await _componentMarks.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AssessmentComponentId == old.Id && !x.IsDeleted, token))
                return Error<AssessmentComponentDto>("Component with existing marks requires an audited amendment.", 409);
            var siblings = await _components.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.AssessmentSubjectId == subject!.Id &&
                !x.IsDeleted && (!componentId.HasValue || x.Id != componentId))
                .Select(x => new { x.Code, x.Weightage }).ToListAsync(token);
            if (siblings.Any(x => x.Code == request.Code.Trim().ToUpperInvariant()) ||
                siblings.Sum(x => x.Weightage) + request.Weightage > 100m)
                return Error<AssessmentComponentDto>("Duplicate component code or combined weight exceeds 100%.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = old ?? new AssessmentComponent
            {
                TenantId = tenant, AssessmentSubjectId = subject.Id,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            row.Name = request.Name.Trim(); row.Code = request.Code.Trim().ToUpperInvariant();
            row.FullMarks = request.FullMarks; row.PassMarks = request.PassMarks;
            row.Weightage = request.Weightage; row.IsMandatoryPass = request.IsMandatoryPass;
            row.DisplayOrder = request.DisplayOrder;
            if (old == null) await _components.AddAsync(row);
            else
            {
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _components.Update(row);
            }
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AssessmentComponentDto>.SuccessResponse(MapComponent(row),
                "Assessment component saved.");
        }, ct);
    }

    public Task<ApiResponse<StudentAssessmentComponentMarkDto>> SaveComponentMarkAsync(long? componentMarkId,
        SaveStudentAssessmentComponentMarkRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Task.FromResult(Error<StudentAssessmentComponentMarkDto>("Marks access denied.", 403));
        if (componentMarkId is <= 0 || request == null || request.AssessmentComponentId <= 0 ||
            request.StudentAssessmentMarkId <= 0 || request.ObtainedMarks < 0m ||
            !Exact2(request.ObtainedMarks) ||
            (request.IsAbsent || request.IsWithheld) && request.ObtainedMarks != 0m)
            return Task.FromResult(Error<StudentAssessmentComponentMarkDto>("Invalid component mark."));
        return WriteAsync("assessment component marks", async token =>
        {
            var tenant = _user.TenantId;
            var component = await _components.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AssessmentComponentId && !x.IsDeleted, token);
            var master = await _marks.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.StudentAssessmentMarkId && !x.IsDeleted, token);
            if (component == null || master == null ||
                component.AssessmentSubjectId != master.AssessmentSubjectId)
                return Error<StudentAssessmentComponentMarkDto>("Component and student mark do not match.", 409);
            var subject = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == component.AssessmentSubjectId && !x.IsDeleted, token);
            var exam = subject == null ? null : await _assessments.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == subject.AssessmentId &&
                    !x.IsDeleted, token);
            if (exam == null || exam.State != AssessmentState.MarksEntry)
                return Error<StudentAssessmentComponentMarkDto>("Assessment is not open for marks entry.", 409);
            if (!CanPublish() && !await CanEditOfferingAsync(subject!.SubjectOfferingId, token))
                return Error<StudentAssessmentComponentMarkDto>("Not assigned to this subject.", 403);
            if (request.ObtainedMarks > component.FullMarks)
                return Error<StudentAssessmentComponentMarkDto>("Mark exceeds component full marks.", 409);
            var row = componentMarkId.HasValue ? await _componentMarks.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == componentMarkId.Value && !x.IsDeleted &&
                x.AssessmentComponentId == component.Id && x.StudentAssessmentMarkId == master.Id, token) : null;
            if (componentMarkId.HasValue && row == null)
                return Error<StudentAssessmentComponentMarkDto>("Component mark not found.", 404);
            if (row != null && !Matches(row.RowVersion, request.RowVersion))
                return Error<StudentAssessmentComponentMarkDto>("Component mark changed.", 409);
            if (row == null && await _componentMarks.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AssessmentComponentId == component.Id &&
                x.StudentAssessmentMarkId == master.Id && !x.IsDeleted, token))
                return Error<StudentAssessmentComponentMarkDto>("Component mark already recorded.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row ??= new StudentAssessmentComponentMark
            {
                TenantId = tenant, AssessmentComponentId = component.Id,
                StudentAssessmentMarkId = master.Id, CreatedAt = now, CreatedBy = _user.UserId
            };
            row.ObtainedMarks = request.ObtainedMarks;
            row.IsAbsent = request.IsAbsent; row.IsWithheld = request.IsWithheld;
            row.EnteredAt = now; row.EnteredByUserId = _user.UserId;
            if (componentMarkId.HasValue)
            {
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                _componentMarks.Update(row);
            }
            else await _componentMarks.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<StudentAssessmentComponentMarkDto>.SuccessResponse(MapComponentMark(row),
                "Component mark saved.");
        }, ct);
    }

    public Task<ApiResponse<ResultPublicationDto>> WithdrawResultPublicationAsync(long publicationId,
        WithdrawResultPublicationRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<ResultPublicationDto>("Publication management denied.", 403));
        if (publicationId <= 0 || !ValidReason(request?.Reason) ||
            !TryVersion(request?.RowVersion, out _))
            return Task.FromResult(Error<ResultPublicationDto>("Withdrawal reason and version required."));
        return WriteAsync("withdraw result publication", async token =>
        {
            var tenant = _user.TenantId;
            var row = await _publications.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == publicationId && !x.IsDeleted, token);
            if (row == null) return Error<ResultPublicationDto>("Publication not found.", 404);
            if (!Matches(row.RowVersion, request!.RowVersion))
                return Error<ResultPublicationDto>("Publication changed; reload.", 409);
            if (row.State != ResultPublicationState.Published)
                return Error<ResultPublicationDto>("Only published results may be withdrawn.", 409);
            var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.AssessmentId && !x.IsDeleted, token);
            var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == row.AcademicBatchId && !x.IsDeleted, token);
            if (exam == null || batch == null)
                return Error<ResultPublicationDto>("Publication scope is no longer valid.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.State = ResultPublicationState.Withdrawn;
            row.VisibleToStudent = false; row.VisibleToGuardian = false;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _publications.Update(row);
            await RecordAuditAsync("Withdraw", "ResultPublication", row.Id, request.Reason, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<ResultPublicationDto>.SuccessResponse(
                MapPublication(row, exam.PublicId, batch.Name), "Published result withdrawn.");
        }, ct);
    }

    public Task<ApiResponse<CertificateTemplateDto>> SaveCertificateTemplateAsync(long? templateId,
        SaveCertificateTemplateRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<CertificateTemplateDto>("Certificate management denied.", 403));
        if (templateId is <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Trim().Length > 50 || string.IsNullOrWhiteSpace(request.HtmlTemplate) ||
            request.HtmlTemplate.Length > 50000 ||
            request.HtmlTemplate.Contains('<') || request.HtmlTemplate.Contains('>'))
            return Task.FromResult(Error<CertificateTemplateDto>(
                "Invalid template. Only escaped text and merge placeholders are accepted."));
        return WriteAsync("certificate template", async token =>
        {
            var tenant = _user.TenantId;
            var code = request.Code.Trim().ToUpperInvariant();
            if (await _templates.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Code == code && !x.IsDeleted &&
                (!templateId.HasValue || x.Id != templateId), token))
                return Error<CertificateTemplateDto>("Template code exists.", 409);
            var row = templateId.HasValue ? await _templates.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == templateId && !x.IsDeleted, token) : null;
            if (templateId.HasValue && row == null)
                return Error<CertificateTemplateDto>("Template not found.", 404);
            if (row != null && !Matches(row.RowVersion, request.RowVersion))
                return Error<CertificateTemplateDto>("Template changed.", 409);
            if (row != null && await _certificates.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.CertificateTemplateId == row.Id &&
                    !x.IsDeleted, token))
                return Error<CertificateTemplateDto>("Issued certificate templates cannot be modified.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (request.IsDefault)
            {
                var currentDefaults = await _templates.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.IsDefault && !x.IsDeleted &&
                    (!templateId.HasValue || x.Id != templateId)).ToListAsync(token);
                foreach (var template in currentDefaults)
                {
                    template.IsDefault = false; template.UpdatedAt = now;
                    template.UpdatedBy = _user.UserId; _templates.Update(template);
                }
            }
            row ??= new CertificateTemplate
            {
                TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId
            };
            row.Name = request.Name.Trim(); row.Code = code;
            row.HtmlTemplate = request.HtmlTemplate;
            row.IsDefault = request.IsDefault; row.IsActive = request.IsActive;
            if (templateId.HasValue)
            {
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _templates.Update(row);
            }
            else await _templates.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<CertificateTemplateDto>.SuccessResponse(MapTemplate(row),
                "Certificate template saved.");
        }, ct);
    }

    public Task<ApiResponse<CertificateIssueDto>> IssueCertificateAsync(
        IssueCertificateRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<CertificateIssueDto>("Certificate issue permission required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.CertificateTemplateId <= 0 || request.StudentReference == Guid.Empty ||
            request.StudentEnrollmentReference == Guid.Empty || request.IssueDate == default)
            return Task.FromResult(Error<CertificateIssueDto>("Invalid certificate request."));
        return WriteAsync("issue certificate", async token =>
        {
            var tenant = _user.TenantId;
            var replay = await _certificates.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                var studentRef = await _students.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Id == replay.StudentId)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                if (replay.CertificateTemplateId != request.CertificateTemplateId ||
                    studentRef != request.StudentReference || replay.IssueDate != request.IssueDate)
                    return Error<CertificateIssueDto>("Request ID used for a different certificate.", 409);
                return ApiResponse<CertificateIssueDto>.SuccessResponse(
                    await MapCertificateAsync(replay, token), "Certificate already issued.");
            }
            var template = await _templates.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.CertificateTemplateId &&
                x.IsActive && !x.IsDeleted, token);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && !x.IsDeleted, token);
            var enrollment = student == null ? null : await _enrollments.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant &&
                    x.PublicId == request.StudentEnrollmentReference && x.StudentId == student.Id &&
                    !x.IsDeleted, token);
            if (template == null || enrollment == null)
                return Error<CertificateIssueDto>("Certificate template or student enrollment not found.", 404);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new CertificateIssue
            {
                TenantId = tenant, PublicId = request.ClientRequestId, StudentId = student!.Id,
                StudentEnrollmentId = enrollment.Id, CertificateTemplateId = template.Id,
                CertificateNumber = "CERT-" + request.ClientRequestId.ToString("N").ToUpperInvariant(),
                IssueDate = request.IssueDate, IssuedByUserId = _user.UserId,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _certificates.AddAsync(row);
            await RecordAuditAsync("Issue", "CertificateIssue", null,
                row.CertificateNumber, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<CertificateIssueDto>.SuccessResponse(
                await MapCertificateAsync(row, token), "Certificate issued.");
        }, ct);
    }

    public Task<ApiResponse<CertificateIssueDto>> RevokeCertificateAsync(long certificateId,
        RevokeCertificateRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<CertificateIssueDto>("Certificate revoke permission required.", 403));
        if (certificateId <= 0 || !ValidReason(request?.Reason) || !TryVersion(request?.RowVersion, out _))
            return Task.FromResult(Error<CertificateIssueDto>("Revocation reason and version required."));
        return WriteAsync("revoke certificate", async token =>
        {
            var row = await _certificates.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == certificateId && !x.IsDeleted, token);
            if (row == null) return Error<CertificateIssueDto>("Certificate not found.", 404);
            if (!Matches(row.RowVersion, request!.RowVersion))
                return Error<CertificateIssueDto>("Certificate changed; reload.", 409);
            if (row.IsRevoked) return Error<CertificateIssueDto>("Certificate already revoked.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.IsRevoked = true; row.RevokedAt = now;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _certificates.Update(row);
            await RecordAuditAsync("Revoke", "CertificateIssue", row.Id, request.Reason, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<CertificateIssueDto>.SuccessResponse(
                await MapCertificateAsync(row, token), "Certificate revoked.");
        }, ct);
    }

    public Task<ApiResponse<TranscriptIssueDto>> IssueTranscriptAsync(
        IssueTranscriptRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<TranscriptIssueDto>("Transcript issue permission required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.StudentReference == Guid.Empty || request.IssueDate == default)
            return Task.FromResult(Error<TranscriptIssueDto>("Invalid transcript request."));
        return WriteAsync("issue transcript", async token =>
        {
            var tenant = _user.TenantId;
            var replay = await _transcripts.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.ClientRequestId && !x.IsDeleted, token);
            if (replay != null)
            {
                var studentRef = await _students.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Id == replay.StudentId)
                    .Select(x => x.PublicId).FirstOrDefaultAsync(token);
                if (studentRef != request.StudentReference || replay.IssueDate != request.IssueDate)
                    return Error<TranscriptIssueDto>("Request ID used for a different transcript.", 409);
                return ApiResponse<TranscriptIssueDto>.SuccessResponse(
                    await MapTranscriptAsync(replay, token), "Transcript already issued.");
            }
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && !x.IsDeleted, token);
            if (student == null) return Error<TranscriptIssueDto>("Student not found.", 404);
            var enrolled = _enrollments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).Select(x => x.Id);
            if (!await (from summary in _summaries.GetQueryable().AsNoTracking()
                join publication in _publications.GetQueryable().AsNoTracking()
                    on new { summary.TenantId, Id = summary.ResultPublicationId }
                    equals new { publication.TenantId, publication.Id }
                where summary.TenantId == tenant &&
                    enrolled.Contains(summary.StudentEnrollmentId) &&
                    !summary.IsDeleted && !publication.IsDeleted &&
                    publication.State == ResultPublicationState.Published &&
                    !summary.IsWithheld
                select summary.Id).AnyAsync(token))
                return Error<TranscriptIssueDto>("No published, released result exists for the student.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new TranscriptIssue
            {
                TenantId = tenant, PublicId = request.ClientRequestId, StudentId = student.Id,
                TranscriptNumber = "TRN-" + request.ClientRequestId.ToString("N").ToUpperInvariant(),
                IssueDate = request.IssueDate, IssuedByUserId = _user.UserId,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _transcripts.AddAsync(row);
            await RecordAuditAsync("Issue", "TranscriptIssue", null,
                row.TranscriptNumber, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<TranscriptIssueDto>.SuccessResponse(
                await MapTranscriptAsync(row, token), "Transcript issued.");
        }, ct);
    }

    public Task<ApiResponse<TranscriptIssueDto>> RevokeTranscriptAsync(Guid transcriptReference,
        RevokeTranscriptRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Task.FromResult(Error<TranscriptIssueDto>("Transcript revoke permission required.", 403));
        if (transcriptReference == Guid.Empty || !ValidReason(request?.Reason) ||
            !TryVersion(request?.RowVersion, out _))
            return Task.FromResult(Error<TranscriptIssueDto>("Revocation reason and version required."));
        return WriteAsync("revoke transcript", async token =>
        {
            var row = await _transcripts.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.PublicId == transcriptReference &&
                !x.IsDeleted, token);
            if (row == null) return Error<TranscriptIssueDto>("Transcript not found.", 404);
            if (!Matches(row.RowVersion, request!.RowVersion))
                return Error<TranscriptIssueDto>("Transcript changed; reload.", 409);
            if (row.IsRevoked) return Error<TranscriptIssueDto>("Transcript already revoked.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.IsRevoked = true; row.RevokedAt = now;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _transcripts.Update(row);
            await RecordAuditAsync("Revoke", "TranscriptIssue", row.Id, request.Reason, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<TranscriptIssueDto>.SuccessResponse(
                await MapTranscriptAsync(row, token), "Transcript revoked.");
        }, ct);
    }

    private Task RecordAuditAsync(string action, string entity, long? entityId,
        string reason, DateTime now) => _auditLogs.AddAsync(new AuditLog
    {
        TenantId = _user.TenantId, UserId = _user.UserId,
        Action = action, EntityName = entity, EntityId = entityId,
        NewValue = JsonSerializer.Serialize(new { reason }),
        IsSuccess = true, OccurredAt = now
    });
    private static bool ValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 1000;
    private static bool Exact2(decimal value) => value == decimal.Round(value, 2);
    private static AssessmentComponentDto MapComponent(AssessmentComponent row) => new()
    {
        Id = row.Id, AssessmentSubjectId = row.AssessmentSubjectId,
        Name = row.Name, Code = row.Code, FullMarks = row.FullMarks,
        PassMarks = row.PassMarks, Weightage = row.Weightage,
        IsMandatoryPass = row.IsMandatoryPass, DisplayOrder = row.DisplayOrder,
        RowVersion = Version(row.RowVersion)
    };
    private static StudentAssessmentComponentMarkDto MapComponentMark(StudentAssessmentComponentMark row) => new()
    {
        Id = row.Id, AssessmentComponentId = row.AssessmentComponentId,
        StudentAssessmentMarkId = row.StudentAssessmentMarkId,
        ObtainedMarks = row.ObtainedMarks, IsAbsent = row.IsAbsent, IsWithheld = row.IsWithheld,
        EnteredByUserId = row.EnteredByUserId, EnteredAt = row.EnteredAt,
        RowVersion = Version(row.RowVersion)
    };
    private static CertificateTemplateDto MapTemplate(CertificateTemplate row) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code, HtmlTemplate = row.HtmlTemplate,
        IsDefault = row.IsDefault, IsActive = row.IsActive,
        RowVersion = Version(row.RowVersion)
    };
    private async Task<CertificateIssueDto> MapCertificateAsync(CertificateIssue row, CancellationToken ct)
    {
        var studentRef = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.StudentId)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        var enrollmentRef = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.StudentEnrollmentId)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        return new CertificateIssueDto
        {
            Id = row.Id, Reference = row.PublicId, StudentReference = studentRef,
            StudentEnrollmentReference = enrollmentRef, CertificateTemplateId = row.CertificateTemplateId,
            CertificateNumber = row.CertificateNumber, IssueDate = row.IssueDate,
            IssuedByUserId = row.IssuedByUserId, FileAssetId = row.FileAssetId,
            IsRevoked = row.IsRevoked, RevokedAt = row.RevokedAt
        };
    }
    private async Task<TranscriptIssueDto> MapTranscriptAsync(TranscriptIssue row, CancellationToken ct)
    {
        var studentRef = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.Id == row.StudentId)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        return new TranscriptIssueDto
        {
            Id = row.Id, Reference = row.PublicId, StudentReference = studentRef,
            TranscriptNumber = row.TranscriptNumber, IssueDate = row.IssueDate,
            IssuedByUserId = row.IssuedByUserId, FileAssetId = row.FileAssetId,
            IsRevoked = row.IsRevoked, RevokedAt = row.RevokedAt,
            RowVersion = Version(row.RowVersion)
        };
    }
}
