const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('Finance evidence workspace remains read-only, scoped and text-safe', () => {
    const script = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/finance/reservation-finance-evidence.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/FinanceAdjustments/ReservationPreflight.cshtml'), 'utf8');
    assert.match(script, /reservations\/evidence\?/);
    assert.match(script, /kind: scope\.kind, reservationId: String\(scope\.id\)/);
    assert.match(script, /credentials:'same-origin'/);
    assert.match(script, /td\.textContent/);
    assert.doesNotMatch(script, /innerHTML|method:\s*['"]POST['"]/);
    assert.match(view, /reservationFinanceEvidenceRows/);
    assert.match(view, /reservation-finance-evidence\.js/);
});
