using System.ComponentModel.DataAnnotations;
using System.Reflection;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Base;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentPublicationRegisterTests
{
    [Fact]
    public void Publication_history_and_tabulation_queries_reject_unbounded_requests()
    {
        Assert.False(Valid(new AssessmentPublicationHistoryQueryDto
        { AssessmentId = 1, AcademicBatchId = 1, Page = 0 }));
        Assert.False(Valid(new AssessmentPublicationHistoryQueryDto
        { AssessmentId = 1, AcademicBatchId = 1, PageSize = 101 }));
        Assert.False(Valid(new PublishedTabulationQueryDto { PublicationId = 0 }));
        Assert.False(Valid(new PublishedTabulationQueryDto { PublicationId = 1, Search = new string('x', 101) }));
        Assert.True(Valid(new PublishedTabulationQueryDto { PublicationId = 1, PageSize = 50 }));
    }

    [Fact]
    public void Published_version_can_transition_to_withdrawn_without_changing_snapshot_fields()
    {
        ValidateChange(row =>
        {
            row.State = ResultPublicationState.Withdrawn;
            row.VisibleToStudent = false;
            row.VisibleToGuardian = false;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = 17;
        }, allowed: true);
    }

    [Fact]
    public void Published_version_cannot_be_silently_edited_or_republished()
    {
        ValidateChange(row => row.PublishNote = "Edited after publication", allowed: false);
        ValidateChange(row =>
        {
            row.State = ResultPublicationState.Withdrawn;
            row.VisibleToStudent = false;
            row.VisibleToGuardian = false;
            row.VersionNo = 2;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = 17;
        }, allowed: false);
        ValidateChange(row =>
        {
            row.State = ResultPublicationState.Withdrawn;
            row.VisibleToStudent = true;
            row.VisibleToGuardian = false;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = 17;
        }, allowed: false);
    }

    [Fact]
    public void Published_version_cannot_be_deleted()
    {
        using var db = CreateContext();
        var row = Publication();
        db.Attach(row);
        db.Entry(row).State = EntityState.Deleted;
        AssertGuardRejects(db);
    }

    [Fact]
    public void Publication_register_reads_historical_snapshots_without_recalculating_marks()
    {
        var source = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.PublicationRegister.cs");
        Assert.Contains("if (!CanPublish())", source);
        Assert.Contains("CanAccessCampusAsync(scope.CampusId, ct)", source);
        Assert.Contains("summary.ResultPublicationId == publication.Id", source);
        Assert.Contains("summary.PublicationVersionNo == publication.VersionNo", source);
        Assert.Contains("enrollment.CampusId == scope.CampusId", source);
        Assert.Contains("valid.CountAsync(ct) != snapshotCount", source);
        Assert.Contains("GetPublicationEvidenceAsync", source);
        Assert.Contains("!proof.IsReconciled", source);
        Assert.Contains("IntegrityStatus", source);
        Assert.Contains(".Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)", source);
        Assert.DoesNotContain("CalculateAsync(", source);
    }

    private static bool Valid(object dto)
    {
        var errors = new List<ValidationResult>();
        return Validator.TryValidateObject(dto, new ValidationContext(dto), errors, true);
    }

    private static EduOSDbContext CreateContext() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("assessment-publication-guard-" + Guid.NewGuid().ToString("N")).Options);

    private static ResultPublication Publication() => new()
    {
        Id = 42, TenantId = 7, AssessmentId = 10, AcademicBatchId = 20,
        VersionNo = 1, State = ResultPublicationState.Published,
        PublishedAt = DateTime.UtcNow, VisibleToStudent = true, VisibleToGuardian = true
    };

    private static void ValidateChange(Action<ResultPublication> mutate, bool allowed)
    {
        using var db = CreateContext();
        var row = Publication();
        db.Attach(row);
        mutate(row);
        db.ChangeTracker.DetectChanges();
        if (allowed) AssertGuardAllows(db);
        else AssertGuardRejects(db);
    }

    private static void AssertGuardAllows(EduOSDbContext db)
    {
        var exception = Record.Exception(() => InvokeGuard(db));
        Assert.Null(exception);
    }

    private static void AssertGuardRejects(EduOSDbContext db)
    {
        var exception = Record.Exception(() => InvokeGuard(db));
        var wrapped = Assert.IsType<TargetInvocationException>(exception);
        Assert.IsType<InvalidOperationException>(wrapped.InnerException);
    }

    private static void InvokeGuard(EduOSDbContext db)
    {
        var method = typeof(EduOSDbContext).GetMethod(
            "ValidateImmutableHistory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, new object[] { db.ChangeTracker.Entries<BaseEntity>().ToList() });
    }

    private static string Read(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
