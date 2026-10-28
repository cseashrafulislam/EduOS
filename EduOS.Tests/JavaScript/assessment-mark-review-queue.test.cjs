const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/assessment/mark-reviews.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/MarkReviews.cshtml'), 'utf8');

test('reviewer queue has server paging, scoped filtering and no innerHTML', () => {
    assert.match(script, /marks\/reviews\/queue\?/);
    assert.match(script, /pageSize: String\(queue\.pageSize\)/);
    assert.match(script, /sequence !== queue\.sequence/);
    assert.match(script, /item\.assessmentSubjectId/);
    assert.match(script, /td\.textContent =/);
    assert.doesNotMatch(script, /\.innerHTML\s*=/);
});
test('queue provides bounded current-page export and responsive reviewer actions', () => {
    assert.match(script, /queue\.rows\.map/);
    assert.match(script, /csvRows\(/);
    assert.match(view, /id="markReviewQueuePanel"/);
    assert.match(view, /id="markReviewQueueCsv"/);
    assert.match(view, /id="markReviewQueueSearch"/);
});
