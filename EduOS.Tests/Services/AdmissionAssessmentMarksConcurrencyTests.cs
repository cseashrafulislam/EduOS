using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionAssessmentMarksConcurrencyTests
{
    [Fact]
    public async Task Saving_marks_updates_test_audit_record_used_for_publication_conflict_detection()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("assessment-marks-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var form = new AdmissionIntakeForm { TenantId = 101, Code = "TEST", Title = "Intake" };
        db.Add(form); await db.SaveChangesAsync();
        var test = new AdmissionTest { TenantId = 101, AdmissionIntakeFormId = form.Id,
            Name = "Entry", TotalMarks = 100m, PassMarks = 40m };
        var applicant = new AdmissionApplicant { TenantId = 101, AdmissionIntakeFormId = form.Id,
            ClientRequestId = Guid.NewGuid(), ApplicationNumber = "A-001", FullName = "Applicant",
            State = AdmissionApplicantState.Submitted };
        db.AddRange(test, applicant); await db.SaveChangesAsync();
        var user = new Mock<ICurrentUserService>();
        user.Setup(x => x.IsAuthenticated).Returns(true);
        user.Setup(x => x.TenantId).Returns(101);
        user.Setup(x => x.UserId).Returns(5);
        user.Setup(x => x.IsTenantAdmin).Returns(true);
        var service = new AdmissionAssessmentService(new GenericRepository<AdmissionTest>(db),
            new GenericRepository<AdmissionResult>(db), new GenericRepository<AdmissionApplicant>(db),
            new GenericRepository<AdmissionIntakeForm>(db), db, user.Object, TimeProvider.System,
            NullLogger<AdmissionAssessmentService>.Instance);
        var saved = await service.SaveResultsAsync(test.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = applicant.Id, ObtainedMarks = 80m }]
        });
        saved.Success.Should().BeTrue(saved.Message);
        var updated = await db.Set<AdmissionTest>().SingleAsync();
        updated.UpdatedAt.Should().NotBeNull();
        updated.UpdatedBy.Should().Be(5);
    }
}
