const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/assessment/mark-reviews.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/MarkReviews.cshtml'), 'utf8');

test('marks review uses authorized scoped selector, rowversion and anti-forgery', () => {
    assert.match(script, /scopes\/page/);
    assert.match(script, /assessmentRowVersion: review\.assessmentRowVersion/);
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(script, /clientRequestId: state\.requestId/);
    assert.match(script, /encodeURIComponent\(id\)/);
});
test('review screen has contextual help and safe history rendering', () => {
    assert.match(view, /aria-label="Marks review help"/);
    assert.match(script, /td\.textContent =/);
    assert.match(script, /replaceChildren\(\)/);
    assert.match(script, /recentHistory/);
});
