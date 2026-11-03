using System.Security.Claims;
using System.Security.Cryptography;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Research;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerResearchAccreditationTests
{
    [Fact]
    public async Task Grants_and_accreditation_enforce_independent_decisions_replay_and_real_sql_versions()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, campusId, programId;
        var identities = new long[7]; // Researcher, grant checker, framework maker, publisher, three reviewers.
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-RAC-" + suffix, Name = "Research CI",
                Email = "research-" + suffix + "@example.test", State = TenantState.Active };
            var users = Enumerable.Range(0, 7).Select(i =>
            {
                var mail = "rac-" + i + "-" + suffix + "@example.test";
                return new ApplicationUser
                {
                    UserName = mail, NormalizedUserName = mail.ToUpperInvariant(), Email = mail,
                    NormalizedEmail = mail.ToUpperInvariant(), FullName = "Research CI " + i,
                    EmailConfirmed = true, IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                };
            }).ToArray();
            setup.Tenants.Add(tenant);
            setup.Users.AddRange(users);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            for (var i = 0; i < users.Length; i++) identities[i] = users[i].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "RAC", Name = "Research Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var program = new AcademicProgram { TenantId = tenantId, Code = "RES", Name = "Research academic program", IsActive = true };
                setup.AcademicPrograms.Add(program);
                await setup.SaveChangesAsync();
                programId = program.Id;
                setup.ProgramCampuses.Add(new ProgramCampus { TenantId = tenantId, CampusId = campusId,
                    AcademicProgramId = programId, IsActive = true });
                await setup.SaveChangesAsync();
                var codes = new[] { "Teacher", "ResearchCoordinator", "AccreditationCoordinator",
                    "AccreditationCoordinator", "AccreditationReviewer", "AccreditationReviewer", "AccreditationReviewer" };
                var roles = new Dictionary<string, ApplicationRole>();
                foreach (var code in codes.Distinct())
                    roles.Add(code, new ApplicationRole
                    {
                        TenantId = tenantId, Name = code, NormalizedName = code.ToUpperInvariant(),
                        IsActive = true
                    });
                setup.Roles.AddRange(roles.Values);
                foreach (var id in identities)
                {
                    setup.TenantMemberships.Add(new TenantMembership
                    { TenantId = tenantId, UserId = id, Status = MembershipStatus.Active });
                    setup.UserCampusAccesses.Add(new UserCampusAccess
                    { TenantId = tenantId, CampusId = campusId, UserId = id, IsActive = true });
                }
                await setup.SaveChangesAsync();
                for (var i = 0; i < identities.Length; i++)
                    setup.UserRoles.Add(new IdentityUserRole<long>
                    { UserId = identities[i], RoleId = roles[codes[i]].Id });
                await setup.SaveChangesAsync();
            }
        }

        var proposalRef = Guid.NewGuid();
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var proposalCreate = Guid.NewGuid();
            await ownerDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.ResearchProposals
                (TenantId, PublicId, CampusId, CreateRequestId, OwnerUserId, Title,
                 Abstract, Protocol, ContentSha256, CurrentRevision, StateCode, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({tenantId}, {proposalRef}, {campusId}, {proposalCreate}, {identities[0]},
                        {"Accepted independent ethics protocol"},
                        {"Research methodology approved by independent ethics reviewers."},
                        {"Approved protocol with valid consent and independent review."},
                        {new string('A', 64)}, {1}, {"EthicsApproved"},
                        {DateTime.UtcNow}, {DateTime.UtcNow})
                """);
        }

        long schemeId;
        await using (var grantChecker = Db(options, tenantId, identities[1]))
        {
            var scheme = await Grant(grantChecker, tenantId, identities[1]).CreateSchemeAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), Code = "LOCAL_" + suffix.ToUpperInvariant(),
                Name = "Research funding " + suffix, CurrencyCode = "BDT", MaxAward = 100000m
            });
            Assert.True(scheme.Success, scheme.Message);
            schemeId = scheme.Data!.Id;
        }
        ResearchGrantDto grant;
        ResearchGrantOutputDto grantOutput;
        ResearchGrantOutputDto rejectedOutput;
        long verifiedMilestone;
        var grantCreateRequest = Guid.NewGuid();
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Grant(ownerDb, tenantId, identities[0]);
            var request = new ResearchGrantCreateDto
            {
                ClientRequestId = grantCreateRequest, ProposalReference = proposalRef,
                SchemeId = schemeId, Title = "Funded ethics-approved research", RequestedAmount = 50000m
            };
            var created = await service.CreateAsync(request);
            Assert.True(created.Success, created.Message);
            grant = created.Data!;
            var replay = await service.CreateAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(grant.Reference, replay.Data!.Reference);
            var cannotAward = await service.CommandAsync(grant.Reference, "award", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion, Amount = 40000m,
                Reason = "An independent award decision." });
            Assert.False(cannotAward.Success);
            var submitted = await service.CommandAsync(grant.Reference, "submit", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            grant = submitted.Data!;
        }
        await using (var checkerDb = Db(options, tenantId, identities[1]))
        {
            var service = Grant(checkerDb, tenantId, identities[1]);
            var awarded = await service.CommandAsync(grant.Reference, "award", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion, Amount = 40000m,
                Reason = "Independent committee approved this award." });
            Assert.True(awarded.Success, awarded.Message);
            grant = awarded.Data!;
            Assert.Equal("Active", grant.StateCode);
            Assert.Equal(1, grant.BudgetVersion);
            var finance = Quality(checkerDb, tenantId, identities[1]);
            var report = await finance.FinanceReportAsync(grant.Reference);
            Assert.True(report.Success, report.Message);
            Assert.Equal(40000m, report.Data!.ApprovedBudget);
            Assert.Equal(0, report.Data!.LinkedJournals);
            Assert.Equal(40000m, report.Data!.AvailableBudget);

            var period = new AccountingPeriod
            { TenantId = tenantId, Name = "Research fiscal period", StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2027, 12, 31) };
            var researchExpense = new Account { TenantId = tenantId, Code = "RES_EXP_" + suffix,
                Name = "Research grant expense", AccountType = AccountType.Expense };
            var cash = new Account { TenantId = tenantId, Code = "RES_CASH_" + suffix,
                Name = "Research grant cash", AccountType = AccountType.Asset };
            checkerDb.AccountingPeriods.Add(period);
            checkerDb.Accounts.AddRange(researchExpense, cash);
            await checkerDb.SaveChangesAsync();
            var journal = new Journal
            { TenantId = tenantId, AccountingPeriodId = period.Id, JournalNumber = "RAC-J-" + suffix,
                JournalDate = DateOnly.FromDateTime(DateTime.UtcNow), State = JournalState.Posted,
                SourceType = "Manual", PostedAt = DateTime.UtcNow, CurrencyCode = "BDT" };
            checkerDb.Journals.Add(journal);
            await checkerDb.SaveChangesAsync();
            checkerDb.JournalLines.AddRange(
                new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 1,
                    AccountId = researchExpense.Id, DebitAmount = 4000m },
                new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 2,
                    AccountId = cash.Id, CreditAmount = 4000m });
            await checkerDb.SaveChangesAsync();
            var mapped = await finance.ConfigureFinanceAccountAsync(new()
            { ClientRequestId = Guid.NewGuid(), SchemeId = schemeId, AccountId = researchExpense.Id,
                FlowCode = "Expense", NormalSide = "Debit" });
            Assert.True(mapped.Success, mapped.Message);
            var linkedRequest = new GrantJournalLinkCreateDto
            { ClientRequestId = Guid.NewGuid(), JournalId = journal.Id };
            Assert.True((await finance.LinkJournalAsync(grant.Reference, linkedRequest)).Success);
            Assert.True((await finance.LinkJournalAsync(grant.Reference, linkedRequest)).Success);
            var withExpense = await finance.FinanceReportAsync(grant.Reference);
            Assert.True(withExpense.Success, withExpense.Message);
            Assert.Equal(4000m, withExpense.Data!.Expenses);
            Assert.Equal(36000m, withExpense.Data!.AvailableBudget);

            // Posted originals are immutable; a separate posted contra journal reverses their net GL effect.
            var reversal = new Journal
            { TenantId = tenantId, AccountingPeriodId = period.Id, JournalNumber = "RAC-R-" + suffix,
                JournalDate = DateOnly.FromDateTime(DateTime.UtcNow), State = JournalState.Posted,
                SourceType = "ManualReversal", SourceId = journal.Id, ReversalOfJournalId = journal.Id,
                PostedAt = DateTime.UtcNow, CurrencyCode = "BDT" };
            checkerDb.Journals.Add(reversal);
            await checkerDb.SaveChangesAsync();
            checkerDb.JournalLines.AddRange(
                new JournalLine { TenantId = tenantId, JournalId = reversal.Id, LineNo = 1,
                    AccountId = researchExpense.Id, CreditAmount = 4000m },
                new JournalLine { TenantId = tenantId, JournalId = reversal.Id, LineNo = 2,
                    AccountId = cash.Id, DebitAmount = 4000m });
            await checkerDb.SaveChangesAsync();
            var reversedReport = await finance.FinanceReportAsync(grant.Reference);
            Assert.True(reversedReport.Success, reversedReport.Message);
            Assert.Equal(0m, reversedReport.Data!.Expenses);
            Assert.Equal(40000m, reversedReport.Data!.AvailableBudget);
        }
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Grant(ownerDb, tenantId, identities[0]);
            var milestone = await service.CommandAsync(grant.Reference, "milestone-add", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                MilestoneTitle = "Publish final outcome", DueDate = DateTime.UtcNow.Date.AddDays(45) });
            Assert.True(milestone.Success, milestone.Message);
            grant = milestone.Data!;
            var list = await service.MilestonesAsync(grant.Reference);
            Assert.Single(list.Data!);
            verifiedMilestone = list.Data![0].Id;
            var complete = await service.CommandAsync(grant.Reference, "milestone-complete", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                MilestoneId = list.Data![0].Id, Outcome = "Published and verified final project outcome." });
            Assert.True(complete.Success, complete.Message);
            grant = complete.Data!;
            var outputs = Outputs(ownerDb, tenantId, identities[0]);
            var outputRequest = new ResearchGrantOutputSubmitDto
            {
                ClientRequestId = Guid.NewGuid(), KindCode = "Publication",
                Title = "Independently validated milk supply study",
                Summary = "A peer-reviewed research outcome was published with a verifiable public DOI and linked to its completed milestone.",
                ReferenceUrl = "https://doi.org/10.1234/research.acceptance", MilestoneId = verifiedMilestone,
                OccurredOn = DateTime.UtcNow.Date
            };
            var published = await outputs.SubmitAsync(grant.Reference, outputRequest);
            Assert.True(published.Success, published.Message);
            grantOutput = published.Data!;
            Assert.Equal("Submitted", grantOutput.StateCode);
            var duplicate = await outputs.SubmitAsync(grant.Reference, outputRequest);
            Assert.Equal(grantOutput.Reference, duplicate.Data!.Reference);
            var wrongMilestone = await outputs.SubmitAsync(grant.Reference, new()
            { ClientRequestId = Guid.NewGuid(), KindCode = "Dataset",
                Title = "Unrelated evidence dataset",
                Summary = "The submitted dataset is attached to a milestone that does not belong to this grant.",
                MilestoneId = verifiedMilestone + 100000, OccurredOn = DateTime.UtcNow.Date });
            Assert.False(wrongMilestone.Success);
            var second = await outputs.SubmitAsync(grant.Reference, new()
            { ClientRequestId = Guid.NewGuid(), KindCode = "Dataset",
                Title = "Research dataset requiring correction",
                Summary = "This research dataset awaits independent validation and may need to be corrected and resubmitted.",
                ReferenceUrl = "https://example.org/research/dataset", OccurredOn = DateTime.UtcNow.Date });
            Assert.True(second.Success, second.Message);
            rejectedOutput = second.Data!;
            var ownerReview = await outputs.ReviewAsync(grant.Reference, grantOutput.Reference, new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grantOutput.RowVersion,
                Decision = "verify", Reason = "Independently reviewed publication evidence.", DeclaresNoConflict = true });
            Assert.False(ownerReview.Success);
            var closureBlocked = await service.CommandAsync(grant.Reference, "request-close", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                Reason = "All approved deliverables have been completed." });
            Assert.Equal(409, closureBlocked.StatusCode);
        }
        await using (var checkerDb = Db(options, tenantId, identities[1]))
        {
            var outputs = Outputs(checkerDb, tenantId, identities[1]);
            var request = new ResearchGrantOutputReviewDto
            { ClientRequestId = Guid.NewGuid(), RowVersion = grantOutput.RowVersion,
                Decision = "verify", Reason = "The publication is independently verified against the completed project milestone.",
                DeclaresNoConflict = true };
            var review = await outputs.ReviewAsync(grant.Reference, grantOutput.Reference, request);
            Assert.True(review.Success, review.Message);
            Assert.Equal("Verified", review.Data!.StateCode);
            var replay = await outputs.ReviewAsync(grant.Reference, grantOutput.Reference, request);
            Assert.True(replay.Success, replay.Message);
            var doubleReview = await outputs.ReviewAsync(grant.Reference, grantOutput.Reference, new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = grantOutput.RowVersion,
                Decision = "reject", Reason = "Conflicting independent outcome decision.", DeclaresNoConflict = true });
            Assert.Equal(409, doubleReview.StatusCode);
            var rejected = await outputs.ReviewAsync(grant.Reference, rejectedOutput.Reference, new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = rejectedOutput.RowVersion,
                Decision = "reject", Reason = "The dataset has not met independent reproducibility checks.",
                DeclaresNoConflict = true });
            Assert.True(rejected.Success, rejected.Message);
            Assert.Equal("Rejected", rejected.Data!.StateCode);
            var report = await outputs.ReportAsync(campusId,
                DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));
            Assert.True(report.Success, report.Message);
            Assert.Contains(report.Data!, x => x.KindCode == "Publication" && x.Verified == 1);
            Assert.Contains(report.Data!, x => x.KindCode == "Dataset" && x.Rejected == 1);
        }
        await using (var unauthorizedDb = Db(options, tenantId, identities[2]))
        {
            var hidden = await Outputs(unauthorizedDb, tenantId, identities[2]).ListAsync(grant.Reference);
            Assert.False(hidden.Success);
        }
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var closure = await Grant(ownerDb, tenantId, identities[0]).CommandAsync(
                grant.Reference, "request-close", new()
                { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                    Reason = "All outcomes independently checked and all milestones completed." });
            Assert.True(closure.Success, closure.Message);
            grant = closure.Data!;
        }
        await using (var checkerDb = Db(options, tenantId, identities[1]))
        {
            var closed = await Grant(checkerDb, tenantId, identities[1]).CommandAsync(
                grant.Reference, "close", new()
                { ClientRequestId = Guid.NewGuid(), RowVersion = grant.RowVersion,
                    Reason = "Independently verified final milestones." });
            Assert.True(closed.Success, closed.Message);
            Assert.Equal("Closed", closed.Data!.StateCode);
        }

        AccreditationFrameworkDto framework;
        AccreditationStandardDto quorumStandard;
        await using (var makerDb = Db(options, tenantId, identities[2]))
        {
            var outcomes = Quality(makerDb, tenantId, identities[2]);
            var draft = new ProgramOutcomeCreateDto
            {
                ClientRequestId = Guid.NewGuid(), AcademicProgramId = programId,
                Code = "PLO_COMM", VersionNo = 1,
                Statement = "Students demonstrate and communicate applied research methods."
            };
            var outcome = await outcomes.CreateOutcomeAsync(draft);
            Assert.True(outcome.Success, outcome.Message);
            var repeated = await outcomes.CreateOutcomeAsync(draft);
            Assert.True(repeated.Success, repeated.Message);
            Assert.Equal(outcome.Data!.Id, repeated.Data!.Id);
            Assert.Single((await outcomes.OutcomesAsync(campusId, programId)).Data!);
            var service = Accreditation(makerDb, tenantId, identities[2]);
            var created = await service.CreateFrameworkAsync(new()
            { ClientRequestId = Guid.NewGuid(), Code = "QA_" + suffix.ToUpperInvariant(),
                Name = "Quality standard version one", VersionNo = 1 });
            Assert.True(created.Success, created.Message);
            framework = created.Data!;
            var standard = await service.AddStandardAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), FrameworkId = framework.Id,
                Code = "STD_1", Title = "Research quality assessment",
                Rubric = "Independently validated research deliverables.",
                RequiredEvidence = "Approved evidence and compliance report.", ValidityDays = 365
            });
            Assert.True(standard.Success, standard.Message);
            var panel = await service.AddStandardAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), FrameworkId = framework.Id,
                Code = "STD_PANEL", Title = "Independent research review panel",
                Rubric = "Two independent reviewers must unanimously approve submitted evidence.",
                RequiredEvidence = "Audited independent research records and supporting documents.",
                ValidityDays = 180, RequiredReviewerCount = 2, RequiresOutcomeMapping = true
            });
            Assert.True(panel.Success, panel.Message);
            quorumStandard = panel.Data!;
            var mapped = await outcomes.MapOutcomeAsync(new()
            { ClientRequestId = Guid.NewGuid(), StandardId = quorumStandard.Id, OutcomeId = outcome.Data!.Id });
            Assert.True(mapped.Success, mapped.Message);
            Assert.Single((await outcomes.StandardOutcomesAsync(quorumStandard.Id)).Data!);
            var ownPublish = await service.PublishFrameworkAsync(framework.Id, new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = framework.RowVersion });
            Assert.False(ownPublish.Success);
        }
        await using (var publisherDb = Db(options, tenantId, identities[3]))
        {
            var published = await Accreditation(publisherDb, tenantId, identities[3])
                .PublishFrameworkAsync(framework.Id, new()
                { ClientRequestId = Guid.NewGuid(), RowVersion = framework.RowVersion });
            Assert.True(published.Success, published.Message);
            Assert.Equal("Published", published.Data!.StateCode);
            var noLateMapping = await Quality(publisherDb, tenantId, identities[3]).MapOutcomeAsync(new()
            { ClientRequestId = Guid.NewGuid(), StandardId = quorumStandard.Id, OutcomeId = 1 });
            Assert.False(noLateMapping.Success);
        }
        AccreditationAssessmentDto assessment;
        var privateStorage = new FakeEvidenceStorage(tenantId);
        var uploadBytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nControlled test evidence");
        Guid fileReference;
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Accreditation(ownerDb, tenantId, identities[0]);
            var standards = await service.StandardsAsync(framework.Id);
            Assert.Equal(2, standards.Data!.Count);
            var created = await service.CreateAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                StandardId = standards.Data!.Single(x => x.Code == "STD_1").Id, PeriodStart = DateTime.UtcNow.Date.AddDays(-30),
                PeriodEnd = DateTime.UtcNow.Date,
                EvidenceSummary = "Verified research and teaching outcomes with independent records."
            });
            Assert.True(created.Success, created.Message);
            assessment = created.Data!;
            var files = Evidence(ownerDb, tenantId, identities[0], privateStorage);
            var failedId = Guid.NewGuid();
            privateStorage.FailScan = true;
            var failed = await files.UploadAsync("accreditation", assessment.Reference, failedId,
                Upload(uploadBytes));
            Assert.False(failed.Success);
            Assert.Empty((await files.ListAsync("accreditation", assessment.Reference)).Data!.Items);
            privateStorage.FailScan = false;
            fileReference = Guid.NewGuid();
            var saved = await files.UploadAsync("accreditation", assessment.Reference, fileReference,
                Upload(uploadBytes));
            Assert.True(saved.Success, saved.Message);
            Assert.Equal(1, saved.Data!.RevisionNo);
            var retry = await files.UploadAsync("accreditation", assessment.Reference, fileReference,
                Upload(uploadBytes));
            Assert.True(retry.Success, retry.Message);
            Assert.Equal(fileReference, retry.Data!.Reference);
            var attached = await files.ListAsync("accreditation", assessment.Reference);
            Assert.Single(attached.Data!.Items);
            Assert.Equal(fileReference, attached.Data!.Items[0].Reference);
            var submitted = await service.CommandAsync(assessment.Reference, "submit", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = assessment.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            assessment = submitted.Data!;
            Assert.Equal(1, assessment.CurrentRevision);
        }
        await using (var coordinatorDb = Db(options, tenantId, identities[2]))
        {
            var service = Accreditation(coordinatorDb, tenantId, identities[2]);
            var assigned = await service.CommandAsync(assessment.Reference, "assign", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = assessment.RowVersion,
                ReviewerUserId = identities[4] });
            Assert.True(assigned.Success, assigned.Message);
            assessment = assigned.Data!;
            Assert.Null(assessment.EvidenceSummary); // Coordinator cannot read private evidence.
            var confidential = await Evidence(coordinatorDb, tenantId, identities[2], privateStorage)
                .ListAsync("accreditation", assessment.Reference);
            Assert.False(confidential.Success);
        }
        await using (var reviewerDb = Db(options, tenantId, identities[4]))
        {
            var service = Accreditation(reviewerDb, tenantId, identities[4]);
            var detail = await service.DetailAsync(assessment.Reference);
            Assert.NotNull(detail.Data!.EvidenceSummary);
            var files = Evidence(reviewerDb, tenantId, identities[4], privateStorage);
            var download = await files.DownloadAsync("accreditation", assessment.Reference, fileReference);
            Assert.True(download.Success, download.Message);
            Assert.Equal(uploadBytes, download.Data!.Content);
            privateStorage.Tamper = true;
            var tampered = await files.DownloadAsync("accreditation", assessment.Reference, fileReference);
            Assert.Equal(409, tampered.StatusCode);
            privateStorage.Tamper = false;
            var foreignPurpose = await files.DownloadAsync("proposal", assessment.Reference, fileReference);
            Assert.False(foreignPurpose.Success);
            var approved = await service.CommandAsync(assessment.Reference, "approve", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = assessment.RowVersion,
                Reason = "The independent evidence meets the quality rubric.",
                DeclaresNoConflict = true });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Approved", approved.Data!.StateCode);
        }
        await using (var revokeDb = Db(options, tenantId, identities[4]))
        {
            var access = await revokeDb.UserCampusAccesses.SingleAsync(x =>
                x.TenantId == tenantId && x.UserId == identities[4] && x.CampusId == campusId);
            access.IsActive = false;
            await revokeDb.SaveChangesAsync();
            var revoked = await Evidence(revokeDb, tenantId, identities[4], privateStorage)
                .DownloadAsync("accreditation", assessment.Reference, fileReference);
            Assert.False(revoked.Success);
        }
        await using (var reportDb = Db(options, tenantId, identities[2]))
        {
            var service = Accreditation(reportDb, tenantId, identities[2]);
            var report = await service.ReportAsync(campusId,
                DateTime.UtcNow.Date.AddDays(-60), DateTime.UtcNow.Date.AddDays(1));
            Assert.True(report.Success, report.Message);
            Assert.Single(report.Data!);
            Assert.Equal(1, report.Data![0].Approved);
        }

        // Panel: exactly two distinct eligible reviewers, immutable individual votes, no early approval.
        AccreditationAssessmentDto panelAssessment;
        Guid panelFile;
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var service = Accreditation(ownerDb, tenantId, identities[0]);
            var created = await service.CreateAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId, AcademicProgramId = programId,
                StandardId = quorumStandard.Id,
                PeriodStart = DateTime.UtcNow.Date.AddDays(-20), PeriodEnd = DateTime.UtcNow.Date,
                EvidenceSummary = "The independent audit evidence covers all program learning outcomes."
            });
            Assert.True(created.Success, created.Message);
            panelAssessment = created.Data!;
            Assert.Equal(2, panelAssessment.RequiredReviewerCount);
            panelFile = Guid.NewGuid();
            var uploaded = await Evidence(ownerDb, tenantId, identities[0], privateStorage)
                .UploadAsync("accreditation", panelAssessment.Reference, panelFile, Upload(uploadBytes));
            Assert.True(uploaded.Success, uploaded.Message);
            var submitted = await service.CommandAsync(panelAssessment.Reference, "submit", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = panelAssessment.RowVersion });
            Assert.True(submitted.Success, submitted.Message);
            panelAssessment = submitted.Data!;
        }
        await using (var coordinatorDb = Db(options, tenantId, identities[2]))
        {
            var service = Accreditation(coordinatorDb, tenantId, identities[2]);
            var invalid = await service.CommandAsync(panelAssessment.Reference, "assign", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = panelAssessment.RowVersion,
                ReviewerUserIds = [identities[5], identities[5]] });
            Assert.False(invalid.Success);
            var assignedId = Guid.NewGuid();
            var assigned = await service.CommandAsync(panelAssessment.Reference, "assign", new()
            { ClientRequestId = assignedId, RowVersion = panelAssessment.RowVersion,
                ReviewerUserIds = [identities[5], identities[6]] });
            Assert.True(assigned.Success, assigned.Message);
            panelAssessment = assigned.Data!;
            Assert.Equal(2, panelAssessment.AssignedReviewerCount);
            Assert.Equal(0, panelAssessment.ApprovalsReceived);
            Assert.Null(panelAssessment.EvidenceSummary);
            var retry = await service.CommandAsync(panelAssessment.Reference, "assign", new()
            { ClientRequestId = assignedId, RowVersion = panelAssessment.RowVersion,
                ReviewerUserIds = [identities[5], identities[6]] });
            Assert.False(retry.Success); // A different concurrency token cannot reuse the request ID.
        }
        var firstVote = Guid.NewGuid();
        var firstVersion = panelAssessment.RowVersion;
        await using (var firstDb = Db(options, tenantId, identities[5]))
        {
            var service = Accreditation(firstDb, tenantId, identities[5]);
            var visible = await service.DetailAsync(panelAssessment.Reference);
            Assert.True(visible.Success, visible.Message);
            Assert.Contains("approve", visible.Data!.AllowedActions);
            var doc = await Evidence(firstDb, tenantId, identities[5], privateStorage)
                .DownloadAsync("accreditation", panelAssessment.Reference, panelFile);
            Assert.True(doc.Success, doc.Message);
            var voted = await service.CommandAsync(panelAssessment.Reference, "approve", new()
            { ClientRequestId = firstVote, RowVersion = firstVersion,
                Reason = "Independent reviewer one confirms complete rubric evidence.", DeclaresNoConflict = true });
            Assert.True(voted.Success, voted.Message);
            panelAssessment = voted.Data!;
            Assert.Equal("Assigned", panelAssessment.StateCode);
            Assert.Equal(1, panelAssessment.ApprovalsReceived);
            Assert.DoesNotContain("approve", panelAssessment.AllowedActions);
            Assert.Null(panelAssessment.ApprovedAtUtc);
            var retry = await service.CommandAsync(panelAssessment.Reference, "approve", new()
            { ClientRequestId = firstVote, RowVersion = firstVersion,
                Reason = "Independent reviewer one confirms complete rubric evidence.", DeclaresNoConflict = true });
            Assert.True(retry.Success, retry.Message);
            Assert.Equal(1, retry.Data!.ApprovalsReceived);
        }
        await using (var secondDb = Db(options, tenantId, identities[6]))
        {
            var service = Accreditation(secondDb, tenantId, identities[6]);
            var stale = await service.CommandAsync(panelAssessment.Reference, "approve", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = firstVersion,
                Reason = "Second independent reviewer confirms the audit evidence.", DeclaresNoConflict = true });
            Assert.Equal(409, stale.StatusCode);
            var approved = await service.CommandAsync(panelAssessment.Reference, "approve", new()
            { ClientRequestId = Guid.NewGuid(), RowVersion = panelAssessment.RowVersion,
                Reason = "Second independent reviewer confirms the audit evidence.", DeclaresNoConflict = true });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Approved", approved.Data!.StateCode);
            Assert.Equal(2, approved.Data!.ApprovalsReceived);
            Assert.NotNull(approved.Data!.ApprovedAtUtc);
        }
        await using (var coordinatorDb = Db(options, tenantId, identities[2]))
        {
            var gaps = await Accreditation(coordinatorDb, tenantId, identities[2]).ComplianceGapsAsync(
                campusId, programId, DateTime.UtcNow.Date, 60);
            Assert.True(gaps.Success, gaps.Message);
            var row = Assert.Single(gaps.Data!, x => x.StandardCode == "STD_PANEL");
            Assert.Equal("Current", row.StatusCode);
            Assert.True(row.RequiresOutcomeMapping);
            Assert.Equal(1, row.MappedOutcomes);
            var denied = await Accreditation(coordinatorDb, tenantId, identities[2]).ComplianceGapsAsync(
                campusId + 100000, programId, DateTime.UtcNow.Date, 60);
            Assert.False(denied.Success);
        }

        var rejectedProposal = Guid.NewGuid();
        var rejectedFile = Guid.NewGuid();
        await using (var ownerDb = Db(options, tenantId, identities[0]))
        {
            var requestId = Guid.NewGuid();
            await ownerDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.ResearchProposals
                (TenantId, PublicId, CampusId, CreateRequestId, OwnerUserId, Title,
                 Abstract, Protocol, ContentSha256, CurrentRevision, StateCode, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({tenantId}, {rejectedProposal}, {campusId}, {requestId}, {identities[0]},
                        {"Revised independent research ethics submission"},
                        {"Approval was withheld pending additional supporting documents."},
                        {"Updated protocol and supporting safety assessment for review."},
                        {new string('B', 64)}, {1}, {"EthicsRejected"},
                        {DateTime.UtcNow}, {DateTime.UtcNow})
                """);
            var files = Evidence(ownerDb, tenantId, identities[0], privateStorage);
            var uploaded = await files.UploadAsync("proposal", rejectedProposal, rejectedFile, Upload(uploadBytes));
            Assert.True(uploaded.Success, uploaded.Message);
            Assert.Equal(2, uploaded.Data!.RevisionNo);
            Assert.Equal(rejectedFile, (await files.ListAsync("proposal", rejectedProposal)).Data!.Items.Single().Reference);
            var frozen = await files.UploadAsync("proposal", proposalRef, Guid.NewGuid(), Upload(uploadBytes));
            Assert.False(frozen.Success); // Ethics-approved evidence cannot be silently amended.
        }
        await using (var coordinatorDb = Db(options, tenantId, identities[1]))
        {
            var hidden = await Evidence(coordinatorDb, tenantId, identities[1], privateStorage)
                .DownloadAsync("proposal", rejectedProposal, rejectedFile);
            Assert.False(hidden.Success); // Coordinators cannot fetch confidential future revisions.
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options, long tenant, long actor)
    {
        var http = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static ResearchGrantService Grant(EduOSDbContext db, long tenant, long actor) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), TimeProvider.System,
            NullLogger<ResearchGrantService>.Instance);
    private static ResearchQualityExtensionService Quality(EduOSDbContext db, long tenant, long actor) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), TimeProvider.System,
            NullLogger<ResearchQualityExtensionService>.Instance);
    private static AccreditationService Accreditation(EduOSDbContext db, long tenant, long actor) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), TimeProvider.System,
            NullLogger<AccreditationService>.Instance);
    private static ResearchEvidenceFileService Evidence(EduOSDbContext db, long tenant, long actor,
        IFileUploadService storage) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), storage, TimeProvider.System,
            NullLogger<ResearchEvidenceFileService>.Instance);
    private static ResearchGrantOutputService Outputs(EduOSDbContext db, long tenant, long actor) =>
        new(db, new ResearchWorkflowAccess(db, new Principal(tenant, actor)), TimeProvider.System,
            NullLogger<ResearchGrantOutputService>.Instance);
    private static PrivateFileUploadDto Upload(byte[] content) => new()
    {
        FileName = "audit.pdf", ContentType = "application/pdf",
        Length = content.Length, Content = new MemoryStream(content)
    };

    private sealed class FakeEvidenceStorage(long tenant) : IFileUploadService
    {
        private readonly Dictionary<string, byte[]> files = new();
        public bool FailScan { get; set; }
        public bool Tamper { get; set; }
        public bool ValidateFile(IFormFile? file) =>
            file != null && file.ContentType == "application/pdf" &&
            file.Length is > 0 and <= 5242880;
        public async Task<FileUploadResult> UploadPrivateAsync(IFormFile file, string folder)
        {
            var input = file.OpenReadStream(); // Caller owns the underlying buffered stream.
            await using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer);
            var key = $"tenant-{tenant}/{folder}/{Guid.NewGuid():N}.pdf";
            files[key] = buffer.ToArray();
            return new FileUploadResult { Success = true, FileUrl = key };
        }
        public Task<string?> ScanPrivateForMalwareAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(FailScan || !files.TryGetValue(key, out var bytes) ? null :
                Convert.ToHexString(SHA256.HashData(bytes)));
        public Task<FileDownloadResult?> GetPrivateFileAsync(string key) =>
            Task.FromResult(files.TryGetValue(key, out var bytes) ? new FileDownloadResult
            { Content = Tamper ? bytes.Append((byte)'X').ToArray() : bytes.ToArray() } : null);
        public Task<bool> DeletePrivateAsync(string key) => Task.FromResult(files.Remove(key));
        public Task<FileUploadResult> UploadAsync(IFormFile file, string folder) =>
            throw new NotSupportedException();
        public Task<FileUploadResult> UploadPrivateForTenantAsync(IFormFile file, string folder, long id) =>
            throw new NotSupportedException();
        public Task<bool> DeleteAsync(string key) => throw new NotSupportedException();
        public Task<bool> DeleteByPathAsync(string key) => throw new NotSupportedException();
        public Task<byte[]> GetFileContentAsync(string key) => throw new NotSupportedException();
        public Task<List<FileInfo>> GetFilesInFolderAsync(string folder) => throw new NotSupportedException();
        public Task<long> GetFolderSizeAsync(string folder) => throw new NotSupportedException();
    }

    private sealed class Principal(long tenant, long actor) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actor;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [];
        public bool IsInRole(string role) => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
