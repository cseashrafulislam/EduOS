using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

public sealed partial class FeeBillingService
{
    private static Guid InstallmentMonthKey(Guid planReference, DateOnly month) =>
        PerEnrollmentKey(planReference, (long)month.Year * 100L + month.Month);

    // A plan-month invoice is a real issued receivable, not a forecast or a payment.
    // The deterministic tenant-unique invoice key binds it permanently to the
    // immutable plan and month without changing existing invoice schema/history.
    public async Task<ApiResponse<StudentFeeInstallmentMonthInvoiceDto>> IssueInstallmentMonthAsync(
        Guid reference, IssueStudentFeeInstallmentMonthDto request, CancellationToken ct = default)
    {
        if (!CanManage() || _uow is not EduOSDbContext db || db.CurrentTenantId != _user.TenantId)
            return Error<StudentFeeInstallmentMonthInvoiceDto>("Finance access required.", 403);
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.MonthStart.Year is < 2000 or > 2200 || request.MonthStart.Day != 1 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 128)
            return Error<StudentFeeInstallmentMonthInvoiceDto>("Invalid plan, month, version or request key.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var plan = await db.StudentFeeInstallmentPlans.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
                if (plan == null || !await CanAccessBillingCampusAsync(plan.CampusId, token))
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Plan unavailable.", 404);
                if (Convert.ToBase64String(plan.RowVersion) != request.RowVersion)
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Plan changed; refresh.", 409);
                if (request.MonthStart < plan.StartMonth || request.MonthStart > plan.EndMonth)
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Month is outside this agreement.", 409);
                var actions = await db.StudentFeeInstallmentPlanEvents.AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.StudentFeeInstallmentPlanId == plan.Id && !x.IsDeleted)
                    .OrderBy(x => x.ActionCode).Take(4).Select(x => x.ActionCode).ToListAsync(token);
                if (actions.Count != 2 || actions[0] != 2 || actions[1] != 3)
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Independent approval is required before billing.", 409);
                if (!await db.StudentEnrollments.AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == plan.StudentEnrollmentId &&
                    !x.IsDeleted && x.IsCurrent && x.State == EnrollmentState.Active &&
                    x.CampusId == plan.CampusId && x.AcademicYearId == plan.AcademicYearId, token))
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Enrollment changed; billing blocked.", 409);
                // Verify the entire frozen agreement, not only the requested month.
                if (await InstallmentPlanDtoAsync(db, plan, true, token) == null)
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Plan integrity check failed.", 409);
                var lines = await db.StudentFeeInstallmentPlanLines.AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.StudentFeeInstallmentPlanId == plan.Id &&
                        !x.IsDeleted && x.MonthStart == request.MonthStart)
                    .OrderBy(x => x.FeeHeadId).Take(51).ToListAsync(token);
                if (lines.Count is < 1 or > 50 ||
                    lines.Any(x => x.GrossAmount <= 0m || x.GrossAmount != decimal.Round(x.GrossAmount, 2) ||
                        x.DueDate < request.MonthStart || x.DueDate >= request.MonthStart.AddMonths(1)) ||
                    lines.Select(x => x.FeeHeadId).Distinct().Count() != lines.Count)
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Month's frozen fee lines are invalid.", 409);
                var subtotal = lines.Sum(x => x.GrossAmount);
                var dueDate = lines.Max(x => x.DueDate);
                var key = InstallmentMonthKey(plan.PublicId, request.MonthStart);
                var invoices = await db.StudentInvoices.AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted &&
                    ((x.StudentEnrollmentId == plan.StudentEnrollmentId &&
                        x.InvoiceDate >= request.MonthStart && x.InvoiceDate < request.MonthStart.AddMonths(1)) ||
                        x.ClientRequestId == key))
                    .Take(3).ToListAsync(token);
                if (invoices.Count > 1)
                    return Error<StudentFeeInstallmentMonthInvoiceDto>("Duplicate or competing invoice evidence.", 409);
                if (invoices.Count == 1)
                {
                    var prior = invoices[0];
                    if (prior.StudentEnrollmentId != plan.StudentEnrollmentId ||
                        prior.ClientRequestId != key || prior.InvoiceDate != request.MonthStart ||
                        prior.DueDate != dueDate || prior.CurrencyCode != plan.CurrencyCode ||
                        prior.State == InvoiceState.Cancelled || prior.Subtotal != subtotal)
                        return Error<StudentFeeInstallmentMonthInvoiceDto>("Existing invoice does not match the approved plan.", 409);
                    var priorLines = await db.StudentInvoiceLines.AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.StudentInvoiceId == prior.Id && !x.IsDeleted)
                        .Select(x => new { x.FeeHeadId, x.Amount, x.DiscountAmount, x.NetAmount })
                        .Take(51).ToListAsync(token);
                    if (priorLines.Count != lines.Count || priorLines.Sum(x => x.Amount) != subtotal ||
                        priorLines.Sum(x => x.DiscountAmount) != prior.DiscountAmount ||
                        priorLines.Sum(x => x.NetAmount) != prior.TotalAmount ||
                        prior.TotalAmount != prior.Subtotal - prior.DiscountAmount + prior.FineAmount ||
                        lines.Any(x => priorLines.Count(y => y.FeeHeadId == x.FeeHeadId && y.Amount == x.GrossAmount) != 1))
                        return Error<StudentFeeInstallmentMonthInvoiceDto>("Historical invoice lines are inconsistent.", 409);
                    return ApiResponse<StudentFeeInstallmentMonthInvoiceDto>.SuccessResponse(
                        new StudentFeeInstallmentMonthInvoiceDto {
                            PlanReference = plan.PublicId, MonthStart = request.MonthStart,
                            InvoiceReference = prior.PublicId, InvoiceNumber = prior.InvoiceNumber,
                            GrossAmount = prior.Subtotal, DiscountAmount = prior.DiscountAmount,
                            NetAmount = prior.TotalAmount, Existing = true
                        }, "Existing plan-month invoice returned.");
                }
                var awards = await (from award in db.StudentDiscounts.AsNoTracking()
                    join rule in db.DiscountRules.AsNoTracking()
                        on new { award.TenantId, Id = award.DiscountRuleId }
                        equals new { rule.TenantId, rule.Id }
                    where award.TenantId == tenant && award.StudentEnrollmentId == plan.StudentEnrollmentId &&
                        award.IsActive && !award.IsDeleted && rule.IsActive && !rule.IsDeleted &&
                        award.EffectiveFrom <= request.MonthStart &&
                        (!award.EffectiveTo.HasValue || award.EffectiveTo >= request.MonthStart) &&
                        (!rule.EffectiveFrom.HasValue || rule.EffectiveFrom <= request.MonthStart) &&
                        (!rule.EffectiveTo.HasValue || rule.EffectiveTo >= request.MonthStart)
                    select rule).ToListAsync(token);
                var discounts = CalculateLineDiscounts(lines.Select(x =>
                    (HeadId: x.FeeHeadId, Amount: x.GrossAmount)).ToArray(), awards);
                var totalDiscount = discounts.Sum();
                var now = _clock.GetUtcNow().UtcDateTime;
                var publicId = Guid.NewGuid();
                var invoice = new StudentInvoice {
                    TenantId = tenant, PublicId = publicId, ClientRequestId = key,
                    StudentEnrollmentId = plan.StudentEnrollmentId,
                    InvoiceNumber = "INV-" + publicId.ToString("N").ToUpperInvariant(),
                    InvoiceDate = request.MonthStart, DueDate = dueDate,
                    Subtotal = subtotal, DiscountAmount = totalDiscount,
                    FineAmount = 0m, TotalAmount = subtotal - totalDiscount,
                    DueAmount = subtotal - totalDiscount, PaidAmount = 0m,
                    CurrencyCode = plan.CurrencyCode, State = InvoiceState.Issued,
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                db.StudentInvoices.Add(invoice);
                await db.SaveChangesAsync(token);
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    db.StudentInvoiceLines.Add(new StudentInvoiceLine {
                        TenantId = tenant, StudentInvoiceId = invoice.Id, FeeHeadId = line.FeeHeadId,
                        FeeHeadCodeSnapshot = line.FeeHeadCodeSnapshot,
                        FeeHeadNameSnapshot = line.FeeHeadNameSnapshot,
                        Description = line.FeeHeadNameSnapshot, Amount = line.GrossAmount,
                        DiscountAmount = discounts[i], NetAmount = line.GrossAmount - discounts[i],
                        FineAmount = 0m, CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
                // Shared tenant rowversion gate coordinates with existing monthly
                // billing and agreement creation without changing their contracts.
                var gate = await db.Tenants.FirstAsync(x => x.Id == tenant && !x.IsDeleted, token);
                gate.UpdatedAt = now; gate.UpdatedBy = _user.UserId;
                await db.SaveChangesAsync(token);
                return ApiResponse<StudentFeeInstallmentMonthInvoiceDto>.SuccessResponse(
                    new StudentFeeInstallmentMonthInvoiceDto {
                        PlanReference = plan.PublicId, MonthStart = request.MonthStart,
                        InvoiceReference = publicId, InvoiceNumber = invoice.InvoiceNumber,
                        GrossAmount = subtotal, DiscountAmount = totalDiscount,
                        NetAmount = subtotal - totalDiscount, Existing = false
                    }, "Approved plan month invoiced atomically; no payment collected.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 3960 or 2601 or 2627)
        {
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentMonthInvoiceDto>("Competing invoice or duplicate request; refresh.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentMonthInvoiceDto>("Invoice changed concurrently; refresh.", 409);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Error<StudentFeeInstallmentMonthInvoiceDto>("Invoice conflicts with financial evidence.", 409);
        }
    }
}
