const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('Finance proposed change review remains read-only and cannot approve changes', () => {
    const script = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/finance/reservation-finance-change-review.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/FinanceAdjustments/ReservationPreflight.cshtml'), 'utf8');
    assert.match(script, /reservations\/change-review\?/);
    assert.match(script, /reservationFinancePreflightLoaded/);
    assert.match(script, /credentials:'same-origin'/);
    assert.match(script, /textContent/);
    assert.match(script, /data\.calculationMode/);
    assert.match(script, /data\.suggestedNetAdjustment/);
    assert.match(script, /additional charge/);
    assert.match(script, /potential credit/);
    assert.match(script, /Finance approval', 'NOT GRANTED'/);
    assert.doesNotMatch(script, /innerHTML|method:\s*['"]POST['"]/);
    assert.match(view, /reservationFinanceChangeReviewForm/);
    assert.match(view, /reservation-finance-change-review\.js/);
});
