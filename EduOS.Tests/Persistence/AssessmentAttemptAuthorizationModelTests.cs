using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class AssessmentAttemptAuthorizationModelTests
{
    [Fact]
    public void Targeted_attempts_are_opt_in_and_a_single_subject_registration_has_one_decision()
    {
        Assert.Equal(AssessmentAttemptMode.Regular, new Assessment().AttemptMode);
        Assert.Equal(AssessmentAttemptDecision.Requested, new AssessmentAttemptRegistration().Decision);
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=localhost;Database=EduOS_ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var context = new EduOSDbContext(options);
        var entity = context.Model.FindEntityType(typeof(AssessmentAttemptRegistration));
        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(new[]
            { "TenantId", "AssessmentSubjectId", "StudentSubjectRegistrationId" }));
        foreach (var foreign in new[] { "AssessmentSubjectId", "StudentSubjectRegistrationId", "PriorResultPublicationId" })
            Assert.Contains(entity.GetForeignKeys(), fk =>
                fk.Properties.Select(x => x.Name).SequenceEqual(new[] { "TenantId", foreign }));
        Assert.NotNull(context.Model.FindEntityType(typeof(Assessment))!.FindProperty("AttemptMode"));
    }
}
