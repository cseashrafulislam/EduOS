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
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Portals;

public sealed class SelfServicePortalService : ISelfServicePortalService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<StudentEnrollment> _studentEnrollments;
    private readonly IGenericRepository<AcademicBatch> _academicBatches;
    private readonly IGenericRepository<RoutineEntry> _routineEntries;
    private readonly IGenericRepository<RoutineTimeSlot> _routineSlots;
    private readonly IGenericRepository<SubjectOffering> _subjectOfferings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<InstructorAssignment> _instructorAssignments;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IGenericRepository<StudentAttendance> _attendance;
    private readonly IGenericRepository<AttendanceSession> _attendanceSessions;
    private readonly IGenericRepository<StudentResultSummary> _results;
    private readonly IGenericRepository<ResultPublication> _resultPublications;
    private readonly IGenericRepository<Assessment> _assessments;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly IGenericRepository<StudentPayment> _payments;
    private readonly IGenericRepository<StudentTransport> _transport;
    private readonly IGenericRepository<Route> _routes;
    private readonly IGenericRepository<Vehicle> _vehicles;
    private readonly IGenericRepository<RouteStop> _routeStops;
    private readonly IGenericRepository<Course> _courses;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<Assignment> _assignments;
    private readonly IGenericRepository<CourseEnrollment> _enrollments;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SelfServicePortalService> _logger;

    public SelfServicePortalService(
        IGenericRepository<Student> students,
        IGenericRepository<Guardian> guardians,
        IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<StudentEnrollment> studentEnrollments,
        IGenericRepository<AcademicBatch> academicBatches,
        IGenericRepository<RoutineEntry> routineEntries,
        IGenericRepository<RoutineTimeSlot> routineSlots,
        IGenericRepository<SubjectOffering> subjectOfferings,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<InstructorAssignment> instructorAssignments,
        IGenericRepository<Employee> employees,
        IGenericRepository<Room> rooms,
        IGenericRepository<StudentAttendance> attendance,
        IGenericRepository<AttendanceSession> attendanceSessions,
        IGenericRepository<StudentResultSummary> results,
        IGenericRepository<ResultPublication> resultPublications,
        IGenericRepository<Assessment> assessments,
        IGenericRepository<StudentInvoice> invoices,
        IGenericRepository<StudentPayment> payments,
        IGenericRepository<StudentTransport> transport,
        IGenericRepository<Route> routes,
        IGenericRepository<Vehicle> vehicles,
        IGenericRepository<RouteStop> routeStops,
        IGenericRepository<Course> courses,
        IGenericRepository<Subject> subjects,
        IGenericRepository<Assignment> assignments,
        IGenericRepository<CourseEnrollment> enrollments,
        ICurrentUserService currentUser,
        ILogger<SelfServicePortalService> logger)
    {
        _students = students;
        _guardians = guardians;
        _studentGuardians = studentGuardians;
        _studentEnrollments = studentEnrollments;
        _academicBatches = academicBatches;
        _routineEntries = routineEntries;
        _routineSlots = routineSlots;
        _subjectOfferings = subjectOfferings;
        _curriculumSubjects = curriculumSubjects;
        _instructorAssignments = instructorAssignments;
        _employees = employees;
        _rooms = rooms;
        _attendance = attendance;
        _attendanceSessions = attendanceSessions;
        _results = results;
        _resultPublications = resultPublications;
        _assessments = assessments;
        _invoices = invoices;
        _payments = payments;
        _transport = transport;
        _routes = routes;
        _vehicles = vehicles;
        _routeStops = routeStops;
        _courses = courses;
        _subjects = subjects;
        _assignments = assignments;
        _enrollments = enrollments;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalTimetableEntryDto>>();

        var enrollment = await GetCurrentEnrollmentAsync(student.Id, cancellationToken);
        if (enrollment == null)
            return ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>.SuccessResponse(Array.Empty<PortalTimetableEntryDto>());

        var tenantId = _currentUser.TenantId;
        var offeringIds = await _subjectOfferings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AcademicBatchId == enrollment.AcademicBatchId && x.AcademicYearId == enrollment.AcademicYearId && x.IsActive)
            .Select(x => x.Id).Take(200).ToListAsync(cancellationToken);
        if (offeringIds.Count == 0)
            return ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>.SuccessResponse(Array.Empty<PortalTimetableEntryDto>());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = await _routineEntries.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && offeringIds.Contains(x.SubjectOfferingId) && x.IsActive && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today))
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlotId).Take(500).ToListAsync(cancellationToken);
        if (entries.Count == 0)
            return ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>.SuccessResponse(Array.Empty<PortalTimetableEntryDto>());

        var slotIds = entries.Select(x => x.RoutineTimeSlotId).Distinct().ToArray();
        var instructorAssignmentIds = entries.Where(x => x.InstructorAssignmentId.HasValue).Select(x => x.InstructorAssignmentId!.Value).Distinct().ToArray();
        var roomIds = entries.Where(x => x.RoomId.HasValue).Select(x => x.RoomId!.Value).Distinct().ToArray();
        var slots = await _routineSlots.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && slotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var offerings = await _subjectOfferings.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && offeringIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var curriculumSubjectIds = offerings.Values.Select(x => x.CurriculumSubjectId).Distinct().ToArray();
        var curriculumSubjects = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && curriculumSubjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var subjectIds = curriculumSubjects.Values.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && subjectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var instructorAssignments = instructorAssignmentIds.Length == 0
            ? new Dictionary<long, InstructorAssignment>()
            : await _instructorAssignments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && instructorAssignmentIds.Contains(x.Id) && x.IsActive)
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var employeeIds = instructorAssignments.Values.Select(x => x.EmployeeId).Distinct().ToArray();
        var employees = employeeIds.Length == 0
            ? new Dictionary<long, Employee>()
            : await _employees.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var rooms = roomIds.Length == 0
            ? new Dictionary<long, Room>()
            : await _rooms.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && roomIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = new List<PortalTimetableEntryDto>();
        foreach (var entry in entries)
        {
            if (!slots.TryGetValue(entry.RoutineTimeSlotId, out var slot) || !offerings.TryGetValue(entry.SubjectOfferingId, out var offering) ||
                !curriculumSubjects.TryGetValue(offering.CurriculumSubjectId, out var curriculumSubject) || !subjects.TryGetValue(curriculumSubject.SubjectId, out var subject))
                continue;

            string teacherName = string.Empty;
            if (entry.InstructorAssignmentId.HasValue && instructorAssignments.TryGetValue(entry.InstructorAssignmentId.Value, out var assignment) &&
                employees.TryGetValue(assignment.EmployeeId, out var employee))
                teacherName = employee.FullName;

            rows.Add(new PortalTimetableEntryDto
            {
                RoutineId = entry.Id,
                DayOfWeek = entry.DayOfWeek.ToString(),
                StartTime = slot.StartTime.ToTimeSpan(),
                EndTime = slot.EndTime.ToTimeSpan(),
                SubjectId = subject.Id,
                SubjectName = subject.Name,
                TeacherName = teacherName,
                RoomNo = entry.RoomId.HasValue && rooms.TryGetValue(entry.RoomId.Value, out var room) ? room.Code : null
            });
        }

        IReadOnlyList<PortalTimetableEntryDto> ordered = rows.OrderBy(x => DayOrder(x.DayOfWeek)).ThenBy(x => x.StartTime).ThenBy(x => x.RoutineId).ToList();
        return ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>.SuccessResponse(ordered);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalStudentDto>>> GetLinkedStudentsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return Denied<IReadOnlyList<PortalStudentDto>>();
        try
        {
            var ids = await GetAuthorizedStudentIdsAsync(cancellationToken);
            var students = await _students.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && ids.Contains(x.Id) && x.IsActive)
                .OrderBy(x => x.FullName).Take(100).ToListAsync(cancellationToken);
            var studentIds = students.Select(x => x.Id).ToArray();
            var enrollments = await _studentEnrollments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && studentIds.Contains(x.StudentId) && x.IsActive)
                .OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);
            var currentByStudent = enrollments.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.First());

            IReadOnlyList<PortalStudentDto> data = students.Select(x =>
            {
                currentByStudent.TryGetValue(x.Id, out var enrollment);
                return new PortalStudentDto
                {
                    Reference = x.PublicId,
                    StudentCode = x.StudentCode,
                    Roll = enrollment?.RollNo ?? string.Empty,
                    Name = x.FullName,
                    AcademicYearId = enrollment?.AcademicYearId ?? 0,
                    ClassId = enrollment?.AcademicLevelId ?? 0,
                    SectionId = enrollment?.AcademicBatchId ?? 0
                };
            }).ToList();
            return ApiResponse<IReadOnlyList<PortalStudentDto>>.SuccessResponse(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Portal linked students failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<IReadOnlyList<PortalStudentDto>>.ErrorResponse("Portal data could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<PortalAttendanceDto>>> GetAttendanceAsync(Guid studentReference, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalAttendanceDto>>();
        if (fromDate.Date > toDate.Date || (toDate.Date - fromDate.Date).TotalDays > 370)
            return ApiResponse<IReadOnlyList<PortalAttendanceDto>>.ErrorResponse("Attendance date range is invalid.");

        var tenantId = _currentUser.TenantId;
        var enrollmentIds = await _studentEnrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        if (enrollmentIds.Count == 0)
            return ApiResponse<IReadOnlyList<PortalAttendanceDto>>.SuccessResponse(Array.Empty<PortalAttendanceDto>());

        var from = DateOnly.FromDateTime(fromDate.Date);
        var to = DateOnly.FromDateTime(toDate.Date);
        var sessions = await _attendanceSessions.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AttendanceDate >= from && x.AttendanceDate <= to)
            .OrderByDescending(x => x.AttendanceDate).Take(1000).ToListAsync(cancellationToken);
        var sessionIds = sessions.Select(x => x.Id).ToArray();
        var sessionMap = sessions.ToDictionary(x => x.Id);
        var attendance = await _attendance.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && enrollmentIds.Contains(x.StudentEnrollmentId) && sessionIds.Contains(x.AttendanceSessionId))
            .OrderByDescending(x => x.RecordedAt).Take(1000).ToListAsync(cancellationToken);

        IReadOnlyList<PortalAttendanceDto> rows = attendance.Where(x => sessionMap.ContainsKey(x.AttendanceSessionId))
            .OrderByDescending(x => sessionMap[x.AttendanceSessionId].AttendanceDate)
            .Select(x => new PortalAttendanceDto
            {
                Date = sessionMap[x.AttendanceSessionId].AttendanceDate.ToDateTime(TimeOnly.MinValue),
                Status = x.State.ToString(),
                InTime = x.CheckInTime?.ToTimeSpan(),
                OutTime = x.CheckOutTime?.ToTimeSpan(),
                Remarks = x.Remarks
            }).ToList();
        return ApiResponse<IReadOnlyList<PortalAttendanceDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalResultDto>>> GetResultsAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalResultDto>>();

        var tenantId = _currentUser.TenantId;
        var enrollmentIds = await _studentEnrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        if (enrollmentIds.Count == 0)
            return ApiResponse<IReadOnlyList<PortalResultDto>>.SuccessResponse(Array.Empty<PortalResultDto>());

        var summaries = await _results.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && enrollmentIds.Contains(x.StudentEnrollmentId))
            .OrderByDescending(x => x.CalculatedAt).Take(200).ToListAsync(cancellationToken);
        var publicationIds = summaries.Select(x => x.ResultPublicationId).Distinct().ToArray();
        var showForGuardian = _currentUser.IsInRole("Guardian") || _currentUser.IsInRole("Parent");
        var publications = await _resultPublications.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && publicationIds.Contains(x.Id) && x.State == ResultPublicationState.Published &&
                        (showForGuardian ? x.VisibleToGuardian : x.VisibleToStudent))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var assessmentIds = summaries.Where(x => publications.ContainsKey(x.ResultPublicationId)).Select(x => x.AssessmentId).Distinct().ToArray();
        var assessments = assessmentIds.Length == 0
            ? new Dictionary<long, Assessment>()
            : await _assessments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && assessmentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        IReadOnlyList<PortalResultDto> rows = summaries
            .Where(x => publications.ContainsKey(x.ResultPublicationId) && assessments.ContainsKey(x.AssessmentId))
            .OrderByDescending(x => publications[x.ResultPublicationId].PublishedAt ?? x.CalculatedAt)
            .Select(x => new PortalResultDto
            {
                ExamId = x.AssessmentId,
                ExamName = assessments[x.AssessmentId].Name,
                TotalMark = x.ObtainedMarks,
                TotalFullMark = x.TotalMarks,
                Percentage = x.Percentage ?? 0m,
                GPA = x.GPA ?? 0m,
                Grade = x.GradeLetter,
                Position = x.MeritPosition,
                IsPassed = x.IsPassed && !x.IsWithheld,
                PublishedAtUtc = publications[x.ResultPublicationId].PublishedAt
            }).ToList();
        return ApiResponse<IReadOnlyList<PortalResultDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<PortalFeeLedgerDto>> GetFeesAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<PortalFeeLedgerDto>();

        var tenantId = _currentUser.TenantId;
        var enrollmentIds = await _studentEnrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        var invoices = enrollmentIds.Count == 0
            ? new List<StudentInvoice>()
            : await _invoices.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && enrollmentIds.Contains(x.StudentEnrollmentId))
                .OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.Id).Take(500).ToListAsync(cancellationToken);
        var payments = await _payments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentId == student.Id && x.State == PaymentState.Successful)
            .OrderByDescending(x => x.PaymentDate).ThenByDescending(x => x.Id).Take(500).ToListAsync(cancellationToken);

        return ApiResponse<PortalFeeLedgerDto>.SuccessResponse(new PortalFeeLedgerDto
        {
            TotalBilled = invoices.Sum(x => x.TotalAmount),
            TotalPaid = invoices.Sum(x => x.PaidAmount),
            TotalDue = invoices.Sum(x => x.DueAmount),
            Invoices = invoices.Select(x => new PortalInvoiceDto
            {
                Reference = x.PublicId,
                InvoiceNo = x.InvoiceNumber,
                Month = x.InvoiceDate.Month.ToString("D2"),
                Year = x.InvoiceDate.Year,
                BilledAmount = x.TotalAmount,
                PaidAmount = x.PaidAmount,
                DueAmount = x.DueAmount,
                Status = x.State.ToString(),
                DueDate = x.DueDate.ToDateTime(TimeOnly.MinValue)
            }).ToList(),
            Payments = payments.Select(x => new PortalPaymentDto
            {
                Reference = x.PublicId,
                ReceiptNo = x.ReceiptNumber,
                Amount = x.Amount,
                PaymentMethod = x.PaymentMethod.ToString(),
                PaymentDate = x.PaymentDate.ToDateTime(TimeOnly.MinValue)
            }).ToList()
        });
    }

    public async Task<ApiResponse<IReadOnlyList<PortalTransportDto>>> GetTransportAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalTransportDto>>();

        var tenantId = _currentUser.TenantId;
        var assignments = await _transport.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
            .OrderBy(x => x.State == TransportAssignmentState.Active ? 0 : 1)
            .ThenByDescending(x => x.StartDate).Take(100).ToListAsync(cancellationToken);
        var routeIds = assignments.Select(x => x.RouteId).Distinct().ToArray();
        var vehicleIds = assignments.Select(x => x.VehicleId).Distinct().ToArray();
        var stopIds = assignments.Where(x => x.PickupStopId.HasValue).Select(x => x.PickupStopId!.Value).Distinct().ToArray();
        var routes = routeIds.Length == 0 ? new Dictionary<long, Route>() : await _routes.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && routeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var vehicles = vehicleIds.Length == 0 ? new Dictionary<long, Vehicle>() : await _vehicles.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var stops = stopIds.Length == 0 ? new Dictionary<long, RouteStop>() : await _routeStops.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && stopIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);

        IReadOnlyList<PortalTransportDto> rows = assignments.Select(x =>
        {
            routes.TryGetValue(x.RouteId, out var route);
            vehicles.TryGetValue(x.VehicleId, out var vehicle);
            RouteStop? stop = null;
            if (x.PickupStopId.HasValue) stops.TryGetValue(x.PickupStopId.Value, out stop);
            return new PortalTransportDto
            {
                Reference = x.PublicId,
                RouteName = route?.Name ?? string.Empty,
                VehicleNo = vehicle?.VehicleNumber ?? string.Empty,
                PickupPoint = stop?.Name,
                DriverName = vehicle?.DriverName,
                DriverPhone = vehicle?.DriverPhone,
                StartDate = x.StartDate.ToDateTime(TimeOnly.MinValue),
                EndDate = x.EndDate?.ToDateTime(TimeOnly.MinValue),
                MonthlyFare = x.MonthlyFare,
                IsActive = x.State == TransportAssignmentState.Active
            };
        }).ToList();
        return ApiResponse<IReadOnlyList<PortalTransportDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalHomeworkDto>>> GetHomeworkAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalHomeworkDto>>();

        var courseIds = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.State == CourseEnrollmentState.Active)
            .Select(x => x.CourseId).Distinct().Take(200).ToListAsync(cancellationToken);
        if (courseIds.Count == 0) return ApiResponse<IReadOnlyList<PortalHomeworkDto>>.SuccessResponse(Array.Empty<PortalHomeworkDto>());

        var assignments = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && courseIds.Contains(x.CourseId) && x.Type == LearningTaskType.Homework && x.IsPublished)
            .OrderByDescending(x => x.OpensAt ?? x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(200).ToListAsync(cancellationToken);
        var usedCourseIds = assignments.Select(x => x.CourseId).Distinct().ToArray();
        var courses = await _courses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && usedCourseIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, x.SubjectId, x.Title }).ToListAsync(cancellationToken);
        var subjectIds = courses.Where(x => x.SubjectId.HasValue).Select(x => x.SubjectId!.Value).Distinct().ToArray();
        var subjects = subjectIds.Length == 0
            ? new Dictionary<long, string>()
            : await _subjects.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _currentUser.TenantId && subjectIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var courseMap = courses.ToDictionary(x => x.Id);

        IReadOnlyList<PortalHomeworkDto> rows = assignments.Where(x => courseMap.ContainsKey(x.CourseId)).Select(x =>
        {
            var course = courseMap[x.CourseId];
            var subjectId = course.SubjectId ?? 0;
            return new PortalHomeworkDto
            {
                HomeworkId = x.Id,
                SubjectId = subjectId,
                SubjectName = subjectId > 0 && subjects.TryGetValue(subjectId, out var subjectName) ? subjectName : course.Title,
                Title = x.Title,
                Description = x.Instructions,
                AssignedDate = x.OpensAt ?? x.CreatedAt,
                DueDate = x.DueAt ?? x.OpensAt ?? x.CreatedAt,
                AttachmentUrl = null
            };
        }).ToList();
        return ApiResponse<IReadOnlyList<PortalHomeworkDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<PortalAssignmentDto>>> GetAssignmentsAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        var student = await GetAuthorizedStudentAsync(studentReference, cancellationToken);
        if (student == null) return Denied<IReadOnlyList<PortalAssignmentDto>>();

        var courseIds = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == student.Id && x.State == CourseEnrollmentState.Active)
            .Select(x => x.CourseId).Distinct().Take(200).ToListAsync(cancellationToken);
        if (courseIds.Count == 0) return ApiResponse<IReadOnlyList<PortalAssignmentDto>>.SuccessResponse(Array.Empty<PortalAssignmentDto>());

        var assignments = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && courseIds.Contains(x.CourseId) && x.Type != LearningTaskType.Homework && x.IsPublished)
            .OrderBy(x => x.DueAt ?? DateTime.MaxValue).ThenBy(x => x.Id)
            .Take(200).ToListAsync(cancellationToken);
        var usedCourseIds = assignments.Select(x => x.CourseId).Distinct().ToArray();
        var courseTitles = await _courses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && usedCourseIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, x.Title }).ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

        IReadOnlyList<PortalAssignmentDto> rows = assignments.Where(x => courseTitles.ContainsKey(x.CourseId))
            .Select(x => new PortalAssignmentDto
            {
                Reference = x.PublicId,
                CourseId = x.CourseId,
                CourseTitle = courseTitles[x.CourseId],
                Title = x.Title,
                Description = x.Instructions,
                TotalMark = decimal.ToInt32(decimal.Round(x.MaxMarks, 0, MidpointRounding.AwayFromZero)),
                DueDate = x.DueAt ?? x.OpensAt ?? x.CreatedAt,
                AttachmentUrl = null
            }).ToList();
        return ApiResponse<IReadOnlyList<PortalAssignmentDto>>.SuccessResponse(rows);
    }

    private async Task<Student?> GetAuthorizedStudentAsync(Guid reference, CancellationToken cancellationToken)
    {
        if (!CanUsePortal() || reference == Guid.Empty) return null;
        var ids = await GetAuthorizedStudentIdsAsync(cancellationToken);
        return await _students.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == reference && ids.Contains(x.Id) && x.IsActive, cancellationToken);
    }

    private async Task<List<long>> GetAuthorizedStudentIdsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var direct = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.IsActive)
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        var guardianIds = await _guardians.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.IsActive)
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        var guarded = guardianIds.Count == 0
            ? new List<long>()
            : await _studentGuardians.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && guardianIds.Contains(x.GuardianId))
                .Select(x => x.StudentId).Distinct().Take(100).ToListAsync(cancellationToken);
        return direct.Concat(guarded).Distinct().Take(100).ToList();
    }

    private Task<StudentEnrollment?> GetCurrentEnrollmentAsync(long studentId, CancellationToken cancellationToken) =>
        _studentEnrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.StudentId == studentId && x.IsActive)
            .OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private bool CanUsePortal() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsInRole("Student") || _currentUser.IsInRole("Guardian") || _currentUser.IsInRole("Parent"));

    private static int DayOrder(string? day) => day?.Trim().ToLowerInvariant() switch
    {
        "saturday" => 0,
        "sunday" => 1,
        "monday" => 2,
        "tuesday" => 3,
        "wednesday" => 4,
        "thursday" => 5,
        "friday" => 6,
        _ => 7
    };

    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("The requested student is not linked to this account.", 403);
}
