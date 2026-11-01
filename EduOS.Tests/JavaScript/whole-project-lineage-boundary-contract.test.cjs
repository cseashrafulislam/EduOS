// Independent whole-project QA guard for authoritative persisted lineage boundaries.
// Source-contract evidence only: this does not certify SQL, browser or production behavior.
const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const read = path => readFileSync(resolve(__dirname, '../../', path), 'utf8');
const files = {
    receipt: 'EduOS.Service/Services/Inventory/InventoryMovementProcurementReceipts.cs',
    payable: 'EduOS.Service/Services/Inventory/ProcurementSupplierInvoicePosting.cs',
    capital: 'EduOS.Service/Services/Inventory/AssetAccountingCapitalizationService.cs',
    unwind: 'EduOS.Service/Services/Inventory/AssetAccountingCapitalizationReversal.cs',
    facility: 'EduOS.Service/Services/Facilities/FacilityWorkflowService.cs',
    register: 'EduOS.Service/Services/Enterprise/V3EnterpriseReconciliationRegisterService.cs',
    assetApi: 'EduOS.App/Controllers/Api/AssetAccountingController.cs'
};
function requires(file, contracts) {
    const code = read(files[file]);
    for (const contract of contracts)
        assert.ok(code.includes(contract), file + ': missing verified contract ' + contract);
    return code;
}

test('PO-approved goods receipt has independent review and atomic canonical stock provenance', () => {
    const code = requires('receipt', [
        'BeginTransactionAsync(IsolationLevel.Serializable',
        'receipt.CreatedBy == user.UserId',
        'receipt.SubmittedByUserId == user.UserId',
        'ReceiptCommittedQuantitiesAsync',
        'SourceType = "ProcurementGoodsReceipt"',
        'movement.SourceId = receipt.Id',
        'receipt.PostedMovementId = movement.Id',
        'CheckPostingAsync(movement',
        'await tx.CommitAsync(ct)'
    ]);
    assert.match(code, /receipt\.StateCode = request\.Decision == "Approve" \? "Posted" : "Rejected"/);
    assert.match(code, /reserved\.GetValueOrDefault\(g\.Key\) \+ g\.Sum\(x => x\.Quantity\) > g\.First\(\)\.ApprovedPoQuantity/);
});

test('supplier payable review is independent and balances the canonical Finance GL', () => {
    requires('payable', [
        'BeginTransactionAsync(IsolationLevel.Serializable',
        'invoice.CreatedBy == user.UserId',
        'invoice.SubmittedByUserId == user.UserId',
        'invoice.PriceVariance != invoice.TotalAmount - invoice.StockValue',
        'debits.Sum(x => x.DebitAmount) != debits.Sum(x => x.CreditAmount)',
        'SourceType = "ProcurementSupplierInvoice", SourceId = invoice.Id',
        'JournalState.Posted',
        'await tx.CommitAsync(ct)'
    ]);
});

test('one approved receipt unit capitalizes through canonical stock issue and balanced GL atomically', () => {
    requires('capital', [
        'GoodsReceiptLineId=line.Id',
        'ReceiptUnitOrdinal=request.ReceiptUnitOrdinal',
        'c.CreatedBy==user.UserId',
        'PostAssetIssueInTransactionAsync',
        'SourceType="AssetCapitalization"',
        'SourceId=c.Id',
        'entries.Sum(x=>x.DebitAmount)!=entries.Sum(x=>x.CreditAmount)',
        'c.StockMovementId=movement.Id',
        'c.JournalId=journal.Id',
        'await tx.CommitAsync(ct)'
    ]);
});

test('capitalization unwind retains original source and explicit GL contra plus stock reversal', () => {
    requires('unwind', [
        'CapReversalSource = "AssetCapitalizationReversal"',
        'ReverseAssetIssueInTransactionAsync',
        'ReversalOfJournalId = command.JournalId',
        'BeginTransactionAsync(IsolationLevel.Serializable',
        'SourceId = command.Id'
    ]);
});

test('Facilities workflow must not be misrepresented as posted stock/Finance integration', () => {
    const code = requires('facility', [
        'Stock and Finance are intentionally NOT posted',
        'FacilityWorkflowEvent',
        'user.UserId != request.RequesterUserId',
        'user.UserId != order?.AssignedTechnicianUserId',
        'Command superseded; reload current work order.',
        'Record changed; reload.',
        'case "verify"'
    ]);
    assert.doesNotMatch(code, /db\.Journals\.(?:Add|AddRange)\s*\(/);
    assert.doesNotMatch(code, /db\.InventoryMovements\.(?:Add|AddRange)\s*\(/);
    assert.doesNotMatch(code, /PostAssetIssueInTransactionAsync|CheckPostingAsync\(movement/);
    // When a domain owner adds real facility stock/GL posting, replace this
    // explicit gap assertion with disposable SQL lineage/reversal acceptance.
});

test('enterprise register keeps separate source types and bounded tenant-scoped evidence', () => {
    const code = requires('register', [
        'kind != "stock" && kind != "asset" && kind != "procurement"',
        'SourceType == "ProcurementGoodsReceipt"',
        'SourceType == "InventoryMovement"',
        'db.CurrentTenantId != user.TenantId',
        'pageSize > 50'
    ]);
    assert.doesNotMatch(code, /kind == "facility"/);
});

test('asset Finance command API requires both entitlements, CSRF and separate maker/reviewer roles', () => {
    requires('assetApi', [
        'RequireModule("INVENTORY")',
        'RequireModule("FINANCE")',
        'AutoValidateAntiforgeryToken',
        'EnableRateLimiting("ApiPolicy")',
        'Authorize(Roles = "TenantAdmin,Accountant")',
        'Authorize(Roles = "TenantAdmin,Principal")'
    ]);
});
