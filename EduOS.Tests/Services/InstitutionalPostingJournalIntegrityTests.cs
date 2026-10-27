using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionalPostingJournalIntegrityTests
{
    private static (Journal Journal, JournalLine[] Lines) Valid()
    {
        var journal = new Journal { Id = 12, TenantId = 41, SourceType = "StudentInvoice",
            SourceId = 8, CampusId = 3, CurrencyCode = "BDT", State = JournalState.Posted };
        var lines = new[]
        {
            new JournalLine { JournalId = 12, TenantId = 41, LineNo = 1,
                AccountId = 21, CampusId = 3, StudentId = 7, DebitAmount = 100m },
            new JournalLine { JournalId = 12, TenantId = 41, LineNo = 2,
                AccountId = 22, CampusId = 3, StudentId = 7, CreditAmount = 100m }
        };
        return (journal, lines);
    }
    private static bool Check(Journal journal, JournalLine[] lines, long? cash = null) =>
        InstitutionalPostingJournalIntegrity.Matches(journal, lines, 41,
            "StudentInvoice", 8, 3, 7, 100m, cash);
    [Fact] public void Balanced_source_linked_journal_passes()
    {
        var (journal, lines) = Valid();
        Check(journal, lines).Should().BeTrue();
        Check(journal, lines, 21).Should().BeTrue();
    }
    [Fact] public void Soft_deleted_header_fails()
    {
        var (journal, lines) = Valid(); journal.IsDeleted = true;
        Check(journal, lines).Should().BeFalse();
    }
    [Fact] public void Soft_deleted_line_fails_even_if_remaining_lines_are_balanced()
    {
        var (journal, lines) = Valid(); lines[1].IsDeleted = true;
        Check(journal, lines).Should().BeFalse();
    }
    [Fact] public void Cross_tenant_or_wrong_source_fails()
    {
        var (journal, lines) = Valid(); journal.SourceId = 99;
        Check(journal, lines).Should().BeFalse(); journal.SourceId = 8;
        lines[0].TenantId = 42; Check(journal, lines).Should().BeFalse();
    }
    [Fact] public void Invalid_precision_and_imbalances_fail()
    {
        var (journal, lines) = Valid(); lines[1].CreditAmount = 99m;
        Check(journal, lines).Should().BeFalse();
        lines[1].CreditAmount = 100m; lines[0].DebitAmount = 100.001m;
        Check(journal, lines).Should().BeFalse();
    }
    [Fact] public void Cash_ledger_must_match_single_full_debit()
    {
        var (journal, lines) = Valid();
        Check(journal, lines, 99).Should().BeFalse();
        lines[0].DebitAmount = 60m; Check(journal, lines, 21).Should().BeFalse();
    }
    [Fact] public void Reversal_or_duplicate_line_number_fails()
    {
        var (journal, lines) = Valid(); journal.ReversalOfJournalId = 4;
        Check(journal, lines).Should().BeFalse(); journal.ReversalOfJournalId = null;
        lines[1].LineNo = 1; Check(journal, lines).Should().BeFalse();
    }
}
