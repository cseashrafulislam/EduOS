using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.HR;

public sealed class HrPayrollService : IPayrollAdministrationService
{
    private readonly IGenericRepository<Employee> _employees;
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
    private readonly IGenericRepository<EmployeeBankAccount> _employeeBankAccounts;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<HrPayrollService> _logger;

    public HrPayrollService(IGenericRepository<Employee> employees,
        IGenericRepository<SalaryComponent> components, IGenericRepository<SalaryStructure> structures,
        IGenericRepository<SalaryStructureLine> structureLines, IGenericRepository<PayrollRun> runs,
        IGenericRepository<PayrollEmployee> payrollEmployees, IGenericRepository<PayrollLine> payrollLines,
        IGenericRepository<PayrollPayment> payments, IGenericRepository<Bonus> bonuses,
        IGenericRepository<LoanAdvance> loans, IGenericRepository<LoanAdvanceRecovery> recoveries,
        IGenericRepository<BankAccount> bankAccounts,
        IGenericRepository<EmployeeBankAccount> employeeBankAccounts,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock, ILogger<HrPayrollService> logger)
    {
        _employees = employees; _components = components; _structures = structures;
        _structureLines = structureLines; _runs = runs; _payrollEmployees = payrollEmployees;
        _payrollLines = payrollLines; _payments = payments; _bonuses = bonuses; _loans = loans;
        _recoveries = recoveries; _bankAccounts = bankAccounts; _employeeBankAccounts = employeeBankAccounts;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
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
                x.Amount is < 0 or > 999999999999m) ||
            request.Lines.Select(x => x.SalaryComponentId).Distinct().Count() != request.Lines.Count)
            return Error<SalaryStructureDto>("Invalid employee, structure dates or salary component lines.");
        var employee = await ActiveEmployeeAsync(request.EmployeeReference, ct);
        if (employee == null) return Error<SalaryStructureDto>("Active employee not found.", 404);
        var components = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && request.Lines.Select(y => y.SalaryComponentId).Contains(x.Id) &&
            x.IsActive && !x.IsDeleted).ToListAsync(ct);
        if (components.Count != request.Lines.Count)
            return Error<SalaryStructureDto>("One or more salary components are inactive.", 409);
        if (!components.Any(x => x.Type == SalaryComponentType.Earning) ||
            request.Lines.Where(x => components.Any(y => y.Id == x.SalaryComponentId &&
                y.Type == SalaryComponentType.Earning)).Sum(x => x.Amount) <= 0m)
            return Error<SalaryStructureDto>("Salary structure requires positive earnings.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
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
                            y.CalculationMethodCode == "Fixed")))
                        return ApiResponse<SalaryStructureDto>.SuccessResponse(
                            MapStructure(current, employee, currentLines, components), "Salary structure already exists.");
                    if (request.EffectiveFrom <= current.EffectiveFrom)
                        return Error<SalaryStructureDto>("New structure must begin after current effective date.", 409);
                    if (request.EffectiveTo.HasValue)
                        return Error<SalaryStructureDto>("A replacement salary structure must remain open-ended.", 409);
                    current.EffectiveTo = request.EffectiveFrom.AddDays(-1);
                    current.IsCurrent = false;
                    current.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                    current.UpdatedBy = _user.UserId;
                    _structures.Update(current);
                }
                if (await _structures.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.EmployeeId == employee.Id &&
                    x.EffectiveFrom == request.EffectiveFrom && !x.IsDeleted, token))
                    return Error<SalaryStructureDto>("Salary structure already exists for this effective date.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var row = new SalaryStructure
                {
                    TenantId = _user.TenantId, EmployeeId = employee.Id,
                    EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                    IsCurrent = !request.EffectiveTo.HasValue,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _structures.AddAsync(row);
                await _uow.SaveChangesAsync(token);
                var lines = request.Lines.Select(x => new SalaryStructureLine
                {
                    TenantId = _user.TenantId, SalaryStructureId = row.Id,
                    SalaryComponentId = x.SalaryComponentId, CalculationMethodCode = "Fixed",
                    Amount = Math.Round(x.Amount, 2, MidpointRounding.AwayFromZero),
                    CreatedAt = now, CreatedBy = _user.UserId
                }).ToList();
                await _structureLines.AddRangeAsync(lines);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<SalaryStructureDto>.SuccessResponse(
                    MapStructure(row, employee, lines, components), "Salary structure saved.");
            }, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Salary structure conflict tenant {TenantId}", _user.TenantId);
            return Error<SalaryStructureDto>("Salary structure conflicts with an existing update.", 409);
        }
    }

    public async Task<ApiResponse<PayrollRunDto>> CreatePayrollRunAsync(CreatePayrollRunRequestDto request,
        CancellationToken ct = default)
    {
        if (!Manager()) return Denied<PayrollRunDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.Year is < 2000 or > 2200 ||
            request.Month is < 1 or > 12)
            return Error<PayrollRunDto>("Invalid payroll period or idempotency key.");
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
                    if (existing.Year != request.Year || existing.Month != request.Month)
                        return Error<PayrollRunDto>("Idempotency key was used for another payroll period.", 409);
                    return ApiResponse<PayrollRunDto>.SuccessResponse(await MapRunAsync(existing, token),
                        "Payroll request already processed.");
                }
                if (await _runs.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Year == request.Year && x.Month == request.Month &&
                    x.State != PayrollRunState.Cancelled && !x.IsDeleted, token))
                    return Error<PayrollRunDto>("A payroll run already exists for the selected period.", 409);
                var employees = await _employees.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.State == EmployeeState.Active && !x.IsDeleted &&
                    x.JoiningDate <= firstDay && (!x.LeavingDate.HasValue || x.LeavingDate >= lastDay))
                    .OrderBy(x => x.Id).Take(5001).ToListAsync(token);
                if (employees.Count > 5000)
                    return Error<PayrollRunDto>("Period exceeds the synchronous payroll size limit.", 409);
                if (employees.Count == 0)
                    return Error<PayrollRunDto>("No employees are eligible for this payroll period.", 409);
                var ids = employees.Select(x => x.Id).ToArray();
                var structures = await _structures.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && ids.Contains(x.EmployeeId) && x.EffectiveFrom <= firstDay &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo >= lastDay) && !x.IsDeleted).ToListAsync(token);
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
                    x.CalculationMethodCode != "Fixed" || x.Percentage.HasValue ||
                    x.BasedOnSalaryComponentId.HasValue || x.Amount < 0))
                    return Error<PayrollRunDto>("Salary calculation method is unsupported or component is missing.", 409);
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
                    .ToListAsync(token);
                var loanComponent = await _components.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Code == "LOAN_RECOVERY" &&
                    x.Type == SalaryComponentType.Deduction && x.IsActive && !x.IsDeleted, token);
                if (loans.Count > 0 && loanComponent == null)
                    return Error<PayrollRunDto>("Active LOAN_RECOVERY deduction component must be configured.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var run = new PayrollRun
                {
                    TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = request.ClientRequestId,
                    RunNumber = "PR-" + request.Year.ToString("D4") + request.Month.ToString("D2") +
                        "-" + request.ClientRequestId.ToString("N").Substring(0, 12),
                    Year = request.Year, Month = request.Month, PeriodStart = firstDay,
                    PeriodEnd = lastDay, State = PayrollRunState.Calculated, CalculatedAt = now,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _runs.AddAsync(run);
                await _uow.SaveChangesAsync(token);
                var structureByEmployee = structures.ToDictionary(x => x.EmployeeId);
                var structureLineLookup = salaryLines.ToLookup(x => x.SalaryStructureId);
                foreach (var employee in employees)
                {
                    var lines = structureLineLookup[structureByEmployee[employee.Id].Id].ToList();
                    decimal earned = lines.Where(x => comps[x.SalaryComponentId].Type == SalaryComponentType.Earning)
                        .Sum(x => x.Amount);
                    decimal withheld = lines.Where(x => comps[x.SalaryComponentId].Type == SalaryComponentType.Deduction)
                        .Sum(x => x.Amount);
                    var bonus = bonusByEmployee.GetValueOrDefault(employee.Id);
                    var loansForEmployee = loans.Where(x => x.EmployeeId == employee.Id).ToList();
                    var recover = loansForEmployee.Sum(x => Math.Min(x.InstallmentAmount, x.OutstandingAmount));
                    var gross = Round(earned + bonus);
                    var deductions = Round(withheld + recover);
                    if (gross <= 0m || deductions > gross)
                        return Error<PayrollRunDto>("Employee has invalid net salary or deductions.", 409);
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
                    var snapshots = lines.Select(x => new PayrollLine
                    {
                        TenantId = tenant, PayrollEmployeeId = row.Id,
                        SalaryComponentId = x.SalaryComponentId, Amount = Round(x.Amount),
                        Description = x.CalculationMethodCode,
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
            }, ct);
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
        if (!Approver()) return Denied<PayrollRunDto>();
        if (runReference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var version))
            return Error<PayrollRunDto>("Payroll reference and version required.");
        var row = await FindRunAsync(runReference, ct);
        if (row == null) return Error<PayrollRunDto>("Payroll run not found.", 404);
        if (!Matches(row.RowVersion, version)) return Error<PayrollRunDto>("Payroll changed; reload.", 409);
        if (row.State != PayrollRunState.Approved)
            return Error<PayrollRunDto>("Payroll must be approved before accounting posting.", 409);
        // There is no authoritative salary-component-to-COA mapping in the canonical model.
        // Do not mark a financially unbalanced or unposted run as Posted.
        return Error<PayrollRunDto>(
            "Payroll posting requires configured salary-component ledger mappings and a balanced accounting journal.", 409);
    }

    public async Task<ApiResponse<BonusDto>> SaveBonusAsync(long? bonusId, SaveBonusRequestDto request, CancellationToken ct = default)
    {
        if (!Manager()) return Denied<BonusDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.EmployeeReference == Guid.Empty ||
            request.BonusDate == default || request.Amount <= 0 || request.Amount > 999999999999m ||
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
            request.IssueDate == default || request.PrincipalAmount <= 0 ||
            request.InstallmentAmount <= 0 || request.InstallmentAmount > request.PrincipalAmount ||
            request.PrincipalAmount > 999999999999m || request.Remarks?.Length > 500)
            return Error<LoanAdvanceDto>("Invalid loan advance or installment.");
        var employee = await ActiveEmployeeAsync(request.EmployeeReference, ct);
        if (employee == null) return Error<LoanAdvanceDto>("Employee not found.", 404);
        if (await _runs.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.Year == request.IssueDate.Year &&
            x.Month == request.IssueDate.Month && x.State != PayrollRunState.Cancelled &&
            !x.IsDeleted, ct))
            return Error<LoanAdvanceDto>("Loan issuance within an existing payroll period requires controlled amendment.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                var loan = new LoanAdvance
                {
                    TenantId = _user.TenantId, EmployeeId = employee.Id,
                    IssueDate = request.IssueDate, PrincipalAmount = Round(request.PrincipalAmount),
                    OutstandingAmount = Round(request.PrincipalAmount),
                    InstallmentAmount = Round(request.InstallmentAmount),
                    Remarks = Trim(request.Remarks), CreatedAt = now, CreatedBy = _user.UserId
                };
                await _loans.AddAsync(loan);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<LoanAdvanceDto>.SuccessResponse(MapLoan(loan, employee),
                    "Loan registered; disbursement journal must be completed separately.");
            }, ct);
        }
        catch (DbUpdateException) { return Error<LoanAdvanceDto>("Loan advance conflicts with existing data.", 409); }
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
        var employeeId = await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId && !x.IsDeleted)
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
            request.PaymentDate == default || !Enum.IsDefined(request.PaymentMethod) ||
            request.Amount <= 0 || request.Amount > 999999999999m ||
            !TryVersion(request.PayrollEmployeeRowVersion, out var expected) ||
            request.BankAccountId is <= 0 || request.EmployeeBankAccountId is <= 0 ||
            request.ExternalReference?.Length > 150)
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
                if (request.PaymentDate < run.PeriodStart)
                    return Error<PayrollPaymentDto>("Payment date predates the payroll period.", 409);
                if (request.BankAccountId.HasValue && !await _bankAccounts.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.Id == request.BankAccountId.Value &&
                        x.IsActive && !x.IsDeleted, token))
                    return Error<PayrollPaymentDto>("Bank account is not available.", 404);
                if (request.EmployeeBankAccountId.HasValue && !await _employeeBankAccounts.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.Id == request.EmployeeBankAccountId.Value &&
                        x.EmployeeId == employee.EmployeeId && x.IsActive && !x.IsDeleted, token))
                    return Error<PayrollPaymentDto>("Employee bank account does not belong to this employee.", 404);
                var pending = await _payments.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.PayrollEmployeeId == employee.Id &&
                    !x.IsDeleted && x.State != PaymentState.Failed &&
                    x.State != PaymentState.Cancelled && x.State != PaymentState.Refunded)
                    .SumAsync(x => (decimal?)x.Amount, token) ?? 0m;
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
                employee.UpdatedAt = now; employee.UpdatedBy = _user.UserId;
                _payrollEmployees.Update(employee);
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
        var byEmployee = lines.ToLookup(x => x.PayrollEmployeeId);
        return rows.Select(row => new PayrollEmployeeDto
        {
            Id = row.Id, EmployeeReference = details.GetValueOrDefault(row.EmployeeId)?.PublicId ?? Guid.Empty,
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
                SalaryComponentName = component.Name, Type = component.Type, Amount = line.Amount
            };
        }).ToList(),
        GrossMonthlyAmount = lines.Where(line => components.Any(x =>
            x.Id == line.SalaryComponentId && x.Type == SalaryComponentType.Earning))
            .Sum(line => line.Amount), RowVersion = Version(x.RowVersion)
    };
    private static PayrollRunDto MapRun(PayrollRun x, int count, decimal gross, decimal deduction, decimal net) => new()
    {
        Id = x.Id, RunNumber = x.RunNumber, CampusId = x.CampusId,
        PeriodStart = x.PeriodStart, PeriodEnd = x.PeriodEnd, Reference = x.PublicId,
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

    private bool Manager() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") ||
         _user.IsInRole("HR") || _user.IsInRole("Accountant"));
    private bool Approver() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("Accountant"));
    private static bool ValidNameCode(string? name, string? code, int limit) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= limit &&
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 50;
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
