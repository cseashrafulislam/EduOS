const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('consent history uses authorized paged readback and text-only rendering', () => {
    const script = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/student-portal/consents.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname, '../../EduOS.App/Views/StudentPortal/Index.cshtml'), 'utf8');
    assert.match(script, /api\/learner-consents\/history\?page=/);
    assert.match(script, /historyPage <= 1/);
    assert.match(script, /historyPage >= 500/);
    assert.match(script, /historyHasNext/);
    assert.match(script, /textContent/);
    assert.doesNotMatch(script, /innerHTML/);
    assert.match(view, /id="learnerConsentHistory"/);
    assert.match(view, /id="learnerConsentHistoryPrevious"/);
    assert.match(view, /id="learnerConsentHistoryNext"/);
});
