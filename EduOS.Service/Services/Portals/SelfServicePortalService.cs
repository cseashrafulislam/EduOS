using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Portals;

public sealed class SelfServicePortalService : ISelfServicePortalService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _links;
    private readonly IGenericRepository<StudentEnrollment> _academicEnrollments;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<RoutineEntry> _routines;
    private readonly IGenericRepository<RoutineTimeSlot> _slots;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<InstructorAssignment> _instructors;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IGenericRepository<StudentAttendance> _attendance;
    private readonly IGenericRepository<AttendanceSession> _sessions;
    private readonly IGenericRepository<StudentResultSummary> _results;
    private readonly IGenericRepository<ResultPublication> _publications;
    private readonly IGenericRepository<Assessment> _assessments;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IGenericRepository<StudentPayment> _payments;
    private readonly IGenericRepository<StudentTransport> _transport;
    private readonly IGenericRepository<Route> _routes;
    private readonly IGenericRepository<Vehicle> _vehicles;
    private readonly IGenericRepository<RouteStop> _stops;
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<CourseEnrollment> _courseEnrollments;
    private readonly IGenericRepository<Assignment> _assignments;
    private readonly ICurrentUserService _user;

    public SelfServicePortalService(
        IGenericRepository<Student> students, IGenericRepository<Guardian> guardians,
        IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<StudentEnrollment> studentEnrollments,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<RoutineEntry> routineEntries, IGenericRepository<RoutineTimeSlot> routineSlots,
        IGenericRepository<SubjectOffering> subjectOfferings,
        IGenericRepository<CurriculumSubject> curriculumSubjects, IGenericRepository<Subject> subjects,
        IGenericRepository<InstructorAssignment> instructorAssignments,
        IGenericRepository<Employee> employees, IGenericRepository<Room> rooms,
        IGenericRepository<StudentAttendance> attendance, IGenericRepository<AttendanceSession> attendanceSessions,
        IGenericRepository<StudentResultSummary> results, IGenericRepository<ResultPublication> resultPublications,
        IGenericRepository<Assessment> assessments, IGenericRepository<StudentInvoice> invoices,
        IGenericRepository<StudentPayment> payments, IGenericRepository<StudentTransport> transport,
        IGenericRepository<Route> routes, IGenericRepository<Vehicle> vehicles,
        IGenericRepository<RouteStop> routeStops, IGenericRepository<Course> courses,
        IGenericRepository<CourseEnrollment> enrollments, IGenericRepository<Assignment> assignments,
        ICurrentUserService currentUser)
    {
        _students = students; _guardians = guardians; _links = studentGuardians;
        _academicEnrollments = studentEnrollments; _registrations = registrations;
        _routines = routineEntries; _slots = routineSlots; _offerings = subjectOfferings;
        _curriculumSubjects = curriculumSubjects; _subjects = subjects;
        _instructors = instructorAssignments; _employees = employees; _rooms = rooms;
        _attendance = attendance; _sessions = attendanceSessions; _results = results;
        _publications = resultPublications; _assessments = assessments; _invoices = invoices;
        _payments = payments; _transport = transport; _routes = routes;
        _vehicles = vehicles; _stops = routeStops; _courses = courses;
        _courseEnrollments = enrollments; _assignments = assignments; _user = currentUser;
    }

    public async Task<ApiResponse<IReadOnlyList<PortalStudentDto>>> GetLinkedStudentsAsync(
        CancellationToken ct = default)
    {
        if (!CanAccess()) return Denied<IReadOnlyList<PortalStudentDto>>();
        var tenant = _user.TenantId;
        var eligible = AuthorizedStudents();
        var students = await eligible.OrderBy(x => x.FullName).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        if (students.Count > 100)
            return Error<IReadOnlyList<PortalStudentDto>>("Linked student list exceeds portal limit.", 409);
        var studentIds = students.Select(x => x.Id).ToArray();
        var active = await _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && studentIds.Contains(x.StudentId) && x.IsCurrent &&
            x.State == EnrollmentState.Active && !x.IsDeleted)
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
            .ToListAsync(ct);
        var byStudent = active.GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.First());
        IReadOnlyList<PortalStudentDto> list = students.Select(x =>
        {
            byStudent.TryGetValue(x.Id, out var e);
            return new PortalStudentDto
            {
                Reference = x.PublicId, StudentCode = x.StudentCode, Name = x.FullName,
                RollNo = e?.RollNo ?? string.Empty, AcademicYearId = e?.AcademicYearId ?? 0,
                AcademicLevelId = e?.AcademicLevelId ?? 0, AcademicBatchId = e?.AcademicBatchId ?? 0
            };
        }).ToList();
        return ApiResponse<IReadOnlyList<PortalStudentDto>>.SuccessResponse(list);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>> GetTimetableAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<IReadOnlyList<PortalTimetableEntryDto>>();
        var tenant = _user.TenantId;
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var enrolled = _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
            x.State == EnrollmentState.Active && !x.IsDeleted).Select(x => x.Id);
        var allowed = from registration in _registrations.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking() on registration.SubjectOfferingId equals offering.Id
            where registration.TenantId == tenant && offering.TenantId == tenant &&
                enrolled.Contains(registration.StudentEnrollmentId) &&
                registration.State == SubjectRegistrationState.Approved && !registration.IsDeleted &&
                offering.IsActive && !offering.IsDeleted
            select offering.Id;
        var entries = await _routines.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && allowed.Contains(x.SubjectOfferingId) &&
            x.IsActive && !x.IsDeleted && x.EffectiveFrom <= now &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= now))
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlotId).ThenBy(x => x.Id)
            .Take(501).ToListAsync(ct);
        if (entries.Count > 500)
            return Error<IReadOnlyList<PortalTimetableEntryDto>>("Timetable exceeds portal limit.", 409);
        var offerIds = entries.Select(x => x.SubjectOfferingId).Distinct().ToArray();
        var slotIds = entries.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
        var instructorIds = entries.Where(x => x.InstructorAssignmentId.HasValue)
            .Select(x => x.InstructorAssignmentId!.Value).Distinct().ToArray();
        var roomIds = entries.Where(x => x.RoomId.HasValue).Select(x => x.RoomId!.Value).Distinct().ToArray();
        var offerings = await _offerings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && offerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var slots = await _slots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && slotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var curriculumIds = offerings.Values.Select(x => x.CurriculumSubjectId).Distinct().ToArray();
        var curriculum = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && curriculumIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var subjectIds = curriculum.Values.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && subjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var instructors = await _instructors.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && instructorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var employeeIds = instructors.Values.Select(x => x.EmployeeId).Distinct().ToArray();
        var employees = await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && employeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var rooms = await _rooms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && roomIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var result = new List<PortalTimetableEntryDto>();
        foreach (var row in entries)
        {
            if (!slots.TryGetValue(row.RoutineTimeSlotId, out var slot) ||
                !offerings.TryGetValue(row.SubjectOfferingId, out var offer) ||
                !curriculum.TryGetValue(offer.CurriculumSubjectId, out var item) ||
                !subjects.TryGetValue(item.SubjectId, out var subject)) continue;
            var teacher = "";
            if (row.InstructorAssignmentId.HasValue &&
                instructors.TryGetValue(row.InstructorAssignmentId.Value, out var instructor) &&
                employees.TryGetValue(instructor.EmployeeId, out var employee)) teacher = employee.FullName;
            result.Add(new PortalTimetableEntryDto
            {
                RoutineId = row.Id, DayOfWeek = row.DayOfWeek, StartTime = slot.StartTime,
                EndTime = slot.EndTime, SubjectId = subject.Id, SubjectName = subject.Name,
                TeacherName = teacher, RoomNo = row.RoomId.HasValue &&
                    rooms.TryGetValue(row.RoomId.Value, out var room) ? room.Code : null
            });
        }
        return ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<PagedResult<PortalAttendanceDto>>> GetAttendanceAsync(
        Guid studentReference, DateOnly fromDate, DateOnly toDate, int page, int pageSize,
        CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<PagedResult<PortalAttendanceDto>>();
        if (!ValidPage(page, pageSize) || fromDate == default || toDate < fromDate ||
            toDate.DayNumber - fromDate.DayNumber > 366)
            return Error<PagedResult<PortalAttendanceDto>>("Invalid attendance date range or page.");
        var tenant = _user.TenantId;
        var enrollments = _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).Select(x => x.Id);
        var query = from att in _attendance.GetQueryable().AsNoTracking()
            join session in _sessions.GetQueryable().AsNoTracking() on att.AttendanceSessionId equals session.Id
            where att.TenantId == tenant && session.TenantId == tenant &&
                enrollments.Contains(att.StudentEnrollmentId) &&
                session.AttendanceDate >= fromDate && session.AttendanceDate <= toDate &&
                !att.IsDeleted && !session.IsDeleted
            select new { att, session.AttendanceDate };
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalAttendanceDto>>("Page exceeds supported range.");
        var rows = await query.OrderByDescending(x => x.AttendanceDate).ThenByDescending(x => x.att.Id)
            .Skip(skip).Take(pageSize).Select(x => new PortalAttendanceDto
            {
                AttendanceSessionId = x.att.AttendanceSessionId, AttendanceDate = x.AttendanceDate,
                State = x.att.State, CheckInTime = x.att.CheckInTime, CheckOutTime = x.att.CheckOutTime,
                Remarks = x.att.Remarks
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PortalAttendanceDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    public async Task<ApiResponse<PagedResult<PortalResultDto>>> GetResultsAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<PagedResult<PortalResultDto>>();
        if (!ValidPage(page, pageSize)) return Error<PagedResult<PortalResultDto>>("Invalid page.");
        var tenant = _user.TenantId;
        var guardianView = _user.IsInRole("Guardian") || _user.IsInRole("Parent");
        var enrollments = _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).Select(x => x.Id);
        var query = from summary in _results.GetQueryable().AsNoTracking()
            join publication in _publications.GetQueryable().AsNoTracking()
                on summary.ResultPublicationId equals publication.Id
            join assessment in _assessments.GetQueryable().AsNoTracking()
                on summary.AssessmentId equals assessment.Id
            where summary.TenantId == tenant && publication.TenantId == tenant &&
                assessment.TenantId == tenant && enrollments.Contains(summary.StudentEnrollmentId) &&
                publication.State == ResultPublicationState.Published &&
                (guardianView ? publication.VisibleToGuardian : publication.VisibleToStudent) &&
                !publication.IsDeleted && !summary.IsDeleted && !assessment.IsDeleted
            select new { summary, publication.PublishedAt, AssessmentName = assessment.Name };
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalResultDto>>("Page exceeds supported range.");
        var rows = await query.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.summary.Id)
            .Skip(skip).Take(pageSize).Select(x => new PortalResultDto
            {
                ResultPublicationId = x.summary.ResultPublicationId,
                AssessmentId = x.summary.AssessmentId, AssessmentName = x.AssessmentName,
                PublicationVersionNo = x.summary.PublicationVersionNo,
                ObtainedMarks = x.summary.ObtainedMarks, TotalMarks = x.summary.TotalMarks,
                Percentage = x.summary.Percentage, GPA = x.summary.GPA, CGPA = x.summary.CGPA,
                GradeLetter = x.summary.GradeLetter, MeritPosition = x.summary.MeritPosition,
                IsPassed = x.summary.IsPassed, IsWithheld = x.summary.IsWithheld,
                PublishedAt = x.PublishedAt
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PortalResultDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    public async Task<ApiResponse<PortalFeeLedgerDto>> GetFeesAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<PortalFeeLedgerDto>();
        var tenant = _user.TenantId;
        var enrollments = _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).Select(x => x.Id);
        var q = _invoices.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && enrollments.Contains(x.StudentEnrollmentId) &&
            !x.IsDeleted && x.State != InvoiceState.Cancelled);
        var summary = await q.GroupBy(x => x.TenantId).Select(g => new
        {
            Billed = g.Sum(x => x.TotalAmount), Paid = g.Sum(x => x.PaidAmount),
            Due = g.Sum(x => x.DueAmount)
        }).FirstOrDefaultAsync(ct);
        return ApiResponse<PortalFeeLedgerDto>.SuccessResponse(new PortalFeeLedgerDto
        {
            TotalBilled = summary?.Billed ?? 0m, TotalPaid = summary?.Paid ?? 0m,
            TotalDue = summary?.Due ?? 0m
        });
    }

    public async Task<ApiResponse<PagedResult<PortalInvoiceDto>>> GetInvoicesAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<PagedResult<PortalInvoiceDto>>();
        if (!ValidPage(page, pageSize)) return Error<PagedResult<PortalInvoiceDto>>("Invalid page.");
        var tenant = _user.TenantId;
        var enrollmentIds = _academicEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted).Select(x => x.Id);
        var query = _invoices.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && enrollmentIds.Contains(x.StudentEnrollmentId) && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalInvoiceDto>>("Page exceeds supported range.");
        var rows = await query.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id)
            .Skip(skip).Take(pageSize).Select(x => new PortalInvoiceDto
            {
                Reference = x.PublicId, InvoiceNumber = x.InvoiceNumber,
                InvoiceDate = x.InvoiceDate, DueDate = x.DueDate,
                TotalAmount = x.TotalAmount, PaidAmount = x.PaidAmount, DueAmount = x.DueAmount,
                State = x.State
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PortalInvoiceDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    public async Task<ApiResponse<PagedResult<PortalPaymentDto>>> GetPaymentsAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<PagedResult<PortalPaymentDto>>();
        if (!ValidPage(page, pageSize)) return Error<PagedResult<PortalPaymentDto>>("Invalid page.");
        var tenant = _user.TenantId;
        var query = _payments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalPaymentDto>>("Page exceeds supported range.");
        var rows = await query.OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id)
            .Skip(skip).Take(pageSize).Select(x => new PortalPaymentDto
            {
                Reference = x.PublicId, ReceiptNumber = x.ReceiptNumber,
                Amount = x.Amount, PaymentMethod = x.PaymentMethod, State = x.State,
                PaymentDate = x.PaymentDate
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PortalPaymentDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    public async Task<ApiResponse<PagedResult<PortalTransportDto>>> GetTransportAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default)
    {
        var student = await AuthorizedStudentAsync(studentReference, ct);
        if (student == null) return Denied<PagedResult<PortalTransportDto>>();
        if (!ValidPage(page, pageSize)) return Error<PagedResult<PortalTransportDto>>("Invalid page.");
        var tenant = _user.TenantId;
        var query = from assignment in _transport.GetQueryable().AsNoTracking()
            join route in _routes.GetQueryable().AsNoTracking() on assignment.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on assignment.VehicleId equals vehicle.Id
            where assignment.TenantId == tenant && route.TenantId == tenant &&
                vehicle.TenantId == tenant && assignment.StudentId == student.Id &&
                !assignment.IsDeleted
            select new { assignment, route.Name, vehicle.VehicleNumber };
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalTransportDto>>("Page exceeds supported range.");
        var data = await query.OrderByDescending(x => x.assignment.StartDate)
            .ThenByDescending(x => x.assignment.Id).Skip(skip).Take(pageSize)
            .Select(x => new
            {
                x.assignment.PublicId, RouteName = x.Name, x.VehicleNumber,
                x.assignment.PickupStopId, x.assignment.StartDate, x.assignment.EndDate,
                x.assignment.MonthlyFare, x.assignment.State
            }).ToListAsync(ct);
        var stopIds = data.Where(x => x.PickupStopId.HasValue)
            .Select(x => x.PickupStopId!.Value).Distinct().ToArray();
        var stops = await _stops.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && stopIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var rows = data.Select(x => new PortalTransportDto
        {
            Reference = x.PublicId, RouteName = x.RouteName, VehicleNumber = x.VehicleNumber,
            PickupPoint = x.PickupStopId.HasValue &&
                stops.TryGetValue(x.PickupStopId.Value, out var stop) ? stop : null,
            StartDate = x.StartDate, EndDate = x.EndDate, MonthlyFare = x.MonthlyFare, State = x.State
        }).ToList();
        return ApiResponse<PagedResult<PortalTransportDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    public Task<ApiResponse<PagedResult<PortalHomeworkDto>>> GetHomeworkAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default) =>
        GetLearningTasksAsync(studentReference, page, pageSize, true, ct);

    public Task<ApiResponse<PagedResult<PortalAssignmentDto>>> GetAssignmentsAsync(
        Guid studentReference, int page, int pageSize, CancellationToken ct = default) =>
        GetAssignmentTasksAsync(studentReference, page, pageSize, ct);

    private async Task<ApiResponse<PagedResult<PortalHomeworkDto>>> GetLearningTasksAsync(
        Guid reference, int page, int pageSize, bool homework, CancellationToken ct)
    {
        var student = await AuthorizedStudentAsync(reference, ct);
        if (student == null) return Denied<PagedResult<PortalHomeworkDto>>();
        if (!ValidPage(page, pageSize)) return Error<PagedResult<PortalHomeworkDto>>("Invalid page.");
        var tenant = _user.TenantId;
        var courseIds = _courseEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted &&
            (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed))
            .Select(x => x.CourseId);
        var query = from assignment in _assignments.GetQueryable().AsNoTracking()
            join course in _courses.GetQueryable().AsNoTracking() on assignment.CourseId equals course.Id
            where assignment.TenantId == tenant && course.TenantId == tenant &&
                courseIds.Contains(assignment.CourseId) && assignment.IsPublished &&
                assignment.Type == LearningTaskType.Homework && !assignment.IsDeleted &&
                course.IsActive && !course.IsDeleted
            select new { assignment, course.SubjectId, course.Title };
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalHomeworkDto>>("Page outside supported range.");
        var data = await query.OrderByDescending(x => x.assignment.OpensAt)
            .ThenByDescending(x => x.assignment.Id).Skip(skip).Take(pageSize).ToListAsync(ct);
        var subjectIds = data.Where(x => x.SubjectId.HasValue)
            .Select(x => x.SubjectId!.Value).Distinct().ToArray();
        var names = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && subjectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var rows = data.Select(x => new PortalHomeworkDto
        {
            AssignmentReference = x.assignment.PublicId, SubjectId = x.SubjectId ?? 0,
            SubjectName = x.SubjectId.HasValue &&
                names.TryGetValue(x.SubjectId.Value, out var name) ? name : x.Title,
            Title = x.assignment.Title, Instructions = x.assignment.Instructions,
            OpensAt = x.assignment.OpensAt, DueAt = x.assignment.DueAt
        }).ToList();
        return ApiResponse<PagedResult<PortalHomeworkDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    private async Task<ApiResponse<PagedResult<PortalAssignmentDto>>> GetAssignmentTasksAsync(
        Guid reference, int page, int pageSize, CancellationToken ct)
    {
        var student = await AuthorizedStudentAsync(reference, ct);
        if (student == null) return Denied<PagedResult<PortalAssignmentDto>>();
        if (!ValidPage(page, pageSize)) return Error<PagedResult<PortalAssignmentDto>>("Invalid page.");
        var tenant = _user.TenantId;
        var courseIds = _courseEnrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted &&
            (x.State == CourseEnrollmentState.Active || x.State == CourseEnrollmentState.Completed))
            .Select(x => x.CourseId);
        var query = from assignment in _assignments.GetQueryable().AsNoTracking()
            join course in _courses.GetQueryable().AsNoTracking() on assignment.CourseId equals course.Id
            where assignment.TenantId == tenant && course.TenantId == tenant &&
                courseIds.Contains(assignment.CourseId) && assignment.IsPublished &&
                assignment.Type != LearningTaskType.Homework && !assignment.IsDeleted &&
                course.IsActive && !course.IsDeleted
            select new { assignment, CourseTitle = course.Title };
        var count = await query.CountAsync(ct);
        var skip = Skip(page, pageSize);
        if (skip < 0) return Error<PagedResult<PortalAssignmentDto>>("Page outside supported range.");
        var rows = await query.OrderBy(x => x.assignment.DueAt)
            .ThenBy(x => x.assignment.Id).Skip(skip).Take(pageSize)
            .Select(x => new PortalAssignmentDto
            {
                Reference = x.assignment.PublicId, CourseId = x.assignment.CourseId,
                CourseTitle = x.CourseTitle, Title = x.assignment.Title,
                Instructions = x.assignment.Instructions, MaxMarks = x.assignment.MaxMarks,
                DueAt = x.assignment.DueAt
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PortalAssignmentDto>>.SuccessResponse(Page(rows, count, page, pageSize));
    }

    private IQueryable<Student> AuthorizedStudents()
    {
        var tenant = _user.TenantId;
        var user = _user.UserId;
        var guardians = _guardians.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.UserId == user && x.IsActive && !x.IsDeleted).Select(x => x.Id);
        var linked = _links.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && guardians.Contains(x.GuardianId) && !x.IsDeleted).Select(x => x.StudentId);
        return _students.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.StatusCode == "Active" && !x.IsDeleted && (x.UserId == user || linked.Contains(x.Id)));
    }

    private Task<Student?> AuthorizedStudentAsync(Guid reference, CancellationToken ct) =>
        !CanAccess() || reference == Guid.Empty ? Task.FromResult<Student?>(null) :
            AuthorizedStudents().FirstOrDefaultAsync(x => x.PublicId == reference, ct);

    private bool CanAccess() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsInRole("Student") || _user.IsInRole("Guardian") || _user.IsInRole("Parent"));
    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100;
    private static int Skip(int page, int size)
    {
        var skipped = (long)(page - 1) * size;
        return skipped > int.MaxValue ? -1 : (int)skipped;
    }
    private static PagedResult<T> Page<T>(List<T> items, int count, int page, int pageSize) => new()
    {
        Page = page, PageSize = pageSize, TotalCount = count, Items = items
    };
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse(
        "Requested student not linked to this account.", 403);
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
