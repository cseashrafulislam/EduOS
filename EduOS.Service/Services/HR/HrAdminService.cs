using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.HR;

public sealed class HrAdminService : IHrAdminService
{
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Designation> _designations;
    private readonly IGenericRepository<OrganizationUnit> _organizations;
    private readonly IGenericRepository<EmployeeLeaveApplication> _leaves;
    private readonly IGenericRepository<LeaveType> _leaveTypes;
    private readonly IGenericRepository<WorkShift> _shifts;
    private readonly IGenericRepository<EmployeeShiftAssignment> _shiftAssignments;
    private readonly IGenericRepository<EmployeeCampusAssignment> _campusAssignments;
    private readonly IGenericRepository<EmployeeAssignmentHistory> _history;
    private readonly IGenericRepository<EmployeeBankAccount> _bankAccounts;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<TenantMembership> _memberships;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly ILogger<HrAdminService> _logger;

    public HrAdminService(IGenericRepository<Employee> employees, IGenericRepository<Designation> designations,
        IGenericRepository<OrganizationUnit> organizations, IGenericRepository<EmployeeLeaveApplication> leaves,
        IGenericRepository<LeaveType> leaveTypes, IGenericRepository<WorkShift> shifts,
        IGenericRepository<EmployeeShiftAssignment> shiftAssignments,
        IGenericRepository<EmployeeCampusAssignment> campusAssignments,
        IGenericRepository<EmployeeAssignmentHistory> history,
        IGenericRepository<EmployeeBankAccount> bankAccounts, IGenericRepository<Campus> campuses,
        IGenericRepository<Person> persons, IGenericRepository<TenantMembership> memberships,
        IGenericRepository<Student> students, IGenericRepository<Guardian> guardians,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock,
        IDataProtectionProvider dataProtection, ILogger<HrAdminService> logger)
    {
        _employees = employees; _designations = designations; _organizations = organizations;
        _leaves = leaves; _leaveTypes = leaveTypes; _shifts = shifts;
        _shiftAssignments = shiftAssignments; _campusAssignments = campusAssignments;
        _history = history; _bankAccounts = bankAccounts; _campuses = campuses; _persons = persons;
        _memberships = memberships; _students = students; _guardians = guardians;
        _uow = uow; _user = user; _clock = clock; _dataProtection = dataProtection; _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<HrEmployeeRowDto>>> GetEmployeesAsync(
        HrEmployeeQueryDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Error<PagedResult<HrEmployeeRowDto>>("HR access required.", 403);
        if (request == null || request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.OrganizationUnitId is <= 0 || request.Search?.Length > 100 ||
            request.State.HasValue && !Enum.IsDefined(request.State.Value))
            return Error<PagedResult<HrEmployeeRowDto>>("Invalid employee search.");
        var tenant = _user.TenantId;
        var query = _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (request.OrganizationUnitId.HasValue)
            query = query.Where(x => x.OrganizationUnitId == request.OrganizationUnitId);
        if (request.CanTeach.HasValue) query = query.Where(x => x.CanTeach == request.CanTeach);
        if (request.State.HasValue) query = query.Where(x => x.State == request.State);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.EmployeeCode.StartsWith(term) || x.FullName.StartsWith(term) ||
                x.Phone != null && x.Phone.Contains(term) || x.Email != null && x.Email.StartsWith(term));
        }
        var total = await query.CountAsync(ct);
        var skip = (long)(request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue) return Error<PagedResult<HrEmployeeRowDto>>("Page outside supported range.");
        var rows = await (from employee in query
            join designation in _designations.GetQueryable().AsNoTracking()
                on new { employee.TenantId, Id = employee.DesignationId }
                equals new { designation.TenantId, designation.Id }
            join org in _organizations.GetQueryable().AsNoTracking()
                on new { employee.TenantId, Id = employee.OrganizationUnitId }
                equals new { org.TenantId, Id = (long?)org.Id } into organizations
            from org in organizations.DefaultIfEmpty()
            orderby employee.EmployeeCode, employee.Id
            select new HrEmployeeRowDto
            {
                Reference = employee.PublicId, EmployeeCode = employee.EmployeeCode,
                FullName = employee.FullName, Phone = employee.Phone, Email = employee.Email,
                DesignationName = designation.Name, OrganizationUnitId = employee.OrganizationUnitId,
                OrganizationUnitName = org == null ? null : org.Name,
                JoiningDate = employee.JoiningDate, CanTeach = employee.CanTeach,
                State = employee.State, RowVersion = Convert.ToBase64String(employee.RowVersion)
            }).Skip((int)skip).Take(request.PageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<HrEmployeeRowDto>>.SuccessResponse(new PagedResult<HrEmployeeRowDto>
        { Page = request.Page, PageSize = request.PageSize, TotalCount = total, Items = rows });
    }

    public async Task<ApiResponse<EmployeeDto>> GetEmployeeAsync(Guid employeeReference,
        CancellationToken ct = default)
    {
        if (!CanHr()) return Error<EmployeeDto>("HR access required.", 403);
        var employee = await FindEmployeeAsync(employeeReference, ct);
        return employee == null ? Error<EmployeeDto>("Employee not found.", 404) :
            ApiResponse<EmployeeDto>.SuccessResponse(await MapEmployeeAsync(employee, ct));
    }

    public async Task<ApiResponse<EmployeeDto>> CreateEmployeeAsync(
        CreateEmployeeRequestDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Error<EmployeeDto>("HR access required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            !ValidNameCode(request.FullName, request.EmployeeCode) || request.DesignationId <= 0 ||
            request.JoiningDate == default || request.PersonId is <= 0 || request.UserId is <= 0 ||
            request.OrganizationUnitId is <= 0 || request.Phone?.Length > 30 ||
            request.Email?.Length > 200 || request.Address?.Length > 1000 ||
            request.PhotoUrl?.Length > 500)
            return Error<EmployeeDto>("Invalid employee identity or employment details.");
        var tenant = _user.TenantId;
        var err = await ValidateReferencesAsync(request.DesignationId, request.OrganizationUnitId,
            request.PersonId, request.UserId, ct);
        if (err != null) return Error<EmployeeDto>(err, 409);
        return await WriteAsync("create employee", async token =>
        {
            var code = request.EmployeeCode.Trim().ToUpperInvariant();
            var existing = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.EmployeeCode == code && !x.IsDeleted, token);
            if (existing != null)
            {
                if (existing.PersonId != request.PersonId && request.PersonId.HasValue ||
                    existing.UserId != request.UserId || existing.FullName != request.FullName.Trim() ||
                    existing.DesignationId != request.DesignationId ||
                    existing.OrganizationUnitId != request.OrganizationUnitId ||
                    existing.JoiningDate != request.JoiningDate || existing.CanTeach != request.CanTeach)
                    return Error<EmployeeDto>("Employee code already exists with different identity or terms.", 409);
                return ApiResponse<EmployeeDto>.SuccessResponse(await MapEmployeeAsync(existing, token),
                    "Employee already exists.");
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            var personId = request.PersonId.GetValueOrDefault();
            if (personId == 0)
            {
                var person = new Person
                {
                    FullName = request.FullName.Trim(), Phone = Trim(request.Phone),
                    Email = Trim(request.Email), PhotoUrl = Trim(request.PhotoUrl),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _persons.AddAsync(person);
                await _uow.SaveChangesAsync(token);
                personId = person.Id;
            }
            var row = new Employee
            {
                TenantId = tenant, PersonId = personId, UserId = request.UserId,
                OrganizationUnitId = request.OrganizationUnitId, DesignationId = request.DesignationId,
                EmployeeCode = code, FullName = request.FullName.Trim(), Phone = Trim(request.Phone),
                Email = Trim(request.Email), Address = Trim(request.Address),
                JoiningDate = request.JoiningDate, CanTeach = request.CanTeach,
                PhotoUrl = Trim(request.PhotoUrl), State = EmployeeState.Active,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _employees.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            await _history.AddAsync(new EmployeeAssignmentHistory
            {
                TenantId = tenant, EmployeeId = row.Id,
                OrganizationUnitId = row.OrganizationUnitId, DesignationId = row.DesignationId,
                EmploymentTypeCode = row.EmploymentTypeCode, EffectiveFrom = row.JoiningDate,
                IsCurrent = true, CreatedAt = now, CreatedBy = _user.UserId
            });
            await _uow.SaveChangesAsync(token);
            return ApiResponse<EmployeeDto>.SuccessResponse(await MapEmployeeAsync(row, token), "Employee created.");
        }, ct);
    }

    public Task<ApiResponse<EmployeeDto>> UpdateEmployeeAsync(Guid employeeReference,
        UpdateEmployeeRequestDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Task.FromResult(Error<EmployeeDto>("HR access required.", 403));
        if (employeeReference == Guid.Empty || request == null || !ValidName(request.FullName) ||
            request.DesignationId <= 0 || request.OrganizationUnitId is <= 0 ||
            !TryVersion(request.RowVersion, out var version) ||
            request.Phone?.Length > 30 || request.Email?.Length > 200 ||
            request.Address?.Length > 1000 || request.PhotoUrl?.Length > 500)
            return Task.FromResult(Error<EmployeeDto>("Invalid employee update or concurrency token."));
        return WriteAsync("update employee", async token =>
        {
            var row = await _employees.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.PublicId == employeeReference && !x.IsDeleted, token);
            if (row == null) return Error<EmployeeDto>("Employee not found.", 404);
            if (!Matches(row.RowVersion, version)) return Error<EmployeeDto>("Employee modified; reload.", 409);
            if (row.State is EmployeeState.Terminated or EmployeeState.Retired)
                return Error<EmployeeDto>("Terminated or retired employees require controlled reinstatement.", 409);
            var err = await ValidateReferencesAsync(request.DesignationId, request.OrganizationUnitId,
                null, null, token);
            if (err != null) return Error<EmployeeDto>(err, 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row.DesignationId != request.DesignationId || row.OrganizationUnitId != request.OrganizationUnitId)
            {
                var previous = await _history.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.EmployeeId == row.Id && x.IsCurrent && !x.IsDeleted, token);
                var effective = DateOnly.FromDateTime(now);
                if (previous != null && previous.EffectiveFrom >= effective)
                    return Error<EmployeeDto>("Changing organization twice on the same effective day requires amendment.", 409);
                if (previous != null)
                {
                    previous.IsCurrent = false; previous.EffectiveTo = effective.AddDays(-1);
                    previous.UpdatedAt = now; previous.UpdatedBy = _user.UserId;
                    _history.Update(previous);
                }
                await _history.AddAsync(new EmployeeAssignmentHistory
                {
                    TenantId = _user.TenantId, EmployeeId = row.Id,
                    CampusId = previous?.CampusId, OrganizationUnitId = request.OrganizationUnitId,
                    DesignationId = request.DesignationId,
                    EmploymentTypeCode = row.EmploymentTypeCode, EffectiveFrom = effective,
                    IsCurrent = true, CreatedAt = now, CreatedBy = _user.UserId
                });
            }
            row.DesignationId = request.DesignationId; row.OrganizationUnitId = request.OrganizationUnitId;
            row.FullName = request.FullName.Trim(); row.Phone = Trim(request.Phone);
            row.Email = Trim(request.Email); row.Address = Trim(request.Address);
            row.CanTeach = request.CanTeach; row.PhotoUrl = Trim(request.PhotoUrl);
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _employees.Update(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<EmployeeDto>.SuccessResponse(await MapEmployeeAsync(row, token),
                "Employee updated.");
        }, ct);
    }

    public Task<ApiResponse<EmployeeDto>> ChangeEmployeeStateAsync(Guid employeeReference,
        ChangeEmployeeStateRequestDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Task.FromResult(Error<EmployeeDto>("HR access required.", 403));
        if (employeeReference == Guid.Empty || request == null ||
            !Enum.IsDefined(request.State) || request.Reason?.Length > 1000 ||
            !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<EmployeeDto>("Invalid employee status or row version."));
        return WriteAsync("employee state transition", async token =>
        {
            var row = await _employees.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.PublicId == employeeReference && !x.IsDeleted, token);
            if (row == null) return Error<EmployeeDto>("Employee not found.", 404);
            if (!Matches(row.RowVersion, expected)) return Error<EmployeeDto>("Employee changed; reload.", 409);
            if (row.State == request.State)
                return ApiResponse<EmployeeDto>.SuccessResponse(await MapEmployeeAsync(row, token),
                    "Employee status unchanged.");
            if (row.State is EmployeeState.Resigned or EmployeeState.Terminated or EmployeeState.Retired)
                return Error<EmployeeDto>("Terminal employment states cannot be reopened by this endpoint.", 409);
            if (row.State == EmployeeState.Active && request.State is not
                (EmployeeState.Suspended or EmployeeState.Resigned or EmployeeState.Terminated or EmployeeState.Retired) ||
                row.State == EmployeeState.Suspended && request.State is not
                (EmployeeState.Active or EmployeeState.Resigned or EmployeeState.Terminated or EmployeeState.Retired))
                return Error<EmployeeDto>("Illegal employee state transition.", 409);
            var exit = request.State is EmployeeState.Resigned or EmployeeState.Terminated or EmployeeState.Retired;
            if (exit && (!request.LeavingDate.HasValue || request.LeavingDate < row.JoiningDate ||
                string.IsNullOrWhiteSpace(request.Reason)))
                return Error<EmployeeDto>("Employment exit requires leaving date and reason.", 409);
            if (!exit && request.LeavingDate.HasValue)
                return Error<EmployeeDto>("Leaving date must be absent for non-terminal states.");
            var now = _clock.GetUtcNow().UtcDateTime;
            row.State = request.State; row.LeavingDate = exit ? request.LeavingDate : null;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _employees.Update(row); await _uow.SaveChangesAsync(token);
            return ApiResponse<EmployeeDto>.SuccessResponse(await MapEmployeeAsync(row, token),
                "Employee status updated.");
        }, ct);
    }

    public Task<ApiResponse<EmployeeShiftAssignmentDto>> AssignShiftAsync(
        AssignEmployeeShiftRequestDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Task.FromResult(Error<EmployeeShiftAssignmentDto>("HR access required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.WorkShiftId <= 0 || request.EffectiveFrom == default ||
            request.EffectiveTo.HasValue && request.EffectiveTo < request.EffectiveFrom)
            return Task.FromResult(Error<EmployeeShiftAssignmentDto>("Invalid employee shift assignment."));
        return WriteAsync("assign shift", async token =>
        {
            var employee = await FindEmployeeAsync(request.EmployeeReference, token);
            if (employee == null) return Error<EmployeeShiftAssignmentDto>("Employee not found.", 404);
            var shift = await _shifts.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == request.WorkShiftId &&
                x.IsActive && !x.IsDeleted, token);
            if (shift == null) return Error<EmployeeShiftAssignmentDto>("Work shift not found.", 404);
            if (request.EffectiveFrom < employee.JoiningDate ||
                employee.LeavingDate.HasValue && request.EffectiveFrom > employee.LeavingDate.Value)
                return Error<EmployeeShiftAssignmentDto>("Assignment date outside employment period.", 409);
            var current = await _shiftAssignments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.EmployeeId == employee.Id && x.IsCurrent &&
                !x.IsDeleted, token);
            if (current != null)
            {
                if (current.WorkShiftId == shift.Id && current.EffectiveFrom == request.EffectiveFrom &&
                    current.EffectiveTo == request.EffectiveTo)
                    return ApiResponse<EmployeeShiftAssignmentDto>.SuccessResponse(MapShift(current, employee, shift),
                        "Shift already assigned.");
                if (request.EffectiveFrom <= current.EffectiveFrom ||
                    current.EffectiveTo.HasValue && request.EffectiveFrom <= current.EffectiveTo)
                    return Error<EmployeeShiftAssignmentDto>("Shift periods overlap; use a later effective date.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            if (current != null)
            {
                current.EffectiveTo = request.EffectiveFrom.AddDays(-1);
                current.IsCurrent = false; current.UpdatedAt = now; current.UpdatedBy = _user.UserId;
                _shiftAssignments.Update(current);
            }
            var row = new EmployeeShiftAssignment
            {
                TenantId = _user.TenantId, EmployeeId = employee.Id, WorkShiftId = shift.Id,
                EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                IsCurrent = !request.EffectiveTo.HasValue, CreatedAt = now, CreatedBy = _user.UserId
            };
            await _shiftAssignments.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<EmployeeShiftAssignmentDto>.SuccessResponse(MapShift(row, employee, shift),
                "Employee shift assigned.");
        }, ct);
    }

    public Task<ApiResponse<EmployeeCampusAssignmentDto>> AssignCampusAsync(
        AssignEmployeeCampusRequestDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Task.FromResult(Error<EmployeeCampusAssignmentDto>("HR access required.", 403));
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.CampusId <= 0 || request.EffectiveFrom == default ||
            request.EffectiveTo.HasValue && request.EffectiveTo < request.EffectiveFrom)
            return Task.FromResult(Error<EmployeeCampusAssignmentDto>("Invalid employee campus assignment."));
        return WriteAsync("assign campus", async token =>
        {
            var employee = await FindEmployeeAsync(request.EmployeeReference, token);
            if (employee == null) return Error<EmployeeCampusAssignmentDto>("Employee not found.", 404);
            var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == request.CampusId &&
                x.IsActive && !x.IsDeleted, token);
            if (campus == null) return Error<EmployeeCampusAssignmentDto>("Campus not found.", 404);
            if (request.EffectiveFrom < employee.JoiningDate ||
                employee.LeavingDate.HasValue && request.EffectiveFrom > employee.LeavingDate.Value)
                return Error<EmployeeCampusAssignmentDto>("Campus assignment outside employment period.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var current = await _campusAssignments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.EmployeeId == employee.Id &&
                x.CampusId == campus.Id && x.IsCurrent && !x.IsDeleted, token);
            if (current != null && current.EffectiveFrom == request.EffectiveFrom &&
                current.EffectiveTo == request.EffectiveTo && current.IsPrimary == request.IsPrimary)
                return ApiResponse<EmployeeCampusAssignmentDto>.SuccessResponse(
                    MapCampus(current, employee), "Campus already assigned.");
            if (current != null && (request.EffectiveFrom <= current.EffectiveFrom ||
                current.EffectiveTo.HasValue && request.EffectiveFrom <= current.EffectiveTo.Value))
                return Error<EmployeeCampusAssignmentDto>("Existing campus assignment overlaps.", 409);
            if (request.IsPrimary && await _campusAssignments.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == _user.TenantId && x.EmployeeId == employee.Id &&
                    x.IsPrimary && x.IsCurrent && !x.IsDeleted && x.CampusId != campus.Id, token))
                return Error<EmployeeCampusAssignmentDto>("An existing primary campus must be closed before replacement.", 409);
            if (current != null)
            {
                current.EffectiveTo = request.EffectiveFrom.AddDays(-1);
                current.IsCurrent = false; current.UpdatedAt = now; current.UpdatedBy = _user.UserId;
                _campusAssignments.Update(current);
            }
            var row = new EmployeeCampusAssignment
            {
                TenantId = _user.TenantId, EmployeeId = employee.Id, CampusId = campus.Id,
                EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                IsPrimary = request.IsPrimary, IsCurrent = !request.EffectiveTo.HasValue,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _campusAssignments.AddAsync(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<EmployeeCampusAssignmentDto>.SuccessResponse(MapCampus(row, employee),
                "Employee campus assigned.");
        }, ct);
    }

    public async Task<ApiResponse<PagedResult<EmployeeAssignmentHistoryDto>>> GetAssignmentHistoryAsync(
        Guid employeeReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanHr()) return Error<PagedResult<EmployeeAssignmentHistoryDto>>("HR access required.", 403);
        if (page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<EmployeeAssignmentHistoryDto>>("Invalid pagination.");
        var employee = await FindEmployeeAsync(employeeReference, ct);
        if (employee == null) return Error<PagedResult<EmployeeAssignmentHistoryDto>>("Employee not found.", 404);
        var query = _history.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.EmployeeId == employee.Id && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<EmployeeAssignmentHistoryDto>>("Page out of range.");
        var rows = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip((int)skip).Take(pageSize).Select(x => new EmployeeAssignmentHistoryDto
            {
                Id = x.Id, EmployeeReference = employeeReference, CampusId = x.CampusId,
                OrganizationUnitId = x.OrganizationUnitId, DesignationId = x.DesignationId,
                EmploymentTypeCode = x.EmploymentTypeCode, EffectiveFrom = x.EffectiveFrom,
                EffectiveTo = x.EffectiveTo, IsCurrent = x.IsCurrent, Reason = x.Reason
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<EmployeeAssignmentHistoryDto>>.SuccessResponse(new PagedResult<EmployeeAssignmentHistoryDto>
        { Page = page, PageSize = pageSize, TotalCount = total, Items = rows });
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeeBankAccountDto>>> GetBankAccountsAsync(
        Guid employeeReference, CancellationToken ct = default)
    {
        if (!CanHr()) return Error<IReadOnlyList<EmployeeBankAccountDto>>("HR access required.", 403);
        var employee = await FindEmployeeAsync(employeeReference, ct);
        if (employee == null) return Error<IReadOnlyList<EmployeeBankAccountDto>>("Employee not found.", 404);
        var list = await _bankAccounts.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.EmployeeId == employee.Id && !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<EmployeeBankAccountDto>>.SuccessResponse(
            list.Select(x => MapBank(x, employee.PublicId)).ToList());
    }

    public Task<ApiResponse<EmployeeBankAccountDto>> SaveBankAccountAsync(long? bankAccountId,
        SaveEmployeeBankAccountRequestDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Task.FromResult(Error<EmployeeBankAccountDto>("HR access required.", 403));
        if (request == null || request.EmployeeReference == Guid.Empty || bankAccountId is <= 0 ||
            !ValidName(request.BankName, 150) || !ValidName(request.AccountName, 150) ||
            string.IsNullOrWhiteSpace(request.AccountNumber) || request.AccountNumber.Length > 200 ||
            request.CurrencyCode?.Length is < 3 or > 10 ||
            request.BranchName?.Length > 100 || request.RoutingNumber?.Length > 100 ||
            bankAccountId.HasValue && !TryVersion(request.RowVersion, out _))
            return Task.FromResult(Error<EmployeeBankAccountDto>("Invalid employee bank details or row version."));
        return WriteAsync("save employee bank account", async token =>
        {
            var employee = await FindEmployeeAsync(request.EmployeeReference, token);
            if (employee == null) return Error<EmployeeBankAccountDto>("Employee not found.", 404);
            var row = bankAccountId.HasValue ? await _bankAccounts.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == bankAccountId &&
                x.EmployeeId == employee.Id && !x.IsDeleted, token) : null;
            if (bankAccountId.HasValue && row == null)
                return Error<EmployeeBankAccountDto>("Bank account not found.", 404);
            if (row != null && !Matches(row.RowVersion, request.RowVersion))
                return Error<EmployeeBankAccountDto>("Bank account changed; reload.", 409);
            var number = request.AccountNumber.Trim();
            var now = _clock.GetUtcNow().UtcDateTime;
            var protector = _dataProtection.CreateProtector(
                "EduOS.HR.EmployeeBankAccount.v1", _user.TenantId.ToString());
            if (row != null && row.ProtectedAccountNumber.Length > 0 &&
                protector.Unprotect(row.ProtectedAccountNumber) == number &&
                row.BankName == request.BankName.Trim() &&
                row.AccountName == request.AccountName.Trim() &&
                row.BranchName == Trim(request.BranchName) &&
                row.RoutingNumber == Trim(request.RoutingNumber) &&
                row.CurrencyCode == request.CurrencyCode.Trim().ToUpperInvariant() &&
                row.IsPrimary == request.IsPrimary && row.IsActive == request.IsActive)
                return ApiResponse<EmployeeBankAccountDto>.SuccessResponse(
                    MapBank(row, employee.PublicId), "Bank account unchanged.");
            if (request.IsPrimary && request.IsActive)
            {
                var otherPrimaries = await _bankAccounts.GetQueryable().Where(x =>
                    x.TenantId == _user.TenantId && x.EmployeeId == employee.Id &&
                    x.IsPrimary && x.IsActive && !x.IsDeleted &&
                    (!bankAccountId.HasValue || x.Id != bankAccountId.Value)).ToListAsync(token);
                foreach (var other in otherPrimaries)
                {
                    other.IsPrimary = false; other.UpdatedAt = now; other.UpdatedBy = _user.UserId;
                    _bankAccounts.Update(other);
                }
            }
            if (row == null)
            {
                row = new EmployeeBankAccount
                {
                    TenantId = _user.TenantId, EmployeeId = employee.Id,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _bankAccounts.AddAsync(row);
            }
            else
            {
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _bankAccounts.Update(row);
            }
            row.BankName = request.BankName.Trim(); row.AccountName = request.AccountName.Trim();
            row.ProtectedAccountNumber = protector.Protect(number);
            row.AccountNumberLast4 = number.Length <= 4 ? number : number[^4..];
            row.BranchName = Trim(request.BranchName); row.RoutingNumber = Trim(request.RoutingNumber);
            row.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
            row.IsPrimary = request.IsPrimary && request.IsActive; row.IsActive = request.IsActive;
            await _uow.SaveChangesAsync(token);
            return ApiResponse<EmployeeBankAccountDto>.SuccessResponse(MapBank(row, employee.PublicId),
                "Protected bank account saved.");
        }, ct);
    }

    public async Task<ApiResponse<PagedResult<HrLeaveRowDto>>> GetEmployeeLeavesAsync(
        HrLeaveQueryDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return Error<PagedResult<HrLeaveRowDto>>("HR access required.", 403);
        if (request == null || request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.Search?.Length > 100 || request.State.HasValue && !Enum.IsDefined(request.State.Value) ||
            request.FromDate.HasValue && request.ToDate.HasValue && request.FromDate > request.ToDate)
            return Error<PagedResult<HrLeaveRowDto>>("Invalid leave filters.");
        var tenant = _user.TenantId;
        var query = from leave in _leaves.GetQueryable().AsNoTracking()
            join employee in _employees.GetQueryable().AsNoTracking()
                on new { leave.TenantId, Id = leave.EmployeeId }
                equals new { employee.TenantId, employee.Id }
            join kind in _leaveTypes.GetQueryable().AsNoTracking()
                on new { leave.TenantId, Id = leave.LeaveTypeId }
                equals new { kind.TenantId, kind.Id }
            where leave.TenantId == tenant && !leave.IsDeleted && !employee.IsDeleted
            select new { leave, employee, kind };
        if (request.State.HasValue) query = query.Where(x => x.leave.State == request.State.Value);
        if (request.FromDate.HasValue) query = query.Where(x => x.leave.ToDate >= request.FromDate.Value);
        if (request.ToDate.HasValue) query = query.Where(x => x.leave.FromDate <= request.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.employee.EmployeeCode.StartsWith(term) ||
                x.employee.FullName.StartsWith(term));
        }
        var total = await query.CountAsync(ct);
        var skip = (long)(request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue) return Error<PagedResult<HrLeaveRowDto>>("Page outside supported range.");
        var rows = await query.OrderByDescending(x => x.leave.FromDate)
            .ThenByDescending(x => x.leave.Id).Skip((int)skip).Take(request.PageSize)
            .Select(x => new
            {
                x.leave.Id, EmployeeReference = x.employee.PublicId, x.employee.EmployeeCode,
                EmployeeName = x.employee.FullName, LeaveTypeName = x.kind.Name,
                x.leave.FromDate, x.leave.ToDate, x.leave.TotalDays, x.leave.Reason,
                x.leave.State, x.leave.ReviewNote, x.leave.RowVersion
            }).ToListAsync(ct);
        var items = rows.Select(x => new HrLeaveRowDto
        {
            Id = x.Id, EmployeeReference = x.EmployeeReference, EmployeeCode = x.EmployeeCode,
            EmployeeName = x.EmployeeName, LeaveTypeName = x.LeaveTypeName,
            FromDate = x.FromDate, ToDate = x.ToDate, TotalDays = x.TotalDays,
            Reason = x.Reason, State = x.State, ReviewNote = x.ReviewNote,
            RowVersion = Version(x.RowVersion)
        }).ToList();
        return ApiResponse<PagedResult<HrLeaveRowDto>>.SuccessResponse(new PagedResult<HrLeaveRowDto>
        { Page = request.Page, PageSize = request.PageSize, TotalCount = total, Items = items });
    }

    public Task<ApiResponse<bool>> ReviewEmployeeLeaveAsync(ReviewEmployeeLeaveDto request,
        CancellationToken ct = default)
    {
        if (!CanHr()) return Task.FromResult(Error<bool>("HR access required.", 403));
        if (request == null || request.Id <= 0 ||
            request.State is not (LeaveState.Approved or LeaveState.Rejected) ||
            !TryVersion(request.RowVersion, out var version) || request.ReviewNote?.Length > 1000 ||
            request.State == LeaveState.Rejected && string.IsNullOrWhiteSpace(request.ReviewNote))
            return Task.FromResult(Error<bool>("Invalid leave decision, version or rejection note."));
        return WriteAsync("review employee leave", async token =>
        {
            var row = await _leaves.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == request.Id && !x.IsDeleted, token);
            if (row == null || !await _employees.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == _user.TenantId && x.Id == row.EmployeeId && !x.IsDeleted, token))
                return Error<bool>("Employee leave application not found.", 404);
            if (!Matches(row.RowVersion, version)) return Error<bool>("Leave application changed.", 409);
            if (row.State != LeaveState.Submitted)
                return Error<bool>("Only submitted leave can be reviewed.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.State = request.State; row.ReviewNote = Trim(request.ReviewNote);
            row.ReviewedByUserId = _user.UserId; row.ReviewedAt = now;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _leaves.Update(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true, "Employee leave reviewed.");
        }, ct);
    }

    private async Task<Employee?> FindEmployeeAsync(Guid reference, CancellationToken ct) =>
        reference == Guid.Empty ? null : await _employees.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);

    private async Task<string?> ValidateReferencesAsync(long designationId, long? organizationId,
        long? personId, long? userId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        if (!await _designations.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == designationId && x.IsActive && !x.IsDeleted, ct))
            return "Designation not found for this tenant.";
        if (organizationId.HasValue && !await _organizations.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == organizationId.Value && x.IsActive && !x.IsDeleted, ct))
            return "Organization unit does not belong to this tenant.";
        if (userId.HasValue && !await _memberships.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.UserId == userId.Value &&
            x.Status == MembershipStatus.Active && !x.IsDeleted, ct))
            return "User is not an active member of this tenant.";
        if (personId.HasValue)
        {
            if (!await _persons.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.Id == personId.Value && !x.IsDeleted, ct))
                return "Person record does not exist.";
            var linked = await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.PersonId == personId.Value && !x.IsDeleted, ct) ||
                await _guardians.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.PersonId == personId.Value && !x.IsDeleted, ct) ||
                await _employees.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.PersonId == personId.Value && !x.IsDeleted, ct);
            if (!linked) return "Person is not associated with the current tenant.";
        }
        return null;
    }

    private async Task<EmployeeDto> MapEmployeeAsync(Employee row, CancellationToken ct)
    {
        var tenant = row.TenantId;
        var designation = await _designations.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == row.DesignationId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        var org = row.OrganizationUnitId.HasValue ? await _organizations.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == row.OrganizationUnitId.Value)
            .Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var assignment = await _shiftAssignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.EmployeeId == row.Id && x.IsCurrent && !x.IsDeleted, ct);
        EmployeeShiftAssignmentDto? shift = null;
        if (assignment != null)
        {
            var name = await _shifts.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Id == assignment.WorkShiftId)
                .Select(x => x.Name).FirstOrDefaultAsync(ct);
            shift = new EmployeeShiftAssignmentDto
            {
                Id = assignment.Id, EmployeeReference = row.PublicId,
                WorkShiftId = assignment.WorkShiftId, WorkShiftName = name ?? string.Empty,
                EffectiveFrom = assignment.EffectiveFrom, EffectiveTo = assignment.EffectiveTo,
                IsCurrent = assignment.IsCurrent, RowVersion = Version(assignment.RowVersion)
            };
        }
        return new EmployeeDto
        {
            Id = row.Id, Reference = row.PublicId, PersonId = row.PersonId, UserId = row.UserId,
            OrganizationUnitId = row.OrganizationUnitId, OrganizationUnitName = org,
            DesignationId = row.DesignationId, DesignationName = designation ?? string.Empty,
            EmployeeCode = row.EmployeeCode, FullName = row.FullName, Phone = row.Phone,
            Email = row.Email, Address = row.Address, JoiningDate = row.JoiningDate,
            LeavingDate = row.LeavingDate, CanTeach = row.CanTeach, State = row.State,
            PhotoUrl = row.PhotoUrl, CurrentShift = shift, RowVersion = Version(row.RowVersion)
        };
    }

    private static EmployeeShiftAssignmentDto MapShift(
        EmployeeShiftAssignment row, Employee employee, WorkShift shift) => new()
    {
        Id = row.Id, EmployeeReference = employee.PublicId, WorkShiftId = shift.Id,
        WorkShiftName = shift.Name, EffectiveFrom = row.EffectiveFrom, EffectiveTo = row.EffectiveTo,
        IsCurrent = row.IsCurrent, RowVersion = Version(row.RowVersion)
    };
    private static EmployeeCampusAssignmentDto MapCampus(EmployeeCampusAssignment row, Employee employee) => new()
    {
        Id = row.Id, EmployeeReference = employee.PublicId, CampusId = row.CampusId,
        EffectiveFrom = row.EffectiveFrom, EffectiveTo = row.EffectiveTo, IsPrimary = row.IsPrimary,
        IsCurrent = row.IsCurrent, RowVersion = Version(row.RowVersion)
    };
    private static EmployeeBankAccountDto MapBank(EmployeeBankAccount row, Guid employeeReference) => new()
    {
        Id = row.Id, EmployeeReference = employeeReference,
        BankName = row.BankName, AccountName = row.AccountName,
        AccountNumberLast4 = row.AccountNumberLast4, BranchName = row.BranchName,
        RoutingNumber = row.RoutingNumber, CurrencyCode = row.CurrencyCode,
        IsPrimary = row.IsPrimary, IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };

    private async Task<ApiResponse<T>> WriteAsync<T>(string operation,
        Func<CancellationToken, Task<ApiResponse<T>>> callback, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(callback, ct); }
        catch (DbUpdateConcurrencyException)
        { return Error<T>("Record changed concurrently; reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "HR record update conflict {Operation} tenant {TenantId}", operation, _user.TenantId);
            return Error<T>("HR record conflicts with an existing transaction.", 409);
        }
    }
    private bool CanHr() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("HR"));
    private static bool ValidName(string? text, int limit = 200) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= limit;
    private static bool ValidNameCode(string? name, string? code) =>
        ValidName(name) && ValidName(code, 50);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static bool TryVersion(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool Matches(byte[] stored, string? version) =>
        TryVersion(version, out var bytes) && Matches(stored, bytes);
    private static bool Matches(byte[] stored, byte[] bytes) =>
        stored != null && stored.Length == bytes.Length && stored.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(stored, bytes);
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
