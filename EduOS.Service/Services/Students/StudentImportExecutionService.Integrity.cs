using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Read-only physical integrity audit of one committed, tenant-owned import
    /// window. It never exposes source keys, global Person IDs or foreign data.
    /// </summary>
    public async Task<ApiResponse<StudentImportIntegrityReportDto>> GetIntegrityAsync(
        Guid importReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Student import access denied.", 403);
        if (importReference == Guid.Empty)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Invalid import reference.", 400);
        var tenant = _user.TenantId;
        var log = await _logs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == importReference &&
                x.ImportType == "Student" && !x.IsDeleted)
            .Select(x => new { x.Id, x.TotalRows, x.SuccessRows, x.FailedRows, x.CompletedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (log == null)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import batch not found.", 404);
        if (!log.CompletedAt.HasValue || log.TotalRows is < 1 or > 100 ||
            log.SuccessRows != log.TotalRows || log.FailedRows != 0)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Only completed 1–100-row student import batches can be verified.", 409);

        var items = await _items.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ImportLogId == log.Id && !x.IsDeleted)
            .OrderBy(x => x.RowNumber).Take(101)
            .Select(x => new { x.RowNumber, x.BusinessKey, x.IsSuccess })
            .ToListAsync(cancellationToken);
        if (items.Count > 100)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import audit row limit exceeded.", 409);

        var sourceIds = items.Where(x => !string.IsNullOrWhiteSpace(x.BusinessKey))
            .Select(x => SourceId(x.BusinessKey!)).Distinct().ToArray();
        var keys = await _keys.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Scope == SourceScope &&
                !x.IsDeleted && x.CompletedAt.HasValue && sourceIds.Contains(x.RequestId))
            .Select(x => new { x.RequestId, x.RequestHash, x.TargetId })
            .ToListAsync(cancellationToken);
        // Historical/corrupted duplicate receipts must be reported as an audit
        // defect, not crash the entire authorized report with ToDictionary.
        var bySource = keys.ToLookup(x => x.RequestId);
        var studentIds = keys.Where(x => x.TargetId.HasValue)
            .Select(x => x.TargetId!.Value).Distinct().ToArray();
        var students = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && studentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PersonId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        // Guardian relationships are optional; the source log does not retain
        // whether a particular imported row originally supplied one.
        var guardianLinks = await _studentGuardians.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && studentIds.Contains(x.StudentId))
            .Select(x => new { x.StudentId, x.GuardianId, x.IsPrimary })
            .Take(501).ToListAsync(cancellationToken);
        if (guardianLinks.Count > 500)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import guardian audit relationship limit exceeded.", 409);
        var guardianIds = guardianLinks.Select(x => x.GuardianId).Distinct().ToArray();
        var guardians = await _guardians.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && guardianIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PersonId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var guardianLinksByStudent = guardianLinks.ToLookup(x => x.StudentId);
        // Global identities are read only after a tenant-owned Student or
        // Guardian has established the relationship; never enumerate Persons.
        var personIds = students.Values.Select(x => x.PersonId)
            .Concat(guardians.Values.Select(x => x.PersonId)).Distinct().ToArray();
        var existingPersons = (await _persons.GetQueryable().AsNoTracking()
            .Where(x => !x.IsDeleted && personIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken)).ToHashSet();
        var links = await _links.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsPrimary &&
                x.UnlinkedAt == null && studentIds.Contains(x.StudentId))
            .Select(x => new { x.StudentId, x.PersonId })
            .ToListAsync(cancellationToken);
        var linksByStudent = links.ToLookup(x => x.StudentId);
        var enrolled = (await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                studentIds.Contains(x.StudentId))
            .Select(x => x.StudentId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        // Audit the CURRENT active academic registration state of imported students.
        // Historical/completed enrollments are never rewritten or compared with
        // today's curriculum; this is a bounded read-only remediation worklist.
        var currentEnrollments = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                x.State == EnrollmentState.Active && studentIds.Contains(x.StudentId))
            .Select(x => new { x.Id, x.StudentId, x.AcademicCurriculumId,
                x.AcademicLevelId, x.AcademicBatchId, x.AcademicYearId })
            .Take(201).ToListAsync(cancellationToken);
        if (currentEnrollments.Count > 200)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Current academic enrollment audit limit exceeded.", 409);
        var curriculumIds = currentEnrollments.Select(x => x.AcademicCurriculumId).Distinct().ToArray();
        var levelIds = currentEnrollments.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var requiredSubjects = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive && !x.IsOptional &&
                curriculumIds.Contains(x.AcademicCurriculumId) && levelIds.Contains(x.AcademicLevelId))
            .Select(x => new { x.Id, x.AcademicCurriculumId, x.AcademicLevelId })
            .Take(2001).ToListAsync(cancellationToken);
        if (requiredSubjects.Count > 2000)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Required-subject audit limit exceeded.", 409);
        var requiredIds = requiredSubjects.Select(x => x.Id).ToArray();
        var batchIds = currentEnrollments.Select(x => x.AcademicBatchId).Distinct().ToArray();
        var yearIds = currentEnrollments.Select(x => x.AcademicYearId).Distinct().ToArray();
        var offerings = await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                requiredIds.Contains(x.CurriculumSubjectId) &&
                batchIds.Contains(x.AcademicBatchId) && yearIds.Contains(x.AcademicYearId))
            .Select(x => new { x.Id, x.CurriculumSubjectId, x.AcademicBatchId, x.AcademicYearId })
            .Take(4001).ToListAsync(cancellationToken);
        if (offerings.Count > 4000)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Required-offering audit limit exceeded.", 409);
        var enrollmentIds = currentEnrollments.Select(x => x.Id).ToArray();
        var registrations = await _registrations.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                enrollmentIds.Contains(x.StudentEnrollmentId))
            .Select(x => new { x.StudentEnrollmentId, x.SubjectOfferingId,
                x.State, x.CreditHoursSnapshot })
            .Take(4001).ToListAsync(cancellationToken);
        if (registrations.Count > 4000)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Subject-registration audit limit exceeded.", 409);
        var currentByStudent = currentEnrollments.ToLookup(x => x.StudentId);
        var requiredByCurriculum = requiredSubjects.ToLookup(x =>
            (x.AcademicCurriculumId, x.AcademicLevelId));
        var offeringsBySubject = offerings.ToLookup(x =>
            (x.CurriculumSubjectId, x.AcademicBatchId, x.AcademicYearId));
        var registrationsByEnrollmentOffering = registrations.ToLookup(x =>
            (x.StudentEnrollmentId, x.SubjectOfferingId));

        var report = new StudentImportIntegrityReportDto
        {
            ImportReference = importReference,
            ExpectedRows = log.TotalRows,
            CheckedRows = items.Count
        };
        var seenStudentTargets = new HashSet<long>();
        var seenSources = new HashSet<Guid>();
        var seenRowNumbers = new HashSet<int>();
        void Issue(int row, string code, string message) =>
            report.Issues.Add(new StudentImportIntegrityIssueDto
            {
                RowNumber = row, Code = code, Message = message
            });
        if (items.Count != log.TotalRows)
            Issue(0, "LogRowCountMismatch", "Committed import row count differs from its audit log.");
        if (items.Count > 0 && items.Select((x, i) => x.RowNumber != items[0].RowNumber + i).Any(x => x))
            Issue(0, "NonContiguousRowNumbers", "Committed source row numbers have a gap or duplicate.");
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = report.Issues.Count;
            if (item.RowNumber < 2 || !seenRowNumbers.Add(item.RowNumber))
                Issue(Math.Max(0, item.RowNumber), "InvalidRowNumber",
                    "Committed source row number is invalid or duplicated.");
            if (!item.IsSuccess || string.IsNullOrWhiteSpace(item.BusinessKey))
                Issue(Math.Max(0, item.RowNumber), "InvalidLogRow",
                    "Import row has no valid committed source identity.");
            else
            {
                var sourceId = SourceId(item.BusinessKey);
                report.SourceReceiptsChecked++;
                var uniqueSource = seenSources.Add(sourceId);
                if (!uniqueSource)
                    Issue(item.RowNumber, "DuplicateSourceKey",
                        "Committed source identity is repeated in this window.");
                var receipts = bySource[sourceId].ToArray();
                if (receipts.Length != 1)
                    Issue(item.RowNumber, receipts.Length == 0 ? "MissingSourceIdentity" : "AmbiguousSourceReceipt",
                        "Committed source identity receipt is missing or ambiguous.");
                else if (!receipts[0].TargetId.HasValue)
                    Issue(item.RowNumber, "MissingSourceTarget",
                        "Committed source identity has no student target.");
                else
                {
                    var expectedHash = Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(item.BusinessKey.ToUpperInvariant())));
                    if (!string.Equals(receipts[0].RequestHash, expectedHash, StringComparison.Ordinal))
                        Issue(item.RowNumber, "SourceReceiptHashMismatch",
                            "Committed source identity fingerprint does not match its audit row.");
                    if (!students.TryGetValue(receipts[0].TargetId.Value, out var student))
                        Issue(item.RowNumber, "MissingStudent",
                            "Tenant-owned student record is unavailable.");
                    else
                    {
                        if (uniqueSource && string.Equals(receipts[0].RequestHash, expectedHash, StringComparison.Ordinal))
                            report.SourceReceiptsHealthy++;
                if (!seenStudentTargets.Add(student.Id))
                    Issue(item.RowNumber, "DuplicateStudentTarget",
                        "Multiple committed source rows resolve to one student record.");
                if (!existingPersons.Contains(student.PersonId))
                    Issue(item.RowNumber, "MissingPersonIdentity",
                        "Linked learner identity is unavailable.");
                var primaryLinks = linksByStudent[student.Id].ToArray();
                if (primaryLinks.Length != 1)
                    Issue(item.RowNumber, "AmbiguousPrimaryLink",
                        "Student must have exactly one active primary identity link.");
                if (!primaryLinks.Any(x => x.PersonId == student.PersonId))
                    Issue(item.RowNumber, "PersonLinkMismatch", "Primary learner identity link is unavailable.");
                if (!enrolled.Contains(student.Id))
                    Issue(item.RowNumber, "MissingEnrollment", "Student enrollment history is unavailable.");

                var activeEnrollments = currentByStudent[student.Id].ToArray();
                if (activeEnrollments.Length == 0)
                    report.AcademicRowsSkipped++;
                else if (activeEnrollments.Length != 1)
                    Issue(item.RowNumber, "AmbiguousCurrentEnrollment",
                        "Student has multiple current active enrollments requiring academic review.");
                else
                {
                    report.CurrentEnrollmentsChecked++;
                    var current = activeEnrollments[0];
                    foreach (var subject in requiredByCurriculum[
                        (current.AcademicCurriculumId, current.AcademicLevelId)])
                    {
                        report.RequiredSubjectsExpected++;
                        var matches = offeringsBySubject[
                            (subject.Id, current.AcademicBatchId, current.AcademicYearId)].ToArray();
                        if (matches.Length != 1)
                        {
                            Issue(item.RowNumber, matches.Length == 0
                                ? "MissingRequiredOffering" : "AmbiguousRequiredOffering",
                                "Current curriculum required-subject offering requires review.");
                            continue;
                        }
                        var subjectRegistrations = registrationsByEnrollmentOffering[
                            (current.Id, matches[0].Id)].ToArray();
                        if (subjectRegistrations.Length != 1)
                            Issue(item.RowNumber, subjectRegistrations.Length == 0
                                ? "MissingRequiredRegistration" : "DuplicateRequiredRegistration",
                                "Current required-subject registration requires reconciliation.");
                        else if (subjectRegistrations[0].State != SubjectRegistrationState.Approved)
                            Issue(item.RowNumber, "UnapprovedRequiredRegistration",
                                "Current required-subject registration is not approved.");
                        else if (!subjectRegistrations[0].CreditHoursSnapshot.HasValue)
                            Issue(item.RowNumber, "MissingRequiredCreditSnapshot",
                                "Approved required-subject registration lacks a credit snapshot.");
                        else
                            report.RequiredSubjectsHealthy++;
                    }
                }

                var relations = guardianLinksByStudent[student.Id].ToArray();
                report.GuardianLinksChecked += relations.Length;
                var multiplePrimary = relations.Count(x => x.IsPrimary) > 1;
                if (multiplePrimary)
                    Issue(item.RowNumber, "AmbiguousPrimaryGuardian",
                        "Multiple primary guardian relationships require review.");
                var seenGuardians = new HashSet<long>();
                foreach (var relation in relations)
                {
                    var unique = seenGuardians.Add(relation.GuardianId);
                    if (!unique)
                        Issue(item.RowNumber, "DuplicateGuardianLink",
                            "Repeated guardian relationship requires review.");
                    if (!guardians.TryGetValue(relation.GuardianId, out var guardian))
                        Issue(item.RowNumber, "MissingGuardian",
                            "Guardian relationship does not resolve within this institution.");
                    else if (!existingPersons.Contains(guardian.PersonId))
                        Issue(item.RowNumber, "MissingGuardianIdentity",
                            "Linked guardian identity is unavailable.");
                    else if (unique && !(multiplePrimary && relation.IsPrimary))
                        report.GuardianLinksHealthy++;
                }
                    }
                }
            }
            if (report.Issues.Count == before) report.HealthyRows++;
        }
        if (report.Issues.Count > 2000)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import integrity issue limit exceeded. Review smaller source windows.", 409);
        return ApiResponse<StudentImportIntegrityReportDto>.SuccessResponse(report);
    }
}
