using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.HR;

/// <summary>Institutional salary accrual. Uses canonical payroll snapshots and institutional GL only.</summary>
public sealed class PayrollJournalPostingService : IPayrollJournalPostingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public PayrollJournalPostingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool CanRead => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool CanPost => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private static bool Money(decimal amount) => amount >= 0m && amount <= 9999999999999999.99m &&
        decimal.Round(amount, 2) == amount;
    private static bool Version(byte[] actual, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) &&
            actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static Guid PostingKey(long tenant, Guid reference)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("EduOS:Institution:PayrollRun:" + tenant + ":" + reference.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }
    public async Task<ApiResponse<PayrollPostingRegisterDto>> GetRegisterAsync(
        bool unpostedOnly, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanRead) return Error<PayrollPostingRegisterDto>("Institutional payroll accounting access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 10 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PayrollPostingRegisterDto>("Invalid payroll register pagination.");
        var tenant = user.TenantId;
        var query = db.PayrollRuns.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted &&
            (x.State == PayrollRunState.Calculated || x.State == PayrollRunState.Approved || x.State == PayrollRunState.Posted));
        if (!user.IsTenantAdmin)
            query = query.Where(x => x.CampusId.HasValue && db.UserCampusAccesses.Any(a =>
                a.TenantId == tenant && a.UserId == user.UserId && a.CampusId == x.CampusId &&
                a.IsActive && !a.IsDeleted));
        if (unpostedOnly) query = query.Where(x => !x.JournalId.HasValue);
        var count = await query.CountAsync(ct);
        var runs = await query.OrderByDescending(x => x.PeriodEnd).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var runIds = runs.Select(x => x.Id).ToArray();
        var employees = await db.PayrollEmployees.AsNoTracking().Where(x => x.TenantId == tenant &&
            runIds.Contains(x.PayrollRunId) && !x.IsDeleted).ToListAsync(ct);
        var employeeIds = employees.Select(x => x.Id).ToArray();
        var lines = await db.PayrollLines.AsNoTracking().Where(x => x.TenantId == tenant &&
            employeeIds.Contains(x.PayrollEmployeeId) && !x.IsDeleted).Take(50001).ToListAsync(ct);
        if (lines.Count > 50000) return Error<PayrollPostingRegisterDto>("Payroll register exceeds safe line limit.", 409);
        var componentIds = lines.Select(x => x.SalaryComponentId).Distinct().ToArray();
        var components = await db.SalaryComponents.AsNoTracking().Where(x => x.TenantId == tenant &&
            componentIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var journalIds = runs.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).ToArray();
        var journals = await db.Journals.AsNoTracking().Where(x => x.TenantId == tenant &&
            journalIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var employeeToRun = employees.ToDictionary(x => x.Id, x => x.PayrollRunId);
        var rows = runs.Select(run =>
        {
            var people = employees.Where(x => x.PayrollRunId == run.Id).ToArray();
            var used = lines.Where(x => employeeToRun.GetValueOrDefault(x.PayrollEmployeeId) == run.Id)
                .GroupBy(x => x.SalaryComponentId).OrderBy(x => x.Key)
                .Select(g =>
                {
                    components.TryGetValue(g.Key, out var component);
                    return new PayrollPostingComponentDto { SalaryComponentId = g.Key,
                        Code = component?.Code ?? "(missing)", Name = component?.Name ?? "(missing)",
                        Type = component?.Type ?? SalaryComponentType.Earning, Amount = g.Sum(x => x.Amount) };
                }).ToArray();
            var journal = run.JournalId.HasValue ? journals.GetValueOrDefault(run.JournalId.Value) : null;
            return new PayrollPostingRowDto { Reference = run.PublicId, RunNumber = run.RunNumber,
                PeriodEnd = run.PeriodEnd, CampusId = run.CampusId, State = run.State,
                GrossAmount = people.Sum(x => x.GrossAmount),
                DeductionAmount = people.Sum(x => x.DeductionAmount),
                NetAmount = people.Sum(x => x.NetAmount),
                RowVersion = Convert.ToBase64String(run.RowVersion),
                JournalReference = journal?.PublicId, Components = used };
        }).ToArray();
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(101)
            .Select(x => new PayrollPostingPeriodDto { Id = x.Id, Name = x.Name,
                StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        if (periods.Count > 100) return Error<PayrollPostingRegisterDto>("Too many open accounting periods.", 409);
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT")
            .OrderBy(x => x.Code).Take(301).ToListAsync(ct);
        if (accounts.Count > 300) return Error<PayrollPostingRegisterDto>("Too many posting accounts for the synchronous selector.", 409);
        return ApiResponse<PayrollPostingRegisterDto>.SuccessResponse(new PayrollPostingRegisterDto
        {
            OpenPeriods = periods, Accounts = accounts.Select(x => new PayrollPostingAccountDto
                { Id = x.Id, Code = x.Code, Name = x.Name, AccountType = x.AccountType.ToString() }).ToArray(),
            Runs = rows, Page = page, PageSize = pageSize, TotalCount = count
        });
    }
    public async Task<ApiResponse<PostPayrollJournalResultDto>> PostAsync(
        Guid reference, PostPayrollJournalRequestDto request, CancellationToken ct = default)
    {
        if (!CanPost) return Error<PostPayrollJournalResultDto>("Tenant administrator permission required.", 403);
        if (reference == Guid.Empty || request == null || request.AccountingPeriodId <= 0 ||
            request.SalaryPayableAccountId <= 0 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 256 || request.Mappings == null ||
            request.Mappings.Count is < 1 or > 80 || request.Mappings.Any(x =>
                x.SalaryComponentId <= 0 || x.AccountId <= 0 || x.OffsetAccountId is <= 0) ||
            request.Mappings.Select(x => x.SalaryComponentId).Distinct().Count() != request.Mappings.Count)
            return Error<PostPayrollJournalResultDto>("Open period, payable ledger, version and unique component mappings required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var run = await db.PayrollRuns.SingleOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (run == null) return Error<PostPayrollJournalResultDto>("Payroll run not found.", 404);
            if (run.JournalId.HasValue)
            {
                var prior = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == run.JournalId.Value && !x.IsDeleted, ct);
                if (prior == null || prior.SourceType != "PayrollRun" || prior.SourceId != run.Id ||
                    prior.State != JournalState.Posted || prior.AccountingPeriodId != request.AccountingPeriodId ||
                    prior.CampusId != run.CampusId || run.State != PayrollRunState.Posted)
                    return Error<PostPayrollJournalResultDto>("Linked payroll journal requires reconciliation.", 409);
                var existingLines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == prior.Id && !x.IsDeleted).ToListAsync(ct);
                if (!existingLines.Any(x => x.Description == "Payroll net payable" &&
                    x.AccountId == request.SalaryPayableAccountId) ||
                    request.Mappings.Any(m => existingLines.Any(x => x.Description != null &&
                        x.Description.StartsWith("Component " + m.SalaryComponentId + " ")) &&
                        (!existingLines.Any(x => x.Description != null &&
                            x.Description.StartsWith("Component " + m.SalaryComponentId + " ") &&
                            x.AccountId == m.AccountId) ||
                        (m.OffsetAccountId.HasValue && !existingLines.Any(x =>
                            x.Description == "Component " + m.SalaryComponentId + " employer liability" &&
                            x.AccountId == m.OffsetAccountId.Value)))))
                    return Error<PostPayrollJournalResultDto>("Retry uses different payroll account mappings.", 409);
                return ApiResponse<PostPayrollJournalResultDto>.SuccessResponse(new PostPayrollJournalResultDto
                { PayrollReference = reference, JournalReference = prior.PublicId,
                    JournalNumber = prior.JournalNumber, TotalDebit = existingLines.Sum(x => x.DebitAmount),
                    TotalCredit = existingLines.Sum(x => x.CreditAmount), AlreadyPosted = true });
            }
            if (run.State != PayrollRunState.Approved || !run.ApprovedByUserId.HasValue ||
                run.ApprovedByUserId == run.CreatedBy)
                return Error<PostPayrollJournalResultDto>("Payroll must be independently approved before GL posting.", 409);
            if (!Version(run.RowVersion, request.RowVersion))
                return Error<PostPayrollJournalResultDto>("Payroll changed; reload and retry.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate <= run.PeriodEnd && x.EndDate >= run.PeriodEnd, ct);
            if (period == null) return Error<PostPayrollJournalResultDto>("Accounting period is closed or does not cover payroll end date.", 409);
            if (run.CampusId.HasValue && !await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == run.CampusId && x.IsActive && !x.IsDeleted, ct))
                return Error<PostPayrollJournalResultDto>("Payroll campus is inactive.", 409);
            var people = await db.PayrollEmployees.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.PayrollRunId == run.Id && !x.IsDeleted).Take(5001).ToListAsync(ct);
            if (people.Count is < 1 or > 5000 || people.Select(x => x.EmployeeId).Distinct().Count() != people.Count)
                return Error<PostPayrollJournalResultDto>("Payroll employee snapshot is missing or duplicated.", 409);
            if (run.CampusId.HasValue)
            {
                var ids = people.Select(x => x.EmployeeId).ToArray();
                var covered = await db.EmployeeCampusAssignments.AsNoTracking().Where(x => x.TenantId == tenant &&
                    ids.Contains(x.EmployeeId) && x.CampusId == run.CampusId &&
                    x.EffectiveFrom <= run.PeriodEnd && (!x.EffectiveTo.HasValue || x.EffectiveTo >= run.PeriodEnd) &&
                    !x.IsDeleted).Select(x => x.EmployeeId).Distinct().CountAsync(ct);
                if (covered != people.Count) return Error<PostPayrollJournalResultDto>("Payroll contains employees outside the selected campus.", 409);
            }
            var peopleIds = people.Select(x => x.Id).ToArray();
            var payrollLines = await db.PayrollLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                peopleIds.Contains(x.PayrollEmployeeId) && !x.IsDeleted).Take(20001).ToListAsync(ct);
            if (payrollLines.Count is < 1 or > 20000 || payrollLines.Any(x => !Money(x.Amount)))
                return Error<PostPayrollJournalResultDto>("Payroll line amounts or line count are invalid.", 409);
            var componentIds = payrollLines.Select(x => x.SalaryComponentId).Distinct().ToArray();
            if (componentIds.Length != request.Mappings.Count)
                return Error<PostPayrollJournalResultDto>("Provide a mapping for every payroll component, without extras.", 409);
            var components = await db.SalaryComponents.AsNoTracking().Where(x => x.TenantId == tenant &&
                componentIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
            if (components.Count != componentIds.Length || componentIds.Any(x =>
                !request.Mappings.Any(m => m.SalaryComponentId == x)))
                return Error<PostPayrollJournalResultDto>("Salary component mapping is missing.", 409);
            if (components.Values.Any(x => x.Code == "LOAN_RECOVERY" &&
                payrollLines.Any(l => l.SalaryComponentId == x.Id && l.Amount > 0m)))
                return Error<PostPayrollJournalResultDto>(
                    "Loan recoveries require loan-balance and recovery-ledger settlement before posting.", 409);
            var lineLookup = payrollLines.ToLookup(x => x.PayrollEmployeeId);
            foreach (var person in people)
            {
                var entries = lineLookup[person.Id].ToArray();
                var gross = entries.Where(x => components[x.SalaryComponentId].Type == SalaryComponentType.Earning)
                    .Sum(x => x.Amount);
                var deductions = entries.Where(x => components[x.SalaryComponentId].Type == SalaryComponentType.Deduction)
                    .Sum(x => x.Amount);
                if (!Money(person.GrossAmount) || !Money(person.DeductionAmount) || !Money(person.NetAmount) ||
                    gross != person.GrossAmount || deductions != person.DeductionAmount ||
                    person.NetAmount != gross - deductions || gross <= 0m || person.NetAmount < 0m)
                    return Error<PostPayrollJournalResultDto>("Payroll component totals do not reconcile with employee snapshots.", 409);
            }
            var net = people.Sum(x => x.NetAmount);
            if (!Money(net) || net <= 0m)
                return Error<PostPayrollJournalResultDto>("Payroll requires a positive net salary payable.", 409);
            var mapping = request.Mappings.ToDictionary(x => x.SalaryComponentId);
            var accountIds = request.Mappings.SelectMany(x => x.OffsetAccountId.HasValue ?
                new[] { x.AccountId, x.OffsetAccountId.Value } : new[] { x.AccountId })
                .Append(request.SalaryPayableAccountId).Distinct().ToArray();
            var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
                accountIds.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                x.AllowsPosting && x.CurrencyCode == "BDT").ToDictionaryAsync(x => x.Id, ct);
            if (accounts.Count != accountIds.Length || accounts[request.SalaryPayableAccountId].AccountType != AccountType.Liability)
                return Error<PostPayrollJournalResultDto>("All posting ledgers must be active BDT accounts; net payable must be a liability.", 409);
            var entriesToPost = new List<JournalLine>();
            foreach (var group in payrollLines.GroupBy(x => x.SalaryComponentId).OrderBy(x => x.Key))
            {
                var component = components[group.Key];
                var m = mapping[group.Key];
                var amount = group.Sum(x => x.Amount);
                if (!Money(amount)) return Error<PostPayrollJournalResultDto>("Component total exceeds supported decimal precision.", 409);
                if (amount == 0m) continue;
                var account = accounts[m.AccountId];
                if (m.AccountId == request.SalaryPayableAccountId)
                    return Error<PostPayrollJournalResultDto>("Component account cannot be the net salary payable account.", 409);
                if (component.Type == SalaryComponentType.Earning)
                {
                    if (account.AccountType != AccountType.Expense || m.OffsetAccountId.HasValue)
                        return Error<PostPayrollJournalResultDto>("Earnings require an expense debit ledger.", 409);
                    entriesToPost.Add(new JournalLine { AccountId = m.AccountId, DebitAmount = amount,
                        Description = "Component " + group.Key + " earning expense" });
                }
                else if (component.Type == SalaryComponentType.Deduction)
                {
                    if (m.OffsetAccountId.HasValue || account.AccountType != AccountType.Liability)
                        return Error<PostPayrollJournalResultDto>("Deductions require a liability credit ledger.", 409);
                    entriesToPost.Add(new JournalLine { AccountId = m.AccountId, CreditAmount = amount,
                        Description = "Component " + group.Key + " deduction liability" });
                }
                else if (component.Type == SalaryComponentType.EmployerContribution)
                {
                    if (account.AccountType != AccountType.Expense || !m.OffsetAccountId.HasValue ||
                        m.OffsetAccountId.Value == request.SalaryPayableAccountId ||
                        accounts[m.OffsetAccountId.Value].AccountType != AccountType.Liability)
                        return Error<PostPayrollJournalResultDto>("Employer contributions require expense and separate liability ledgers.", 409);
                    entriesToPost.Add(new JournalLine { AccountId = m.AccountId, DebitAmount = amount,
                        Description = "Component " + group.Key + " employer expense" });
                    entriesToPost.Add(new JournalLine { AccountId = m.OffsetAccountId.Value, CreditAmount = amount,
                        Description = "Component " + group.Key + " employer liability" });
                }
                else return Error<PostPayrollJournalResultDto>("Unsupported salary component type.", 409);
            }
            entriesToPost.Add(new JournalLine { AccountId = request.SalaryPayableAccountId,
                CreditAmount = net, Description = "Payroll net payable" });
            var debit = entriesToPost.Sum(x => x.DebitAmount);
            var credit = entriesToPost.Sum(x => x.CreditAmount);
            if (entriesToPost.Count is < 2 or > 100 || debit <= 0m || debit != credit || !Money(debit))
                return Error<PostPayrollJournalResultDto>("Payroll accrual journal is not balanced or exceeds line limits.", 409);
            if (await db.Journals.AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                ((x.SourceType == "PayrollRun" && x.SourceId == run.Id) ||
                 x.IdempotencyKey == PostingKey(tenant, run.PublicId)), ct))
                return Error<PostPayrollJournalResultDto>("Unlinked payroll journal exists; reconcile before retry.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var referenceId = Guid.NewGuid();
            var journal = new Journal
            {
                TenantId = tenant, PublicId = referenceId, IdempotencyKey = PostingKey(tenant, run.PublicId),
                JournalNumber = "JRN-" + referenceId.ToString("N").ToUpperInvariant(),
                JournalDate = run.PeriodEnd, AccountingPeriodId = period.Id, CampusId = run.CampusId,
                CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "PayrollRun", SourceId = run.Id, ReferenceNumber = run.RunNumber,
                Description = "Institutional payroll accrual " + run.RunNumber,
                ApprovedByUserId = run.ApprovedByUserId, ApprovedAt = run.ApprovedAt,
                PostedByUserId = user.UserId, PostedAt = now, CreatedBy = user.UserId, CreatedAt = now
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            for (var i = 0; i < entriesToPost.Count; i++)
            {
                var line = entriesToPost[i]; line.TenantId = tenant; line.JournalId = journal.Id;
                line.CampusId = run.CampusId; line.LineNo = i + 1; line.CreatedAt = now;
                line.CreatedBy = user.UserId;
            }
            db.JournalLines.AddRange(entriesToPost);
            run.JournalId = journal.Id; run.State = PayrollRunState.Posted; run.PostedAt = now;
            run.UpdatedAt = now; run.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PostPayrollJournalResultDto>.SuccessResponse(new PostPayrollJournalResultDto
            {
                PayrollReference = reference, JournalReference = referenceId, JournalNumber = journal.JournalNumber,
                TotalDebit = debit, TotalCredit = credit
            }, "Payroll accrual posted to institutional GL.");
        }
        catch (DbUpdateConcurrencyException) { return Error<PostPayrollJournalResultDto>("Payroll changed concurrently; reload.", 409); }
        catch (DbUpdateException) { return Error<PostPayrollJournalResultDto>("Payroll journal conflicts with another posting; reload.", 409); }
    }
}
