const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '../../', file), 'utf8');
const script = read('EduOS.App/wwwroot/js/alumni/career-employers.js');
const view = read('EduOS.App/Views/CareerEmployers/Index.cshtml');
const help = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('employer page has bilingual accessible form and existing shared help guide', () => {
    for (const locale of ['en-BD', 'bn-BD']) {
        const guide = help.guideFor('/CareerEmployers/Index', locale);
        assert.ok(guide);
        assert.ok(guide.steps.length >= 3);
        assert.ok(guide.example && guide.recovery);
    }
    assert.match(view, /@Html.AntiForgeryToken\(\)/);
    assert.match(view, /id="careerEmployerNotice"/);
    assert.match(view, /aria-live="polite"/);
    assert.match(script, /প্রতিষ্ঠান|নিয়োগদাতা/);
    assert.doesNotMatch(script, /innerHTML/);
});
test('employer workflow uses versioned CSRF writes and bounded export', () => {
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(script, /clientRequestId: createRequest/);
    assert.match(script, /rowVersion: item.rowVersion/);
    assert.match(script, /verifyRequests\.set\(key, crypto\.randomUUID\(\)\)/);
    assert.match(script, /pageSize=20&status=2/);
    assert.match(script, /generation !== serial/);
    assert.match(view, /id="careerEmployerRows"/);
});
