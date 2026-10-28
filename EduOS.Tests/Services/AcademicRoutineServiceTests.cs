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
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicRoutineServiceTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public async Task Time_slot_and_routine_entry_creation_are_retry_safe()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new TestUser(101));
        var slotRequest = new SaveRoutineTimeSlotRequestDto
        { Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) };
        var slot = await service.CreateTimeSlotAsync(slotRequest);
        var duplicateSlot = await service.CreateTimeSlotAsync(slotRequest);
        var assignment = await service.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        var request = Entry(scope.OfferingReference, slot.Data!.Id, assignment.Data!.Id, scope.RoomId);
        var entry = await service.CreateEntryAsync(request);
        var retry = await service.CreateEntryAsync(request);
        slot.Success.Should().BeTrue();
        duplicateSlot.Data!.Id.Should().Be(slot.Data.Id);
        assignment.Success.Should().BeTrue();
        entry.Success.Should().BeTrue(entry.Message);
        entry.Data!.SubjectOfferingReference.Should().Be(scope.OfferingReference);
        retry.Success.Should().BeTrue();
        retry.Data!.Id.Should().Be(entry.Data.Id);
        (await db.RoutineEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Instructor_collision_across_batches_is_rejected()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var second = new AcademicBatch { TenantId = 101, CampusId = scope.CampusId,
            AcademicYearId = scope.YearId, AcademicProgramId = scope.ProgramId,
            AcademicLevelId = scope.LevelId, Name = "Batch B", Code = "B-B", Capacity = 25 };
        db.AcademicBatches.Add(second); await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, PublicId = Guid.NewGuid(),
            AcademicBatchId = second.Id, AcademicYearId = scope.YearId,
            CurriculumSubjectId = scope.CurriculumSubjectId, Code = "MATH-B" };
        db.Add(offering); await db.SaveChangesAsync();
        var service = Service(db, new TestUser(101));
        var slot = (await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) })).Data!;
        var first = await service.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        var other = await service.AssignInstructorAsync(Assign(offering.PublicId, scope.TeacherReference));
        first.Success.Should().BeTrue();
        other.Success.Should().BeTrue();
        (await service.CreateEntryAsync(Entry(scope.OfferingReference, slot.Id, first.Data!.Id))).Success
            .Should().BeTrue();
        var collision = await service.CreateEntryAsync(Entry(offering.PublicId, slot.Id, other.Data!.Id));
        collision.StatusCode.Should().Be(409);
        (await db.RoutineEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Large_unrelated_timetable_does_not_block_valid_class_or_hide_real_collision()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new TestUser(101));
        var first = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) });
        var later = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Later Period", StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 45) });
        var assigned = await service.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        first.Success.Should().BeTrue(first.Message);
        later.Success.Should().BeTrue(later.Message);
        assigned.Success.Should().BeTrue(assigned.Message);

        // Simulate a large pre-existing timetable outside the requested slot.
        var offeringId = await db.SubjectOfferings.Where(x => x.PublicId == scope.OfferingReference)
            .Select(x => x.Id).SingleAsync();
        db.RoutineEntries.AddRange(Enumerable.Range(0, 2001).Select(_ => new RoutineEntry
        {
            TenantId = 101, SubjectOfferingId = offeringId,
            RoutineTimeSlotId = later.Data!.Id,
            InstructorAssignmentId = assigned.Data!.Id,
            DayOfWeek = DayOfWeek.Monday, EffectiveFrom = Start, IsActive = true
        }));
        await db.SaveChangesAsync();

        var valid = await service.CreateEntryAsync(Entry(
            scope.OfferingReference, first.Data!.Id, assigned.Data!.Id));
        valid.Success.Should().BeTrue(valid.Message);
        var conflicting = await service.CreateEntryAsync(Entry(
            scope.OfferingReference, later.Data!.Id, assigned.Data!.Id, scope.RoomId));
        conflicting.StatusCode.Should().Be(409);
        (await db.RoutineEntries.CountAsync()).Should().Be(2002);
    }

    [Fact]
    public async Task Overlapping_time_slots_conflict_even_when_slot_ids_differ()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var second = new AcademicBatch
        {
            TenantId = 101, CampusId = scope.CampusId, AcademicYearId = scope.YearId,
            AcademicProgramId = scope.ProgramId, AcademicLevelId = scope.LevelId,
            Name = "Batch B", Code = "B-B", Capacity = 25
        };
        db.AcademicBatches.Add(second);
        await db.SaveChangesAsync();
        var offering = new SubjectOffering
        {
            TenantId = 101, PublicId = Guid.NewGuid(), AcademicBatchId = second.Id,
            AcademicYearId = scope.YearId, CurriculumSubjectId = scope.CurriculumSubjectId,
            Code = "MATH-B"
        };
        db.SubjectOfferings.Add(offering);
        await db.SaveChangesAsync();
        var service = Service(db, new TestUser(101));
        var first = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Morning", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) });
        var overlapping = await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Overlap", StartTime = new TimeOnly(9, 30), EndTime = new TimeOnly(10, 15) });
        var a = await service.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        var b = await service.AssignInstructorAsync(Assign(offering.PublicId, scope.TeacherReference));
        (await service.CreateEntryAsync(Entry(
            scope.OfferingReference, first.Data!.Id, a.Data!.Id))).Success.Should().BeTrue();
        var conflict = await service.CreateEntryAsync(Entry(
            offering.PublicId, overlapping.Data!.Id, b.Data!.Id));
        conflict.StatusCode.Should().Be(409);
        conflict.Message.Should().Contain("Instructor");
    }

    [Fact]
    public async Task Other_tenant_cannot_assign_instructor_to_foreign_offering()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 202);
        var response = await Service(db, new TestUser(202))
            .AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        response.StatusCode.Should().Be(404);
        (await db.InstructorAssignments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Principal_cannot_read_write_or_cancel_routines_outside_active_campus_grants()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var secondCampus = new Campus { TenantId = 101, Name = "Other", Code = "OTHER" };
        db.Campuses.Add(secondCampus);
        await db.SaveChangesAsync();
        var otherBatch = new AcademicBatch
        {
            TenantId = 101, CampusId = secondCampus.Id, AcademicYearId = scope.YearId,
            AcademicProgramId = scope.ProgramId, AcademicLevelId = scope.LevelId,
            Name = "Other Campus", Code = "OTHER-B", Capacity = 25
        };
        db.AcademicBatches.Add(otherBatch);
        await db.SaveChangesAsync();
        var otherOffering = new SubjectOffering
        {
            TenantId = 101, PublicId = Guid.NewGuid(), AcademicBatchId = otherBatch.Id,
            AcademicYearId = scope.YearId, CurriculumSubjectId = scope.CurriculumSubjectId,
            Code = "OTHER-MATH"
        };
        db.SubjectOfferings.Add(otherOffering);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 7, CampusId = scope.CampusId, IsActive = true
        });
        await db.SaveChangesAsync();

        var admin = Service(db, new TestUser(101));
        var slot = (await admin.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        {
            Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45)
        })).Data!;
        var otherAssignment = await admin.AssignInstructorAsync(Assign(otherOffering.PublicId, scope.TeacherReference));
        otherAssignment.Success.Should().BeTrue();
        var otherEntry = await admin.CreateEntryAsync(
            Entry(otherOffering.PublicId, slot.Id, otherAssignment.Data!.Id));
        otherEntry.Success.Should().BeTrue(otherEntry.Message);

        var principal = Service(db, new TestUser(101, "Principal"));
        (await principal.GetAssignmentsAsync(otherBatch.Id, null)).StatusCode.Should().Be(404);
        (await principal.GetBatchTimetableAsync(otherBatch.Id, null)).StatusCode.Should().Be(404);
        (await principal.AssignInstructorAsync(Assign(otherOffering.PublicId, scope.TeacherReference)))
            .Success.Should().BeFalse();
        (await principal.CreateEntryAsync(Entry(otherOffering.PublicId, slot.Id, otherAssignment.Data.Id)))
            .Success.Should().BeFalse();
        (await principal.DeactivateEntryAsync(otherEntry.Data!.Id, "AQ==")).StatusCode.Should().Be(404);

        var teacherId = await db.Employees.Where(x => x.PublicId == scope.TeacherReference)
            .Select(x => x.Id).SingleAsync();
        var restrictedTimetable = await principal.GetTeacherTimetableAsync(teacherId, scope.YearId, null);
        restrictedTimetable.Success.Should().BeTrue();
        restrictedTimetable.Data.Should().BeEmpty();
        var adminTimetable = await admin.GetTeacherTimetableAsync(teacherId, scope.YearId, null);
        adminTimetable.Data.Should().ContainSingle(x => x.Id == otherEntry.Data.Id);

        var ownBatchId = await db.AcademicBatches.Where(x => x.CampusId == scope.CampusId)
            .Select(x => x.Id).SingleAsync();
        (await principal.GetAssignmentsAsync(ownBatchId, null)).Success.Should().BeTrue();
        (await db.RoutineEntries.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Principal_grants_must_remain_active_with_active_campus()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var batchId = await db.AcademicBatches.Select(x => x.Id).SingleAsync();
        var inactive = new UserCampusAccess
        {
            TenantId = 101, UserId = 7, CampusId = scope.CampusId, IsActive = false
        };
        db.UserCampusAccesses.Add(inactive);
        await db.SaveChangesAsync();
        var principal = Service(db, new TestUser(101, "VicePrincipal"));
        (await principal.GetBatchTimetableAsync(batchId, null)).StatusCode.Should().Be(404);

        inactive.IsActive = true;
        await db.SaveChangesAsync();
        (await principal.GetBatchTimetableAsync(batchId, null)).Success.Should().BeTrue();

        var campus = await db.Campuses.SingleAsync(x => x.Id == scope.CampusId);
        campus.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.GetBatchTimetableAsync(batchId, null)).StatusCode.Should().Be(404);
        (await Service(db, new TestUser(101)).GetBatchTimetableAsync(batchId, null))
            .Success.Should().BeTrue();
    }

    [Fact]
    public async Task Teacher_batch_timetable_does_not_disclose_other_instructors_entries()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var other = new Employee { TenantId = 101, EmployeeCode = "T-002",
            FullName = "Teacher Two", UserId = 71, Phone = "01700000001",
            DesignationId = 1, JoiningDate = Start, CanTeach = true, State = EmployeeState.Active };
        db.Employees.Add(other); await db.SaveChangesAsync();
        var admin = Service(db, new TestUser(101));
        var slot = (await admin.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) })).Data!;
        var first = await admin.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        var assistantRequest = Assign(scope.OfferingReference, other.PublicId);
        assistantRequest.IsPrimary = false;
        var second = await admin.AssignInstructorAsync(assistantRequest);
        (await admin.CreateEntryAsync(Entry(scope.OfferingReference, slot.Id, first.Data!.Id))).Success.Should().BeTrue();
        // Another day avoids the class timetable collision.
        var otherEntry = Entry(scope.OfferingReference, slot.Id, second.Data!.Id);
        otherEntry.DayOfWeek = DayOfWeek.Tuesday;
        (await admin.CreateEntryAsync(otherEntry)).Success.Should().BeTrue();
        var batchId = await db.AcademicBatches.Select(x => x.Id).SingleAsync();
        var teacher = Service(db, new TestUser(101, "Teacher", 70));
        var timetable = await teacher.GetBatchTimetableAsync(batchId, null);
        timetable.Success.Should().BeTrue(timetable.Message);
        var ownEntryId = await db.RoutineEntries.Where(x => x.InstructorAssignmentId == first.Data.Id)
            .Select(x => x.Id).SingleAsync();
        timetable.Data.Should().ContainSingle(x => x.Id == ownEntryId);
    }

    [Fact]
    public async Task Teacher_timetable_pages_are_scoped_and_validate_bounds()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var admin = Service(db, new TestUser(101));
        var slot = (await admin.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) })).Data!;
        var assignment = (await admin.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference))).Data!;
        var first = Entry(scope.OfferingReference, slot.Id, assignment.Id);
        var second = Entry(scope.OfferingReference, slot.Id, assignment.Id);
        second.DayOfWeek = DayOfWeek.Tuesday;
        (await admin.CreateEntryAsync(first)).Success.Should().BeTrue();
        (await admin.CreateEntryAsync(second)).Success.Should().BeTrue();
        var employeeId = await db.Employees.Where(x => x.PublicId == scope.TeacherReference)
            .Select(x => x.Id).SingleAsync();
        var teacher = Service(db, new TestUser(101, "Teacher", 70));
        var page1 = await teacher.GetTeacherTimetablePageAsync(employeeId, scope.YearId, null, 1, 1);
        var page2 = await teacher.GetTeacherTimetablePageAsync(employeeId, scope.YearId, null, 2, 1);
        page1.Success.Should().BeTrue(page1.Message);
        page1.Data!.TotalCount.Should().Be(2);
        page1.Data.Items.Should().ContainSingle();
        page2.Data!.Items.Should().ContainSingle();
        page2.Data.Items[0].Id.Should().NotBe(page1.Data.Items[0].Id);
        (await teacher.GetTeacherTimetablePageAsync(employeeId, scope.YearId, null, 0, 50))
            .StatusCode.Should().Be(400);
        (await teacher.GetTeacherTimetablePageAsync(employeeId, scope.YearId, null, 1, 101))
            .StatusCode.Should().Be(400);
        (await Service(db, new TestUser(202)).GetTeacherTimetablePageAsync(employeeId, scope.YearId, null, 1, 50))
            .StatusCode.Should().Be(404);
        (await Service(db, new TestUser(101, "Teacher", 999)).GetTeacherTimetablePageAsync(employeeId, scope.YearId, null, 1, 50))
            .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Assignment_pages_cover_large_batch_and_restrict_teacher_campus_and_tenant()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var batchId = await db.AcademicBatches.Select(x => x.Id).SingleAsync();
        var employeeId = await db.Employees.Select(x => x.Id).SingleAsync();
        var offerings = Enumerable.Range(0, 121).Select(i => new SubjectOffering
        {
            TenantId = 101, PublicId = Guid.NewGuid(), AcademicBatchId = batchId,
            AcademicYearId = scope.YearId, CurriculumSubjectId = scope.CurriculumSubjectId,
            Code = "SPEC-" + i.ToString("D3")
        }).ToList();
        db.SubjectOfferings.AddRange(offerings);
        await db.SaveChangesAsync();
        db.InstructorAssignments.AddRange(offerings.Select(x => new InstructorAssignment
        {
            TenantId = 101, SubjectOfferingId = x.Id, EmployeeId = employeeId,
            EffectiveFrom = Start, IsActive = true
        }));
        await db.SaveChangesAsync();

        var admin = Service(db, new TestUser(101));
        var first = await admin.GetAssignmentsPageAsync(batchId, null, null, 1, 50);
        var last = await admin.GetAssignmentsPageAsync(batchId, null, null, 3, 50);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.TotalCount.Should().Be(121);
        first.Data.Items.Should().HaveCount(50);
        last.Data!.Items.Should().HaveCount(21);
        (await admin.GetAssignmentsPageAsync(batchId, null, "SPEC-120", 1, 50))
            .Data!.Items.Should().ContainSingle();
        (await admin.GetAssignmentsPageAsync(batchId, null, null, 0, 50)).StatusCode.Should().Be(400);
        (await admin.GetAssignmentsPageAsync(batchId, null, null, 1, 101)).StatusCode.Should().Be(400);
        (await Service(db, new TestUser(101, "Teacher", 70))
            .GetAssignmentsPageAsync(batchId, null, null, 1, 50)).Data!.TotalCount.Should().Be(121);
        (await Service(db, new TestUser(101, "Teacher", 999))
            .GetAssignmentsPageAsync(batchId, null, null, 1, 50)).StatusCode.Should().Be(403);
        (await Service(db, new TestUser(202))
            .GetAssignmentsPageAsync(batchId, null, null, 1, 50)).StatusCode.Should().Be(404);
        (await Service(db, new TestUser(101, "Principal"))
            .GetAssignmentsPageAsync(batchId, null, null, 1, 50)).StatusCode.Should().Be(404);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 7, CampusId = scope.CampusId, IsActive = true
        });
        await db.SaveChangesAsync();
        (await Service(db, new TestUser(101, "Principal"))
            .GetAssignmentsPageAsync(batchId, null, null, 1, 50)).Data!.TotalCount.Should().Be(121);
    }

    [Fact]
    public async Task Batch_timetable_pages_read_beyond_1000_and_enforce_teacher_tenant_and_live_campus_grants()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var batchId = await db.AcademicBatches.Select(x => x.Id).SingleAsync();
        var offeringId = await db.SubjectOfferings.Select(x => x.Id).SingleAsync();
        var teacherId = await db.Employees.Select(x => x.Id).SingleAsync();
        var slot = new RoutineTimeSlot
        {
            TenantId = 101, Name = "Morning", StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 45), IsActive = true
        };
        var assignment = new InstructorAssignment
        {
            TenantId = 101, SubjectOfferingId = offeringId, EmployeeId = teacherId,
            EffectiveFrom = Start, IsActive = true
        };
        db.AddRange(slot, assignment);
        await db.SaveChangesAsync();
        db.RoutineEntries.AddRange(Enumerable.Range(0, 1001).Select(i => new RoutineEntry
        {
            TenantId = 101, SubjectOfferingId = offeringId, InstructorAssignmentId = assignment.Id,
            RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Monday,
            EffectiveFrom = Start, IsActive = true
        }));
        await db.SaveChangesAsync();

        var admin = Service(db, new TestUser(101));
        var first = await admin.GetBatchTimetablePageAsync(batchId, null, 1, 50);
        var last = await admin.GetBatchTimetablePageAsync(batchId, null, 21, 50);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.TotalCount.Should().Be(1001);
        first.Data.Items.Should().HaveCount(50);
        last.Data!.Items.Should().ContainSingle();
        last.Data.Items[0].Id.Should().NotBe(first.Data.Items[0].Id);
        (await admin.GetBatchTimetablePageAsync(batchId, null, 0, 50)).StatusCode.Should().Be(400);
        (await admin.GetBatchTimetablePageAsync(batchId, null, 1, 101)).StatusCode.Should().Be(400);
        (await Service(db, new TestUser(202)).GetBatchTimetablePageAsync(batchId, null, 1, 50))
            .StatusCode.Should().Be(404);
        (await Service(db, new TestUser(101, "Teacher", 999))
            .GetBatchTimetablePageAsync(batchId, null, 1, 50)).StatusCode.Should().Be(403);
        var teacher = await Service(db, new TestUser(101, "Teacher", 70))
            .GetBatchTimetablePageAsync(batchId, null, 21, 50);
        teacher.Data!.TotalCount.Should().Be(1001);
        teacher.Data.Items.Should().ContainSingle();

        var principal = Service(db, new TestUser(101, "Principal"));
        (await principal.GetBatchTimetablePageAsync(batchId, null, 1, 50)).StatusCode.Should().Be(404);
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7,
            CampusId = scope.CampusId, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        (await principal.GetBatchTimetablePageAsync(batchId, null, 1, 50)).Success.Should().BeTrue();
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.GetBatchTimetablePageAsync(batchId, null, 1, 50)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Instructor_picker_pages_large_teacher_roster_and_checks_batch_campus_before_query()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        db.Employees.AddRange(Enumerable.Range(1, 120).Select(i => new Employee
        {
            TenantId = 101, PublicId = Guid.NewGuid(), EmployeeCode = $"T-{i + 100:000}",
            FullName = $"Instructor {i:000}", DesignationId = 1,
            JoiningDate = Start, CanTeach = true, State = EmployeeState.Active
        }));
        await db.SaveChangesAsync();
        var batchId = await db.AcademicBatches.Select(x => x.Id).SingleAsync();
        var admin = Service(db, new TestUser(101));
        var first = await admin.GetInstructorChoicesPageAsync(batchId, null, 1, 50);
        var last = await admin.GetInstructorChoicesPageAsync(batchId, null, 3, 50);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.TotalCount.Should().Be(121);
        first.Data.Items.Should().HaveCount(50);
        last.Data!.Items.Should().HaveCount(21);
        (await admin.GetInstructorChoicesPageAsync(batchId, "T-119", 1, 50))
            .Data!.Items.Should().ContainSingle(x => x.EmployeeCode == "T-119");
        (await admin.GetInstructorChoicesPageAsync(batchId, null, 0, 50)).StatusCode.Should().Be(400);
        (await admin.GetInstructorChoicesPageAsync(batchId, null, 1, 101)).StatusCode.Should().Be(400);
        (await admin.GetInstructorChoicesPageAsync(batchId, new string('x', 101), 1, 50)).StatusCode.Should().Be(400);
        (await Service(db, new TestUser(202)).GetInstructorChoicesPageAsync(batchId, null, 1, 50))
            .StatusCode.Should().Be(404);
        (await Service(db, new TestUser(101, "Teacher", 70)).GetInstructorChoicesPageAsync(batchId, null, 1, 50))
            .StatusCode.Should().Be(403);
        var principal = Service(db, new TestUser(101, "Principal"));
        (await principal.GetInstructorChoicesPageAsync(batchId, null, 1, 50)).StatusCode.Should().Be(404);
        var grant = new UserCampusAccess
        {
            TenantId = 101, UserId = 7, CampusId = scope.CampusId, IsActive = true
        };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        (await principal.GetInstructorChoicesPageAsync(batchId, null, 1, 50)).Success.Should().BeTrue();
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.GetInstructorChoicesPageAsync(batchId, null, 1, 50)).StatusCode.Should().Be(404);
    }

    private static SaveInstructorAssignmentRequestDto Assign(Guid offering, Guid teacher) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offering,
        EmployeeReference = teacher, IsPrimary = true, EffectiveFrom = Start
    };

    private static SaveRoutineEntryRequestDto Entry(Guid offering, long slot, long instructor, long? room = null) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offering,
        InstructorAssignmentId = instructor, RoutineTimeSlotId = slot, RoomId = room,
        DayOfWeek = DayOfWeek.Monday, EffectiveFrom = Start
    };

    private static AcademicRoutineService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<RoutineTimeSlot>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<RoutineEntry>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<Subject>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<Room>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db), db, user,
        TimeProvider.System, NullLogger<AcademicRoutineService>.Instance);

    private static async Task<Scope> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101);
        var campus = new Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        var year = new AcademicYear { TenantId = 101, Name = "2026", Code = "2026",
            StartDate = Start, EndDate = new DateOnly(2026, 12, 31), IsCurrent = true };
        var program = new AcademicProgram { TenantId = 101, Name = "Secondary", Code = "SEC" };
        db.AddRange(campus, year, program); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id,
            Name = "Class Nine", Code = "C9", LevelNo = 9 };
        var subject = new Subject { TenantId = 101, Name = "Math", Code = "MATH" };
        var teacher = new Employee { TenantId = 101, EmployeeCode = "T-001",
            FullName = "Teacher One", UserId = 70, Phone = "01700000000",
            DesignationId = 1, JoiningDate = new DateOnly(2020, 1, 1),
            CanTeach = true, State = EmployeeState.Active };
        var room = new Room { TenantId = 101, CampusId = campus.Id, Name = "Room 101",
            Code = "R101", Capacity = 40 };
        db.AddRange(level, subject, teacher, room); await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, CampusId = campus.Id,
            AcademicYearId = year.Id, AcademicProgramId = program.Id,
            AcademicLevelId = level.Id, Name = "Batch A", Code = "B-A", Capacity = 30 };
        var curriculum = new AcademicCurriculum { TenantId = 101,
            AcademicProgramId = program.Id, Name = "Secondary 2026", Code = "SEC-2026",
            EffectiveFrom = Start, IsCurrent = true };
        db.AddRange(batch, curriculum); await db.SaveChangesAsync();
        var item = new CurriculumSubject { TenantId = 101,
            AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
            SubjectId = subject.Id, FullMarks = 100m, PassMarks = 33m };
        db.Add(item); await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, PublicId = Guid.NewGuid(),
            AcademicBatchId = batch.Id, CurriculumSubjectId = item.Id,
            AcademicYearId = year.Id, Code = "MATH-A" };
        db.Add(offering); await db.SaveChangesAsync();
        return new Scope(campus.Id, year.Id, program.Id, level.Id, item.Id,
            offering.PublicId, teacher.PublicId, room.Id);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "academic-routine-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record Scope(long CampusId, long YearId, long ProgramId, long LevelId,
        long CurriculumSubjectId, Guid OfferingReference, Guid TeacherReference, long RoomId);
    private sealed class TestUser(long tenant, string role = "TenantAdmin", long userId = 7) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => role;
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
