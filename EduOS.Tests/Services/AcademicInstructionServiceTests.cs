using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicInstructionServiceTests
{
    private static readonly DateOnly LessonDate = new(2026, 9, 20);

    [Fact]
    public async Task Lesson_plan_creation_is_tenant_scoped_and_exact_retry_is_idempotent()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var service = Service(db, 101, 7, "TenantAdmin");
        var request = Lesson(seed.OfferingReference);
        var created = await service.CreateLessonPlanAsync(request);
        var replay = await service.CreateLessonPlanAsync(request);
        var changed = Lesson(seed.OfferingReference);
        changed.Title = request.Title; changed.Content = "Different lesson material";
        var conflict = await service.CreateLessonPlanAsync(changed);
        created.StatusCode.Should().Be(201);
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(created.Data!.Id);
        created.Data.SubjectOfferingReference.Should().Be(seed.OfferingReference);
        replay.Data.SubjectOfferingReference.Should().Be(seed.OfferingReference);
        conflict.StatusCode.Should().Be(409);
        (await db.LessonPlans.CountAsync()).Should().Be(1);

        await using var otherDb = Context(options, 202, 8, "TenantAdmin");
        var other = Service(otherDb, 202, 8, "TenantAdmin");
        (await other.CreateLessonPlanAsync(Lesson(seed.OfferingReference))).StatusCode.Should().Be(404);
        var page = await other.GetLessonPlansAsync(null, null, null, 1, 25);
        page.Success.Should().BeTrue();
        page.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Assigned_teacher_submits_and_manager_reviews_canonical_lesson_plan()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long id;
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var created = await teacher.CreateLessonPlanAsync(Lesson(seed.OfferingReference));
            created.StatusCode.Should().Be(201);
            created.Data!.State.Should().Be(LessonPlanState.Draft);
            id = created.Data.Id;
        }
        var version = await VersionAsync(options, id, [1, 2, 3, 4, 5, 6, 7, 8]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var submitted = await teacher.SubmitLessonPlanAsync(id, version);
            submitted.Success.Should().BeTrue();
            submitted.Data!.State.Should().Be(LessonPlanState.Submitted);
        }
        var reviewVersion = await VersionAsync(options, id, [2, 3, 4, 5, 6, 7, 8, 9]);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var reviewed = await manager.ReviewLessonPlanAsync(id, new ReviewLessonPlanRequestDto
            {
                Approve = true, Note = "Ready for instruction", RowVersion = reviewVersion
            });
            reviewed.Success.Should().BeTrue();
            reviewed.Data!.State.Should().Be(LessonPlanState.Approved);
            reviewed.Data.ReviewedByUserId.Should().Be(7);
            var stale = await manager.UpdateLessonPlanAsync(id, new SaveLessonPlanRequestDto
            {
                ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = seed.OfferingReference,
                LessonDate = LessonDate, Title = "Changed after approval",
                RowVersion = reviewVersion
            });
            stale.StatusCode.Should().Be(409);
        }
    }

    [Fact]
    public async Task Approved_teacher_starts_and_completes_lesson_with_audit_and_retry_safety()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long id;
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            id = (await teacher.CreateLessonPlanAsync(Lesson(seed.OfferingReference))).Data!.Id;
        }
        var beforeReview = await VersionAsync(options, id, [1, 2, 3, 4, 5, 6, 7, 8]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            (await teacher.SubmitLessonPlanAsync(id, beforeReview)).Success.Should().BeTrue();
        }
        var reviewVersion = await VersionAsync(options, id, [2, 3, 4, 5, 6, 7, 8, 9]);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var reviewed = await manager.ReviewLessonPlanAsync(id, new ReviewLessonPlanRequestDto
            {
                Approve = true, RowVersion = reviewVersion
            });
            reviewed.Success.Should().BeTrue(reviewed.Message);
            (await manager.StartLessonPlanAsync(id, new AdvanceLessonPlanRequestDto
            {
                RowVersion = reviewVersion
            })).StatusCode.Should().Be(403);
        }
        var startVersion = await VersionAsync(options, id, [3, 4, 5, 6, 7, 8, 9, 10]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var start = await teacher.StartLessonPlanAsync(id, new AdvanceLessonPlanRequestDto { RowVersion = startVersion });
            start.Success.Should().BeTrue();
            start.Data!.State.Should().Be(LessonPlanState.InProgress);
            start.Data.CompletedAt.Should().BeNull();
            var retry = await teacher.StartLessonPlanAsync(id, new AdvanceLessonPlanRequestDto { RowVersion = startVersion });
            retry.Success.Should().BeTrue();
            retry.Data!.State.Should().Be(LessonPlanState.InProgress);
        }
        var completionVersion = await VersionAsync(options, id, [4, 5, 6, 7, 8, 9, 10, 11]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var completed = await teacher.CompleteLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = completionVersion });
            completed.Success.Should().BeTrue();
            completed.Data!.State.Should().Be(LessonPlanState.Completed);
            completed.Data.CompletedAt.Should().NotBeNull();
            (await teacher.CompleteLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = completionVersion })).Success.Should().BeTrue();
            (await teacher.StartLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = startVersion })).StatusCode.Should().Be(409);
        }
        await using (var db = Context(options, 202, 8, "Teacher"))
        {
            (await Service(db, 202, 8, "Teacher").CompleteLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = completionVersion })).StatusCode.Should().Be(404);
        }
    }

    [Fact]
    public async Task Unapproved_or_future_lesson_cannot_enter_delivery()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long id;
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var request = Lesson(seed.OfferingReference);
            request.LessonDate = new DateOnly(2026, 12, 1);
            var created = await teacher.CreateLessonPlanAsync(request);
            created.Success.Should().BeTrue();
            id = created.Data!.Id;
        }
        var draftVersion = await VersionAsync(options, id, [1, 2, 3, 4, 5, 6, 7, 8]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            (await teacher.StartLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = draftVersion })).StatusCode.Should().Be(409);
            (await teacher.SubmitLessonPlanAsync(id, draftVersion)).Success.Should().BeTrue();
        }
        var submittedVersion = await VersionAsync(options, id, [2, 3, 4, 5, 6, 7, 8, 9]);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            (await Service(db, 101, 7, "TenantAdmin").ReviewLessonPlanAsync(id,
                new ReviewLessonPlanRequestDto { Approve = true, RowVersion = submittedVersion }))
                .Success.Should().BeTrue();
        }
        var approvedVersion = await VersionAsync(options, id, [3, 4, 5, 6, 7, 8, 9, 10]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher",
                new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero)));
            (await teacher.StartLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = approvedVersion })).StatusCode.Should().Be(409);
            (await teacher.CompleteLessonPlanAsync(id,
                new AdvanceLessonPlanRequestDto { RowVersion = approvedVersion })).StatusCode.Should().Be(409);
        }
    }

    [Fact]
    public async Task Substitution_is_retry_safe_tenant_scoped_and_cancellable_with_rowversion()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long id;
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var request = new CreateSubstitutionRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
                Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference,
                Reason = "Approved teacher absence"
            };
            var created = await manager.CreateSubstitutionAsync(request);
            var replay = await manager.CreateSubstitutionAsync(request);
            created.StatusCode.Should().Be(201);
            replay.Data!.Id.Should().Be(created.Data!.Id);
            id = created.Data.Id;
            (await db.Substitutions.CountAsync()).Should().Be(1);
        }
        await using (var db = Context(options, 101, 71, "Teacher"))
        {
            var teacher = Service(db, 101, 71, "Teacher");
            var rows = await teacher.GetSubstitutionsAsync(LessonDate, LessonDate, seed.BatchId);
            rows.Success.Should().BeTrue();
            rows.Data.Should().ContainSingle();
            rows.Data![0].Id.Should().Be(id);
            rows.Data[0].BatchName.Should().Be("Batch A");
            rows.Data[0].SubjectName.Should().Be("Mathematics");
            rows.Data[0].OriginalTeacherName.Should().Be("Original teacher");
            rows.Data[0].SubstituteEmployeeName.Should().Be("Substitute teacher");
        }
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var originalTeacher = Service(db, 101, 70, "Teacher");
            var ownSubstitutions = await originalTeacher.GetSubstitutionsAsync(LessonDate, LessonDate, seed.BatchId);
            ownSubstitutions.Success.Should().BeTrue();
            ownSubstitutions.Data.Should().ContainSingle();
            ownSubstitutions.Data![0].Id.Should().Be(id);
        }
        var version = await VersionAsync(options, id, [7, 6, 5, 4, 3, 2, 1, 8], true);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var cancelled = await manager.CancelSubstitutionAsync(id,
                new CancelSubstitutionRequestDto { RowVersion = version });
            cancelled.Success.Should().BeTrue();
            cancelled.Data!.IsCancelled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Lesson_plan_register_is_paged_date_scoped_and_tenant_isolated()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var first = Lesson(seed.OfferingReference);
            var second = Lesson(seed.OfferingReference);
            second.Title = "Quadratic equations";
            (await manager.CreateLessonPlanAsync(first)).Success.Should().BeTrue();
            (await manager.CreateLessonPlanAsync(second)).Success.Should().BeTrue();

            var firstPage = await manager.GetLessonPlansAsync(seed.BatchId, LessonDate, LessonDate, 1, 1);
            var secondPage = await manager.GetLessonPlansAsync(seed.BatchId, LessonDate, LessonDate, 2, 1);
            firstPage.Success.Should().BeTrue();
            secondPage.Success.Should().BeTrue();
            firstPage.Data!.TotalCount.Should().Be(2);
            firstPage.Data.Items.Should().ContainSingle();
            firstPage.Data.Items[0].SubjectOfferingReference.Should().Be(seed.OfferingReference);
            secondPage.Data!.Items.Should().ContainSingle();
            firstPage.Data.Items[0].Id.Should().NotBe(secondPage.Data.Items[0].Id);

            (await manager.GetLessonPlansAsync(seed.BatchId, LessonDate.AddDays(1),
                LessonDate.AddDays(1), 1, 20)).Data!.Items.Should().BeEmpty();
            (await manager.GetLessonPlansAsync(seed.BatchId, LessonDate, LessonDate, 1, 101))
                .StatusCode.Should().Be(400);
        }
        await using (var otherDb = Context(options, 202, 8, "TenantAdmin"))
        {
            var other = Service(otherDb, 202, 8, "TenantAdmin");
            var page = await other.GetLessonPlansAsync(null, LessonDate, LessonDate, 1, 20);
            page.Success.Should().BeTrue();
            page.Data!.Items.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Lesson_offering_selector_is_paged_and_teacher_and_tenant_scoped()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var result = await manager.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20);
            result.Success.Should().BeTrue();
            result.Data!.Items.Should().ContainSingle();
            result.Data.Items[0].Reference.Should().Be(seed.OfferingReference);
            (await manager.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 101))
                .StatusCode.Should().Be(400);
        }
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var result = await teacher.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20);
            result.Success.Should().BeTrue();
            result.Data!.Items.Should().ContainSingle();
        }
        await using (var db = Context(options, 101, 71, "Teacher"))
        {
            var unassigned = Service(db, 101, 71, "Teacher");
            var result = await unassigned.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20);
            result.Success.Should().BeTrue();
            result.Data!.Items.Should().BeEmpty();
        }
        await using (var db = Context(options, 202, 8, "TenantAdmin"))
        {
            var other = Service(db, 202, 8, "TenantAdmin");
            (await other.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20))
                .StatusCode.Should().Be(404);
        }
    }

    [Fact]
    public async Task Revoked_original_teacher_assignment_cannot_read_private_substitution_history()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            (await manager.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
                Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
            })).StatusCode.Should().Be(201);
            var assignmentId = (await db.RoutineEntries.SingleAsync(x => x.Id == seed.RoutineEntryId))
                .InstructorAssignmentId;
            (await db.InstructorAssignments.SingleAsync(x => x.Id == assignmentId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var result = await Service(db, 101, 70, "Teacher")
                .GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 1, 20);
            result.Data!.Items.Should().BeEmpty();
        }
        await using (var db = Context(options, 101, 71, "Teacher"))
        {
            var result = await Service(db, 101, 71, "Teacher")
                .GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 1, 20);
            result.Data!.Items.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Substitution_routine_choices_are_date_eligible_searchable_and_sql_paged()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long secondRoutineId;
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var assignmentId = await db.RoutineEntries.Where(x => x.Id == seed.RoutineEntryId)
                .Select(x => x.InstructorAssignmentId).SingleAsync();
            var slot = new RoutineTimeSlot
            {
                TenantId = 101, Name = "Period Two",
                StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0)
            };
            db.RoutineTimeSlots.Add(slot);
            await db.SaveChangesAsync();
            var entry = new RoutineEntry
            {
                TenantId = 101, SubjectOfferingId = (await db.RoutineEntries
                    .SingleAsync(x => x.Id == seed.RoutineEntryId)).SubjectOfferingId,
                InstructorAssignmentId = assignmentId, RoutineTimeSlotId = slot.Id,
                DayOfWeek = LessonDate.DayOfWeek, EffectiveFrom = LessonDate.AddDays(-7)
            };
            db.RoutineEntries.Add(entry);
            await db.SaveChangesAsync();
            secondRoutineId = entry.Id;
        }
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var service = Service(db, 101, 7, "TenantAdmin");
            var first = await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 1);
            var second = await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 2, 1);
            first.Success.Should().BeTrue();
            first.Data!.TotalCount.Should().Be(2);
            first.Data.Items.Should().ContainSingle();
            second.Data!.Items.Should().ContainSingle();
            first.Data.Items[0].Id.Should().NotBe(second.Data.Items[0].Id);
            var filtered = await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, "Period Two", 1, 20);
            filtered.Data!.Items.Should().ContainSingle(x => x.Id == secondRoutineId);
            (await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate.AddDays(1), null, 1, 20))
                .Data!.Items.Should().BeEmpty();
            (await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 0, 20))
                .StatusCode.Should().Be(400);
            (await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 101))
                .StatusCode.Should().Be(400);
        }
        await using (var db = Context(options, 202, 8, "TenantAdmin"))
            (await Service(db, 202, 8, "TenantAdmin")
                .GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 20))
                .StatusCode.Should().Be(404);
        await using (var db = Context(options, 101, 70, "Teacher"))
            (await Service(db, 101, 70, "Teacher")
                .GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 20))
                .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Direct_routine_id_cannot_schedule_cover_after_subject_offering_is_deactivated()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var offering = await db.SubjectOfferings.SingleAsync(x => x.PublicId == seed.OfferingReference);
        offering.IsActive = false;
        await db.SaveChangesAsync();
        var manager = Service(db, 101, 7, "TenantAdmin");
        (await manager.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 20))
            .Data!.Items.Should().BeEmpty();
        (await manager.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        })).StatusCode.Should().Be(409);
        (await db.Substitutions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Expired_original_instructor_assignment_blocks_substitution_and_routine_selection()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var assignmentId = (await db.RoutineEntries.SingleAsync(x => x.Id == seed.RoutineEntryId))
            .InstructorAssignmentId;
        var assignment = await db.InstructorAssignments.SingleAsync(x => x.Id == assignmentId);
        assignment.EffectiveTo = LessonDate.AddDays(-1);
        await db.SaveChangesAsync();
        var service = Service(db, 101, 7, "TenantAdmin");
        (await service.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 20))
            .Data!.Items.Should().BeEmpty();
        (await service.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Substitution_conflict_checks_all_existing_bookings_beyond_two_hundred()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var original = await db.RoutineEntries.SingleAsync(x => x.Id == seed.RoutineEntryId);
        var substituteId = await db.Employees.Where(x => x.UserId == 71).Select(x => x.Id).SingleAsync();
        var freeSlot = new RoutineTimeSlot
        {
            TenantId = 101, Name = "Nonoverlapping Period",
            StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(12, 0)
        };
        db.RoutineTimeSlots.Add(freeSlot);
        await db.SaveChangesAsync();
        for (var i = 0; i < 201; i++)
        {
            var routine = new RoutineEntry
            {
                TenantId = 101, SubjectOfferingId = original.SubjectOfferingId,
                RoutineTimeSlotId = i == 200 ? original.RoutineTimeSlotId : freeSlot.Id,
                InstructorAssignmentId = original.InstructorAssignmentId,
                DayOfWeek = LessonDate.DayOfWeek, EffectiveFrom = LessonDate.AddDays(-7)
            };
            db.RoutineEntries.Add(routine);
            await db.SaveChangesAsync();
            db.Substitutions.Add(new Substitution
            {
                TenantId = 101, ClientRequestId = Guid.NewGuid(),
                RoutineEntryId = routine.Id, Date = LessonDate,
                SubstituteEmployeeId = substituteId
            });
        }
        await db.SaveChangesAsync();
        var service = Service(db, 101, 7, "TenantAdmin");
        var result = await service.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        });
        result.StatusCode.Should().Be(409);
        (await db.Substitutions.CountAsync()).Should().Be(201);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_create_lesson_plan_or_substitution()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 72, "Student");
        var service = Service(db, 101, 72, "Student");
        (await service.CreateLessonPlanAsync(Lesson(seed.OfferingReference))).StatusCode.Should().Be(403);
        (await service.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        })).StatusCode.Should().Be(403);
    }

    private static SaveLessonPlanRequestDto Lesson(Guid offeringReference) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offeringReference,
        LessonDate = LessonDate, Title = "Linear equations", Objectives = "Explain and solve equations",
        Content = "Examples with one variable", Resources = "Mathematics textbook"
    };

    [Fact]
    public async Task Substitution_register_paging_is_stable_tenant_scoped_and_bounded()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var substituteId = (await db.Employees.SingleAsync(x => x.UserId == 71)).Id;
            for (var i = 0; i < 5; i++)
                db.Substitutions.Add(new Substitution
                {
                    TenantId = 101, ClientRequestId = Guid.NewGuid(),
                    RoutineEntryId = seed.RoutineEntryId, Date = LessonDate,
                    SubstituteEmployeeId = substituteId, Reason = "Cover " + i
                });
            await db.SaveChangesAsync();
        }
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var first = await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 1, 2);
            var second = await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 2, 2);
            var third = await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 3, 2);
            first.Success.Should().BeTrue();
            first.Data!.TotalCount.Should().Be(5);
            first.Data.Items.Should().HaveCount(2);
            second.Data!.Items.Should().HaveCount(2);
            third.Data!.Items.Should().ContainSingle();
            first.Data.Items.Select(x => x.Id).Intersect(second.Data.Items.Select(x => x.Id)).Should().BeEmpty();
            (await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 1, 501))
                .StatusCode.Should().Be(400);
            (await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 0, 20))
                .StatusCode.Should().Be(400);
        }
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            (await Service(db, 101, 70, "Teacher")
                .GetSubstitutionsPageAsync(LessonDate, LessonDate, seed.BatchId, 1, 20))
                .Data!.TotalCount.Should().Be(5);
        }
        await using (var db = Context(options, 202, 8, "TenantAdmin"))
        {
            (await Service(db, 202, 8, "TenantAdmin")
                .GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 20))
                .Data!.Items.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Substitution_register_keeps_historical_rows_with_missing_routine_context_and_stable_page_counts()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var substituteId = (await db.Employees.SingleAsync(x => x.UserId == 71)).Id;
            db.Substitutions.Add(new Substitution
            {
                TenantId = 101, ClientRequestId = Guid.NewGuid(),
                RoutineEntryId = seed.RoutineEntryId, Date = LessonDate,
                SubstituteEmployeeId = substituteId, Reason = "Existing routine"
            });
            db.Substitutions.Add(new Substitution
            {
                TenantId = 101, ClientRequestId = Guid.NewGuid(),
                RoutineEntryId = long.MaxValue, Date = LessonDate,
                SubstituteEmployeeId = substituteId, Reason = "Historical missing routine"
            });
            await db.SaveChangesAsync();
        }
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var all = await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 20);
            all.Success.Should().BeTrue();
            all.Data!.TotalCount.Should().Be(2);
            all.Data.Items.Should().HaveCount(2);
            all.Data.Items[0].ContextUnavailable.Should().BeTrue();
            all.Data.Items[0].Reason.Should().Be("Historical missing routine");
            all.Data.Items[1].ContextUnavailable.Should().BeFalse();
            all.Data.Items[1].BatchName.Should().Be("Batch A");
            all.Data.Items[0].Id.Should().BeGreaterThan(all.Data.Items[1].Id);
            var first = await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 1);
            var second = await manager.GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 2, 1);
            first.Data!.TotalCount.Should().Be(2);
            first.Data.Items.Should().ContainSingle();
            second.Data!.Items.Should().ContainSingle();
            first.Data.Items[0].Id.Should().NotBe(second.Data.Items[0].Id);
        }
        await using (var db = Context(options, 101, 71, "Teacher"))
        {
            var teacher = await Service(db, 101, 71, "Teacher")
                .GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 20);
            teacher.Data!.TotalCount.Should().Be(2);
            teacher.Data.Items.Should().HaveCount(2);
        }
        await using (var db = Context(options, 202, 8, "TenantAdmin"))
        {
            var other = await Service(db, 202, 8, "TenantAdmin")
                .GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 20);
            other.Data!.TotalCount.Should().Be(0);
            other.Data.Items.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Principal_is_limited_to_explicit_active_campus_grants()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 9, "Principal");
        var otherCampus = new Campus { TenantId = 101, Name = "Other", Code = "OTHER" };
        db.Campuses.Add(otherCampus);
        await db.SaveChangesAsync();
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 9, CampusId = otherCampus.Id, IsActive = true
        });
        await db.SaveChangesAsync();
        var admin = Service(db, 101, 7, "TenantAdmin");
        var lesson = await admin.CreateLessonPlanAsync(Lesson(seed.OfferingReference));
        lesson.StatusCode.Should().Be(201);
        var substitution = await admin.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        });
        substitution.StatusCode.Should().Be(201);
        var principal = Service(db, 101, 9, "Principal");
        (await principal.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20)).StatusCode.Should().Be(403);
        (await principal.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 20)).StatusCode.Should().Be(403);
        (await principal.GetLessonPlansAsync(null, null, null, 1, 20)).Data!.TotalCount.Should().Be(0);
        (await principal.GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 20)).Data!.TotalCount.Should().Be(0);
        (await principal.CreateLessonPlanAsync(Lesson(seed.OfferingReference))).StatusCode.Should().Be(403);
        (await principal.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        })).StatusCode.Should().Be(403);
        (await principal.ReviewLessonPlanAsync(lesson.Data!.Id, new ReviewLessonPlanRequestDto
        {
            Approve = true, RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
        })).StatusCode.Should().Be(403);
        (await principal.CancelSubstitutionAsync(substitution.Data!.Id,
            new CancelSubstitutionRequestDto
            {
                RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
            })).StatusCode.Should().Be(403);
        var campusId = await db.AcademicBatches.Where(x => x.Id == seed.BatchId)
            .Select(x => x.CampusId).SingleAsync();
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 9, CampusId = campusId, IsActive = true
        });
        await db.SaveChangesAsync();
        (await principal.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20)).Success.Should().BeTrue();
        (await principal.GetSubstitutionRoutineChoicesAsync(seed.BatchId, LessonDate, null, 1, 20)).Success.Should().BeTrue();
        (await principal.GetLessonPlansAsync(null, null, null, 1, 20)).Data!.TotalCount.Should().Be(1);
        (await principal.GetSubstitutionsPageAsync(LessonDate, LessonDate, null, 1, 20)).Data!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Inactive_grant_does_not_authorize_vice_principal()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 10, "VicePrincipal");
        var campusId = await db.AcademicBatches.Where(x => x.Id == seed.BatchId)
            .Select(x => x.CampusId).SingleAsync();
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 10, CampusId = campusId, IsActive = false
        });
        await db.SaveChangesAsync();
        var manager = Service(db, 101, 10, "VicePrincipal");
        (await manager.GetLessonOfferingChoicesAsync(seed.BatchId, null, 1, 20)).StatusCode.Should().Be(403);
        (await manager.GetLessonPlansAsync(null, null, null, 1, 20)).Data!.TotalCount.Should().Be(0);
    }

    private static AcademicInstructionService Service(EduOSDbContext db, long tenant, long user, string role, TimeProvider? clock = null) => new(
        new GenericRepository<Substitution>(db),
        new GenericRepository<LessonPlan>(db),
        new GenericRepository<RoutineEntry>(db),
        new GenericRepository<RoutineTimeSlot>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<Subject>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        db, new TestUser(tenant, user, role), clock ?? TimeProvider.System,
        NullLogger<AcademicInstructionService>.Instance);

    private static async Task<Seed> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var campus = new EduOS.Core.Entities.SaaS.Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        var year = new AcademicYear
        {
            TenantId = 101, Name = "2026", Code = "AY-2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            IsCurrent = true
        };
        var program = new AcademicProgram { TenantId = 101, Name = "Secondary", Code = "SEC" };
        var subject = new Subject { TenantId = 101, Name = "Mathematics", Code = "MATH" };
        var teacher = Teacher(70, "T-70", "Original teacher");
        var substitute = Teacher(71, "T-71", "Substitute teacher");
        db.AddRange(campus, year, program, subject, teacher, substitute);
        await db.SaveChangesAsync();
        var level = new AcademicLevel
        {
            TenantId = 101, AcademicProgramId = program.Id, Name = "Nine",
            Code = "NINE", LevelNo = 9
        };
        var curriculum = new AcademicCurriculum
        {
            TenantId = 101, AcademicProgramId = program.Id,
            Name = "Secondary Curriculum", Code = "SEC2026",
            EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true
        };
        var term = new AcademicTerm
        {
            TenantId = 101, AcademicYearId = year.Id, Name = "Autumn",
            StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 12, 15)
        };
        db.AddRange(level, curriculum, term); await db.SaveChangesAsync();
        var batch = new AcademicBatch
        {
            TenantId = 101, CampusId = campus.Id, AcademicProgramId = program.Id,
            AcademicYearId = year.Id, AcademicTermId = term.Id,
            AcademicLevelId = level.Id, Name = "Batch A", Code = "BA", Capacity = 30
        };
        var curriculumSubject = new CurriculumSubject
        {
            TenantId = 101, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = level.Id, SubjectId = subject.Id
        };
        db.AddRange(batch, curriculumSubject); await db.SaveChangesAsync();
        var offering = new SubjectOffering
        {
            TenantId = 101, AcademicBatchId = batch.Id,
            CurriculumSubjectId = curriculumSubject.Id,
            AcademicYearId = year.Id, AcademicTermId = term.Id, Code = "MATH-NINE"
        };
        db.Add(offering); await db.SaveChangesAsync();
        var assignment = new InstructorAssignment
        {
            TenantId = 101, SubjectOfferingId = offering.Id,
            EmployeeId = teacher.Id, EffectiveFrom = new DateOnly(2026, 9, 1),
            IsPrimary = true, IsActive = true
        };
        var slot = new RoutineTimeSlot
        {
            TenantId = 101, Name = "Period One",
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0)
        };
        db.AddRange(assignment, slot); await db.SaveChangesAsync();
        var routine = new RoutineEntry
        {
            TenantId = 101, SubjectOfferingId = offering.Id,
            InstructorAssignmentId = assignment.Id, RoutineTimeSlotId = slot.Id,
            DayOfWeek = DayOfWeek.Sunday, EffectiveFrom = new DateOnly(2026, 9, 1)
        };
        db.Add(routine); await db.SaveChangesAsync();
        return new Seed(offering.PublicId, batch.Id, routine.Id, substitute.PublicId);
    }

    private static Employee Teacher(long userId, string code, string name) => new()
    {
        TenantId = 101, UserId = userId, EmployeeCode = code,
        FullName = name, PersonId = 1, DesignationId = 1,
        JoiningDate = new DateOnly(2020, 1, 1),
        CanTeach = true, State = EmployeeState.Active
    };

    private static async Task<string> VersionAsync(
        DbContextOptions<EduOSDbContext> options, long id, byte[] bytes, bool substitution = false)
    {
        await using var db = Context(options, 101, 7, "TenantAdmin");
        if (substitution)
            (await db.Substitutions.SingleAsync(x => x.Id == id)).RowVersion = bytes;
        else
            (await db.LessonPlans.SingleAsync(x => x.Id == id)).RowVersion = bytes;
        await db.SaveChangesAsync();
        return Convert.ToBase64String(bytes);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-instruction-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant, long user, string role)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record Seed(Guid OfferingReference, long BatchId, long RoutineEntryId, Guid SubstituteReference);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestUser(long tenant, long user, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => user;
        public string? FullName => "Instruction user";
        public string? Email => "instruction@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => new[] { role };
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
