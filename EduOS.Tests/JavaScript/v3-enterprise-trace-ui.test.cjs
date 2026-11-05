const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { Script } = require('node:vm');

test('enterprise trace JavaScript parses and shows bilingual UNPOSTED settlement evidence', () => {
    const path = resolve(__dirname, '../../EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js');
    const source = readFileSync(path, 'utf8');
    assert.doesNotThrow(() => new Script(source, { filename: path }));
    assert.match(source, /settlementInstructions/);
    assert.match(source, /settlementReservedAmount/);
    assert.match(source, /NOT PAID/);
    assert.match(source, /অপরিশোধিত/);
    assert.match(source, /textContent = String\(value \?\? ''\)/);
    assert.match(source, /Source\/journal amount mismatch/);
    assert.match(source, /উৎসের অঙ্ক ও জার্নালের অঙ্ক মেলে না/);
});

test('asset Finance detail and register reject campusless journal evidence', () => {
    const detail = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseAssetFinanceTraceService.cs'), 'utf8');
    const register = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseReconciliationRegisterService.cs'), 'utf8');
    assert.match(detail, /journals\.Any\(x => x\.IsDeleted \|\| x\.CampusId != run\.CampusId\)/);
    assert.match(register, /j\.IsDeleted \|\| j\.CampusId != r\.CampusId/);
});

test('facility Finance and stock contras require one shared tenant-scoped proof', () => {
    const detail = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseLedgerTraceService.cs'), 'utf8');
    const register = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseReconciliationRegisterService.cs'), 'utf8');
    const validator = readFileSync(resolve(__dirname,
        '../../EduOS.Service/Services/Enterprise/V3EnterpriseFacilityReversalEvidenceValidator.cs'), 'utf8');
    const ui = readFileSync(resolve(__dirname,
        '../../EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js'), 'utf8');
    const csv = readFileSync(resolve(__dirname,
        '../../EduOS.App/Controllers/Api/V3EnterpriseLedgerTraceController.cs'), 'utf8');
    assert.match(detail, /V3EnterpriseFacilityReversalEvidenceValidator\.InspectAsync/);
    assert.match(register, /V3EnterpriseFacilityReversalEvidenceValidator\.InspectAsync/);
    assert.match(validator, /IgnoreQueryFilters\(\)/);
    assert.match(validator, /reversal\.ClientRequestId != contra\.PublicId/);
    assert.match(validator, /a\.UnitCost == b\.UnitCost/);
    assert.match(validator, /a\.DebitAmount == b\.CreditAmount/);
    assert.match(ui, /ExplicitReversedReference/);
    assert.match(ui, /ReversalPending/);
    assert.match(ui, /d\.financeReversalReference/);
    assert.match(csv, /Cell\("FinanceEvidenceStatus"\)/);
    assert.match(csv, /Cell\("StockReversal"\)/);
    assert.match(csv, /Cell\("FinanceContra"\)/);
});
