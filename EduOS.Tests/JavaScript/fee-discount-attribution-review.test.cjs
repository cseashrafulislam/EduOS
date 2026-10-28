const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/finance/fee-discount-attribution-review.js'), 'utf8');

test('historical discount review uses scoped read-only API and no finance mutations', () => {
    assert.match(source, /\/api\/finance\/fees\/discount-attribution-review/);
    assert.match(source, /credentials: 'same-origin'/);
    assert.match(source, /cache: 'no-store'/);
    assert.doesNotMatch(source, /method:\s*['"](?:POST|PUT|DELETE)['"]/);
    assert.match(source, /pageSize/);
    assert.match(source, /AbortController/);
});
test('CSV uses formula injection defense and reports candidate as unverified', () => {
    assert.match(source, /\[=\+\\-@\]/);
    assert.match(source, /Candidate amount \(unverified\)/);
    assert.match(source, /textContent/);
    assert.match(source, /window\.print/);
});
