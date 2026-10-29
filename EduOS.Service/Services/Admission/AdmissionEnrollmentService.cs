using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionEnrollmentService : IAdmissionEnrollmentService
{
    private readonly IGenericRepository<AdmissionApplicant> _applicants;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<AdmissionDecision> _decisions;
    private readonly IGenericRepository<AdmissionApplicantDocument> _documents;
    private readonly IGenericRepository<AdmissionApplicantGuardian> _applicantGuardians;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<StudentPersonLink> _personLinks;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicTrack> _tracks;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IUnitOfWork _uow;
    private readonly EduOSDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionEnrollmentService> _logger;

    public AdmissionEnrollmentService(IGenericRepository<AdmissionApplicant> applicants,
        IGenericRepository<AdmissionIntakeForm> forms, IGenericRepository<AdmissionDecision> decisions,
        IGenericRepository<AdmissionApplicantDocument> documents,
        IGenericRepository<AdmissionApplicantGuardian> applicantGuardians,
        IGenericRepository<Student> students, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Guardian> guardians, IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<Person> persons, IGenericRepository<StudentPersonLink> personLinks,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicTrack> tracks,
        IGenericRepository<AcademicCurriculum> curricula, IGenericRepository<AcademicYear> years,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<AdmissionEnrollmentService> logger, EduOSDbContext db)
    {
        _applicants = applicants; _forms = forms; _decisions = decisions; _documents = documents;
        _applicantGuardians = applicantGuardians; _students = students;
        _enrollments = enrollments; _guardians = guardians; _studentGuardians = studentGuardians;
        _persons = persons; _personLinks = personLinks; _batches = batches;
        _tracks = tracks; _curricula = curricula; _years = years;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger; _db = db;
    }

    public async Task<ApiResponse<AdmissionEnrollmentOptionsDto>> GetOptionsAsync(Guid applicationReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionEnrollmentOptionsDto>();
        if (applicationReference == Guid.Empty)
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Applicant reference is required.");
        var tenant = _user.TenantId;
        var application = await _applicants.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.PublicId == applicationReference && !x.IsDeleted, cancellationToken);
        if (application == null) return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Applicant not found.", 404);
        if (application.State is not (AdmissionApplicantState.Qualified or AdmissionApplicantState.Admitted))
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Applicant must qualify before enrollment.", 409);
        var form = await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == application.AdmissionIntakeFormId, cancellationToken);
        if (form == null) return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Intake form not found.", 409);
        var accepted = await _decisions.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.AdmissionApplicantId == application.Id && x.State == AdmissionDecisionState.Accepted)
            .OrderByDescending(x => x.AcceptedAt).ThenByDescending(x => x.Id)
            .Select(x => x.OfferedAcademicBatchId).FirstOrDefaultAsync(cancellationToken);
        // Qualified applicants may inspect eligible batches to receive an offer.
        // Actual conversion still requires a verified Accepted decision.
        var sectionsQuery = _batches.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.IsActive && x.CampusId == form.CampusId && x.AcademicYearId == form.AcademicYearId &&
            x.AcademicProgramId == form.AcademicProgramId && x.AcademicLevelId == form.AcademicLevelId);
        if (form.AcademicTermId.HasValue)
            sectionsQuery = sectionsQuery.Where(x => x.AcademicTermId == form.AcademicTermId);
        if (form.AcademicTrackId.HasValue)
            sectionsQuery = sectionsQuery.Where(x => x.AcademicTrackId == form.AcademicTrackId);
        if (accepted.HasValue)
            sectionsQuery = sectionsQuery.Where(x => x.Id == accepted.Value);
        var sections = await sectionsQuery.OrderBy(x => x.Name).Select(x =>
            new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name, ParentId = x.AcademicLevelId })
            .ToListAsync(cancellationToken);
        var tracks = await _tracks.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive &&
            (x.AcademicProgramId == null || x.AcademicProgramId == form.AcademicProgramId))
            .OrderBy(x => x.Name).Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);
        return ApiResponse<AdmissionEnrollmentOptionsDto>.SuccessResponse(new AdmissionEnrollmentOptionsDto
        {
            AcademicBatches = sections, AcademicTracks = tracks
        });
    }


    public async Task<ApiResponse<AdmissionDecisionDto?>> GetOfferAsync(Guid applicationReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionDecisionDto?>();
        var tenant = _user.TenantId;
        var applicant = await _applicants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == applicationReference && !x.IsDeleted, cancellationToken);
        if (applicant == null) return ApiResponse<AdmissionDecisionDto?>.ErrorResponse("Applicant not found.", 404);
        var decision = await _decisions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AdmissionApplicantId == applicant.Id)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        return ApiResponse<AdmissionDecisionDto?>.SuccessResponse(decision == null ? null : new AdmissionDecisionDto
        {
            Id = decision.Id, AdmissionApplicantReference = applicant.PublicId, State = decision.State,
            OfferedAcademicBatchId = decision.OfferedAcademicBatchId, OfferedAt = decision.OfferedAt,
            ExpiresAt = decision.ExpiresAt, AcceptedAt = decision.AcceptedAt,
            DecidedByUserId = decision.DecidedByUserId, Note = decision.Note,
            RowVersion = Convert.ToBase64String(decision.RowVersion)
        });
    }

    public async Task<ApiResponse<AdmissionDecisionDto>> IssueOfferAsync(Guid applicationReference,
        MakeAdmissionDecisionRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionDecisionDto>();
        if (applicationReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.AdmissionApplicantReference != applicationReference ||
            request.State != AdmissionDecisionState.Offered || request.OfferedAcademicBatchId is null or <= 0 ||
            !TryVersion(request.ApplicantRowVersion, out var expected))
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Valid offer, applicant, request ID and row version are required.");
        var now = _clock.GetUtcNow().UtcDateTime;
        if (!request.ExpiresAt.HasValue || request.ExpiresAt.Value.Kind != DateTimeKind.Utc ||
            request.ExpiresAt.Value <= now || request.ExpiresAt.Value > now.AddDays(30))
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer expiry must be UTC and within 30 days.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var applicant = await _applicants.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == applicationReference && !x.IsDeleted, token);
                if (applicant == null) return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Applicant not found.", 404);
                var prior = await _decisions.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.AdmissionApplicantId == applicant.Id)
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(token);
                if (prior != null && prior.ClientRequestId == request.ClientRequestId &&
                    prior.State == AdmissionDecisionState.Offered &&
                    prior.OfferedAcademicBatchId == request.OfferedAcademicBatchId &&
                    prior.ExpiresAt == request.ExpiresAt.Value)
                    return ApiResponse<AdmissionDecisionDto>.SuccessResponse(new AdmissionDecisionDto
                    {
                        Id = prior.Id, AdmissionApplicantReference = applicationReference,
                        State = prior.State, OfferedAcademicBatchId = prior.OfferedAcademicBatchId,
                        OfferedAt = prior.OfferedAt, ExpiresAt = prior.ExpiresAt,
                        RowVersion = Convert.ToBase64String(prior.RowVersion)
                    }, "Offer already issued.");
                if (prior != null) return ApiResponse<AdmissionDecisionDto>.ErrorResponse("An admission decision already exists.", 409);
                if (applicant.State != AdmissionApplicantState.Qualified || applicant.ConvertedStudentId.HasValue)
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Only qualified, unconverted applicants can receive offers.", 409);
                if (!VersionsMatch(applicant.RowVersion, expected))
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Applicant changed. Reload and retry.", 409);
                var form = await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == applicant.AdmissionIntakeFormId && !x.IsDeleted, token);
                if (form == null) return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Intake form not found.", 409);
                var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.OfferedAcademicBatchId.Value && x.IsActive && !x.IsDeleted &&
                    x.CampusId == form.CampusId && x.AcademicYearId == form.AcademicYearId &&
                    x.AcademicProgramId == form.AcademicProgramId && x.AcademicLevelId == form.AcademicLevelId &&
                    (!form.AcademicTermId.HasValue || x.AcademicTermId == form.AcademicTermId) &&
                    (!form.AcademicTrackId.HasValue || x.AcademicTrackId == form.AcademicTrackId), token);
                if (batch == null || batch.Capacity <= 0)
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Batch is not eligible for this intake.", 409);
                var decision = new AdmissionDecision
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId,
                    AdmissionApplicantId = applicant.Id, State = AdmissionDecisionState.Offered,
                    OfferedAcademicBatchId = batch.Id, OfferedAt = now, ExpiresAt = request.ExpiresAt.Value,
                    DecidedByUserId = _user.UserId, Note = request.Note?.Trim(),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _decisions.AddAsync(decision);
                applicant.UpdatedAt = now; applicant.UpdatedBy = _user.UserId;
                _applicants.Update(applicant);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AdmissionDecisionDto>.SuccessResponse(new AdmissionDecisionDto
                {
                    Id = decision.Id, AdmissionApplicantReference = applicationReference,
                    State = decision.State, OfferedAcademicBatchId = batch.Id,
                    OfferedAt = now, ExpiresAt = request.ExpiresAt.Value,
                    RowVersion = Convert.ToBase64String(decision.RowVersion)
                }, "Offer issued. Verified applicant or guardian acceptance is required.");
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Applicant changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission offer conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Concurrent admission offer conflict.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission offer issuance failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer could not be issued.", 500);
        }
    }


    // The public reference/mobile status check never authorizes acceptance.
    // Only authorized staff can attest to an independently verified signed document.
    public async Task<ApiResponse<AdmissionDecisionDto>> RecordVerifiedAcceptanceAsync(Guid applicationReference,
        VerifyAdmissionOfferAcceptanceRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage() || !_user.IsTenantAdmin) return Denied<AdmissionDecisionDto>();
        if (applicationReference == Guid.Empty || request == null || request.AcceptanceDocumentId <= 0 ||
            !request.IdentityChecked || request.SignerRole is not ("Applicant" or "Guardian") ||
            !TryVersion(request.DecisionRowVersion, out var expected))
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Verified signer, signed document and offer version are required.");
        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var applicant = await _applicants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == applicationReference && !x.IsDeleted, token);
                if (applicant == null) return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Applicant not found.", 404);
                if (applicant.State != AdmissionApplicantState.Qualified || applicant.ConvertedStudentId.HasValue ||
                    !applicant.DateOfBirth.HasValue)
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Applicant is not eligible for acceptance.", 409);
                var isMinor = applicant.DateOfBirth.Value > DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddYears(-18);
                if (isMinor && request.SignerRole != "Guardian")
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("A verified guardian must sign for a minor.", 409);
                if (request.SignerRole == "Guardian" && !await _applicantGuardians.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AdmissionApplicantId == applicant.Id && x.IsPrimary && !x.IsDeleted, token))
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Primary guardian record is required.", 409);
                var decision = await _decisions.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.AdmissionApplicantId == applicant.Id && !x.IsDeleted, token);
                if (decision == null) return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer not found.", 404);
                var doc = await _documents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.AdmissionApplicantId == applicant.Id &&
                    x.Id == request.AcceptanceDocumentId && x.DocumentTypeCode == "OFFER_ACCEPTANCE" &&
                    x.IsVerified && x.VerifiedAt.HasValue && x.VerifiedByUserId.HasValue && !x.IsDeleted, token);
                if (doc == null || !decision.OfferedAt.HasValue ||
                    doc.CreatedAt < decision.OfferedAt.Value || doc.VerifiedAt < decision.OfferedAt.Value)
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("A post-offer signed document verified by staff is required.", 409);
                var marker = $"[Signer={request.SignerRole};AcceptanceDocument={doc.Id}]";
                if (decision.State == AdmissionDecisionState.Accepted)
                {
                    if (decision.Note?.Contains(marker, StringComparison.Ordinal) != true)
                        return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer was accepted using different evidence.", 409);
                    return ApiResponse<AdmissionDecisionDto>.SuccessResponse(new AdmissionDecisionDto
                    {
                        Id = decision.Id, AdmissionApplicantReference = applicationReference, State = decision.State,
                        OfferedAcademicBatchId = decision.OfferedAcademicBatchId, OfferedAt = decision.OfferedAt,
                        ExpiresAt = decision.ExpiresAt, AcceptedAt = decision.AcceptedAt,
                        DecidedByUserId = decision.DecidedByUserId, Note = decision.Note,
                        RowVersion = Convert.ToBase64String(decision.RowVersion)
                    }, "Acceptance already recorded.");
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                if (decision.State != AdmissionDecisionState.Offered ||
                    !decision.ExpiresAt.HasValue || decision.ExpiresAt.Value <= now)
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer has expired or is not active.", 409);
                if (!VersionsMatch(decision.RowVersion, expected))
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer changed. Reload and retry.", 409);
                var note = string.IsNullOrWhiteSpace(decision.Note) ? marker : decision.Note + "\n" + marker;
                if (note.Length > 1000)
                    return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer note cannot retain acceptance audit.", 409);
                decision.State = AdmissionDecisionState.Accepted; decision.AcceptedAt = now;
                decision.Note = note; decision.UpdatedAt = now; decision.UpdatedBy = _user.UserId;
                _decisions.Update(decision);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AdmissionDecisionDto>.SuccessResponse(new AdmissionDecisionDto
                {
                    Id = decision.Id, AdmissionApplicantReference = applicationReference, State = decision.State,
                    OfferedAcademicBatchId = decision.OfferedAcademicBatchId, OfferedAt = decision.OfferedAt,
                    ExpiresAt = decision.ExpiresAt, AcceptedAt = decision.AcceptedAt,
                    DecidedByUserId = decision.DecidedByUserId, Note = decision.Note,
                    RowVersion = Convert.ToBase64String(decision.RowVersion)
                }, "Verified signed acceptance recorded. Enrollment can proceed.");
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Offer changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission acceptance conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Concurrent admission acceptance conflict.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission acceptance failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionDecisionDto>.ErrorResponse("Acceptance could not be recorded.", 500);
        }
    }

    public async Task<ApiResponse<AdmittedStudentDto>> AdmitAsync(Guid applicationReference,
        AdmitAdmissionApplicationDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmittedStudentDto>();
        if (applicationReference == Guid.Empty || request == null || request.AcademicBatchId <= 0 ||
            request.AcademicTrackId is <= 0 || string.IsNullOrWhiteSpace(request.Roll) || request.Roll.Trim().Length > 50 ||
            !TryVersion(request.RowVersion, out var expected))
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Valid applicant, batch, roll and row version are required.");
        var tenant = _user.TenantId;
        var roll = request.Roll.Trim();
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                    var application = await _applicants.GetQueryable().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.PublicId == applicationReference && !x.IsDeleted, cancellationToken);
                    if (application == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant not found.", 404);
                    if (application.ConvertedStudentId.HasValue && application.ConvertedEnrollmentId.HasValue)
                    {
                        var existingStudent = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                            x.TenantId == tenant && x.Id == application.ConvertedStudentId.Value, cancellationToken);
                        var existingEnrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                            x.TenantId == tenant && x.Id == application.ConvertedEnrollmentId.Value, cancellationToken);
                        if (existingStudent == null || existingEnrollment == null ||
                            existingStudent.AdmissionApplicantId != application.Id ||
                            existingEnrollment.StudentId != existingStudent.Id ||
                            existingEnrollment.AcademicBatchId != request.AcademicBatchId ||
                            existingEnrollment.RollNo != roll)
                            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant conversion has inconsistent references.", 409);
                        return ApiResponse<AdmittedStudentDto>.SuccessResponse(Map(existingStudent, existingEnrollment),
                            "Applicant was already admitted.");
                    }
                    if (application.State != AdmissionApplicantState.Qualified)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Only a qualified applicant can be admitted.", 409);
                    if (!VersionsMatch(application.RowVersion, expected))
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant changed. Reload and retry.", 409);
                    if (await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.AdmissionApplicantId == application.Id, cancellationToken))
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant has an existing student record.", 409);

                    var form = await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == application.AdmissionIntakeFormId, cancellationToken);
                    if (form == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Intake form not found.", 409);
                    var decision = await _decisions.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.AdmissionApplicantId == application.Id)
                        .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
                    if (decision == null || decision.State != AdmissionDecisionState.Accepted ||
                        decision.OfferedAcademicBatchId != request.AcademicBatchId)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("An accepted offer for the selected batch is required.", 409);

                    var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == request.AcademicBatchId && x.IsActive &&
                        x.CampusId == form.CampusId && x.AcademicYearId == form.AcademicYearId &&
                        x.AcademicProgramId == form.AcademicProgramId && x.AcademicLevelId == form.AcademicLevelId &&
                        (!form.AcademicTermId.HasValue || x.AcademicTermId == form.AcademicTermId) &&
                        (!form.AcademicTrackId.HasValue || x.AcademicTrackId == form.AcademicTrackId),
                        cancellationToken);
                    if (batch == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Batch is not eligible for this intake.", 409);
                    if (request.AcademicTrackId.HasValue && request.AcademicTrackId != batch.AcademicTrackId)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Selected academic track does not match the batch.", 409);
                    var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                    var yearActive = await _years.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive &&
                        today >= x.StartDate && today <= x.EndDate, cancellationToken);
                    if (!yearActive) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Academic year is not active for today's admission date.", 409);
                    var curricula = await _curricula.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.AcademicProgramId == batch.AcademicProgramId &&
                        x.IsCurrent && x.IsActive && x.AcademicTrackId == batch.AcademicTrackId &&
                        x.MediumId == batch.MediumId && x.EffectiveFrom <= today &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo >= today))
                        .Take(2).Select(x => x.Id).ToArrayAsync(cancellationToken);
                    if (curricula.Length != 1)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Exactly one active curriculum must match the batch.", 409);
                    // Serialize allocations for this batch BEFORE capacity and roll checks.
                    // All business validation above is read-only, so invalid requests leave
                    // no batch version/audit mutation behind.
                    var lockedBatch = await _batches.GetQueryable().FirstAsync(x =>
                        x.TenantId == tenant && x.Id == batch.Id && !x.IsDeleted, cancellationToken);
                    lockedBatch.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    lockedBatch.UpdatedBy = _user.UserId;
                    _batches.Update(lockedBatch);
                    await _uow.SaveChangesAsync(cancellationToken);
                    // Match AcademicEnrollmentService: zero/nonpositive capacity is closed,
                    // never implicitly unlimited. Existing zero-capacity batches require
                    // review before enabling admissions during deployment.
                    if (batch.Capacity <= 0 || await _enrollments.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && x.AcademicBatchId == batch.Id &&
                        x.IsCurrent && x.State == EnrollmentState.Active && !x.IsDeleted, cancellationToken) >= batch.Capacity)
                        throw new BatchAllocationException("The batch is at capacity.");
                    if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.AcademicBatchId == batch.Id && x.RollNo == roll && x.IsCurrent && !x.IsDeleted, cancellationToken))
                        throw new BatchAllocationException("Roll is already assigned in this batch.");
                    if (!await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(_db, tenant, 1, token))
                        throw new BatchAllocationException("Subscription active student capacity reached.");

                    var now = _clock.GetUtcNow().UtcDateTime;
                    var person = application.PersonId.HasValue
                        ? await _persons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                            x.Id == application.PersonId.Value, cancellationToken)
                        : null;
                    if (application.PersonId.HasValue && person == null)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Linked person record is missing.", 409);
                    if (person == null)
                    {
                        person = new Person
                        {
                            FullName = application.FullName, FullNameBangla = application.FullNameBangla,
                            DateOfBirth = application.DateOfBirth, Gender = application.Gender,
                            Phone = application.Phone, Email = application.Email, CreatedAt = now
                        };
                        await _persons.AddAsync(person);
                        await _uow.SaveChangesAsync(cancellationToken);
                        application.PersonId = person.Id;
                    }
                    var student = new Student
                    {
                        TenantId = tenant, PublicId = Guid.NewGuid(), PersonId = person.Id,
                        AdmissionApplicantId = application.Id, StudentCode = "STU-" + application.PublicId.ToString("N").ToUpperInvariant(),
                        FullName = application.FullName, FullNameBangla = application.FullNameBangla,
                        DateOfBirth = application.DateOfBirth, Gender = application.Gender,
                        Phone = application.Phone, Email = application.Email, Address = application.Address,
                        AdmissionDate = today, PreferredLanguage = person.PreferredLanguage,
                        StatusCode = "Active", CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _students.AddAsync(student);
                    await _uow.SaveChangesAsync(cancellationToken);
                    await _personLinks.AddAsync(new StudentPersonLink
                    {
                        TenantId = tenant, StudentId = student.Id, PersonId = person.Id,
                        IsPrimary = true, LinkedAt = now, LinkedByUserId = _user.UserId,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                    var guardianInputs = await _applicantGuardians.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.AdmissionApplicantId == application.Id).OrderByDescending(x => x.IsPrimary)
                        .ToListAsync(cancellationToken);
                    foreach (var input in guardianInputs)
                    {
                        var guardianPerson = new Person
                        {
                            FullName = input.FullName, Phone = input.Phone, Email = input.Email,
                            CreatedAt = now
                        };
                        await _persons.AddAsync(guardianPerson);
                        await _uow.SaveChangesAsync(cancellationToken);
                        var guardian = new Guardian
                        {
                            TenantId = tenant, PersonId = guardianPerson.Id, FullName = input.FullName,
                            Phone = input.Phone, Email = input.Email, Occupation = input.Occupation,
                            CreatedAt = now, CreatedBy = _user.UserId
                        };
                        await _guardians.AddAsync(guardian);
                        await _uow.SaveChangesAsync(cancellationToken);
                        await _studentGuardians.AddAsync(new StudentGuardian
                        {
                            TenantId = tenant, StudentId = student.Id, GuardianId = guardian.Id,
                            RelationCode = input.RelationCode, IsPrimary = input.IsPrimary,
                            CreatedAt = now, CreatedBy = _user.UserId
                        });
                    }

                    var enrollment = new StudentEnrollment
                    {
                        TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                        StudentId = student.Id, CampusId = batch.CampusId,
                        AcademicYearId = batch.AcademicYearId, AcademicTermId = batch.AcademicTermId,
                        AcademicProgramId = batch.AcademicProgramId, AcademicLevelId = batch.AcademicLevelId,
                        AcademicBatchId = batch.Id, AcademicCurriculumId = curricula[0],
                        AcademicTrackId = batch.AcademicTrackId, MediumId = batch.MediumId, ShiftId = batch.ShiftId,
                        RollNo = roll, EnrollmentDate = today, State = EnrollmentState.Active,
                        IsCurrent = true, CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _enrollments.AddAsync(enrollment);
                    await _uow.SaveChangesAsync(cancellationToken);
                    application.State = AdmissionApplicantState.Admitted;
                    application.ConvertedStudentId = student.Id;
                    application.ConvertedEnrollmentId = enrollment.Id;
                    application.ConvertedAt = now;
                    application.UpdatedAt = now;
                    application.UpdatedBy = _user.UserId;
                    await _uow.SaveChangesAsync(cancellationToken);
                    var result = Map(student, enrollment);
                    return new ApiResponse<AdmittedStudentDto>
                    {
                        Success = true, StatusCode = 201, Message = "Applicant admitted.", Data = result
                    };
            }, cancellationToken);
        }
        catch (BatchAllocationException ex)
        {
            // Rolling back the transaction also rolls back the batch concurrency update.
            return ApiResponse<AdmittedStudentDto>.ErrorResponse(ex.Message, 409);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Admission conversion version conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission conversion integrity conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Conversion conflicts with an existing student or roll.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222)
        {
            _logger.LogWarning(ex, "Admission quota allocation contention for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Admission is busy; reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission conversion failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant could not be admitted.", 500);
        }
    }

    private sealed class BatchAllocationException(string message) : Exception(message);

    private static AdmittedStudentDto Map(Student student, StudentEnrollment enrollment) => new()
    {
        StudentReference = student.PublicId, StudentCode = student.StudentCode,
        StudentId = student.Id, EnrollmentId = enrollment.Id, Roll = enrollment.RollNo,
        ApplicationState = AdmissionApplicantState.Admitted
    };

    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("AdmissionOfficer"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Admission officer access is required.", 403);
    private static bool TryVersion(string? supplied, out byte[] version)
    {
        version = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { version = Convert.FromBase64String(supplied); return version.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
}
