const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('occupied Hostel Finance transfer review is read-only and never grants approval', () => {
    const script = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/finance/hostel-transfer-finance-review.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/FinanceAdjustments/ReservationPreflight.cshtml'), 'utf8');
    assert.match(script, /reservations\/hostel-transfer-review\?/);
    assert.match(script, /reservationFinancePreflightLoaded/);
    assert.match(script, /credentials:'same-origin'/);
    assert.match(script, /textContent/);
    assert.match(script, /NOT GRANTED/);
    assert.doesNotMatch(script, /innerHTML|method:\s*['"]POST['"]/);
    assert.match(view, /hostelTransferFinanceReviewForm/);
    assert.match(view, /hostel-transfer-finance-review\.js/);
});
