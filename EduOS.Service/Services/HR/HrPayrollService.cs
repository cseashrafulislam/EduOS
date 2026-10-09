using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.HR;

public sealed class HrPayrollService : IHrPayrollService
{
    private const string BasicCode = "BASIC";
    private const string HouseRentCode = "HOUSE_RENT";
    private const string MedicalCode = "MEDICAL";
    private const string TransportCode = "TRANSPORT";
    private const string OtherCode = "OTHER";
    private const string BonusCode = "BONUS";
    private const string AttendanceDeductionCode = "ATTENDANCE_DEDUCTION";
    private const string LoanRecoveryCode = "LOAN_RECOVERY";

    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<EmployeeAttendance> _attendance;
    private readonly IGenericRepository<SalaryStructure> _salaryStructures;
    private readonly IGenericRepository<SalaryStructureLine> _salaryLines;
    private readonly IGenericRepository<SalaryComponent> _salaryComponents;
    private readonly IGenericRepository<PayrollRun> _payrollRuns;
    private readonly IGenericRepository<PayrollEmployee> _payrollEmployees;
    private readonly IGenericRepository<PayrollLine> _payrollLines;
    private readonly IGenericRepository<PayrollPayment> _payrollPayments;
    private readonly IGenericRepository<Bonus> _bonuses;
    private readonly IGenericRepository<LoanAdvance> _loans;
    private readonly IGenericRepository<LoanAdvanceRecovery> _loanRecoveries;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<HrPayrollService> _log;

    public HrPayrollService(
        IGenericRepository<Employee> employees,
        IGenericRepository<EmployeeAttendance> attendance,
        IGenericRepository<SalaryStructure> salaryStructures,
        IGenericRepository<SalaryStructureLine> salaryLines,
        IGenericRepository<SalaryComponent> salaryComponents,
        IGenericRepository<PayrollRun> payrollRuns,
        IGenericRepository<PayrollEmployee> payrollEmployees,
        IGenericRepository<PayrollLine> payrollLines,
        IGenericRepository<PayrollPayment> payrollPayments,
        IGenericRepository<Bonus> bonuses,
        IGenericRepository<LoanAdvance> loans,
        IGenericRepository<LoanAdvanceRecovery> loanRecoveries,
        IUnitOfWork uow,
        ICurrentUserService user,
        TimeProvider clock,
        ILogger<HrPayrollService> log)
    {
        _employees = employees;
        _attendance = attendance;
        _salaryStructures = salaryStructures;
        _salaryLines = salaryLines;
        _salaryComponents = salaryComponents;
        _payrollRuns = payrollRuns;
        _payrollEmployees = payrollEmployees;
        _payrollLines = payrollLines;
        _payrollPayments = payrollPayments;
        _bonuses = bonuses;
        _loans = loans;
        _loanRecoveries = loanRecoveries;
        _uow = uow;
        _user = user;
        _clock = clock;
        _log = log;
    }

    public async Task<ApiResponse<bool>> SaveSalaryStructureAsync(SaveSalaryStructureDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return ApiResponse<bool>.ErrorResponse("Payroll access is required.", 403);
        if (request.EmployeeReference == Guid.Empty) return ApiResponse<bool>.ErrorResponse("Employee reference is required.");
        var tenantId = _user.TenantId;
        var employee = await _employees.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == request.EmployeeReference && x.State == EmployeeState.Active, ct);
        if (employee == null) return ApiResponse<bool>.ErrorResponse("Employee not found.", 404);

        var effectiveFrom = DateOnly.FromDateTime(request.EffectiveFrom.Date);
        var current = await _salaryStructures.GetQueryable()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.IsCurrent)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (current != null && effectiveFrom <= current.EffectiveFrom)
            return ApiResponse<bool>.ErrorResponse("The new salary structure must start after the current structure.", 409);

        try
        {
            await _uow.BeginTransactionAsync();
            var components = await EnsureCompatibilityComponentsAsync(ct);
            var now = _clock.GetUtcNow().UtcDateTime;

            if (current != null)
            {
                current.IsCurrent = false;
                current.EffectiveTo = effectiveFrom.AddDays(-1);
                current.UpdatedAt = now;
                current.UpdatedBy = _user.UserId;
                await _uow.SaveChangesAsync(ct);
            }

            var structure = new SalaryStructure
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                EffectiveFrom = effectiveFrom,
                IsCurrent = true,
                CreatedAt = now,
                CreatedBy = _user.UserId
            };
            await _salaryStructures.AddAsync(structure);
            await _uow.SaveChangesAsync(ct);

            var amounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [BasicCode] = request.BasicSalary,
                [HouseRentCode] = request.HouseRent,
                [MedicalCode] = request.Medical,
                [TransportCode] = request.Transport,
                [OtherCode] = request.Others
            };
            var lines = amounts.Select(x => new SalaryStructureLine
            {
                TenantId = tenantId,
                SalaryStructureId = structure.Id,
                SalaryComponentId = components[x.Key].Id,
                CalculationMethodCode = "Fixed",
                Amount = x.Value,
                CreatedAt = now,
                CreatedBy = _user.UserId
            }).ToList();
            await _salaryLines.AddRangeAsync(lines);
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Salary structure saved.");
        }
        catch (DbUpdateException ex)
        {
            await SafeRollback();
            _log.LogWarning(ex, "Salary structure conflict for tenant {TenantId} employee {EmployeeId}", tenantId, employee.Id);
            return ApiResponse<bool>.ErrorResponse("Salary structure conflicts with an existing record.", 409);
        }
        catch (Exception ex)
        {
            await SafeRollback();
            _log.LogError(ex, "Salary structure save failed for tenant {TenantId} employee {EmployeeId}", tenantId, employee.Id);
            return ApiResponse<bool>.ErrorResponse("Salary structure could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<int>> SaveAttendanceAsync(SaveEmployeeAttendanceDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return ApiResponse<int>.ErrorResponse("HR attendance access is required.", 403);
        if (request.Items.Count == 0) return ApiResponse<int>.ErrorResponse("At least one attendance row is required.");
        if (request.Items.GroupBy(x => x.EmployeeReference).Any(g => g.Key == Guid.Empty || g.Count() > 1))
            return ApiResponse<int>.ErrorResponse("Duplicate or invalid employee references were supplied.");

        var tenantId = _user.TenantId;
        var references = request.Items.Select(x => x.EmployeeReference).Distinct().ToArray();
        var employees = await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && references.Contains(x.PublicId) && x.State == EmployeeState.Active)
            .Take(5000).ToListAsync(ct);
        if (employees.Count != references.Length) return ApiResponse<int>.ErrorResponse("One or more employees are unavailable.", 409);

        var employeeIds = employees.Select(x => x.Id).ToArray();
        var attendanceDate = DateOnly.FromDateTime(request.Date.Date);
        var existingRows = await _attendance.GetQueryable()
            .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.AttendanceDate == attendanceDate)
            .ToListAsync(ct);
        var byEmployee = existingRows.ToDictionary(x => x.EmployeeId);
        var employeeByReference = employees.ToDictionary(x => x.PublicId);
        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var item in request.Items)
        {
            var employee = employeeByReference[item.EmployeeReference];
            if (!TryAttendanceState(item.Status, out var state))
                return ApiResponse<int>.ErrorResponse($"Unsupported attendance status '{item.Status}'.");

            if (!byEmployee.TryGetValue(employee.Id, out var row))
            {
                row = new EmployeeAttendance
                {
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    AttendanceDate = attendanceDate,
                    SourceCode = "Manual",
                    RecordedAt = now,
                    RecordedByUserId = _user.UserId,
                    CreatedAt = now,
                    CreatedBy = _user.UserId
                };
                await _attendance.AddAsync(row);
                byEmployee[employee.Id] = row;
            }

            row.State = state;
            row.InTime = item.InTime.HasValue ? TimeOnly.FromTimeSpan(item.InTime.Value) : null;
            row.OutTime = item.OutTime.HasValue ? TimeOnly.FromTimeSpan(item.OutTime.Value) : null;
            row.OvertimeHours = item.OvertimeHours;
            row.Remarks = Trim(item.Remarks);
            row.RecordedAt = now;
            row.RecordedByUserId = _user.UserId;
            row.UpdatedAt = now;
            row.UpdatedBy = _user.UserId;
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<int>.SuccessResponse(request.Items.Count, "Employee attendance saved.");
    }

    public async Task<ApiResponse<PayrollBatchDto>> GeneratePayrollAsync(GeneratePayrollDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return ApiResponse<PayrollBatchDto>.ErrorResponse("Payroll access is required.", 403);
        if (request.ClientRequestId == Guid.Empty) return ApiResponse<PayrollBatchDto>.ErrorResponse("Client request reference is required.");
        if (request.Month is < 1 or > 12 || request.Year is < 2000 or > 2200) return ApiResponse<PayrollBatchDto>.ErrorResponse("Payroll period is invalid.");

        var tenantId = _user.TenantId;
        var prior = await _payrollRuns.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, ct);
        if (prior != null)
        {
            if (prior.Month != request.Month || prior.Year != request.Year)
                return ApiResponse<PayrollBatchDto>.ErrorResponse("Client request reference was already used for another payroll period.", 409);
            return ApiResponse<PayrollBatchDto>.SuccessResponse(await BuildBatchAsync(prior, true, ct), "Payroll request was already processed.");
        }

        var periodRun = await _payrollRuns.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Year == request.Year && x.Month == request.Month && x.State != PayrollRunState.Cancelled, ct);
        if (periodRun != null)
            return ApiResponse<PayrollBatchDto>.ErrorResponse("A payroll run already exists for this period.", 409);

        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var employees = await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.State == EmployeeState.Active && x.JoiningDate <= periodEnd && (x.LeavingDate == null || x.LeavingDate >= periodStart))
            .OrderBy(x => x.EmployeeCode).Take(5000).ToListAsync(ct);
        if (employees.Count == 0)
            return ApiResponse<PayrollBatchDto>.SuccessResponse(new PayrollBatchDto { ClientRequestId = request.ClientRequestId }, "No active employees were eligible for this payroll period.");

        try
        {
            await _uow.BeginTransactionAsync();
            var components = await EnsureCompatibilityComponentsAsync(ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            var run = new PayrollRun
            {
                TenantId = tenantId,
                PublicId = Guid.NewGuid(),
                ClientRequestId = request.ClientRequestId,
                RunNumber = $"PR-{request.Year:D4}{request.Month:D2}-{request.ClientRequestId:N}"[..25],
                Year = request.Year,
                Month = request.Month,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                State = PayrollRunState.Draft,
                CreatedAt = now,
                CreatedBy = _user.UserId
            };
            await _payrollRuns.AddAsync(run);
            await _uow.SaveChangesAsync(ct);

            var employeeIds = employees.Select(x => x.Id).ToArray();
            var structureRows = await _salaryStructures.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.EffectiveFrom <= periodEnd && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
                .ToListAsync(ct);
            var structures = structureRows.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.First());
            var structureIds = structures.Values.Select(x => x.Id).ToArray();
            var salaryLines = structureIds.Length == 0
                ? new List<SalaryStructureLine>()
                : await _salaryLines.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && structureIds.Contains(x.SalaryStructureId)).ToListAsync(ct);
            var componentIds = salaryLines.Select(x => x.SalaryComponentId).Concat(salaryLines.Where(x => x.BasedOnSalaryComponentId.HasValue).Select(x => x.BasedOnSalaryComponentId!.Value)).Distinct().ToArray();
            var salaryComponentRows = componentIds.Length == 0
                ? new List<SalaryComponent>()
                : await _salaryComponents.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && componentIds.Contains(x.Id)).ToListAsync(ct);
            var componentById = salaryComponentRows.Concat(components.Values).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

            var absences = await _attendance.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.AttendanceDate >= periodStart && x.AttendanceDate <= periodEnd && x.State == AttendanceState.Absent)
                .GroupBy(x => x.EmployeeId).Select(g => new { EmployeeId = g.Key, Count = g.Count() }).ToListAsync(ct);
            var absentByEmployee = absences.ToDictionary(x => x.EmployeeId, x => x.Count);
            var bonusRows = await _bonuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && x.BonusDate >= periodStart && x.BonusDate <= periodEnd)
                .GroupBy(x => x.EmployeeId).Select(g => new { EmployeeId = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
            var bonusByEmployee = bonusRows.ToDictionary(x => x.EmployeeId, x => x.Amount);
            var loanRows = await _loans.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId) && !x.IsClosed && x.IssueDate <= periodEnd && x.OutstandingAmount > 0)
                .Select(x => new { x.EmployeeId, x.InstallmentAmount, x.OutstandingAmount }).ToListAsync(ct);
            var loanByEmployee = loanRows.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(x => Math.Min(x.InstallmentAmount, x.OutstandingAmount)));

            var payrollEmployees = new List<PayrollEmployee>();
            var calculation = new Dictionary<long, PayrollCalculation>();
            foreach (var employee in employees)
            {
                if (!structures.TryGetValue(employee.Id, out var structure)) continue;
                var structureLines = salaryLines.Where(x => x.SalaryStructureId == structure.Id).ToList();
                var calculatedLines = CalculateStructureLines(structureLines, componentById);
                var baseGross = calculatedLines.Where(x => x.Component.Type == SalaryComponentType.Earning).Sum(x => x.Amount);
                var structuralDeduction = calculatedLines.Where(x => x.Component.Type == SalaryComponentType.Deduction).Sum(x => x.Amount);
                var bonus = bonusByEmployee.GetValueOrDefault(employee.Id);
                var gross = Math.Round(baseGross + bonus, 2);
                var remaining = gross;
                var appliedStructuralDeduction = Math.Min(Math.Max(0m, structuralDeduction), remaining);
                remaining -= appliedStructuralDeduction;
                var absentDays = absentByEmployee.GetValueOrDefault(employee.Id);
                var attendanceRequested = baseGross <= 0 ? 0m : Math.Round(baseGross / DateTime.DaysInMonth(request.Year, request.Month) * absentDays, 2);
                var attendanceDeduction = Math.Min(Math.Max(0m, attendanceRequested), remaining);
                remaining -= attendanceDeduction;
                var loanRequested = loanByEmployee.GetValueOrDefault(employee.Id);
                var loanDeduction = Math.Min(Math.Max(0m, loanRequested), remaining);
                remaining -= loanDeduction;
                var deductions = Math.Round(appliedStructuralDeduction + attendanceDeduction + loanDeduction, 2);
                var net = Math.Round(gross - deductions, 2);

                var payrollEmployee = new PayrollEmployee
                {
                    TenantId = tenantId,
                    PayrollRunId = run.Id,
                    EmployeeId = employee.Id,
                    EmployeeCodeSnapshot = employee.EmployeeCode,
                    EmployeeNameSnapshot = employee.FullName,
                    GrossAmount = gross,
                    DeductionAmount = deductions,
                    NetAmount = net,
                    CreatedAt = now,
                    CreatedBy = _user.UserId
                };
                payrollEmployees.Add(payrollEmployee);
                calculation[employee.Id] = new PayrollCalculation(calculatedLines, bonus, attendanceDeduction, loanDeduction);
            }

            if (payrollEmployees.Count > 0)
            {
                await _payrollEmployees.AddRangeAsync(payrollEmployees);
                await _uow.SaveChangesAsync(ct);

                var payrollLines = new List<PayrollLine>();
                foreach (var payrollEmployee in payrollEmployees)
                {
                    var calc = calculation[payrollEmployee.EmployeeId];
                    foreach (var line in calc.StructureLines.Where(x => x.Amount > 0))
                    {
                        payrollLines.Add(new PayrollLine
                        {
                            TenantId = tenantId,
                            PayrollEmployeeId = payrollEmployee.Id,
                            SalaryComponentId = line.Component.Id,
                            Amount = Math.Round(line.Amount, 2),
                            Description = line.Source.CalculationMethodCode,
                            CreatedAt = now,
                            CreatedBy = _user.UserId
                        });
                    }
                    if (calc.Bonus > 0)
                        payrollLines.Add(NewPayrollLine(tenantId, payrollEmployee.Id, components[BonusCode].Id, calc.Bonus, "Monthly bonus", now));
                    if (calc.AttendanceDeduction > 0)
                        payrollLines.Add(NewPayrollLine(tenantId, payrollEmployee.Id, components[AttendanceDeductionCode].Id, calc.AttendanceDeduction, "Attendance deduction", now));
                    if (calc.LoanDeduction > 0)
                        payrollLines.Add(NewPayrollLine(tenantId, payrollEmployee.Id, components[LoanRecoveryCode].Id, calc.LoanDeduction, "Loan recovery", now));
                }
                if (payrollLines.Count > 0) await _payrollLines.AddRangeAsync(payrollLines);
            }

            run.State = PayrollRunState.Calculated;
            run.CalculatedAt = now;
            run.UpdatedAt = now;
            run.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync();
            var batch = await BuildBatchAsync(run, false, ct);
            batch.Generated = payrollEmployees.Count;
            return ApiResponse<PayrollBatchDto>.SuccessResponse(batch, "Payroll generated.");
        }
        catch (DbUpdateException ex)
        {
            await SafeRollback();
            _log.LogWarning(ex, "Payroll generation conflict for tenant {TenantId} period {Year}-{Month}", tenantId, request.Year, request.Month);
            return ApiResponse<PayrollBatchDto>.ErrorResponse("Payroll conflicts with an existing request or period.", 409);
        }
        catch (Exception ex)
        {
            await SafeRollback();
            _log.LogError(ex, "Payroll generation failed for tenant {TenantId} period {Year}-{Month}", tenantId, request.Year, request.Month);
            return ApiResponse<PayrollBatchDto>.ErrorResponse("Payroll generation failed.", 500);
        }
    }

    public async Task<ApiResponse<PayrollRowDto>> PayAsync(PayPayrollDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll access is required.", 403);
        if (request.PayrollReference == Guid.Empty || !TryVersion(request.RowVersion, out var version))
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll payment request is invalid.", 400);
        if (!TryPaymentMethod(request.PaymentMethod, out var paymentMethod))
            return ApiResponse<PayrollRowDto>.ErrorResponse("Unsupported payment method.");

        var tenantId = _user.TenantId;
        var payrollEmployee = await _payrollEmployees.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.RowVersion == version, ct);
        if (payrollEmployee == null)
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll changed by another user. Reload and try again.", 409);
        if (LegacyPayrollReference(tenantId, payrollEmployee.PayrollRunId, payrollEmployee.Id) != request.PayrollReference)
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll reference is invalid.", 404);

        var run = await _payrollRuns.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == payrollEmployee.PayrollRunId, ct);
        if (run == null) return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll run not found.", 404);
        if (run.State == PayrollRunState.Cancelled || run.State == PayrollRunState.Draft)
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll run is not ready for payment.", 409);
        if (payrollEmployee.NetAmount <= 0)
            return ApiResponse<PayrollRowDto>.ErrorResponse("Zero-value payroll does not require a payment.", 409);

        var existingPayment = await _payrollPayments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PayrollEmployeeId == payrollEmployee.Id && x.State == PaymentState.Successful)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (existingPayment != null)
            return ApiResponse<PayrollRowDto>.SuccessResponse(await BuildRowAsync(run, payrollEmployee, ct), "Payroll payment was already processed.");

        try
        {
            await _uow.BeginTransactionAsync();
            var now = _clock.GetUtcNow().UtcDateTime;
            if (run.State == PayrollRunState.Calculated)
            {
                run.State = PayrollRunState.Approved;
                run.ApprovedAt = now;
                run.ApprovedByUserId = _user.UserId;
                run.UpdatedAt = now;
                run.UpdatedBy = _user.UserId;
            }

            var payment = new PayrollPayment
            {
                TenantId = tenantId,
                PublicId = Guid.NewGuid(),
                ClientRequestId = StableGuid("legacy-pay", tenantId, payrollEmployee.PayrollRunId, payrollEmployee.Id),
                PayrollEmployeeId = payrollEmployee.Id,
                PaymentDate = DateOnly.FromDateTime(now),
                PaymentMethod = paymentMethod,
                Amount = payrollEmployee.NetAmount,
                CurrencyCode = "BDT",
                ExternalReference = Truncate(Trim(request.Note), 150),
                State = PaymentState.Successful,
                CreatedAt = now,
                CreatedBy = _user.UserId
            };
            await _payrollPayments.AddAsync(payment);

            var components = await _salaryComponents.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Code == LoanRecoveryCode)
                .Select(x => new { x.Id }).ToListAsync(ct);
            var loanComponentId = components.Select(x => x.Id).FirstOrDefault();
            decimal loanRecoveryAmount = 0;
            if (loanComponentId > 0)
            {
                loanRecoveryAmount = await _payrollLines.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.PayrollEmployeeId == payrollEmployee.Id && x.SalaryComponentId == loanComponentId)
                    .SumAsync(x => x.Amount, ct);
            }

            if (loanRecoveryAmount > 0)
            {
                var loans = await _loans.GetQueryable()
                    .Where(x => x.TenantId == tenantId && x.EmployeeId == payrollEmployee.EmployeeId && !x.IsClosed && x.OutstandingAmount > 0)
                    .OrderBy(x => x.IssueDate).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
                var remaining = loanRecoveryAmount;
                foreach (var loan in loans)
                {
                    if (remaining <= 0) break;
                    var amount = Math.Min(remaining, Math.Min(loan.InstallmentAmount, loan.OutstandingAmount));
                    if (amount <= 0) continue;
                    await _loanRecoveries.AddAsync(new LoanAdvanceRecovery
                    {
                        TenantId = tenantId,
                        ClientRequestId = StableGuid("legacy-loan-recovery", tenantId, payrollEmployee.Id, loan.Id),
                        LoanAdvanceId = loan.Id,
                        PayrollEmployeeId = payrollEmployee.Id,
                        RecoveryDate = DateOnly.FromDateTime(now),
                        Amount = amount,
                        Remarks = "Recovered through payroll payment.",
                        CreatedAt = now,
                        CreatedBy = _user.UserId
                    });
                    loan.OutstandingAmount -= amount;
                    loan.IsClosed = loan.OutstandingAmount <= 0;
                    loan.UpdatedAt = now;
                    loan.UpdatedBy = _user.UserId;
                    remaining -= amount;
                }
            }

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync();
            return ApiResponse<PayrollRowDto>.SuccessResponse(await BuildRowAsync(run, payrollEmployee, ct), "Payroll marked paid.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollback();
            _log.LogWarning(ex, "Concurrent payroll payment {Reference}", request.PayrollReference);
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll changed by another user.", 409);
        }
        catch (DbUpdateException ex)
        {
            await SafeRollback();
            _log.LogWarning(ex, "Duplicate or conflicting payroll payment {Reference}", request.PayrollReference);
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll payment conflicts with an existing transaction.", 409);
        }
        catch (Exception ex)
        {
            await SafeRollback();
            _log.LogError(ex, "Payroll payment failed {Reference}", request.PayrollReference);
            return ApiResponse<PayrollRowDto>.ErrorResponse("Payroll payment failed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<PayrollEmployeeOptionDto>>> GetEmployeeOptionsAsync(string? search,CancellationToken ct=default)
    {
        if (!CanManage()) return ApiResponse<IReadOnlyList<PayrollEmployeeOptionDto>>.ErrorResponse("Payroll permission required.",403);
        if (search?.Length>100) return ApiResponse<IReadOnlyList<PayrollEmployeeOptionDto>>.ErrorResponse("Search exceeds 100 characters.");
        var term=search?.Trim();
        var query=_employees.GetQueryable().AsNoTracking().Where(x=>x.TenantId==_user.TenantId&&x.State==EmployeeState.Active);
        if (!string.IsNullOrWhiteSpace(term))
            query=query.Where(x=>x.FullName.StartsWith(term)||x.EmployeeCode.StartsWith(term));
        IReadOnlyList<PayrollEmployeeOptionDto> rows=await query.OrderBy(x=>x.EmployeeCode).ThenBy(x=>x.Id)
            .Select(x=>new PayrollEmployeeOptionDto{Reference=x.PublicId,EmployeeCode=x.EmployeeCode,Name=x.FullName})
            .Take(100).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<PayrollEmployeeOptionDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<PayrollPeriodPageDto>> GetPeriodAsync(int year,int month,int page,int pageSize,CancellationToken ct=default)
    {
        if (!CanManage()) return ApiResponse<PayrollPeriodPageDto>.ErrorResponse("Payroll permission required.",403);
        if (year is < 2000 or > 2200||month is < 1 or > 12||page<1||pageSize is < 1 or > 50)
            return ApiResponse<PayrollPeriodPageDto>.ErrorResponse("Invalid payroll period or pagination.");
        var tenant=_user.TenantId;
        var run=await _payrollRuns.GetQueryable().AsNoTracking()
            .Where(x=>x.TenantId==tenant&&x.Year==year&&x.Month==month&&x.State!=PayrollRunState.Cancelled)
            .OrderByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        var response=new PayrollPeriodPageDto{Page=page,PageSize=pageSize};
        if (run==null) return ApiResponse<PayrollPeriodPageDto>.SuccessResponse(response);
        var query=_payrollEmployees.GetQueryable().AsNoTracking().Where(x=>x.TenantId==tenant&&x.PayrollRunId==run.Id);
        response.TotalCount=await query.CountAsync(ct);
        if ((long)(page-1)*pageSize>=response.TotalCount) return ApiResponse<PayrollPeriodPageDto>.SuccessResponse(response);
        var entries=await query.OrderBy(x=>x.EmployeeCodeSnapshot).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        foreach(var entry in entries) response.Rows.Add(await BuildRowAsync(run,entry,ct));
        return ApiResponse<PayrollPeriodPageDto>.SuccessResponse(response);
    }

    public async Task<ApiResponse<IReadOnlyList<PayrollRowDto>>> GetMyPayrollAsync(CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.TenantId <= 0)
            return ApiResponse<IReadOnlyList<PayrollRowDto>>.ErrorResponse("Authentication is required.", 403);

        var tenantId = _user.TenantId;
        var employee = await _employees.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == _user.UserId && x.State == EmployeeState.Active, ct);
        if (employee == null)
            return ApiResponse<IReadOnlyList<PayrollRowDto>>.ErrorResponse("Employee profile is not linked to this account.", 403);

        var payrollEmployees = await _payrollEmployees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employee.Id)
            .OrderByDescending(x => x.Id).Take(120).ToListAsync(ct);
        if (payrollEmployees.Count == 0)
            return ApiResponse<IReadOnlyList<PayrollRowDto>>.SuccessResponse(Array.Empty<PayrollRowDto>());

        var runIds = payrollEmployees.Select(x => x.PayrollRunId).Distinct().ToArray();
        var runs = await _payrollRuns.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && runIds.Contains(x.Id))
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(ct);
        var runMap = runs.ToDictionary(x => x.Id);
        var rows = new List<PayrollRowDto>();
        foreach (var payrollEmployee in payrollEmployees.Where(x => runMap.ContainsKey(x.PayrollRunId)))
            rows.Add(await BuildRowAsync(runMap[payrollEmployee.PayrollRunId], payrollEmployee, ct));

        IReadOnlyList<PayrollRowDto> ordered = rows.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToList();
        return ApiResponse<IReadOnlyList<PayrollRowDto>>.SuccessResponse(ordered);
    }

    private async Task<PayrollBatchDto> BuildBatchAsync(PayrollRun run, bool existing, CancellationToken ct)
    {
        var employees = await _payrollEmployees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == run.TenantId && x.PayrollRunId == run.Id)
            .OrderBy(x => x.EmployeeCodeSnapshot).Take(5000).ToListAsync(ct);
        var rows = new List<PayrollRowDto>(employees.Count);
        foreach (var employee in employees) rows.Add(await BuildRowAsync(run, employee, ct));
        return new PayrollBatchDto
        {
            ClientRequestId = run.ClientRequestId,
            Existing = existing ? rows.Count : 0,
            Generated = existing ? 0 : rows.Count,
            Rows = rows
        };
    }

    private async Task<PayrollRowDto> BuildRowAsync(PayrollRun run, PayrollEmployee payrollEmployee, CancellationToken ct)
    {
        var tenantId = run.TenantId;
        var employee = await _employees.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == payrollEmployee.EmployeeId)
            .Select(x => new { x.PublicId, x.EmployeeCode, x.FullName }).FirstOrDefaultAsync(ct);
        var lines = await _payrollLines.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PayrollEmployeeId == payrollEmployee.Id)
            .Select(x => new { x.SalaryComponentId, x.Amount }).ToListAsync(ct);
        var componentIds = lines.Select(x => x.SalaryComponentId).Distinct().ToArray();
        var components = componentIds.Length == 0
            ? new Dictionary<long, string>()
            : await _salaryComponents.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && componentIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        decimal Amount(string code) => lines.Where(x => components.TryGetValue(x.SalaryComponentId, out var c) && string.Equals(c, code, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);

        var payment = await _payrollPayments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PayrollEmployeeId == payrollEmployee.Id && x.State == PaymentState.Successful)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var absentDays = await _attendance.GetQueryable().AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.EmployeeId == payrollEmployee.EmployeeId && x.AttendanceDate >= run.PeriodStart && x.AttendanceDate <= run.PeriodEnd && x.State == AttendanceState.Absent, ct);

        return new PayrollRowDto
        {
            Reference = LegacyPayrollReference(tenantId, run.Id, payrollEmployee.Id),
            EmployeeReference = employee?.PublicId ?? Guid.Empty,
            EmployeeCode = payrollEmployee.EmployeeCodeSnapshot ?? employee?.EmployeeCode ?? string.Empty,
            EmployeeName = payrollEmployee.EmployeeNameSnapshot ?? employee?.FullName ?? string.Empty,
            Month = run.Month.ToString("D2"),
            Year = run.Year,
            GrossSalary = payrollEmployee.GrossAmount,
            AbsentDays = absentDays,
            AttendanceDeduction = Amount(AttendanceDeductionCode),
            LoanDeduction = Amount(LoanRecoveryCode),
            Bonus = Amount(BonusCode),
            NetSalary = payrollEmployee.NetAmount,
            Status = payment != null ? "Paid" : run.State.ToString(),
            PaymentDate = payment?.PaymentDate.ToDateTime(TimeOnly.MinValue),
            RowVersion = Convert.ToBase64String(payrollEmployee.RowVersion)
        };
    }

    private async Task<Dictionary<string, SalaryComponent>> EnsureCompatibilityComponentsAsync(CancellationToken ct)
    {
        var expected = new Dictionary<string, (string Name, SalaryComponentType Type)>(StringComparer.OrdinalIgnoreCase)
        {
            [BasicCode] = ("Basic Salary", SalaryComponentType.Earning),
            [HouseRentCode] = ("House Rent", SalaryComponentType.Earning),
            [MedicalCode] = ("Medical", SalaryComponentType.Earning),
            [TransportCode] = ("Transport", SalaryComponentType.Earning),
            [OtherCode] = ("Other Allowance", SalaryComponentType.Earning),
            [BonusCode] = ("Bonus", SalaryComponentType.Earning),
            [AttendanceDeductionCode] = ("Attendance Deduction", SalaryComponentType.Deduction),
            [LoanRecoveryCode] = ("Loan Recovery", SalaryComponentType.Deduction)
        };
        var tenantId = _user.TenantId;
        var codes = expected.Keys.ToArray();
        var existing = await _salaryComponents.GetQueryable()
            .Where(x => x.TenantId == tenantId && codes.Contains(x.Code))
            .ToListAsync(ct);
        var byCode = existing.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var item in expected)
        {
            if (byCode.TryGetValue(item.Key, out var component))
            {
                if (component.Type != item.Value.Type)
                    throw new InvalidOperationException($"Salary component '{item.Key}' has an incompatible type.");
                if (!component.IsActive)
                {
                    component.IsActive = true;
                    component.UpdatedAt = now;
                    component.UpdatedBy = _user.UserId;
                }
                continue;
            }

            component = new SalaryComponent
            {
                TenantId = tenantId,
                Name = item.Value.Name,
                Code = item.Key,
                Type = item.Value.Type,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = _user.UserId
            };
            await _salaryComponents.AddAsync(component);
            byCode[item.Key] = component;
        }
        await _uow.SaveChangesAsync(ct);
        return byCode;
    }

    private static List<CalculatedSalaryLine> CalculateStructureLines(
        IReadOnlyList<SalaryStructureLine> lines,
        IReadOnlyDictionary<long, SalaryComponent> components)
    {
        var amounts = new Dictionary<long, decimal>();
        var unresolved = new List<SalaryStructureLine>();
        foreach (var line in lines)
        {
            if (!components.TryGetValue(line.SalaryComponentId, out _)) continue;
            if (!string.Equals(line.CalculationMethodCode, "Percentage", StringComparison.OrdinalIgnoreCase) || !line.BasedOnSalaryComponentId.HasValue || !line.Percentage.HasValue)
                amounts[line.SalaryComponentId] = Math.Max(0m, line.Amount);
            else
                unresolved.Add(line);
        }
        for (var pass = 0; pass < lines.Count && unresolved.Count > 0; pass++)
        {
            for (var i = unresolved.Count - 1; i >= 0; i--)
            {
                var line = unresolved[i];
                if (!line.BasedOnSalaryComponentId.HasValue || !amounts.TryGetValue(line.BasedOnSalaryComponentId.Value, out var baseAmount)) continue;
                amounts[line.SalaryComponentId] = Math.Max(0m, Math.Round(baseAmount * line.Percentage!.Value / 100m, 2));
                unresolved.RemoveAt(i);
            }
        }
        foreach (var line in unresolved) amounts[line.SalaryComponentId] = Math.Max(0m, line.Amount);

        return lines.Where(x => components.ContainsKey(x.SalaryComponentId))
            .Select(x => new CalculatedSalaryLine(x, components[x.SalaryComponentId], amounts.GetValueOrDefault(x.SalaryComponentId)))
            .ToList();
    }

    private PayrollLine NewPayrollLine(long tenantId, long payrollEmployeeId, long componentId, decimal amount, string description, DateTime now) => new()
    {
        TenantId = tenantId,
        PayrollEmployeeId = payrollEmployeeId,
        SalaryComponentId = componentId,
        Amount = Math.Round(amount, 2),
        Description = description,
        CreatedAt = now,
        CreatedBy = _user.UserId
    };

    private static Guid LegacyPayrollReference(long tenantId, long payrollRunId, long payrollEmployeeId) =>
        StableGuid("legacy-payroll-row", tenantId, payrollRunId, payrollEmployeeId);

    private static Guid StableGuid(string scope, long tenantId, long value1, long value2)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{tenantId}:{value1}:{value2}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static bool TryAttendanceState(string value, out AttendanceState state)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "present": state = AttendanceState.Present; return true;
            case "absent": state = AttendanceState.Absent; return true;
            case "late": state = AttendanceState.Late; return true;
            case "leave": state = AttendanceState.Leave; return true;
            case "holiday":
            case "excused": state = AttendanceState.Excused; return true;
            default: state = default; return false;
        }
    }

    private static bool TryPaymentMethod(string value, out PaymentMethodType method)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "cash": method = PaymentMethodType.Cash; return true;
            case "bank": method = PaymentMethodType.BankTransfer; return true;
            case "bkash":
            case "nagad": method = PaymentMethodType.MobileFinancialService; return true;
            default: method = default; return false;
        }
    }

    private bool CanHr() => _user.IsAuthenticated && _user.TenantId > 0 && (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("HR"));
    private bool CanManage() => CanHr() || _user.IsInRole("Accountant");
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Truncate(string? value, int maxLength) => value == null || value.Length <= maxLength ? value : value[..maxLength];
    private static bool TryVersion(string? value, out byte[] version)
    {
        try
        {
            version = Convert.FromBase64String(value ?? string.Empty);
            return version.Length > 0;
        }
        catch
        {
            version = Array.Empty<byte>();
            return false;
        }
    }

    private async Task SafeRollback()
    {
        try { await _uow.RollbackTransactionAsync(); }
        catch (Exception ex) { _log.LogError(ex, "Payroll rollback failed."); }
    }

    private sealed record CalculatedSalaryLine(SalaryStructureLine Source, SalaryComponent Component, decimal Amount);
    private sealed record PayrollCalculation(IReadOnlyList<CalculatedSalaryLine> StructureLines, decimal Bonus, decimal AttendanceDeduction, decimal LoanDeduction);
}
