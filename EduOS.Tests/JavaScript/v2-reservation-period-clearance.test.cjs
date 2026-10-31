const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const load = relative => fs.readFileSync(path.join(root, relative), 'utf8');

test('Finance clearance screens expose upcoming charged period operations and signed amounts', () => {
    const hostel = load('EduOS.App/Views/FinanceAdjustments/HostelClearances.cshtml');
    const transport = load('EduOS.App/Views/FinanceAdjustments/TransportClearances.cshtml');
    assert.match(hostel, /value="3">Upcoming start reschedule/);
    assert.match(transport, /value="3">Upcoming rider extension/);
    for (const [view, marker] of [[hostel, 'hfManualNet'], [transport, 'tfManualNet']]) {
        assert.match(view, new RegExp(`id="${marker}"`));
        assert.match(view, /Final \/ correction \(BDT\)/);
        assert.match(view, /value="hybrid"/);
    }
});

test('Clearance IDs are optional for no-charge operations but explicit for paid date changes', () => {
    const hostel = load('EduOS.App/wwwroot/js/hostel-operations/upcoming-start-reschedule.js');
    const transport = load('EduOS.App/wwwroot/js/transport-operations/upcoming-rider-extension.js');
    for (const script of [hostel, transport]) {
        assert.match(script, /financeClearanceId/);
        assert.match(script, /Number\.isSafeInteger\(financeClearanceId\)/);
        assert.match(script, /rowVersion: pending\.rowVersion/);
        assert.doesNotMatch(script, /innerHTML\s*=/);
    }
});

test('Finance review posts signed exact-decimal text and retains anti-forgery header', () => {
    const hostel = load('EduOS.App/wwwroot/js/finance/hostel-clearances.js');
    const transport = load('EduOS.App/wwwroot/js/finance/transport-clearances.js');
    for (const script of [hostel, transport]) {
        assert.match(script, /manualNetAdjustment:/);
        assert.match(script, /RequestVerificationToken/);
        assert.match(script, /document\.createElement\('td'\)/);
        assert.doesNotMatch(script, /innerHTML\s*=/);
    }
});
