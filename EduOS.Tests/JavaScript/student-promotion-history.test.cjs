const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('promotion workspace supports tenant-scoped student search and paged history readback', () => {
    const js = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/students/progressions.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/StudentProgressions/Index.cshtml'), 'utf8');
    assert.match(js, /\/api\/students\?' \+ query/);
    assert.match(js, /\/promotions\/history\?' \+ query/);
    assert.match(js, /request !== state.historyPending/);
    assert.match(js, /request !== state.lookupPending/);
    assert.match(js, /encodeURIComponent\(reference\)/);
    assert.match(js, /textContent/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.match(js, /clientRequestId: state.requestId/);
    assert.match(js, /sourceEnrollmentRowVersion/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(view, /id="promotionResults"/);
    assert.match(view, /id="promotionHistoryRows"/);
    assert.match(view, /id="promotionHistoryPrevious"/);
    assert.match(view, /id="promotionHistoryNext"/);
});
