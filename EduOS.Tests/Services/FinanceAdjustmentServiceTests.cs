using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FinanceAdjustmentServiceTests
{
    [Fact]
    public async Task Discount_rules_are_tenant_scoped()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-discounts-"+Guid.NewGuid().ToString("N")).Options,accessor);
        db.DiscountRules.AddRange(
            new DiscountRule{TenantId=11,Name="Own",Value=10m,IsPercentage=true},
            new DiscountRule{TenantId=12,Name="Foreign",Value=15m,IsPercentage=true});
        await TestTenantSeeder.SaveAsync(db);
        var result=await new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System).GetDiscountRulesAsync();
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle().Which.Name.Should().Be("Own");
    }
    [Fact]
    public async Task Non_admin_cannot_access_tenant_finance_adjustments()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-security-"+Guid.NewGuid().ToString("N")).Options,accessor);
        var service=new FinanceAdjustmentService(db,new TestUser(11,false),TimeProvider.System);
        (await service.GetFineRulesAsync()).StatusCode.Should().Be(403);
        (await service.GetDiscountRulesAsync()).StatusCode.Should().Be(403);
        (await service.AssignDiscountAsync(new AssignStudentDiscountRequestDto())).StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Invalid_discount_precision_is_rejected_before_database_write()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-precision-"+Guid.NewGuid().ToString("N")).Options,accessor);
        var service=new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System);
        var result=await service.SaveDiscountRuleAsync(null,new SaveDiscountRuleRequestDto
            {Name="Invalid precision",Value=0.00001m,IsPercentage=true});
        result.Success.Should().BeFalse();
        db.DiscountRules.Should().BeEmpty();
    }

    [Fact]
    public async Task Discount_assignment_retry_returns_original_and_reused_key_with_different_terms_conflicts()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-discount-retry-"+Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w=>w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options,accessor);
        var enrollment=new EduOS.Core.Entities.Academic.StudentEnrollment{TenantId=11,StudentId=7,CampusId=3,RollNo="D-7"};
        var rule=new DiscountRule{TenantId=11,Name="Merit",Value=10m,IsPercentage=true};
        db.StudentEnrollments.Add(enrollment);db.DiscountRules.Add(rule);
        await TestTenantSeeder.SaveAsync(db);
        var service=new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System);
        var request=new AssignStudentDiscountRequestDto{ClientRequestId=Guid.NewGuid(),
            StudentEnrollmentReference=enrollment.PublicId,DiscountRuleId=rule.Id,
            EffectiveFrom=new DateOnly(2026,10,1),Reason="Approved merit"};
        var original=await service.AssignDiscountAsync(request);
        var retry=await service.AssignDiscountAsync(request);
        var conflict=await service.AssignDiscountAsync(new AssignStudentDiscountRequestDto
        {ClientRequestId=request.ClientRequestId,StudentEnrollmentReference=enrollment.PublicId,
            DiscountRuleId=rule.Id,EffectiveFrom=request.EffectiveFrom,Reason="Changed reason"});
        rule.IsActive=false;
        await TestTenantSeeder.SaveAsync(db);
        var afterDeactivation=await service.AssignDiscountAsync(request);
        original.Success.Should().BeTrue();
        retry.Success.Should().BeTrue();
        retry.Data!.Id.Should().Be(original.Data!.Id);
        afterDeactivation.Success.Should().BeTrue();
        afterDeactivation.Data!.Id.Should().Be(original.Data.Id);
        conflict.StatusCode.Should().Be(409);
        db.StudentDiscounts.Should().ContainSingle();
        db.StudentDiscounts.Single().ClientRequestId.Should().Be(request.ClientRequestId);
    }

    [Fact]
    public async Task Fine_application_retry_does_not_record_a_second_fine()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-fine-retry-"+Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w=>w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options,accessor);
        var enrollment=new EduOS.Core.Entities.Academic.StudentEnrollment{TenantId=11,StudentId=8,CampusId=3,RollNo="F-8"};
        var rule=new FineRule{TenantId=11,Name="Late fee",Amount=50m};
        db.StudentEnrollments.Add(enrollment);db.FineRules.Add(rule);
        await TestTenantSeeder.SaveAsync(db);
        var service=new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System);
        var request=new ApplyStudentFineRequestDto{ClientRequestId=Guid.NewGuid(),
            StudentEnrollmentReference=enrollment.PublicId,FineRuleId=rule.Id,
            FineDate=new DateOnly(2026,10,1),Reason="Approved late charge"};
        var original=await service.ApplyFineAsync(request);
        var retry=await service.ApplyFineAsync(request);
        var conflict=await service.ApplyFineAsync(new ApplyStudentFineRequestDto
        {ClientRequestId=request.ClientRequestId,StudentEnrollmentReference=enrollment.PublicId,
            FineRuleId=rule.Id,FineDate=request.FineDate,Reason="Changed reason"});
        rule.IsActive=false;
        await TestTenantSeeder.SaveAsync(db);
        var afterDeactivation=await service.ApplyFineAsync(request);
        original.Success.Should().BeTrue();
        retry.Success.Should().BeTrue();
        retry.Data!.Id.Should().Be(original.Data!.Id);
        afterDeactivation.Success.Should().BeTrue();
        afterDeactivation.Data!.Id.Should().Be(original.Data.Id);
        conflict.StatusCode.Should().Be(409);
        db.StudentFines.Should().ContainSingle();
        db.StudentFines.Single().ClientRequestId.Should().Be(request.ClientRequestId);
    }

    [Fact]
    public void Refund_planner_allocates_in_stable_order_without_touching_original_amounts()
    {
        var sources=new[]{
            new RefundAllocationBalance(8,18,100m,20m,80m),
            new RefundAllocationBalance(3,13,50m,10m,40m)};
        var lines=RefundAllocationPlanner.Plan(sources,90m);
        lines.Select(x=>x.AllocationId).Should().Equal(3,8);
        lines.Select(x=>x.Amount).Should().Equal(40m,50m);
        sources[0].OriginalAmount.Should().Be(100m);
    }
    [Fact]
    public void Refund_planner_rejects_over_refund_and_invalid_precision()
    {
        var sources=new[]{new RefundAllocationBalance(1,11,50m,45m,5m)};
        Action excess=()=>RefundAllocationPlanner.Plan(sources,5.01m);
        Action precision=()=>RefundAllocationPlanner.Plan(sources,0.001m);
        excess.Should().Throw<InvalidOperationException>();
        precision.Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public void Refund_planner_respects_current_invoice_paid_capacity_and_moves_to_next_original_allocation()
    {
        var sources=new[]{
            new RefundAllocationBalance(1,11,100m,0m,20m),
            new RefundAllocationBalance(2,12,100m,10m,90m)};
        var result=RefundAllocationPlanner.Plan(sources,70m);
        result.Select(x=>x.AllocationId).Should().Equal(1,2);
        result.Select(x=>x.Amount).Should().Equal(20m,50m);
        sources[0].OriginalAmount.Should().Be(100m);
        Action excess=()=>RefundAllocationPlanner.Plan(sources,111m);
        excess.Should().Throw<InvalidOperationException>();
        Action negativePaid=()=>RefundAllocationPlanner.Plan(
            new[]{new RefundAllocationBalance(1,11,100m,0m,-1m)},1m);
        negativePaid.Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public void Refund_planner_shares_paid_balance_between_multiple_allocations_on_one_invoice()
    {
        var sources=new[]{
            new RefundAllocationBalance(1,21,100m,0m,80m),
            new RefundAllocationBalance(2,21,100m,0m,80m),
            new RefundAllocationBalance(3,22,100m,20m,30m)};
        var result=RefundAllocationPlanner.Plan(sources,105m);
        result.Select(x=>x.AllocationId).Should().Equal(1,3);
        result.Select(x=>x.Amount).Should().Equal(80m,25m);
        Action excess=()=>RefundAllocationPlanner.Plan(sources,111m);
        excess.Should().Throw<InvalidOperationException>();
        Action conflictingSnapshot=()=>RefundAllocationPlanner.Plan(new[]{
            new RefundAllocationBalance(1,21,100m,0m,80m),
            new RefundAllocationBalance(2,21,100m,0m,100m)},10m);
        conflictingSnapshot.Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public async Task Refund_requests_reserve_only_current_paid_capacity_and_prevent_overbooking()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-refund-reservations-"+Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w=>w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options,accessor);
        var invoice=new StudentInvoice{TenantId=11,InvoiceNumber="INV-RESERVE-1",
            State=EduOS.Core.Enums.Domain.InvoiceState.PartiallyPaid,
            TotalAmount=200m,PaidAmount=50m,DueAmount=150m};
        var payment=new StudentPayment{TenantId=11,ReceiptNumber="RCP-RESERVE-1",
            Amount=100m,State=EduOS.Core.Enums.Domain.PaymentState.Successful};
        db.StudentInvoices.Add(invoice);db.StudentPayments.Add(payment);
        await TestTenantSeeder.SaveAsync(db);
        db.PaymentAllocations.Add(new PaymentAllocation{TenantId=11,
            StudentInvoiceId=invoice.Id,StudentPaymentId=payment.Id,Amount=100m});
        await TestTenantSeeder.SaveAsync(db);
        var service=new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System);
        Task<EduOS.Core.Common.ApiResponse<RefundDto>> Request(decimal amount)=>
            service.RequestRefundAsync(new CreateRefundRequestDto{ClientRequestId=Guid.NewGuid(),
                StudentPaymentReference=payment.PublicId,Amount=amount,Reason="Verified overpayment"});
        (await Request(60m)).StatusCode.Should().Be(409);
        (await Request(30m)).Success.Should().BeTrue();
        (await Request(25m)).StatusCode.Should().Be(409);
        (await Request(20m)).Success.Should().BeTrue();
        db.Refunds.Should().HaveCount(2);
        db.Refunds.Sum(x=>x.Amount).Should().Be(50m);
        invoice.PaidAmount.Should().Be(50m);
        db.RefundAllocations.Should().BeEmpty();
    }
    [Fact]
    public void Paid_invoices_cannot_be_mutated_by_refund_workflow()
    {
        EduOS.Service.Services.Finance.RefundInvoicePolicy.CanAdjust(
            EduOS.Core.Enums.Domain.InvoiceState.Paid).Should().BeFalse();
        EduOS.Service.Services.Finance.RefundInvoicePolicy.CanAdjust(
            EduOS.Core.Enums.Domain.InvoiceState.PartiallyPaid).Should().BeTrue();
    }
    [Fact]
    public async Task Refund_review_and_payout_require_institution_admin()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-refund-security-"+Guid.NewGuid().ToString("N")).Options,accessor);
        var service=new FinanceAdjustmentService(db,new TestUser(11,false),TimeProvider.System);
        (await service.GetRefundRegisterAsync(null)).StatusCode.Should().Be(403);
        (await service.ReviewRefundAsync(1,new ReviewRefundRequestDto())).StatusCode.Should().Be(403);
        (await service.RecordRefundDisbursementAsync(1,new RecordRefundDisbursementRequestDto())).StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Invalid_refund_payout_never_writes_a_journal()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        accessor.HttpContext.Items["TenantId"]=11L;
        await using var db=new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-refund-validation-"+Guid.NewGuid().ToString("N")).Options,accessor);
        var service=new FinanceAdjustmentService(db,new TestUser(11,true),TimeProvider.System);
        var response=await service.RecordRefundDisbursementAsync(1,new RecordRefundDisbursementRequestDto
            {ClientRequestId=Guid.NewGuid(),PayoutEvidenceReference="",AccountingPeriodId=1,
             PayoutDate=new DateOnly(2026,10,25),RowVersion="invalid"});
        response.Success.Should().BeFalse();
        db.Journals.Should().BeEmpty();
        db.RefundAllocations.Should().BeEmpty();
    }
    [Fact]
    public async Task Legacy_soft_deleted_posted_invoice_blocks_discount_but_new_fine_assessment_is_separate()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 11L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-historical-invoice-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
        var enrollment = new EduOS.Core.Entities.Academic.StudentEnrollment
        {
            TenantId = 11, StudentId = 5, CampusId = 7, RollNo = "T-5"
        };
        var discount = new DiscountRule { TenantId = 11, Name = "Historical discount", Value = 10m, IsPercentage = true };
        var fine = new FineRule { TenantId = 11, Name = "Historical fine", Amount = 10m };
        db.StudentEnrollments.Add(enrollment);
        db.DiscountRules.Add(discount);
        db.FineRules.Add(fine);
        await TestTenantSeeder.SaveAsync(db);
        db.StudentInvoices.Add(new StudentInvoice
        {
            TenantId = 11, StudentEnrollmentId = enrollment.Id,
            InvoiceNumber = "HIST-POSTED-1", InvoiceDate = new DateOnly(2026, 10, 20),
            DueDate = new DateOnly(2026, 10, 31),
            State = EduOS.Core.Enums.Domain.InvoiceState.Cancelled,
            JournalId = 123, IsDeleted = true
        });
        await TestTenantSeeder.SaveAsync(db);

        var service = new FinanceAdjustmentService(db, new TestUser(11, true), TimeProvider.System);
        var discountResult = await service.AssignDiscountAsync(new AssignStudentDiscountRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = enrollment.PublicId,
            DiscountRuleId = discount.Id, EffectiveFrom = new DateOnly(2026, 10, 1),
            Reason = "Historical posting must remain immutable"
        });
        var fineResult = await service.ApplyFineAsync(new ApplyStudentFineRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = enrollment.PublicId,
            FineRuleId = fine.Id, FineDate = new DateOnly(2026, 10, 1),
            Reason = "Historical posting must remain immutable"
        });
        discountResult.StatusCode.Should().Be(409);
        fineResult.Success.Should().BeTrue();
        db.StudentDiscounts.Should().BeEmpty();
        db.StudentFines.Should().ContainSingle();
        db.StudentInvoices.IgnoreQueryFilters().Should().ContainSingle();
        db.Journals.Should().BeEmpty();
    }

    private sealed class TestUser(long tenant,bool admin):ICurrentUserService
    {
        public bool IsAuthenticated=>true;
        public long UserId=>99;
        public long TenantId=>tenant;
        public string? FullName=>"Test";
        public string? Email=>null;
        public bool IsSuperAdmin=>false;
        public bool IsTenantAdmin=>admin;
        public IReadOnlyList<string> Roles=>admin?["TenantAdmin"]:["Staff"];
        public bool IsInRole(string role)=>Roles.Contains(role);
        public string? IpAddress=>null;
        public string? UserAgent=>null;
    }
}
