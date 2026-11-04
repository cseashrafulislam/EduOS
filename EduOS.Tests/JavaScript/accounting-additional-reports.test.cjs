const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root,name), 'utf8');
const view = read('EduOS.App/Views/AccountingReports/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/accounting-additional-reports.js');
const api = read('EduOS.App/Controllers/Api/AccountingReportsController.cs');
const service = read('EduOS.Service/Services/Accounting/AccountingReportingAdditionalStatements.cs');
const mainService = read('EduOS.Service/Services/Accounting/AccountingReportingService.cs');
const ledgerActivity = read('EduOS.Service/Services/Accounting/AccountingReportingLedgerActivity.cs');

test('new accounting reports have tenant-restricted read-only API and matching view/JS', () => {
    for (const [route, method, panel] of [
        ['day-book','GetPostedDayBookAsync','dayBookPanel'],
        ['comparative-income-statement','GetComparativeIncomeStatementAsync','compareIncomePanel'],
        ['comparative-balance-sheet','GetComparativeBalanceSheetAsync','compareBalancePanel'],
        ['income-expense-breakdown','GetIncomeExpenseBreakdownAsync','incomeExpenseBreakdownPanel']]) {
        assert.ok(api.includes(`[HttpGet("${route}"), Authorize(Roles = "TenantAdmin")]`));
        assert.ok(api.includes(method));
        assert.ok(service.includes(` ${method}(`));
        assert.ok(view.includes(`id="${panel}"`));
        assert.ok(js.includes(`/api/finance/accounting/reports/${route}`));
    }
    assert.match(view, /accounting-additional-reports\.js/);
    assert.match(view, /User\.IsInRole\("TenantAdmin"\)/);
});

test('day book does not confuse page totals with full period totals or report drafts', () => {
    assert.match(service, /Posted\.Where\(j => j\.JournalDate/);
    assert.match(service, /HistoricalPostingErrorAsync\(fromDate, toDate, null, ct\)/);
    assert.match(service, /x\.Debit != x\.Credit/);
    assert.match(service, /Take\(pageSize\)/);
    assert.match(service, /TotalDebit = totals\?\.Debit/);
    assert.match(js, /data\.journals\.totalCount/);
    assert.match(js, /pageSize:'25'/);
    assert.match(view, /Export Page CSV/);
    assert.match(js, /api\/finance\/accounting\/journals\//);
    assert.match(js, /detail\.lines \|\| \[\]/);
    assert.match(js, /debit !== credit \|\| debit !== cents\(summary\.debitAmount\)/);
});

test('comparative statements retain signed prior-only accounts and verify exact decimal balances', () => {
    assert.match(service, /GetIncomeStatementAsync\(comparisonFrom, comparisonTo, ct\)/);
    assert.match(service, /GetBalanceSheetAsync\(comparisonAsOfDate, ct\)/);
    assert.match(service, /row\.Variance = row\.CurrentAmount - row\.ComparisonAmount/);
    assert.match(js, /BigInt\(parts\[0\]\)/);
    assert.match(js, /cents\(row\.currentAmount\) - cents\(row\.comparisonAmount\)/);
    assert.match(js, /currentSignature\(\)/);
    assert.match(js, /\[=\+\\-@\]/);
});

test('campus/source breakdown is based on posted journal-line attribution and reconciles to income statement', () => {
    assert.match(service, /GetIncomeStatementAsync\(fromDate, toDate, ct\)/);
    assert.match(service, /x\.LineCampus \?\? x\.JournalCampus/);
    assert.match(service, /Unassigned campus/);
    assert.match(service, /rows\.Sum\(x => x\.Income\) != statement\.Data\.TotalIncome/);
    assert.match(js, /groupBy:group/);
    assert.match(js, /Campus\/source breakdown does not match institutional P&L/);
});

test('core ledger and income reports reject malformed lines before rendering amounts', () => {
    assert.match(mainService, /GetLedgerActivityAsync\(accountId, fromDate, toDate, 1, 1, ct\)/);
    assert.match(mainService, /Invalid debit\/credit journal line prevents financial reporting/);
    assert.match(mainService, /a\.IsGroup \|\| !a\.AllowsPosting/);
    assert.match(ledgerActivity, /A posted journal has an invalid line or posting account/);
});
