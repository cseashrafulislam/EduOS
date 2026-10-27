const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('lifecycle audit UI has bounded paging, stale-response cancellation and safe text rendering', () => {
    const js = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/students/lifecycle-timeline.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/Students/Index.cshtml'), 'utf8');
    assert.match(js, /URLSearchParams/);
    assert.match(js, /pageSize = 20/);
    assert.match(js, /request !== pending/);
    assert.match(js, /pending\.abort\(\)/);
    assert.match(js, /textContent/);
    assert.doesNotMatch(js, /innerHTML/);
    assert.match(view, /id="studentLifecyclePrevious"/);
    assert.match(view, /id="studentLifecycleNext"/);
    assert.match(view, /id="studentLifecyclePage"/);
});
