using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.HR;

public sealed class HrPayrollService : IPayrollAdministrationService
{
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<ApplicationUser> _identityUsers;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<EmployeeCampusAssignment> _campusAssignments;
    private readonly IGenericRepository<SalaryComponent> _components;
    private readonly IGenericRepository<SalaryStructure> _structures;
    private readonly IGenericRepository<SalaryStructureLine> _structureLines;
    private readonly IGenericRepository<PayrollRun> _runs;
    private readonly IGenericRepository<PayrollEmployee> _payrollEmployees;
    private readonly IGenericRepository<PayrollLine> _payrollLines;
    private readonly IGenericRepository<PayrollPayment> _payments;
    private readonly IGenericRepository<Bonus> _bonuses;
    private readonly IGenericRepository<LoanAdvance> _loans;
    private readonly IGenericRepository<LoanAdvanceRecovery> _recoveries;
    private readonly IGenericRepository<BankAccount> _bankAccounts;
    private readonly IGenericRepository<Journal> _journals;
    private readonly IGenericRepository<EmployeeBankAccount> _employeeBankAccounts;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<HrPayrollService> _logger;
    private readonly IPayrollJournalPostingService? _journalPosting;

    public HrPayrollService(IGenericRepository<Employee> employees,
        IGenericRepository<ApplicationUser> identityUsers,
        IGenericRepository<Campus> campuses, IGenericRepository<EmployeeCampusAssignment> campusAssignments,
        IGenericRepository<SalaryComponent> components, IGenericRepository<SalaryStructure> structures,
        IGenericRepository<SalaryStructureLine> structureLines, IGenericRepository<PayrollRun> runs,
        IGenericRepository<PayrollEmployee> payrollEmployees, IGenericRepository<PayrollLine> payrollLines,
        IGenericRepository<PayrollPayment> payments, IGenericRepository<Bonus> bonuses,
        IGenericRepository<LoanAdvance> loans, IGenericRepository<LoanAdvanceRecovery> recoveries,
        IGenericRepository<BankAccount> bankAccounts,
        IGenericRepository<Journal> journals,
        IGenericRepository<EmployeeBankAccount> employeeBankAccounts,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock, ILogger<HrPayrollService> logger,
        IPayrollJournalPostingService? journalPosting = null)
    {
        _employees = employees; _identityUsers = identityUsers;
        _campuses = campuses; _campusAssignments = campusAssignments;
        _components = components; _structures = structures;
        _structureLines = structureLines; _runs = runs; _payrollEmployees = payrollEmployees;
        _payrollLines = payrollLines; _payments = payments; _bonuses = bonuses; _loans = loans;
        _recoveries = recoveries; _bankAccounts = bankAccounts; _journals = journals;
        _employeeBankAccounts = employeeBankAccounts;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
        _journalPosting = journalPosting;
    }

    public async Task<ApiResponse<PagedResult<PayrollEmployeeOptionDto>>> GetEmployeeOptionsAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PagedResult<PayrollEmployeeOptionDto>>();
        if (search?.Length > 100 || page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<PayrollEmployeeOptionDto>>("Invalid employee search or pagination.");
        var query = _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.State == EmployeeState.Active && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.EmployeeCode.Contains(term) || x.FullName.Contains(term));
        }
        var count = await query.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
            return Error<PagedResult<PayrollEmployeeOptionDto>>("Employee page exceeds supported range.");
        var items = await query.OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id)
            .Skip((int)skip).Take(pageSize)
            .Select(x => new PayrollEmployeeOptionDto
            {
                Reference = x.PublicId, EmployeeCode = x.EmployeeCode, FullName = x.FullName
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<PayrollEmployeeOptionDto>>.SuccessResponse(
            new PagedResult<PayrollEmployeeOptionDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count, Items = items
            });
    }

    public async Task<ApiResponse<IReadOnlyList<SalaryComponentDto>>> GetSalaryComponentsAsync(CancellationToken ct = default)
    {
        if (!Manager()) return Denied<IReadOnlyList<SalaryComponentDto>>();
        var rows = await _components.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted)
            .OrderBy(x => x.Code).Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SalaryComponentDto>>.SuccessResponse(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<SalaryComponentDto>> SaveSalaryComponentAsync(
        long? componentId, SaveSalaryComponentRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<SalaryComponentDto>();
        if (request == null || !ValidNameCode(request.Name, request.Code, 100) ||
            !Enum.IsDefined(request.Type) || componentId is <= 0)
            return Error<SalaryComponentDto>("Salary component name, code or type is invalid.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var code = request.Code.Trim().ToUpperInvariant();
                // Tenant-reviewed Tax/PF percentages are supported alongside manual amounts.
                // We never infer statutory rates or exemptions from a component code.
                if (code.StartsWith("STAT_", StringComparison.Ordinal) &&
                    (request.Type != SalaryComponentType.Deduction || request.IsTaxable))
                    return Error<SalaryComponentDto>(
                        "STAT_ withholding components must be non-taxable deductions.");
                var row = componentId.HasValue ? await _components.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == componentId.Value && !x.IsDeleted, token) : null;
                if (componentId.HasValue && row == null) return Error<SalaryComponentDto>("Component not found.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion))
                    return Error<SalaryComponentDto>("Salary component changed. Reload and retry.", 409);
                var other = await _components.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Code == code && !x.IsDeleted && (!componentId.HasValue || x.Id != componentId), token);
                if (other != null) return Error<SalaryComponentDto>("Salary component code already exists.", 409);
                if (row != null && row.Type != request.Type && await _structureLines.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.SalaryComponentId == row.Id && !x.IsDeleted, token))
                    return Error<SalaryComponentDto>("Component type cannot change after salary structure usage.", 409);
                if (row != null && row.Code.StartsWith("STAT_", StringComparison.Ordinal) &&
                    row.Code != code && await _structureLines.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.SalaryComponentId == row.Id && !x.IsDeleted, token))
                    return Error<SalaryComponentDto>("Used statutory withholding codes cannot be renamed.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new SalaryComponent { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                    await _components.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now; row.UpdatedBy = _user.UserId; _components.Update(row);
                }
                row.Code = code; row.Name = request.Name.Trim(); row.Type = request.Type;
                row.IsTaxable = request.IsTaxable; row.IsActive = request.IsActive;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<SalaryComponentDto>.SuccessResponse(Map(row), "Salary component saved.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Salary component write conflict tenant {TenantId}", _user.TenantId);
            return Error<SalaryComponentDto>("Salary component conflicts with another update.", 409);
        }
    }

    public async Task<ApiResponse<SalaryStructureDto>> SaveSalaryStructureAsync(
        SaveSalaryStructureRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<SalaryStructureDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.EffectiveFrom == default || request.EffectiveTo < request.EffectiveFrom ||
            request.Lines == null || request.Lines.Count is < 1 or > 100 ||
            request.Lines.Any(x => x == null || x.SalaryComponentId <= 0 ||
                x.Amount is < 0 or > 999999999999m ||
                x.CalculationMethodCode is not ("Fixed" or "Manual" or "Auto")) ||
            request.Lines.Select(x => x.SalaryComponentId).Distinct().Count() != request.Lines.Count)
            return Error<SalaryStructureDto>("Invalid employee, structure dates or salary component lines.");
        if (request.Lines.Any(x => decimal.Round(x.Amount, 2) != x.Amount ||
            x.Percentage.HasValue && decimal.Round(x.Percentage.Value, 4) != x.Percentage.Value))
            return Error<SalaryStructureDto>("Amount precision is two decimals; rate precision is four decimals.");
        var fingerprint = SalaryStructureFingerprint(request);
        var previous = await _structures.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
        if (previous != null)
        {
            if (previous.RequestFingerprint != fingerprint)
                return Error<SalaryStructureDto>("Request key belongs to another salary structure.", 409);
            var historicalEmployee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.Id == previous.EmployeeId, ct);
            if (historicalEmployee == null) return Error<SalaryStructureDto>("Salary employee is missing.", 409);
            var historicalLines = await _structureLines.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == _user.TenantId && x.SalaryStructureId == previous.Id && !x.IsDeleted)
                .ToListAsync(ct);
            var historicalIds = historicalLines.Select(x => x.SalaryComponentId).Distinct().ToArray();
            var historicalComponents = await _components.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == _user.TenantId && historicalIds.Contains(x.Id)).ToListAsync(ct);
            if (historicalComponents.Count != historicalIds.Length)
                return Error<SalaryStructureDto>("Salary history references missing components.", 409);
            return ApiResponse<SalaryStructureDto>.SuccessResponse(
                MapStructure(previous, historicalEmployee, historicalLines, historicalComponents),
                "Existing salary request returned.");
        }
        var employee = await ActiveEmployeeAsync(request.EmployeeReference, ct);
        if (employee == null) return Error<SalaryStructureDto>("Active employee not found.", 404);
        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && request.Lines.Select(y => y.SalaryComponentId).Contains(x.Id) &&
            x.IsActive && !x.IsDeleted).ToListAsync(ct);
        if (components.Count != request.Lines.Count)
            return Error<SalaryStructureDto>("One or more salary components are inactive.", 409);
        if (components.Any(x => x.Code == "BONUS" || x.Code == "LOAN_RECOVERY"))
            return Error<SalaryStructureDto>(
                "BONUS and LOAN_RECOVERY are transaction-derived components; do not add them as fixed salary.", 409);
        var statutory = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.IsActive && !x.IsDeleted &&
            x.Code.StartsWith("STAT_")).OrderBy(x => x.Id).Take(51).ToListAsync(ct);
        if (statutory.Count > 50 || statutory.Any(x => x.Type != SalaryComponentType.Deduction || x.IsTaxable))
            return Error<SalaryStructureDto>("Statutory withholding component configuration requires reconciliation.", 409);
        if (!PayrollStatutoryCoverage.IncludesEach(statutory.Select(x => x.Id),
            request.Lines.Select(x => x.SalaryComponentId)))
            return Error<SalaryStructureDto>(
                "Every active STAT_ deduction requires a salary line, including a zero amount for reviewed exemptions.", 409);
        if (request.Lines.Any(x => decimal.Round(x.Amount, 2) != x.Amount))
            return Error<SalaryStructureDto>("Salary component amounts must have at most two decimal places.", 409);
        var componentTypeById = components.ToDictionary(x => x.Id, x => x.Type);
        if (request.Lines.Any(x => !PayrollStatutoryCalculation.ValidLine(
            components.First(c => c.Id == x.SalaryComponentId).Code,
            componentTypeById[x.SalaryComponentId], x.CalculationMethodCode,
            x.Amount, x.Percentage, x.BasedOnSalaryComponentId, componentTypeById)))
            return Error<SalaryStructureDto>(
                "Tax/PF Auto requires an approved 0–100% rate and an eligible earning base (PF requires a base). Other components use Manual BDT amounts.", 409);
        var previewLines = request.Lines.Select(x => new SalaryStructureLine
        {
            SalaryComponentId = x.SalaryComponentId,
            CalculationMethodCode = x.CalculationMethodCode == "Auto" ? "Auto" : "Fixed",
            Amount = x.Amount, Percentage = x.Percentage,
            BasedOnSalaryComponentId = x.BasedOnSalaryComponentId
        }).ToList();
        if (!PayrollStatutoryCalculation.TryCalculate(previewLines,
            components.ToDictionary(x => x.Id), 30, 30, "FullMonthOnly", 0m, out var preview))
            return Error<SalaryStructureDto>(
                "Auto calculation base must be included among this salary structure's earnings.", 409);
        var gross = preview.Where(x => componentTypeById[x.SalaryComponentId] == SalaryComponentType.Earning)
            .Sum(x => x.Amount);
        var deductions = preview.Where(x => componentTypeById[x.SalaryComponentId] == SalaryComponentType.Deduction)
            .Sum(x => x.Amount);
        if (gross <= 0m || deductions > gross)
            return Error<SalaryStructureDto>("Salary structure requires positive earnings and nonnegative net salary.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                // Retry after an overlapping request committed while preflight was running.
                var replay = await _structures.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.ClientRequestId == request.ClientRequestId, token);
                if (replay != null)
                {
                    if (replay.RequestFingerprint != fingerprint || replay.EmployeeId != employee.Id)
                        return Error<SalaryStructureDto>("Request key belongs to another salary structure.", 409);
                    var replayLines = await _structureLines.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == _user.TenantId && x.SalaryStructureId == replay.Id && !x.IsDeleted)
                        .ToListAsync(token);
                    return ApiResponse<SalaryStructureDto>.SuccessResponse(
                        MapStructure(replay, employee, replayLines, components), "Existing salary request returned.");
                }
                var duplicateEffectiveDate = await _structures.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.EmployeeId == employee.Id &&
                    x.EffectiveFrom == request.EffectiveFrom && !x.IsDeleted, token);
                var current = await _structures.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.EmployeeId == employee.Id &&
                    x.IsCurrent && !x.IsDeleted, token);
                if (current != null)
                {
                    var currentLines = await _structureLines.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == _user.TenantId && x.SalaryStructureId == current.Id && !x.IsDeleted)
                        .ToListAsync(token);
                    if (current.EffectiveFrom == request.EffectiveFrom &&
                        current.EffectiveTo == request.EffectiveTo && currentLines.Count == request.Lines.Count &&
                        request.Lines.All(x => currentLines.Any(y =>
                            y.SalaryComponentId == x.SalaryComponentId && y.Amount == x.Amount &&
                            y.CalculationMethodCode == (x.CalculationMethodCode == "Auto" ? "Auto" : "Fixed") &&
                            y.Percentage == x.Percentage && y.BasedOnSalaryComponentId == x.BasedOnSalaryComponentId)))
                        return ApiResponse<SalaryStructureDto>.SuccessResponse(
                            MapStructure(current, employee, currentLines, components), "Salary structure already exists.");
                    if (request.EffectiveFrom <= current.EffectiveFrom)
                        return Error<SalaryStructureDto>("New structure must begin after current effective date.", 409);
                    if (request.EffectiveTo.HasValue)
                        return Error<SalaryStructureDto>("A replacement salary structure must remain open-ended.", 409);
                    if (duplicateEffectiveDate)
                        return Error<SalaryStructureDto>("Salary structure already exists for this effective date.", 409);
                    current.EffectiveTo = request.EffectiveFrom.AddDays(-1);
                    current.IsCurrent = false;
                    current.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    current.UpdatedBy = _user.UserId;
                    _structures.Update(current);
                }
                if (duplicateEffectiveDate)
                    return Error<SalaryStructureDto>("Salary structure already exists for this effective date.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var row = new SalaryStructure
                {
                    TenantId = _user.TenantId, EmployeeId = employee.Id,
                    ClientRequestId = request.ClientRequestId, RequestFingerprint = fingerprint,
                    EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                    IsCurrent = !request.EffectiveTo.HasValue,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _structures.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                var lines = request.Lines.Select(x => new SalaryStructureLine
                {
                    TenantId = _user.TenantId, SalaryStructureId = row.Id,
                    SalaryComponentId = x.SalaryComponentId,
                    CalculationMethodCode = x.CalculationMethodCode == "Auto" ? "Auto" : "Fixed",
                    Percentage = x.Percentage, BasedOnSalaryComponentId = x.BasedOnSalaryComponentId,
                    Amount = Math.Round(x.Amount, 2, MidpointRounding.AwayFromZero),
                    CreatedAt = now, CreatedBy = _user.UserId
                }).ToList();
                await _structureLines.AddRangeAsync(lines);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<SalaryStructureDto>.SuccessResponse(
                    MapStructure(row, employee, lines, components), "Salary structure saved.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Salary structure conflict tenant {TenantId}", _user.TenantId);
            return Error<SalaryStructureDto>("Salary structure conflicts with an existing update.", 409);
        }
    }


    /// <summary>Tenant-administrator-only historical salary structure register.
    /// Cross-campus payroll data is never exposed to campus-limited roles.</summary>
    public async Task<ApiResponse<PagedResult<SalaryStructureDto>>> GetSalaryStructuresAsync(
        Guid employeeReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return Denied<PagedResult<SalaryStructureDto>>();
        if (employeeReference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<SalaryStructureDto>>("Invalid employee or pagination.");
        var tenant = _user.TenantId;
        var employee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == employeeReference && !x.IsDeleted, ct);
        if (employee == null)
            return Error<PagedResult<SalaryStructureDto>>("Employee not found.", 404);
        var query = _structures.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.EmployeeId == employee.Id && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var lines = await _structureLines.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids.Contains(x.SalaryStructureId) && !x.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var componentIds = lines.Select(x => x.SalaryComponentId).Distinct().ToArray();
        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && componentIds.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
        if (lines.Any(x => !components.Any(c => c.Id == x.SalaryComponentId)))
            return Error<PagedResult<SalaryStructureDto>>(
                "Salary history references a missing component; contact payroll administration.", 409);
        var lookup = lines.ToLookup(x => x.SalaryStructureId);
        var items = rows.Select(x => MapStructure(x, employee, lookup[x.Id].ToList(), components)).ToList();
        return ApiResponse<PagedResult<SalaryStructureDto>>.SuccessResponse(
            new PagedResult<SalaryStructureDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count, Items = items
            });
    }

    public async Task<ApiResponse<PayrollRunDto>> CreatePayrollRunAsync(CreatePayrollRunRequestDto request,
        CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PayrollRunDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.Year is < 2000 or > 2200 ||
            request.Month is < 1 or > 12 || request.CampusId is <= 0 ||
            request.ProrationModeCode is not ("FullMonthOnly" or "CalendarDays"))
            return Error<PayrollRunDto>("Invalid payroll period, campus or idempotency key.");
        var tenant = _user.TenantId;
        var firstDay = new DateOnly(request.Year, request.Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var existing = await _runs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.Year != request.Year || existing.Month != request.Month ||
                        existing.CampusId != request.CampusId ||
                        existing.ProrationModeCode != request.ProrationModeCode)
                        return Error<PayrollRunDto>("Idempotency key was used for a different payroll scope or policy.", 409);
                    return ApiResponse<PayrollRunDto>.SuccessResponse(await MapRunAsync(existing, token),
                        "Payroll request already processed.");
                }
                if (await _runs.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Year == request.Year && x.Month == request.Month &&
                    x.State != PayrollRunState.Cancelled && !x.IsDeleted &&
                    (!x.CampusId.HasValue || !request.CampusId.HasValue ||
                     x.CampusId == request.CampusId), token))
                    return Error<PayrollRunDto>("An overlapping institution/campus payroll run already exists.", 409);
                if (request.CampusId.HasValue && !await _campuses.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.Id == request.CampusId &&
                        x.IsActive && !x.IsDeleted, token))
                    return Error<PayrollRunDto>("Active payroll campus not found.", 404);

                // Primary assignments must cover each employee's actually employed dates
                // in this month. Split/transfer rows are rejected rather than silently omitted.
                var campusScope = _campusAssignments.GetQueryable().AsNoTracking().Where(a =>
                    a.TenantId == tenant && !a.IsDeleted && a.IsPrimary &&
                    a.CampusId == request.CampusId &&
                    a.EffectiveFrom <= lastDay &&
                    (!a.EffectiveTo.HasValue || a.EffectiveTo >= firstDay));
                var campusEmployeeIds = from a in campusScope
                                        join e in _employees.GetQueryable().AsNoTracking()
                                            on new { a.TenantId, Id = a.EmployeeId }
                                            equals new { e.TenantId, e.Id }
                                        where !e.IsDeleted &&
                                            a.EffectiveFrom <= (e.JoiningDate > firstDay ? e.JoiningDate : firstDay) &&
                                            (!a.EffectiveTo.HasValue || a.EffectiveTo >=
                                                (e.LeavingDate.HasValue && e.LeavingDate < lastDay ?
                                                    e.LeavingDate.Value : lastDay))
                                        select a.EmployeeId;
                if (request.CampusId.HasValue &&
                    await campusScope.AnyAsync(a => !campusEmployeeIds.Contains(a.EmployeeId), token))
                    return Error<PayrollRunDto>(
                        "A primary campus assignment does not cover the employed dates. Resolve the transfer first.", 409);
                if (request.CampusId.HasValue && await _campusAssignments.GetQueryable().AsNoTracking()
                    .AnyAsync(a => a.TenantId == tenant && !a.IsDeleted && a.IsPrimary &&
                        a.CampusId != request.CampusId && campusEmployeeIds.Contains(a.EmployeeId) &&
                        a.EffectiveFrom <= lastDay &&
                        (!a.EffectiveTo.HasValue || a.EffectiveTo >= firstDay), token))
                    return Error<PayrollRunDto>("Overlapping primary campus assignments must be reconciled.", 409);
                // No silent exclusion of partial-month service or non-active staff.
                // Without an approved institution proration/leave policy, fail before snapshot creation.
                var employeeScope = _employees.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted);
                if (request.CampusId.HasValue)
                    employeeScope = employeeScope.Where(x => campusEmployeeIds.Contains(x.Id));
                var calendarDays = request.ProrationModeCode == "CalendarDays";
                // Suspended employees and exits without an effective leaving date need
                // a reviewed HR pay decision; no country-specific statutory rules are inferred.
                var needsPolicy = await employeeScope.AnyAsync(x =>
                    x.JoiningDate <= lastDay &&
                    (!x.LeavingDate.HasValue || x.LeavingDate >= firstDay) &&
                    (calendarDays
                        ? x.State == EmployeeState.Suspended ||
                          x.State != EmployeeState.Active &&
                          (x.State != EmployeeState.Resigned &&
                           x.State != EmployeeState.Terminated &&
                           x.State != EmployeeState.Retired ||
                           !x.LeavingDate.HasValue ||
                           x.LeavingDate > lastDay)
                        : x.JoiningDate > firstDay ||
                          (x.LeavingDate.HasValue && x.LeavingDate < lastDay) ||
                          x.State != EmployeeState.Active), token);
                if (needsPolicy)
                    return Error<PayrollRunDto>(
                        "Partial-month employment or non-active staff needs an approved payroll proration/leave policy. No payroll was generated.", 409);
                var employees = await employeeScope.Where(x =>
                    x.JoiningDate <= lastDay &&
                    (!x.LeavingDate.HasValue || x.LeavingDate >= firstDay) &&
                    (x.State == EmployeeState.Active ||
                     calendarDays && x.LeavingDate.HasValue &&
                     x.LeavingDate <= lastDay &&
                     (x.State == EmployeeState.Resigned ||
                      x.State == EmployeeState.Terminated ||
                      x.State == EmployeeState.Retired)))
                    .OrderBy(x => x.Id).Take(5001).ToListAsync(token);
                if (employees.Count > 5000)
                    return Error<PayrollRunDto>("Period exceeds the synchronous payroll size limit.", 409);
                if (employees.Count == 0)
                    return Error<PayrollRunDto>("No employees are eligible for this payroll period.", 409);
                var ids = employees.Select(x => x.Id).ToArray();
                var structures = await _structures.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && ids.Contains(x.EmployeeId) && !x.IsDeleted &&
                    x.EffectiveFrom <= lastDay &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= firstDay))
                    .OrderBy(x => x.EmployeeId).ThenByDescending(x => x.EffectiveFrom)
                    .Take(10001).ToListAsync(token);
                if (structures.Count > 10000)
                    return Error<PayrollRunDto>("Salary history exceeds safe calculation bounds.", 409);
                structures = structures.Where(x =>
                    employees.Any(e => e.Id == x.EmployeeId &&
                        x.EffectiveFrom <= (e.JoiningDate > firstDay ? e.JoiningDate : firstDay) &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo >=
                            (e.LeavingDate.HasValue && e.LeavingDate < lastDay ?
                                e.LeavingDate.Value : lastDay)))).ToList();
                if (structures.Select(x => x.EmployeeId).Distinct().Count() != employees.Count ||
                    structures.GroupBy(x => x.EmployeeId).Any(x => x.Count() != 1))
                    return Error<PayrollRunDto>("Every employee needs exactly one salary structure covering the full month.", 409);
                var structureIds = structures.Select(x => x.Id).ToArray();
                var salaryLines = await _structureLines.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && structureIds.Contains(x.SalaryStructureId) && !x.IsDeleted)
                    .ToListAsync(token);
                var componentIds = salaryLines.Select(x => x.SalaryComponentId).Distinct().ToArray();
                var comps = await _components.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && componentIds.Contains(x.Id) && !x.IsDeleted)
                    .ToDictionaryAsync(x => x.Id, token);
                if (comps.Count != componentIds.Length || salaryLines.Any(x =>
                    comps.TryGetValue(x.SalaryComponentId, out var component) &&
                        (component.Code == "BONUS" || component.Code == "LOAN_RECOVERY")))
                    return Error<PayrollRunDto>(
                        "Invalid or transaction-derived salary component; reconcile the salary structure.", 409);
                // Deactivation affects new calculations only. Never silently keep withholding
                // from an inactive component; posted payroll snapshots remain untouched.
                if (comps.Values.Any(x => !x.IsActive))
                    return Error<PayrollRunDto>(
                        "A salary structure uses an inactive component. Register a new effective-dated salary structure before calculating payroll.", 409);
                var statutoryComponents = await _components.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.IsActive && !x.IsDeleted &&
                    x.Code.StartsWith("STAT_")).OrderBy(x => x.Id).Take(51).ToListAsync(token);
                if (statutoryComponents.Count > 50 || statutoryComponents.Any(x =>
                    x.Type != SalaryComponentType.Deduction || x.IsTaxable))
                    return Error<PayrollRunDto>("Statutory withholding configuration requires reconciliation.", 409);
                var statutoryIds = statutoryComponents.Select(x => x.Id).ToArray();
                var statutoryLines = salaryLines.ToLookup(x => x.SalaryStructureId);
                if (statutoryIds.Length > 0 && structures.Any(s =>
                    !PayrollStatutoryCoverage.IncludesEach(statutoryIds,
                        statutoryLines[s.Id].Select(line => line.SalaryComponentId))))
                    return Error<PayrollRunDto>(
                        "All eligible employees need effective-dated STAT_ withholding lines (zero for reviewed exemptions). No payroll was generated.", 409);
                var bonuses = await _bonuses.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && ids.Contains(x.EmployeeId) &&
                    x.BonusDate >= firstDay && x.BonusDate <= lastDay && !x.IsDeleted)
                    .GroupBy(x => x.EmployeeId).Select(x => new { EmployeeId = x.Key, Amount = x.Sum(y => y.Amount) })
                    .ToListAsync(token);
                var bonusByEmployee = bonuses.ToDictionary(x => x.EmployeeId, x => x.Amount);
                var bonusComponent = await _components.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Code == "BONUS" && x.Type == SalaryComponentType.Earning &&
                    x.IsActive && !x.IsDeleted, token);
                if (bonuses.Any(x => x.Amount > 0m) && bonusComponent == null)
                    return Error<PayrollRunDto>("Active BONU​S earning component must be configured for payroll bonus.", 409);
                var loans = await _loans.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && ids.Contains(x.EmployeeId) &&
                    !x.IsClosed && x.IssueDate <= lastDay && x.OutstandingAmount > 0m && !x.IsDeleted)
                    .OrderBy(x => x.Id).Take(5001).ToListAsync(token);
                if (loans.Count > 5000)
                    return Error<PayrollRunDto>("Loan volume exceeds the safe payroll calculation limit.", 409);
                if (loans.Count > 0)
                {
                    // A registered advance is NOT a paid advance. Salary must never withhold
                    // repayment without an actual, non-reversed institutional disbursement.
                    var loanIds = loans.Select(x => x.Id).ToArray();
                    var sources = await _journals.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.SourceType == "LoanAdvance" &&
                        x.SourceId.HasValue && loanIds.Contains(x.SourceId.Value))
                        .Select(x => new { x.Id, x.SourceId, x.State, x.JournalDate,
                            x.ReversalOfJournalId, x.CurrencyCode, x.CampusId })
                        .Take(5001).ToListAsync(token);
                    if (sources.Count > 5000)
                        return Error<PayrollRunDto>("Loan journal history exceeds the safe reconciliation limit.", 409);
                    if (loans.Any(loan =>
                    {
                        var history = sources.Where(x => x.SourceId == loan.Id).ToArray();
                        return history.Length != 1 || history[0].State != JournalState.Posted ||
                            history[0].ReversalOfJournalId.HasValue ||
                            history[0].JournalDate > lastDay || history[0].CurrencyCode != "BDT" ||
                            (request.CampusId.HasValue && history[0].CampusId != request.CampusId);
                    }))
                        return Error<PayrollRunDto>(
                            "Loan withholding requires one valid posted disbursement journal for every open loan. No payroll was generated.", 409);
                    var journalIds = sources.Select(x => x.Id).ToArray();
                    if (await _journals.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.State == JournalState.Posted &&
                        x.ReversalOfJournalId.HasValue &&
                        journalIds.Contains(x.ReversalOfJournalId.Value), token))
                        return Error<PayrollRunDto>(
                            "A loan disbursement has been reversed. Reconcile the loan before calculating salary.", 409);
                }
                var loanByEmployee = loans.ToLookup(x => x.EmployeeId);
                var loanComponent = await _components.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Code == "LOAN_RECOVERY" &&
                    x.Type == SalaryComponentType.Deduction && x.IsActive && !x.IsDeleted, token);
                if (loans.Count > 0 && loanComponent == null)
                    return Error<PayrollRunDto>("Active LOAN_RECOVERY deduction component must be configured.", 409);
                var structureByEmployee = structures.ToDictionary(x => x.EmployeeId);
                var structureLineLookup = salaryLines.ToLookup(x => x.SalaryStructureId);
                var amounts = new Dictionary<long, (decimal Gross, decimal Deductions)>();
                var calculatedLines = new Dictionary<long, IReadOnlyList<PayrollStatutoryCalculation.CalculatedLine>>();
                foreach (var employee in employees)
                {
                    var sourceLines = structureLineLookup[structureByEmployee[employee.Id].Id].ToList();
                    var serviceDays = PayrollProration.ServiceDays(employee.JoiningDate, employee.LeavingDate,
                        firstDay, lastDay);
                    var monthDays = lastDay.DayNumber - firstDay.DayNumber + 1;
                    var bonus = bonusByEmployee.GetValueOrDefault(employee.Id);
                    if (!PayrollStatutoryCalculation.TryCalculate(sourceLines, comps,
                        serviceDays, monthDays, request.ProrationModeCode,
                        bonusComponent?.IsTaxable == true ? bonus : 0m, out var calculated))
                        return Error<PayrollRunDto>(
                            "Invalid Tax/PF Auto or Manual salary configuration; payroll was not generated.", 409);
                    calculatedLines.Add(employee.Id, calculated);
                    var earnings = calculated.Where(x => comps[x.SalaryComponentId].Type == SalaryComponentType.Earning)
                        .Sum(x => x.Amount);
                    var withheld = calculated.Where(x => comps[x.SalaryComponentId].Type == SalaryComponentType.Deduction)
                        .Sum(x => x.Amount);
                    var recover = loanByEmployee[employee.Id].Sum(x => Math.Min(x.InstallmentAmount, x.OutstandingAmount));
                    var gross = Round(earnings + bonus);
                    var deductions = Round(withheld + recover);
                    if (gross <= 0m || deductions > gross)
                        return Error<PayrollRunDto>("Employee has invalid net salary or deductions.", 409);
                    amounts.Add(employee.Id, (gross, deductions));
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                var run = new PayrollRun
                {
                    TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    RunNumber = "PR-" + request.Year.ToString("D4") + request.Month.ToString("D2") +
                        "-" + request.ClientRequestId.ToString("N").Substring(0, 12),
                    Year = request.Year, Month = request.Month, CampusId = request.CampusId,
                    ProrationModeCode = request.ProrationModeCode,
                    PeriodStart = firstDay, PeriodEnd = lastDay,
                    State = PayrollRunState.Calculated, CalculatedAt = now,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _runs.AddAsync(run);
                await _uow.SaveChangesAsync(token);
                foreach (var employee in employees)
                {
                    var bonus = bonusByEmployee.GetValueOrDefault(employee.Id);
                    var recover = loanByEmployee[employee.Id]
                        .Sum(x => Math.Min(x.InstallmentAmount, x.OutstandingAmount));
                    var (gross, deductions) = amounts[employee.Id];
                    var row = new PayrollEmployee
                    {
                        TenantId = tenant, PayrollRunId = run.Id, EmployeeId = employee.Id,
                        EmployeeCodeSnapshot = employee.EmployeeCode,
                        EmployeeNameSnapshot = employee.FullName,
                        GrossAmount = gross, DeductionAmount = deductions,
                        NetAmount = Round(gross - deductions), CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _payrollEmployees.AddAsync(row);
                    await _uow.SaveChangesAsync(token);
                    var snapshots = calculatedLines[employee.Id].Select(x => new PayrollLine
                    {
                        TenantId = tenant, PayrollEmployeeId = row.Id,
                        SalaryComponentId = x.SalaryComponentId,
                        Amount = x.Amount, Description = x.Description,
                        CreatedAt = now, CreatedBy = _user.UserId
                    }).ToList();
                    if (bonus > 0m && bonusComponent != null)
                        snapshots.Add(new PayrollLine
                        {
                            TenantId = tenant, PayrollEmployeeId = row.Id,
                            SalaryComponentId = bonusComponent.Id, Amount = Round(bonus),
                            Description = "Period bonus", CreatedAt = now, CreatedBy = _user.UserId
                        });
                    if (recover > 0m && loanComponent != null)
                        snapshots.Add(new PayrollLine
                        {
                            TenantId = tenant, PayrollEmployeeId = row.Id,
                            SalaryComponentId = loanComponent.Id, Amount = Round(recover),
                            Description = "Scheduled loan recovery", CreatedAt = now, CreatedBy = _user.UserId
                        });
                    await _payrollLines.AddRangeAsync(snapshots);
                }
                await _uow.SaveChangesAsync(token);
                return ApiResponse<PayrollRunDto>.SuccessResponse(await MapRunAsync(run, token),
                    "Payroll calculated; approval and accounting posting are required.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PayrollRunDto>("Payroll was updated concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Payroll generation conflict for tenant {TenantId}", tenant);
            return Error<PayrollRunDto>("Duplicate payroll period or request.", 409);
        }
    }

    public async Task<ApiResponse<PayrollRunDto>> GetPayrollRunAsync(Guid runReference, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PayrollRunDto>();
        var row = await FindRunAsync(runReference, ct);
        return row == null ? Error<PayrollRunDto>("Payroll run not found.", 404) :
            ApiResponse<PayrollRunDto>.SuccessResponse(await MapRunAsync(row, ct));
    }

    public Task<ApiResponse<PayrollRunDto>> ApprovePayrollRunAsync(Guid runReference,
        ApprovePayrollRunRequestDto request, CancellationToken ct = default)
    {
        if (!Approver()) return Task.FromResult(Denied<PayrollRunDto>());
        if (runReference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var version))
            return Task.FromResult(Error<PayrollRunDto>("Payroll reference and version are required."));
        return ExecuteWriteAsync("approve payroll", async token =>
        {
            var row = await _runs.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == _user.TenantId && x.PublicId == runReference && !x.IsDeleted, token);
            if (row == null) return Error<PayrollRunDto>("Payroll run not found.", 404);
            if (!Matches(row.RowVersion, version)) return Error<PayrollRunDto>("Payroll was modified; reload.", 409);
            if (row.State != PayrollRunState.Calculated)
                return Error<PayrollRunDto>("Only calculated payroll can be approved.", 409);
            if (row.CreatedBy == _user.UserId)
                return Error<PayrollRunDto>("Payroll creator cannot approve their own run.", 403);
            var now = _clock.GetUtcNow().UtcDateTime;
            row.State = PayrollRunState.Approved; row.ApprovedAt = now;
            row.ApprovedByUserId = _user.UserId; row.UpdatedAt = now;
            row.UpdatedBy = _user.UserId; _runs.Update(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<PayrollRunDto>.SuccessResponse(await MapRunAsync(row, token),
                "Payroll approved; accounting posting is required.");
        }, ct);
    }

    public async Task<ApiResponse<PayrollRunDto>> PostPayrollRunAsync(Guid runReference,
        PostPayrollRunRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PayrollRunDto>();
        if (runReference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out _) ||
            request.AccountingPeriodId <= 0 || request.SalaryPayableAccountId <= 0 || request.Mappings == null)
            return Error<PayrollRunDto>("Payroll reference, row version, accounting period and complete component ledger mappings are required.");
        if (_journalPosting == null)
            return Error<PayrollRunDto>("Institutional payroll posting service is unavailable.", 503);
        // Both API routes must use the same serializable, idempotent GL transaction.
        // No state transition is permitted here independently of a balanced journal.
        var posted = await _journalPosting.PostAsync(runReference, new PostPayrollJournalRequestDto
        {
            RowVersion = request.RowVersion,
            AccountingPeriodId = request.AccountingPeriodId,
            SalaryPayableAccountId = request.SalaryPayableAccountId,
            Mappings = request.Mappings
        }, ct);
        if (!posted.Success)
            return Error<PayrollRunDto>(posted.Message, posted.StatusCode);
        var run = await GetPayrollRunAsync(runReference, ct);
        if (!run.Success) return run;
        run.Message = posted.Data?.AlreadyPosted == true ? "Payroll was already posted." :
            "Payroll GL journal posted and reconciled.";
        return run;
    }

    /// <summary>Tenant administrator's immutable bonus register; payroll snapshots are not changed by this read.</summary>
    public async Task<ApiResponse<PagedResult<BonusDto>>> GetBonusesAsync(
        int? year, int? month, Guid? employeeReference, int page, int pageSize, CancellationToken ct = default)
    {
        // The payroll administration service has no campus-access repository. Restrict this
        // cross-campus financial register to the tenant administrator rather than leaking HR data.
        if (!_user.IsAuthenticated || _user.TenantId <= 0 || !_user.IsTenantAdmin)
            return Denied<PagedResult<BonusDto>>();
        if (year is < 2000 or > 2200 || month is < 1 or > 12 ||
            employeeReference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<BonusDto>>("Invalid bonus filters or pagination.");
        var tenant = _user.TenantId;
        var query = from bonus in _bonuses.GetQueryable().AsNoTracking()
                    join employee in _employees.GetQueryable().AsNoTracking()
                        on new { bonus.TenantId, Id = bonus.EmployeeId }
                        equals new { employee.TenantId, employee.Id }
                    where bonus.TenantId == tenant && !bonus.IsDeleted && !employee.IsDeleted
                    select new { Bonus = bonus, Employee = employee };
        if (year.HasValue) query = query.Where(x => x.Bonus.BonusDate.Year == year.Value);
        if (month.HasValue) query = query.Where(x => x.Bonus.BonusDate.Month == month.Value);
        if (employeeReference.HasValue)
            query = query.Where(x => x.Employee.PublicId == employeeReference.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Bonus.BonusDate)
            .ThenByDescending(x => x.Bonus.Id).Skip((int)((long)(page - 1) * pageSize))
            .Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<BonusDto>>.SuccessResponse(new PagedResult<BonusDto>
        {
            Items = rows.Select(x => MapBonus(x.Bonus, x.Employee)).ToList(),
            Page = page, PageSize = pageSize, TotalCount = count
        });
    }

    public async Task<ApiResponse<BonusDto>> SaveBonusAsync(long? bonusId, SaveBonusRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<BonusDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.BonusDate == default || request.Amount <= 0 || request.Amount > 999999999999m ||
            request.Amount != decimal.Round(request.Amount, 2) ||
            request.Reason?.Length > 500 || bonusId is <= 0)
            return Error<BonusDto>("Invalid bonus request.");
        var employee = await ActiveEmployeeAsync(request.EmployeeReference, ct);
        if (employee == null) return Error<BonusDto>("Employee not found.", 404);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = bonusId.HasValue ? await _bonuses.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.Id == bonusId.Value && !x.IsDeleted, token) : null;
                if (bonusId.HasValue && row == null) return Error<BonusDto>("Bonus not found.", 404);
                if (row != null) return Error<BonusDto>("Existing bonuses cannot be overwritten after payroll calculation.", 409);
                if (await _runs.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.Year == request.BonusDate.Year &&
                    x.Month == request.BonusDate.Month && x.State != PayrollRunState.Cancelled &&
                    !x.IsDeleted, token))
                    return Error<BonusDto>("Bonus date belongs to an existing payroll run.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                row = new Bonus
                {
                    TenantId = _user.TenantId, EmployeeId = employee.Id,
                    BonusDate = request.BonusDate, Amount = Round(request.Amount),
                    Reason = Trim(request.Reason), CreatedAt = now, CreatedBy = _user.UserId
                };
                await _bonuses.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<BonusDto>.SuccessResponse(MapBonus(row, employee), "Bonus saved.");
            }, ct);
        }
        catch (DbUpdateException)
        { return Error<BonusDto>("Bonus write conflicts with another update.", 409); }
    }

    public async Task<ApiResponse<LoanAdvanceDto>> CreateLoanAdvanceAsync(
        CreateLoanAdvanceRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<LoanAdvanceDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.IssueDate == default || request.PrincipalAmount <= 0m ||
            request.InstallmentAmount <= 0m || request.InstallmentAmount > request.PrincipalAmount ||
            request.PrincipalAmount > 999999999999m || request.Remarks?.Length > 500 ||
            Round(request.PrincipalAmount) != request.PrincipalAmount ||
            Round(request.InstallmentAmount) != request.InstallmentAmount)
            return Error<LoanAdvanceDto>("Invalid loan advance, amount precision or installment.");

        var tenant = _user.TenantId;
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                // Check the immutable request receipt BEFORE employment/period preflight.
                // Retries must return the original result even when the employee state later changes.
                var replay = await _loans.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, token);
                if (replay != null)
                {
                    var originalEmployee = await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == replay.EmployeeId, token);
                    if (originalEmployee == null || originalEmployee.PublicId != request.EmployeeReference ||
                        replay.IssueDate != request.IssueDate ||
                        replay.PrincipalAmount != request.PrincipalAmount ||
                        replay.InstallmentAmount != request.InstallmentAmount ||
                        replay.Remarks != Trim(request.Remarks))
                        return Error<LoanAdvanceDto>("Request key already belongs to a different loan.", 409);
                    return ApiResponse<LoanAdvanceDto>.SuccessResponse(MapLoan(replay, originalEmployee),
                        "Existing loan registration returned.");
                }

                var employee = await ActiveEmployeeAsync(request.EmployeeReference, token);
                if (employee == null) return Error<LoanAdvanceDto>("Active employee not found.", 404);
                if (request.IssueDate < employee.JoiningDate ||
                    employee.LeavingDate.HasValue && request.IssueDate > employee.LeavingDate.Value)
                    return Error<LoanAdvanceDto>("Loan date is outside the employee's employment period.", 409);
                if (await _runs.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Year == request.IssueDate.Year &&
                    x.Month == request.IssueDate.Month && x.State != PayrollRunState.Cancelled &&
                    !x.IsDeleted, token))
                    return Error<LoanAdvanceDto>("Loan issuance within an existing payroll period requires controlled amendment.", 409);

                var now = _clock.GetUtcNow().UtcDateTime;
                var loan = new LoanAdvance
                {
                    TenantId = tenant, ClientRequestId = request.ClientRequestId,
                    EmployeeId = employee.Id, IssueDate = request.IssueDate,
                    PrincipalAmount = request.PrincipalAmount, OutstandingAmount = request.PrincipalAmount,
                    InstallmentAmount = request.InstallmentAmount, Remarks = Trim(request.Remarks),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _loans.AddAsync(loan);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LoanAdvanceDto>.SuccessResponse(MapLoan(loan, employee),
                    "Loan registered; disbursement journal must be completed separately.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<LoanAdvanceDto>("Loan registration changed concurrently; reload the register.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Loan registration conflict tenant {TenantId}", tenant);
            return Error<LoanAdvanceDto>("Loan request already processed or conflicts with another registration. Reload the register.", 409);
        }
    }

    public async Task<ApiResponse<PagedResult<PayrollRunDto>>> GetPayrollRunsAsync(
        int? year, int? month, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PagedResult<PayrollRunDto>>();
        if (year is < 2000 or > 2200 || month is < 1 or > 12 || page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<PayrollRunDto>>("Invalid period or pagination.");
        var q = _runs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && !x.IsDeleted);
        if (year.HasValue) q = q.Where(x => x.Year == year.Value);
        if (month.HasValue) q = q.Where(x => x.Month == month.Value);
        var count = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<PayrollRunDto>>("Page is outside supported range.");
        var rows = await q.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .ThenByDescending(x => x.Id).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var totals = await _payrollEmployees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && ids.Contains(x.PayrollRunId))
            .GroupBy(x => x.PayrollRunId).Select(x => new
            {
                Id = x.Key, Count = x.Count(), Gross = x.Sum(y => y.GrossAmount),
                Deduction = x.Sum(y => y.DeductionAmount), Net = x.Sum(y => y.NetAmount)
            }).ToListAsync(ct);
        var byId = totals.ToDictionary(x => x.Id);
        return ApiResponse<PagedResult<PayrollRunDto>>.SuccessResponse(new PagedResult<PayrollRunDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x =>
            {
                var t = byId.GetValueOrDefault(x.Id);
                return MapRun(x, t?.Count ?? 0, t?.Gross ?? 0m, t?.Deduction ?? 0m, t?.Net ?? 0m);
            }).ToList()
        });
    }

    public async Task<ApiResponse<PagedResult<PayrollEmployeeDto>>> GetPayrollEmployeesAsync(
        Guid runReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PagedResult<PayrollEmployeeDto>>();
        if (runReference == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<PayrollEmployeeDto>>("Invalid payroll reference or pagination.");
        var run = await FindRunAsync(runReference, ct);
        if (run == null) return Error<PagedResult<PayrollEmployeeDto>>("Payroll run not found.", 404);
        var q = _payrollEmployees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.PayrollRunId == run.Id && !x.IsDeleted);
        var total = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<PayrollEmployeeDto>>("Requested page is too large.");
        var employees = await q.OrderBy(x => x.EmployeeCodeSnapshot).ThenBy(x => x.Id)
            .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        var models = await MapEmployeesAsync(employees, ct);
        return ApiResponse<PagedResult<PayrollEmployeeDto>>.SuccessResponse(new PagedResult<PayrollEmployeeDto>
        { Page = page, PageSize = pageSize, TotalCount = total, Items = models });
    }

    public async Task<ApiResponse<PagedResult<PayrollEmployeeDto>>> GetMyPayslipsAsync(
        int? year, int page, int pageSize, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.TenantId <= 0)
            return Denied<PagedResult<PayrollEmployeeDto>>();
        if (year is < 2000 or > 2200 || page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<PayrollEmployeeDto>>("Invalid year or pagination.");
        var linkedPersons = _identityUsers.GetQueryable().AsNoTracking().Where(x =>
            x.Id == _user.UserId && x.IsActive && !x.IsDeleted && x.PersonId.HasValue)
            .Select(x => x.PersonId!.Value);
        var employeeId = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId &&
            linkedPersons.Contains(x.PersonId) && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (employeeId == 0) return Error<PagedResult<PayrollEmployeeDto>>("Employee not linked to this user.", 404);
        var runIds = _runs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.State == PayrollRunState.Posted &&
            !x.IsDeleted && (!year.HasValue || x.Year == year.Value)).Select(x => x.Id);
        var q = _payrollEmployees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.EmployeeId == employeeId &&
            runIds.Contains(x.PayrollRunId) && !x.IsDeleted);
        var total = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue) return Error<PagedResult<PayrollEmployeeDto>>("Page exceeds result range.");
        var rows = await q.OrderByDescending(x => x.PayrollRunId).ThenByDescending(x => x.Id)
            .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<PayrollEmployeeDto>>.SuccessResponse(new PagedResult<PayrollEmployeeDto>
        { Page = page, PageSize = pageSize, TotalCount = total,
            Items = await MapEmployeesAsync(rows, ct) });
    }

    public async Task<ApiResponse<PayrollPaymentDto>> RecordPayrollPaymentAsync(
        RecordPayrollPaymentRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PayrollPaymentDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.PayrollEmployeeId <= 0 ||
            request.PaymentDate == default ||
            request.PaymentMethod is not (PaymentMethodType.Cash or PaymentMethodType.BankTransfer) ||
            request.Amount <= 0 || request.Amount > 999999999999m ||
            !TryVersion(request.PayrollEmployeeRowVersion, out var expected) ||
            request.BankAccountId is <= 0 || request.EmployeeBankAccountId is <= 0 ||
            request.ExternalReference?.Length > 150 ||
            (request.PaymentMethod == PaymentMethodType.Cash &&
                (request.BankAccountId.HasValue || request.EmployeeBankAccountId.HasValue)) ||
            (request.PaymentMethod == PaymentMethodType.BankTransfer &&
                (!request.BankAccountId.HasValue || !request.EmployeeBankAccountId.HasValue ||
                    string.IsNullOrWhiteSpace(request.ExternalReference))))
            return Error<PayrollPaymentDto>("Invalid payroll payment request or row version.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var previous = await _payments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (previous != null)
                {
                    if (previous.PayrollEmployeeId != request.PayrollEmployeeId ||
                        previous.Amount != Round(request.Amount) ||
                        previous.PaymentMethod != request.PaymentMethod ||
                        previous.PaymentDate != request.PaymentDate ||
                        previous.BankAccountId != request.BankAccountId ||
                        previous.EmployeeBankAccountId != request.EmployeeBankAccountId ||
                        previous.ExternalReference != Trim(request.ExternalReference))
                        return Error<PayrollPaymentDto>("Idempotency key reused for a different payment.", 409);
                    return ApiResponse<PayrollPaymentDto>.SuccessResponse(MapPayment(previous),
                        "Payment request already recorded.");
                }
                var employee = await _payrollEmployees.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.PayrollEmployeeId && !x.IsDeleted, token);
                if (employee == null) return Error<PayrollPaymentDto>("Payroll employee not found.", 404);
                if (!Matches(employee.RowVersion, expected))
                    return Error<PayrollPaymentDto>("Payroll employee changed. Reload and retry.", 409);
                var run = await _runs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == employee.PayrollRunId && !x.IsDeleted, token);
                if (run == null || run.State != PayrollRunState.Posted || run.JournalId == null)
                    return Error<PayrollPaymentDto>("Payroll must be journal-posted before payment.", 409);
                if (await _journals.GetQueryable().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.ReversalOfJournalId == run.JournalId &&
                    (x.State == JournalState.Posted || x.State == JournalState.Reversed ||
                     (!x.IsDeleted && x.State != JournalState.Rejected && x.State != JournalState.Cancelled)), token))
                    return Error<PayrollPaymentDto>("Payroll accrual has an active or posted reversal; salary payment is blocked.", 409);
                if (request.PaymentDate < run.PeriodEnd)
                    return Error<PayrollPaymentDto>("Salary settlement date must follow payroll period end.", 409);
                if (request.PaymentMethod == PaymentMethodType.BankTransfer)
                {
                    if (!await _bankAccounts.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == request.BankAccountId &&
                        x.IsActive && !x.IsDeleted && x.CurrencyCode == "BDT" &&
                        (!x.CampusId.HasValue || x.CampusId == run.CampusId), token))
                        return Error<PayrollPaymentDto>("Active BDT source bank account is unavailable for this campus.", 409);
                    if (!await _employeeBankAccounts.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == request.EmployeeBankAccountId &&
                        x.EmployeeId == employee.EmployeeId && x.IsActive && !x.IsDeleted &&
                        x.CurrencyCode == "BDT", token))
                        return Error<PayrollPaymentDto>("Active BDT employee bank destination is unavailable.", 409);
                }
                // A returned successful payment remains an immutable historical row.
                // It frees available salary only after a second administrator posts an
                // exact verified contra journal. Pending requests still reserve capacity.
                var paymentHistory = await _payments.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.PayrollEmployeeId == employee.Id &&
                        x.State != PaymentState.Failed && x.State != PaymentState.Cancelled)
                    .Take(501).ToListAsync(token);
                if (paymentHistory.Count > 500 || paymentHistory.Any(x => x.IsDeleted ||
                    x.State == PaymentState.Refunded || x.Amount <= 0m))
                    return Error<PayrollPaymentDto>("Historical payroll payments need reconciliation before a new request.", 409);
                var returned = new HashSet<long>();
                if (_uow is EduOSDbContext payrollDb)
                {
                    var paidIds = paymentHistory.Where(x => x.State == PaymentState.Successful)
                        .Select(x => x.Id).ToArray();
                    var returnedIds = (await payrollDb.Journals.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.TenantId == tenant && x.SourceType == "PayrollPaymentReturn" &&
                            x.SourceId.HasValue && paidIds.Contains(x.SourceId.Value) &&
                            x.State == JournalState.Posted)
                        .Select(x => x.SourceId!.Value).Distinct().Take(501).ToListAsync(token)).ToHashSet();
                    foreach (var prior in paymentHistory.Where(x => returnedIds.Contains(x.Id)))
                    {
                        if (!await PayrollPaymentPostingService.HasVerifiedReturnAsync(
                            payrollDb, tenant, prior, token))
                            return Error<PayrollPaymentDto>("A prior returned salary lacks verified contra GL evidence.", 409);
                        returned.Add(prior.Id);
                    }
                }
                var pending = paymentHistory.Where(x => !returned.Contains(x.Id))
                    .Sum(x => x.Amount);
                if (Round(request.Amount) > employee.NetAmount - pending)
                    return Error<PayrollPaymentDto>("Payment exceeds the outstanding net salary.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var row = new PayrollPayment
                {
                    TenantId = tenant, PublicId = Guid.NewGuid(),
                    ClientRequestId = request.ClientRequestId,
                    PayrollEmployeeId = employee.Id, PaymentDate = request.PaymentDate,
                    PaymentMethod = request.PaymentMethod, Amount = Round(request.Amount),
                    BankAccountId = request.BankAccountId,
                    EmployeeBankAccountId = request.EmployeeBankAccountId,
                    ExternalReference = Trim(request.ExternalReference),
                    State = PaymentState.AwaitingVerification, CreatedAt = now,
                    CreatedBy = _user.UserId
                };
                await _payments.AddAsync(row);
                // PayrollEmployee belongs to a posted immutable run. Recording a separate
                // payment request must never rewrite its salary snapshot or rowversion.
                await _uow.SaveChangesAsync(token);
                return ApiResponse<PayrollPaymentDto>.SuccessResponse(MapPayment(row),
                    "Payment request recorded pending verification and accounting reconciliation.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        { return Error<PayrollPaymentDto>("Payroll payment changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Payroll payment conflict tenant {TenantId}", _user.TenantId);
            return Error<PayrollPaymentDto>("Payroll payment conflicts with another request.", 409);
        }
    }

    private async Task<Employee?> ActiveEmployeeAsync(Guid reference, CancellationToken ct) =>
        await _employees.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference &&
            x.State == EmployeeState.Active && !x.IsDeleted, ct);

    private async Task<PayrollRun?> FindRunAsync(Guid reference, CancellationToken ct) =>
        await _runs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);

    private async Task<PayrollRunDto> MapRunAsync(PayrollRun row, CancellationToken ct)
    {
        var summary = await _payrollEmployees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == row.TenantId && x.PayrollRunId == row.Id && !x.IsDeleted)
            .GroupBy(x => x.PayrollRunId).Select(x => new
            {
                Count = x.Count(), Gross = x.Sum(z => z.GrossAmount),
                Deduction = x.Sum(z => z.DeductionAmount), Net = x.Sum(z => z.NetAmount)
            }).FirstOrDefaultAsync(ct);
        return MapRun(row, summary?.Count ?? 0, summary?.Gross ?? 0m,
            summary?.Deduction ?? 0m, summary?.Net ?? 0m);
    }

    private async Task<List<PayrollEmployeeDto>> MapEmployeesAsync(
        IReadOnlyList<PayrollEmployee> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var tenant = _user.TenantId;
        var employeeIds = rows.Select(x => x.EmployeeId).Distinct().ToArray();
        var details = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && employeeIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId }).ToDictionaryAsync(x => x.Id, ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var lines = await _payrollLines.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && ids.Contains(x.PayrollEmployeeId) && !x.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && lines.Select(y => y.SalaryComponentId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var runIds = rows.Select(x => x.PayrollRunId).Distinct().ToArray();
        var runs = await _runs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && runIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.PublicId, x.RunNumber, x.PeriodStart,
                x.PeriodEnd, x.CampusId, x.State })
            .ToDictionaryAsync(x => x.Id, ct);
        var byEmployee = lines.ToLookup(x => x.PayrollEmployeeId);
        return rows.Select(row => new PayrollEmployeeDto
        {
            Id = row.Id, RowVersion = Version(row.RowVersion),
            EmployeeReference = details.GetValueOrDefault(row.EmployeeId)?.PublicId ?? Guid.Empty,
            RunReference = runs.GetValueOrDefault(row.PayrollRunId)?.PublicId ?? Guid.Empty,
            RunNumber = runs.GetValueOrDefault(row.PayrollRunId)?.RunNumber ?? string.Empty,
            PeriodStart = runs.GetValueOrDefault(row.PayrollRunId)?.PeriodStart ?? default,
            PeriodEnd = runs.GetValueOrDefault(row.PayrollRunId)?.PeriodEnd ?? default,
            CampusId = runs.GetValueOrDefault(row.PayrollRunId)?.CampusId,
            RunState = runs.GetValueOrDefault(row.PayrollRunId)?.State ?? PayrollRunState.Draft,
            EmployeeCode = row.EmployeeCodeSnapshot ?? string.Empty,
            EmployeeName = row.EmployeeNameSnapshot ?? string.Empty,
            GrossAmount = row.GrossAmount, DeductionAmount = row.DeductionAmount,
            NetAmount = row.NetAmount,
            Lines = byEmployee[row.Id].Select(line =>
            {
                components.TryGetValue(line.SalaryComponentId, out var component);
                return new PayrollLineDto
                {
                    Id = line.Id, SalaryComponentId = line.SalaryComponentId,
                    SalaryComponentName = component?.Name ?? string.Empty,
                    ComponentType = component?.Type ?? SalaryComponentType.Earning,
                    Amount = line.Amount, Description = line.Description
                };
            }).ToList()
        }).ToList();
    }

    private static SalaryComponentDto Map(SalaryComponent x) => new()
    {
        Id = x.Id, Code = x.Code, Name = x.Name, Type = x.Type,
        IsTaxable = x.IsTaxable, IsActive = x.IsActive, RowVersion = Version(x.RowVersion)
    };
    private static SalaryStructureDto MapStructure(SalaryStructure x, Employee employee,
        IReadOnlyList<SalaryStructureLine> lines, IReadOnlyList<SalaryComponent> components) => new()
    {
        Id = x.Id, EmployeeReference = employee.PublicId, EmployeeCode = employee.EmployeeCode,
        EmployeeName = employee.FullName, EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo,
        IsCurrent = x.IsCurrent,
        Lines = lines.Select(line =>
        {
            var component = components.First(y => y.Id == line.SalaryComponentId);
            return new SalaryStructureLineDto
            {
                Id = line.Id, SalaryComponentId = line.SalaryComponentId,
                SalaryComponentName = component.Name, Type = component.Type, Amount = line.Amount,
                CalculationMethodCode = line.CalculationMethodCode == "Auto" ? "Auto" : "Manual",
                Percentage = line.Percentage, BasedOnSalaryComponentId = line.BasedOnSalaryComponentId
            };
        }).ToList(),
        GrossMonthlyAmount = lines.Where(line => components.Any(x =>
            x.Id == line.SalaryComponentId && x.Type == SalaryComponentType.Earning))
            .Sum(line => line.Amount), RowVersion = Version(x.RowVersion)
    };
    private static PayrollRunDto MapRun(PayrollRun x, int count, decimal gross, decimal deduction, decimal net) => new()
    {
        Id = x.Id, RunNumber = x.RunNumber, CampusId = x.CampusId,
        PeriodStart = x.PeriodStart, PeriodEnd = x.PeriodEnd,
        ProrationModeCode = x.ProrationModeCode, Reference = x.PublicId,
        Year = x.Year, Month = x.Month, State = x.State, CalculatedAt = x.CalculatedAt,
        ApprovedAt = x.ApprovedAt, ApprovedByUserId = x.ApprovedByUserId,
        PostedAt = x.PostedAt, JournalId = x.JournalId, EmployeeCount = count,
        GrossAmount = gross, DeductionAmount = deduction, NetAmount = net,
        RowVersion = Version(x.RowVersion)
    };
    private static BonusDto MapBonus(Bonus x, Employee employee) => new()
    {
        Id = x.Id, EmployeeReference = employee.PublicId, EmployeeName = employee.FullName,
        BonusDate = x.BonusDate, Amount = x.Amount, Reason = x.Reason,
        RowVersion = Version(x.RowVersion)
    };
    private static LoanAdvanceDto MapLoan(LoanAdvance x, Employee employee) => new()
    {
        Id = x.Id, EmployeeReference = employee.PublicId, EmployeeName = employee.FullName,
        IssueDate = x.IssueDate, PrincipalAmount = x.PrincipalAmount,
        OutstandingAmount = x.OutstandingAmount, InstallmentAmount = x.InstallmentAmount,
        Remarks = x.Remarks, IsClosed = x.IsClosed, RowVersion = Version(x.RowVersion)
    };
    private static PayrollPaymentDto MapPayment(PayrollPayment x) => new()
    {
        Id = x.Id, Reference = x.PublicId, PayrollEmployeeId = x.PayrollEmployeeId,
        PaymentDate = x.PaymentDate, PaymentMethod = x.PaymentMethod, Amount = x.Amount,
        CurrencyCode = x.CurrencyCode, State = x.State, BankAccountId = x.BankAccountId,
        EmployeeBankAccountId = x.EmployeeBankAccountId, ExternalReference = x.ExternalReference,
        JournalId = x.JournalId
    };

    private Task<ApiResponse<T>> ExecuteWriteAsync<T>(
        string operation, Func<CancellationToken, Task<ApiResponse<T>>> action, CancellationToken ct) =>
        ExecuteWriteCoreAsync(operation, action, ct);

    private async Task<ApiResponse<T>> ExecuteWriteCoreAsync<T>(
        string operation, Func<CancellationToken, Task<ApiResponse<T>>> action, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(action, ct); }
        catch (DbUpdateConcurrencyException)
        { return Error<T>("Payroll changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Payroll write conflict on {Operation}", operation);
            return Error<T>("Payroll conflicts with an existing transaction.", 409);
        }
    }

    // This administration service processes institution-wide financial records.
    // Campus delegation requires its own grant-checked salary and payroll flow.
    private bool Manager() => _user.IsAuthenticated && _user.TenantId > 0 && _user.IsTenantAdmin;
    private bool Approver() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("Accountant"));
    private static bool ValidNameCode(string? name, string? code, int limit) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= limit &&
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 50;
    private static string SalaryStructureFingerprint(SaveSalaryStructureRequestDto request)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var canonical = request.EmployeeReference.ToString("N") + "|" +
            request.EffectiveFrom.ToString("yyyy-MM-dd", culture) + "|" +
            (request.EffectiveTo?.ToString("yyyy-MM-dd", culture) ?? "") + "|" +
            string.Join("|", request.Lines.OrderBy(x => x.SalaryComponentId).Select(x =>
                x.SalaryComponentId.ToString(culture) + ":" + x.Amount.ToString("0.00", culture) +
                (x.CalculationMethodCode == "Auto" ? ":Auto:" +
                    x.Percentage?.ToString("0.0000", culture) + ":" +
                    x.BasedOnSalaryComponentId?.ToString(culture) : "")));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static bool TryVersion(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { bytes = Convert.FromBase64String(text); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool Matches(byte[] actual, string? encoded) =>
        TryVersion(encoded, out var expected) && Matches(actual, expected);
    private static bool Matches(byte[] actual, byte[] expected) =>
        actual != null && actual.Length == expected.Length && actual.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(actual, expected);
    private static ApiResponse<T> Denied<T>() => Error<T>("Payroll access denied.", 403);
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
