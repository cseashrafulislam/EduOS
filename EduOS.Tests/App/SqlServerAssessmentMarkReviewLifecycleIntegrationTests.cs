using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.AssessmentAdministration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Exercises the real assessment service against disposable SQL Server, including
/// instructor ownership, reviewer separation, rejected-mark correction, replay,
/// optimistic versions and frozen approved evidence. No production records.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAssessmentMarkReviewLifecycleIntegrationTests
{
    [Fact]
    public async Task Published_result_rescrutiny_is_tenant_scoped_idempotent_and_independently_decided()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        Guid studentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var registration = await db.StudentSubjectRegistrations.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.RegistrationId);
            var enrollment = await db.StudentEnrollments.AsNoTracking()
                .SingleAsync(x => x.Id == registration.StudentEnrollmentId);
            studentReference = await db.Students.AsNoTracking()
                .Where(x => x.Id == enrollment.StudentId).Select(x => x.PublicId).SingleAsync();
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published, PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId,
                StudentEnrollmentId = enrollment.Id, PublicationVersionNo = 1,
                TotalMarks = 100m, ObtainedMarks = 70m, Percentage = 70m,
                GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        var requestKey = Guid.NewGuid();
        Guid caseReference;
        string version;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var request = new RequestResultRescrutinyDto
            {
                ClientRequestId = requestKey, PublicationId = publicationId,
                StudentReference = studentReference, Reason = "Please verify the published mark."
            };
            var created = await service.RequestResultRescrutinyAsync(request);
            Assert.True(created.Success, created.Message);
            caseReference = created.Data!.CaseReference;
            version = created.Data.RequestRowVersion;
            Assert.Equal(ResultRescrutinyAction.Requested, created.Data.Status);
            var replay = await service.RequestResultRescrutinyAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(caseReference, replay.Data!.CaseReference);
            var duplicate = await service.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference, Reason = "A second duplicate application."
            });
            Assert.Equal(409, duplicate.StatusCode);
            var page = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, PageSize = 1
            });
            Assert.True(page.Success, page.Message);
            Assert.Single(page.Data!.Items);
            Assert.False(page.Data.HasMore);
            var workload = await service.GetResultRescrutinySummaryAsync(publicationId);
            Assert.True(workload.Success, workload.Message);
            Assert.Equal(1, workload.Data!.Total);
            Assert.Equal(1, workload.Data.Pending);
            Assert.Equal(0, workload.Data.AcceptedForCorrection);
            Assert.NotNull(workload.Data.OldestPendingAt);
            var export = await service.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, PageSize = 50
            });
            Assert.True(export.Success, export.Message);
            Assert.False(export.Data!.Truncated);
            Assert.Equal(caseReference, Assert.Single(export.Data.Items).CaseReference);
            var history = await service.GetResultRescrutinyHistoryAsync(caseReference);
            Assert.True(history.Success, history.Message);
            Assert.Equal(studentReference, history.Data!.StudentReference);
            Assert.Equal(ResultRescrutinyAction.Requested, Assert.Single(history.Data.Events).Action);
            var review = await service.GetResultRescrutinyReviewAsync(caseReference);
            Assert.True(review.Success, review.Message);
            Assert.Equal(studentReference, review.Data!.StudentReference);
            Assert.Equal(70m, review.Data.ObtainedMarks);
            Assert.Equal(100m, review.Data.TotalMarks);
            Assert.Equal(3m, review.Data.GPA);
            Assert.Equal(ResultRescrutinyAction.Requested, review.Data.Status);
            Assert.Equal(version, review.Data.RequestRowVersion);
            var pending = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Requested
            });
            Assert.True(pending.Success, pending.Message);
            Assert.Single(pending.Data!.Items);
            var rejected = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Rejected
            });
            Assert.True(rejected.Success, rejected.Message);
            Assert.Empty(rejected.Data!.Items);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db, new Actor(seeded.TenantId + 1, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(404, (await foreign.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId
            })).StatusCode);
            Assert.Equal(404, (await foreign.GetResultRescrutinySummaryAsync(publicationId)).StatusCode);
            Assert.Equal(404, (await foreign.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId
            })).StatusCode);
            Assert.Equal(404, (await foreign.GetResultRescrutinyHistoryAsync(caseReference)).StatusCode);
            Assert.Equal(404, (await foreign.GetResultRescrutinyReviewAsync(caseReference)).StatusCode);
            Assert.Equal(404, (await foreign.DecideResultRescrutinyAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), RequestRowVersion = version,
                Decision = ResultRescrutinyAction.Rejected,
                Reason = "This is a foreign case."
            })).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(403, (await service.DecideResultRescrutinyAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), RequestRowVersion = version,
                Decision = ResultRescrutinyAction.Rejected,
                Reason = "Self-review must be denied."
            })).StatusCode);
        }
        var decisionKey = Guid.NewGuid();
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "TenantAdmin"));
            var decision = new DecideResultRescrutinyDto
            {
                ClientRequestId = decisionKey, RequestRowVersion = version,
                Decision = ResultRescrutinyAction.AcceptedForCorrection,
                Reason = "Independent review accepted for correction investigation."
            };
            var approved = await service.DecideResultRescrutinyAsync(caseReference, decision);
            Assert.True(approved.Success, approved.Message);
            var replay = await service.DecideResultRescrutinyAsync(caseReference, decision);
            Assert.True(replay.Success, replay.Message);
            var applicant = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var intakeReplayAfterDecision = await applicant.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = requestKey, PublicationId = publicationId,
                StudentReference = studentReference, Reason = "Please verify the published mark."
            });
            Assert.True(intakeReplayAfterDecision.Success, intakeReplayAfterDecision.Message);
            Assert.Equal(caseReference, intakeReplayAfterDecision.Data!.CaseReference);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection, intakeReplayAfterDecision.Data.Status);
            Assert.Equal(decision.Reason, intakeReplayAfterDecision.Data.DecisionReason);
            Assert.Equal(seeded.TeacherId, intakeReplayAfterDecision.Data.DecidedByUserId);
            Assert.Equal(409, (await service.DecideResultRescrutinyAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), RequestRowVersion = version,
                Decision = ResultRescrutinyAction.Rejected,
                Reason = "Second decision must fail."
            })).StatusCode);
            var page = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId
            });
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection,
                Assert.Single(page.Data!.Items).Status);
            var workload = await service.GetResultRescrutinySummaryAsync(publicationId);
            Assert.True(workload.Success, workload.Message);
            Assert.Equal(1, workload.Data!.Total);
            Assert.Equal(0, workload.Data.Pending);
            Assert.Equal(1, workload.Data.AcceptedForCorrection);
            Assert.Null(workload.Data.OldestPendingAt);
            var export = await service.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.AcceptedForCorrection
            });
            Assert.True(export.Success, export.Message);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection,
                Assert.Single(export.Data!.Items).Status);
            var history = await service.GetResultRescrutinyHistoryAsync(caseReference);
            Assert.True(history.Success, history.Message);
            Assert.Equal(2, history.Data!.Events.Count);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection, history.Data.Events[1].Action);
            var reviewed = await service.GetResultRescrutinyReviewAsync(caseReference);
            Assert.True(reviewed.Success, reviewed.Message);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection, reviewed.Data!.Status);
            Assert.Equal(70m, reviewed.Data.ObtainedMarks);
            var accepted = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.AcceptedForCorrection
            });
            Assert.True(accepted.Success, accepted.Message);
            Assert.Single(accepted.Data!.Items);
            var pending = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Requested
            });
            Assert.True(pending.Success, pending.Message);
            Assert.Empty(pending.Data!.Items);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            Assert.Equal(2, await db.ResultRescrutinyEvents.AsNoTracking()
                .CountAsync(x => x.CaseReference == caseReference));
            Assert.Equal(70m, (await db.StudentResultSummaries.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == publicationId)).ObtainedMarks);
            var eventRow = await db.ResultRescrutinyEvents.SingleAsync(x =>
                x.CaseReference == caseReference && !x.IsTerminal);
            eventRow.Reason = "Tampering attempt";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Linked_student_and_guardian_can_apply_and_read_only_their_published_rescrutiny()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        Guid studentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var enrollmentId = await db.StudentSubjectRegistrations.AsNoTracking()
                .Where(x => x.Id == seeded.RegistrationId)
                .Select(x => x.StudentEnrollmentId).SingleAsync();
            var enrollment = await db.StudentEnrollments.SingleAsync(x => x.Id == enrollmentId);
            var student = await db.Students.SingleAsync(x => x.Id == enrollment.StudentId);
            student.UserId = seeded.UnassignedId;
            studentReference = student.PublicId;
            var guardian = new Guardian
            {
                TenantId = seeded.TenantId, PersonId = student.PersonId,
                UserId = seeded.TeacherId, FullName = "Synthetic linked guardian"
            };
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();
            db.StudentGuardians.Add(new StudentGuardian
            {
                TenantId = seeded.TenantId, StudentId = student.Id,
                GuardianId = guardian.Id, RelationCode = "Parent"
            });
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published, PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId,
                VisibleToStudent = true, VisibleToGuardian = true
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId,
                StudentEnrollmentId = enrollment.Id, PublicationVersionNo = 1,
                TotalMarks = 100m, ObtainedMarks = 70m, Percentage = 70m,
                GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        var request = new RequestResultRescrutinyDto
        {
            ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
            StudentReference = studentReference,
            Reason = "Please review the published result for this student."
        };
        Guid caseReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var student = Service(db, new Actor(seeded.TenantId, seeded.UnassignedId, "Student"));
            var created = await student.RequestResultRescrutinyAsync(request);
            Assert.True(created.Success, created.Message);
            caseReference = created.Data!.CaseReference;
            var replay = await student.RequestResultRescrutinyAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(caseReference, replay.Data!.CaseReference);
            var page = await student.SearchLinkedResultRescrutinyAsync(studentReference, publicationId, null);
            Assert.True(page.Success, page.Message);
            Assert.Equal(caseReference, Assert.Single(page.Data!.Items).CaseReference);
            Assert.Equal(ResultRescrutinyAction.Requested, page.Data.Items[0].Status);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var guardian = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "Guardian"));
            var page = await guardian.SearchLinkedResultRescrutinyAsync(studentReference, publicationId, null);
            Assert.True(page.Success, page.Message);
            Assert.Equal(caseReference, Assert.Single(page.Data!.Items).CaseReference);
            var duplicate = await guardian.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference, Reason = "A second application is not allowed."
            });
            Assert.Equal(409, duplicate.StatusCode);
            var stranger = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "Guardian"));
            Assert.Equal(404, (await stranger.SearchLinkedResultRescrutinyAsync(
                studentReference, publicationId, null)).StatusCode);
            Assert.Equal(404, (await stranger.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference, Reason = "Unlinked guardian attempt must be denied."
            })).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db, new Actor(seeded.TenantId + 1, seeded.UnassignedId, "Student"));
            Assert.Equal(404, (await foreign.SearchLinkedResultRescrutinyAsync(
                studentReference, publicationId, null)).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var administrator = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var pending = await administrator.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Requested
            });
            Assert.Equal(caseReference, Assert.Single(pending.Data!.Items).CaseReference);
            Assert.Equal(70m, (await db.StudentResultSummaries.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == publicationId)).ObtainedMarks);
        }
    }

    [Fact]
    public async Task Teacher_submission_rejection_correction_resubmission_and_independent_approval_are_atomic()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        var tenant = seeded.TenantId;
        var subjectId = seeded.AssessmentSubjectId;
        var teacher = new Actor(tenant, seeded.TeacherId, "Teacher");
        var reviewer = new Actor(tenant, seeded.ReviewerId, "TenantAdmin");
        var stranger = new Actor(tenant, seeded.UnassignedId, "Teacher");

        await using (var db = TenantDb(options, tenant))
        {
            var unassigned = await Service(db, stranger).GetMarkReviewAsync(subjectId);
            Assert.Equal(403, unassigned.StatusCode);
        }
        await using (var db = TenantDb(options, tenant + 1))
        {
            var foreign = await Service(db, new Actor(tenant + 1, seeded.ReviewerId, "TenantAdmin"))
                .GetMarkReviewAsync(subjectId);
            Assert.Equal(404, foreign.StatusCode);
        }

        var submissionKey = Guid.NewGuid();
        string initialAssessmentVersion;
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var before = await service.GetMarkReviewAsync(subjectId);
            Assert.True(before.Success, before.Message);
            Assert.True(before.Data!.CanSubmit);
            initialAssessmentVersion = before.Data.AssessmentRowVersion;
            var submitted = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.True(submitted.Success, submitted.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, submitted.Data!.CurrentDecision);
            Assert.False(submitted.Data.CanSubmit);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var replay = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.True(replay.Success, replay.Message);
            var changedPayload = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Rejected, "Altered"));
            // Authorization rejects a teacher trying to forge a reviewer decision before replay checks.
            Assert.Equal(403, changedPayload.StatusCode);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var blocked = await service.SaveMarksRegisterAsync(MarkChange(
                subjectId, seeded.RegistrationId, mark, 80m));
            Assert.Equal(409, blocked.StatusCode);
            var selfApproval = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), (await service.GetMarkReviewAsync(subjectId)).Data!
                    .AssessmentRowVersion, AssessmentMarkReviewDecision.Approved));
            Assert.Equal(403, selfApproval.StatusCode);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, reviewer);
            var pending = await service.GetMarkReviewAsync(subjectId);
            Assert.True(pending.Success, pending.Message);
            Assert.True(pending.Data!.CanReview);
            var reusedTeacherKey = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Rejected, "Forged review"));
            Assert.Equal(409, reusedTeacherKey.StatusCode);
            var rejected = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Rejected, "Component totals require checking"));
            Assert.True(rejected.Success, rejected.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Rejected, rejected.Data!.CurrentDecision);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var corrected = await service.SaveMarksRegisterAsync(
                MarkChange(subjectId, seeded.RegistrationId, mark, 80m));
            Assert.True(corrected.Success, corrected.Message);
        }
        var resubmissionKey = Guid.NewGuid();
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var correctedReview = await service.GetMarkReviewAsync(subjectId);
            Assert.True(correctedReview.Data!.CanSubmit);
            var resubmitted = await service.DecideMarkReviewAsync(subjectId,
                Decision(resubmissionKey, correctedReview.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(resubmitted.Success, resubmitted.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, resubmitted.Data!.CurrentDecision);
            // Old request keys cannot acknowledge a superseded decision.
            var obsolete = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.Equal(409, obsolete.StatusCode);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, reviewer);
            var pending = await service.GetMarkReviewAsync(subjectId);
            Assert.True(pending.Data!.CanReview);
            var stale = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), initialAssessmentVersion, AssessmentMarkReviewDecision.Approved));
            Assert.Equal(409, stale.StatusCode);
            var approved = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Approved));
            Assert.True(approved.Success, approved.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Approved, approved.Data!.CurrentDecision);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var frozen = await service.SaveMarksRegisterAsync(
                MarkChange(subjectId, seeded.RegistrationId, mark, 90m));
            Assert.Equal(409, frozen.StatusCode);
            var status = await service.GetMarkReviewAsync(subjectId);
            Assert.False(status.Data!.CanSubmit);
            Assert.False(status.Data.CanReview);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var decisions = await db.AssessmentMarkReviewEvents.AsNoTracking()
                .Where(x => x.TenantId == tenant && x.AssessmentSubjectId == subjectId)
                .OrderBy(x => x.Id).Select(x => x.Decision).ToArrayAsync();
            Assert.Equal(new[] {
                AssessmentMarkReviewDecision.Submitted, AssessmentMarkReviewDecision.Rejected,
                AssessmentMarkReviewDecision.Submitted, AssessmentMarkReviewDecision.Approved
            }, decisions);
            Assert.Equal(80m, (await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId)).ObtainedMarks);
            Assert.Equal(4, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == subjectId));
        }
    }

    [Fact]
    public async Task Teacher_submission_reconciles_actual_component_values_before_persisting_review()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var first = new AssessmentComponent {
                TenantId = seeded.TenantId, AssessmentSubjectId = seeded.AssessmentSubjectId,
                Name = "Written", Code = "WRITTEN", FullMarks = 100m, Weightage = 50m
            };
            var second = new AssessmentComponent {
                TenantId = seeded.TenantId, AssessmentSubjectId = seeded.AssessmentSubjectId,
                Name = "Practical", Code = "PRACTICAL", FullMarks = 100m, Weightage = 50m
            };
            db.AssessmentComponents.AddRange(first, second);
            await db.SaveChangesAsync();
            var master = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId);
            db.StudentAssessmentComponentMarks.AddRange(
                new StudentAssessmentComponentMark {
                    TenantId = seeded.TenantId, AssessmentComponentId = first.Id,
                    StudentAssessmentMarkId = master.Id, ObtainedMarks = 60m,
                    EnteredByUserId = seeded.TeacherId
                },
                new StudentAssessmentComponentMark {
                    TenantId = seeded.TenantId, AssessmentComponentId = second.Id,
                    StudentAssessmentMarkId = master.Id, ObtainedMarks = 80m,
                    EnteredByUserId = seeded.TeacherId
                });
            await db.SaveChangesAsync();
        }
        var teacher = new Actor(seeded.TenantId, seeded.TeacherId, "Teacher");
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var review = await service.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            Assert.True(review.Success, review.Message);
            var invalid = await service.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                Decision(Guid.NewGuid(), review.Data!.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.Equal(409, invalid.StatusCode);
            Assert.Empty(await db.AssessmentMarkReviewEvents.AsNoTracking().ToListAsync());
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var part = await db.StudentAssessmentComponentMarks.SingleAsync(x =>
                x.ObtainedMarks == 80m);
            part.ObtainedMarks = 90m;
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var review = await service.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            var valid = await service.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                Decision(Guid.NewGuid(), review.Data!.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(valid.Success, valid.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, valid.Data!.CurrentDecision);
            Assert.Single(await db.AssessmentMarkReviewEvents.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Historical_grade_distribution_and_version_comparison_execute_on_real_sql_with_tenant_guards()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long baselineId;
        long targetId;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var registration = await db.StudentSubjectRegistrations.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.RegistrationId);
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var baseline = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Withdrawn,
                PublishedAt = DateTime.UtcNow.AddMinutes(-10),
                PublishedByUserId = seeded.ReviewerId
            };
            var target = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 2,
                State = ResultPublicationState.Published,
                PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.AddRange(baseline, target);
            await db.SaveChangesAsync();
            baselineId = baseline.Id;
            targetId = target.Id;
            db.StudentResultSummaries.AddRange(
                new StudentResultSummary
                {
                    TenantId = seeded.TenantId, ResultPublicationId = baselineId,
                    AssessmentId = subject.AssessmentId,
                    StudentEnrollmentId = registration.StudentEnrollmentId,
                    PublicationVersionNo = 1, TotalMarks = 100m, ObtainedMarks = 75m,
                    Percentage = 75m, GPA = 3m, GradeLetter = "   ", IsPassed = true
                },
                new StudentResultSummary
                {
                    TenantId = seeded.TenantId, ResultPublicationId = targetId,
                    AssessmentId = subject.AssessmentId,
                    StudentEnrollmentId = registration.StudentEnrollmentId,
                    PublicationVersionNo = 2, TotalMarks = 100m, ObtainedMarks = 90m,
                    Percentage = 90m, GPA = 4m, GradeLetter = " A ", IsPassed = true
                });
            await db.SaveChangesAsync();
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var baseline = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = baselineId });
            Assert.True(baseline.Success, baseline.Message);
            Assert.Equal(1, baseline.Data!.StudentCount);
            Assert.Equal(1, baseline.Data.PassedCount);
            Assert.Equal("Unassigned", Assert.Single(baseline.Data.Grades).GradeLetter);
            Assert.Equal(75m, baseline.Data.Grades[0].TotalObtainedMarks);

            var target = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = targetId });
            Assert.True(target.Success, target.Message);
            Assert.Equal(90m, Assert.Single(target.Data!.Grades).TotalObtainedMarks);

            var comparison = await service.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = baselineId, TargetPublicationId = targetId });
            Assert.True(comparison.Success, comparison.Message);
            Assert.Equal(0, comparison.Data!.StudentCountChange);
            Assert.Equal(-1, comparison.Data.Grades.Single(x =>
                x.GradeLetter == "Unassigned").CountChange);
            var added = comparison.Data.Grades.Single(x => x.GradeLetter == "A");
            Assert.Equal(1, added.CountChange);
            Assert.Equal(90m, added.ObtainedMarksChange);
            Assert.Null(added.AveragePercentageChange);

            var reversed = await service.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = targetId, TargetPublicationId = baselineId });
            Assert.Equal(409, reversed.StatusCode);
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var teacher = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "Teacher"));
            Assert.Equal(403, (await teacher.GetPublishedGradeDistributionAsync(
                new() { PublicationId = baselineId })).StatusCode);
            Assert.Equal(403, (await teacher.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = baselineId,
                    TargetPublicationId = targetId })).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db,
                new Actor(seeded.TenantId + 1, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(404, (await foreign.GetPublishedGradeDistributionAsync(
                new() { PublicationId = targetId })).StatusCode);
            Assert.Equal(404, (await foreign.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = baselineId,
                    TargetPublicationId = targetId })).StatusCode);
        }
    }

    [Fact]
    public async Task Historical_grade_report_fails_closed_when_finalized_snapshot_is_soft_deleted()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var enrollmentId = await db.StudentSubjectRegistrations.AsNoTracking()
                .Where(x => x.Id == seeded.RegistrationId)
                .Select(x => x.StudentEnrollmentId).SingleAsync();
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published,
                PublishedAt = DateTime.UtcNow, PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId, StudentEnrollmentId = enrollmentId,
                PublicationVersionNo = 1, TotalMarks = 100m, ObtainedMarks = 75m,
                Percentage = 75m, GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db,
                new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var valid = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = publicationId });
            Assert.True(valid.Success, valid.Message);

            // Simulate corrupted historical evidence via SQL, deliberately bypassing
            // the EF append-only guard in this disposable integration database.
            var affected = await db.StudentResultSummaries
                .Where(x => x.ResultPublicationId == publicationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDeleted, true));
            Assert.Equal(1, affected);
            var denied = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = publicationId });
            Assert.Equal(409, denied.StatusCode);
            Assert.Null(denied.Data);
        }
    }


    [Fact]
    public async Task Competing_review_approvals_commit_once_and_lock_requires_the_winning_review()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        var teacher = new Actor(seeded.TenantId, seeded.TeacherId, "Teacher");
        var reviewer = new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin");
        Guid examReference;

        // The lock calculation uses the canonical grade scheme, not a test-only bypass.
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var exam = await db.Assessments.SingleAsync(x => x.Id == subject.AssessmentId);
            examReference = exam.PublicId;
            var scheme = new GradeScheme
            {
                TenantId = seeded.TenantId, Name = "Concurrent review grading",
                Code = "REVIEW-" + Guid.NewGuid().ToString("N")[..12]
            };
            db.GradeSchemes.Add(scheme);
            await db.SaveChangesAsync();
            db.GradeRules.Add(new GradeRule
            {
                TenantId = seeded.TenantId, GradeSchemeId = scheme.Id,
                MinMarks = 0m, MaxMarks = 100m, GradeLetter = "A",
                GradePoint = 4m, DisplayOrder = 1
            });
            exam.GradeSchemeId = scheme.Id;
            await db.SaveChangesAsync();
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var review = await service.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            Assert.True(review.Success, review.Message);
            var submitted = await service.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                Decision(Guid.NewGuid(), review.Data!.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(submitted.Success, submitted.Message);
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, reviewer);
            var exam = await service.GetAssessmentAsync(examReference);
            Assert.True(exam.Success, exam.Message);
            var prematureLock = await service.ChangeAssessmentStateAsync(examReference,
                new ChangeAssessmentStateRequestDto
                {
                    State = AssessmentState.Locked, RowVersion = exam.Data!.RowVersion
                });
            Assert.Equal(409, prematureLock.StatusCode);
        }

        await using (var firstDb = TenantDb(options, seeded.TenantId))
        await using (var secondDb = TenantDb(options, seeded.TenantId))
        {
            var first = Service(firstDb, reviewer);
            var second = Service(secondDb, reviewer);
            var firstView = await first.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            var secondView = await second.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            Assert.True(firstView.Data!.CanReview);
            Assert.True(secondView.Data!.CanReview);

            var outcomes = await Task.WhenAll(
                first.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                    Decision(Guid.NewGuid(), firstView.Data.AssessmentRowVersion,
                        AssessmentMarkReviewDecision.Approved)),
                second.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                    Decision(Guid.NewGuid(), secondView.Data.AssessmentRowVersion,
                        AssessmentMarkReviewDecision.Approved)));
            Assert.Single(outcomes.Where(x => x.Success));
            Assert.Single(outcomes.Where(x => x.StatusCode == 409));
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            Assert.Equal(2, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId));
            Assert.Equal(1, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId &&
                    x.Decision == AssessmentMarkReviewDecision.Approved));
            var service = Service(db, reviewer);
            var exam = await service.GetAssessmentAsync(examReference);
            Assert.True(exam.Success, exam.Message);
            var locked = await service.ChangeAssessmentStateAsync(examReference,
                new ChangeAssessmentStateRequestDto
                {
                    State = AssessmentState.Locked, RowVersion = exam.Data!.RowVersion
                });
            Assert.True(locked.Success, locked.Message);
            Assert.Equal(AssessmentState.Locked, locked.Data!.State);
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId);
            var edit = await service.SaveMarksRegisterAsync(
                MarkChange(seeded.AssessmentSubjectId, seeded.RegistrationId, mark, 85m));
            Assert.Equal(409, edit.StatusCode);
            Assert.Equal(75m, (await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.Id == mark.Id)).ObtainedMarks);
        }
    }

    private static AssessmentMarkReviewRequestDto Decision(Guid key, string version,
        AssessmentMarkReviewDecision decision, string? reason = null) => new()
    {
        ClientRequestId = key, AssessmentRowVersion = version,
        Decision = decision, Reason = reason
    };

    private static SaveMarksRegisterRequestDto MarkChange(long subjectId, long registrationId,
        StudentAssessmentMark mark, decimal marks) => new()
    {
        AssessmentSubjectId = subjectId,
        Marks = [new SaveStudentAssessmentMarkRequestDto
        {
            StudentSubjectRegistrationId = registrationId, ObtainedMarks = marks,
            RowVersion = Convert.ToBase64String(mark.RowVersion)
        }]
    };

    private static AssessmentAdministrationService Service(EduOSDbContext db, Actor actor)
    {
        GenericRepository<T> Repo<T>() where T : class => new(db);
        return new AssessmentAdministrationService(
            Repo<Assessment>(), Repo<AssessmentSubject>(), Repo<AssessmentMarkReviewEvent>(),
            Repo<AssessmentSchedule>(), Repo<StudentAssessmentMark>(), Repo<ResultPublication>(),
            Repo<StudentResultSummary>(), Repo<GradeScheme>(), Repo<GradeRule>(),
            Repo<StudentEnrollment>(), Repo<Subject>(), Repo<StudentSubjectRegistration>(),
            Repo<SubjectOffering>(), Repo<CurriculumSubject>(), Repo<AcademicBatch>(),
            Repo<AcademicYear>(), Repo<AcademicTerm>(), Repo<Campus>(), Repo<UserCampusAccess>(),
            Repo<Room>(), Repo<Student>(), Repo<Employee>(), Repo<InstructorAssignment>(),
            Repo<AssessmentComponent>(), Repo<StudentAssessmentComponentMark>(),
            Repo<CertificateTemplate>(), Repo<CertificateIssue>(), Repo<TranscriptIssue>(),
            Repo<EduOS.Core.Entities.System.AuditLog>(), db, actor, TimeProvider.System,
            NullLogger<AssessmentAdministrationService>.Instance,
            Repo<ResultRescrutinyEvent>(), Repo<Guardian>(), Repo<StudentGuardian>());
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static async Task<Seeded> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new EduOSDbContext(options);
        var tenant = new Tenant { Code = "CI-EXFLOW-" + suffix, Name = "Review lifecycle",
            Email = "exflow-" + suffix + "@example.test", State = TenantState.Active };
        ApplicationUser User(string role) {
            var name = "exflow-" + role + "-" + suffix + "@example.test";
            return new ApplicationUser { UserName = name,
                NormalizedUserName = name.ToUpperInvariant(), Email = name,
                NormalizedEmail = name.ToUpperInvariant(), FullName = role,
                IsActive = true, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
        }
        var teacher = User("teacher");
        var reviewer = User("reviewer");
        var unassigned = User("unassigned");
        var teacherPerson = new Person { FullName = "Synthetic teacher" };
        var studentPerson = new Person { FullName = "Synthetic student" };
        setup.AddRange(tenant, teacher, reviewer, unassigned, teacherPerson, studentPerson);
        await setup.SaveChangesAsync();
        var tenantId = tenant.Id;
        setup.ChangeTracker.Clear();
        using (setup.BeginSystemTenantScope(tenantId))
        {
            var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
            var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
            var program = new AcademicProgram { TenantId = tenantId, Name = "School", Code = "SCH" };
            var subject = new Subject { TenantId = tenantId, Name = "Math", Code = "MATH" };
            var designation = new Designation { TenantId = tenantId, Name = "Teacher", Code = "TCHR" };
            var student = new Student { TenantId = tenantId, PersonId = studentPerson.Id,
                FullName = "Synthetic student", StudentCode = "S-" + suffix,
                StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) };
            setup.AddRange(campus, year, program, subject, designation, student);
            await setup.SaveChangesAsync();
            var employee = new Employee { TenantId = tenantId, PersonId = teacherPerson.Id,
                UserId = teacher.Id, DesignationId = designation.Id, EmployeeCode = "T-" + suffix,
                FullName = "Synthetic teacher", CanTeach = true, State = EmployeeState.Active,
                JoiningDate = new DateOnly(2026, 1, 1) };
            var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                Name = "One", Code = "ONE", LevelNo = 1 };
            var curriculum = new AcademicCurriculum { TenantId = tenantId,
                AcademicProgramId = program.Id, Name = "Curriculum", Code = "CUR",
                EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
            setup.AddRange(employee, level, curriculum);
            await setup.SaveChangesAsync();
            var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, Name = "Class One", Code = "C1" };
            var curriculumSubject = new CurriculumSubject { TenantId = tenantId,
                AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
                SubjectId = subject.Id };
            setup.AddRange(batch, curriculumSubject);
            await setup.SaveChangesAsync();
            var offering = new SubjectOffering { TenantId = tenantId,
                AcademicBatchId = batch.Id, AcademicYearId = year.Id,
                CurriculumSubjectId = curriculumSubject.Id, Code = "MATH" };
            var exam = new Assessment { TenantId = tenantId, CampusId = campus.Id,
                AcademicYearId = year.Id, Name = "Final", Code = "FINAL",
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 2),
                State = AssessmentState.MarksEntry };
            var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                AcademicYearId = year.Id, CampusId = campus.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                AcademicBatchId = batch.Id, AcademicCurriculumId = curriculum.Id,
                RollNo = "01", EnrollmentDate = new DateOnly(2026, 1, 1),
                State = EnrollmentState.Active, IsCurrent = true, ClientRequestId = Guid.NewGuid() };
            setup.AddRange(offering, exam, enrollment);
            await setup.SaveChangesAsync();
            var assessmentSubject = new AssessmentSubject { TenantId = tenantId,
                AssessmentId = exam.Id, SubjectOfferingId = offering.Id };
            var registration = new StudentSubjectRegistration { TenantId = tenantId,
                StudentEnrollmentId = enrollment.Id, SubjectOfferingId = offering.Id,
                ClientRequestId = Guid.NewGuid(), State = SubjectRegistrationState.Approved };
            var assignment = new InstructorAssignment { TenantId = tenantId,
                SubjectOfferingId = offering.Id, EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
            setup.AddRange(assessmentSubject, registration, assignment);
            await setup.SaveChangesAsync();
            var mark = new StudentAssessmentMark { TenantId = tenantId,
                AssessmentSubjectId = assessmentSubject.Id,
                StudentSubjectRegistrationId = registration.Id, ObtainedMarks = 75m,
                EnteredByUserId = teacher.Id, EnteredAt = DateTime.UtcNow };
            setup.StudentAssessmentMarks.Add(mark);
            await setup.SaveChangesAsync();
            return new Seeded(tenantId, teacher.Id, reviewer.Id, unassigned.Id,
                assessmentSubject.Id, registration.Id);
        }
    }

    private sealed record Seeded(long TenantId, long TeacherId, long ReviewerId,
        long UnassignedId, long AssessmentSubjectId, long RegistrationId);

    private sealed class Actor(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "SQL exam test";
        public string? Email => "sql-exam@example.test";
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL exam lifecycle";
    }
}
