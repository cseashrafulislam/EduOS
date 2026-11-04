const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const read = path => fs.readFileSync(path, 'utf8');
const view = read('EduOS.App/Views/AccountingReports/Index.cshtml');
const setupView = read('EduOS.App/Views/AccountingSetup/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/accounting-cash-movements.js');
const setupJs = read('EduOS.App/wwwroot/js/finance/accounting-setup.js');
const api = read('EduOS.App/Controllers/Api/AccountingReportsController.cs');
const reporting = read('EduOS.Service/Services/Accounting/AccountingReportingCashMovements.cs');
const setup = read('EduOS.Service/Services/Accounting/AccountingSetupService.cs');

test('cash report has explicit tenant restricted APIs, GL-only book and no financial write', () => {
    assert.match(api, /HttpGet\("cash-book\/\{accountId:long\}"\), Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /HttpGet\("cash-movements"\), Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(reporting, /if \(!TenantWideAllowed\)/);
    assert.match(reporting, /a\.IsCashInHand/);
    assert.match(reporting, /return await GetLedgerStatementAsync\(accountId, fromDate, toDate, ct\)/);
    assert.match(reporting, /HistoricalPostingErrorAsync\(null, toDate, ledgerIds, ct\)/);
    assert.match(reporting, /report\.TransfersIn != report\.TransfersOut/);
    assert.match(reporting, /Mixed transfer and external cash journal requires explicit allocation/);
    assert.doesNotMatch(reporting, /SaveChangesAsync|ExecuteUpdateAsync|AddAsync/);
});

test('cash designation cannot collide with bank, or reinterpret posted account history', () => {
    assert.match(setup, /hasHistory && row\.IsCashInHand != markCash/);
    assert.match(setup, /hasBankLink && markCash/);
    assert.match(setup, /request\.IsCashInHand \?\? row\?\.IsCashInHand \?\? false/);
    assert.match(setup, /if \(account\.IsCashInHand\)/);
    assert.match(setupView, /id="accountCash"/);
    assert.match(setupJs, /isCashInHand:\$\('accountCash'\)\.checked/);
    assert.match(setupJs, /!x\.isCashInHand/);
});

test('cash movement page never guesses statutory activity and preserves decimals through JS', () => {
    for (const name of ['cashBookPanel','cashMovementsPanel','cashBookRows','cashMovementsTotals'])
        assert.match(view, new RegExp(`id="${name}"`));
    assert.match(view, /not a classified statutory cash-flow statement/);
    assert.match(view, /accounting-cash-movements\.js/);
    assert.match(js, /BigInt\(parts\[0\]\)/);
    assert.match(js, /replace\(decimalKeys/);
    assert.match(js, /cents\(movements\.receipts\) - cents\(movements\.payments\)/);
    assert.match(js, /credentials:'same-origin'/);
    assert.match(js, /if \(!response\.ok \|\| !payload\.success\)/);
});
