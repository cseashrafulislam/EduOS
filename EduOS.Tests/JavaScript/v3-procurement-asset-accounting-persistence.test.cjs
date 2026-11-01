const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(base, file), 'utf8');

test('asset accounting APIs demand both Finance and Inventory entitlement, roles and CSRF', () => {
    const api = read('EduOS.App/Controllers/Api/AssetAccountingController.cs');
    for (const value of ['RequireModule("INVENTORY")', 'RequireModule("FINANCE")',
        'AutoValidateAntiforgeryToken', 'EnableRateLimiting("ApiPolicy")',
        'NoStore = true', 'TenantAdmin,Accountant', 'TenantAdmin,Principal',
        'ReviewCapitalizationAsync', 'ReviewDepreciationAsync', 'ReviewImpairmentAsync',
        'ReviewDisposalAsync', 'ReviewEventReversalAsync'])
        assert.ok(api.includes(value), 'missing accounting route guard: ' + value);
});

test('receipt unit has tenant-scoped uniqueness and source-bound canonical inventory and GL links', () => {
    const migration = read('EduOS.Persistence/Migrations/20261101220000_AssetCapitalizationCommands.cs');
    const model = read('EduOS.Persistence/Context/EduOSDbContext.cs');
    const service = read('EduOS.Service/Services/Inventory/AssetAccountingCapitalizationService.cs');
    const stock = read('EduOS.Service/Services/Inventory/InventoryMovementAssetCapitalization.cs');
    for (const text of ['ReceiptUnitOrdinal', 'GoodsReceiptLineId', 'AssetBookId',
        'StockMovementId', 'JournalId', 'TenantId', 'ClientRequestId',
        'CK_AssetCapitalizationCommand_Cost', 'IsDeleted] = 0'])
        assert.ok(migration.includes(text) && model.includes(text), 'missing FK/invariant ' + text);
    for (const value of ['IsolationLevel.Serializable', 'GetCapitalizationReadinessAsync',
        'AccountsValidAsync', 'PostAssetIssueInTransactionAsync',
        'JournalState.Posted', 'SourceType="AssetCapitalization"',
        'StockMovementId=movement.Id', 'StateCode="Posted"'])
        assert.ok(service.includes(value), 'missing stock+GL atomic posting ' + value);
    for (const value of ['CheckPostingAsync', 'InventoryMovementLines.Add', 'Postable',
        'SourceType = "AssetCapitalization"', 'MovementTypeCode = "Issue"'])
        assert.ok(stock.includes(value), 'missing canonical stock ledger hook ' + value);
});

test('periodic book valuation never updates a second general ledger or bypasses independent review', () => {
    const operations = read('EduOS.Service/Services/Inventory/AssetAccountingOperations.cs');
    const disposal = read('EduOS.Service/Services/Inventory/AssetAccountingImpairmentDisposal.cs');
    const reversals = read('EduOS.Service/Services/Inventory/AssetAccountingReversals.cs');
    for (const value of ['IsolationLevel.Serializable', 'PostEventAsync',
        'JournalState.Posted', 'ApprovedByUserId = user.UserId',
        'source.Book', 'ReversalOfJournalId', 'JournalState.Submitted'])
        assert.ok((operations + disposal + reversals).includes(value), 'missing book+GL review ' + value);
    for (const value of ['ReversalOfJournalId', 'l.DebitAmount != newLines[i].CreditAmount',
        'l.CreditAmount != newLines[i].DebitAmount', 'original.State != JournalState.Posted',
        'source.State != "Posted"'])
        assert.ok(reversals.includes(value), 'missing exact source GL contra ' + value);
    assert.doesNotMatch(operations + disposal + reversals, /HttpClient|BankTransfer|TransferMoney/);
});

test('asset Finance page exposes evidence, valuation policy, balances and reversal actions', () => {
    const view = read('EduOS.App/Views/AssetAccounting/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/inventory/asset-accounting.js');
    for (const id of ['aaCandidate', 'aaOrdinal', 'aaCapForm', 'aaCapMode',
        'aaLife', 'aaAccum', 'aaDepExp', 'aaImpExp', 'aaEventForm', 'aaKind',
        'aaClearing', 'aaBook', 'aaEvents'])
        assert.ok(view.includes('id="' + id + '"') && js.includes("'" + id + "'"),
            'UI control not bound: ' + id);
    for (const action of ['Review GL contra', 'Request exact GL reversal',
        'assetRowVersion', 'pendingReversalRowVersion', 'credentials'])
        if (action !== 'credentials') assert.ok(js.includes(action), 'missing UI action ' + action);
    const transport = read('EduOS.App/wwwroot/js/inventory/movements-api.js');
    assert.ok(transport.includes("credentials: 'same-origin'"));
    assert.ok(transport.includes('RequestVerificationToken'));
    assert.ok(js.includes('textContent'), 'server values must be encoded in text nodes');
});
