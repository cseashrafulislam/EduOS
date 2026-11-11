const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const read = file => fs.readFileSync(path.join(__dirname, '../..', file), 'utf8');
const source = read('EduOS.App/wwwroot/js/finance/accounting-setup.js');

function runner() {
    const start = source.indexOf('const pendingForms = new WeakSet();');
    const end = source.indexOf('const guards = {};', start);
    assert.ok(start > 0 && end > start, 'Shared form runner is present');
    const errors = [];
    const run = vm.runInNewContext('(() => { ' + source.slice(start, end) + 'return runForm; })()', {
        alert: (...args) => errors.push(args)
    });
    return { run, errors };
}
function form() {
    const controls = [{ disabled: false }, { disabled: true }, { disabled: false }];
    return { controls, querySelectorAll: () => controls };
}
function deferred() {
    let release;
    const promise = new Promise(resolve => { release = resolve; });
    return { promise, release };
}

test('Account save blocks repeated submit while pending and preserves pre-disabled controls', async () => {
    const { run } = runner(), editor = form(), gate = deferred();
    let writes = 0;
    const first = run(editor, async () => { writes++; await gate.promise; });
    await run(editor, async () => { writes++; });
    assert.equal(writes, 1);
    assert.deepEqual(editor.controls.map(c => c.disabled), [true, true, true]);
    gate.release(); await first;
    assert.deepEqual(editor.controls.map(c => c.disabled), [false, true, false]);
    await run(editor, async () => { writes++; });
    assert.equal(writes, 2);
});

test('Independent period and bank forms can save concurrently, and rejected save unlocks', async () => {
    const { run, errors } = runner(), period = form(), bank = form(), gate = deferred();
    let saved = 0;
    const first = run(period, async () => { await gate.promise; saved++; });
    await run(bank, async () => { saved++; throw Error('Rejected by server'); });
    assert.equal(saved, 1);
    assert.equal(period.controls[0].disabled, true);
    assert.deepEqual(bank.controls.map(c => c.disabled), [false, true, false]);
    assert.deepEqual(errors[0], ['Rejected by server', true]);
    gate.release(); await first;
    assert.equal(saved, 2);
});

test('All three posting/edit forms use the guarded runner and explicit unsaved guards', () => {
    const razor = read('EduOS.App/Views/AccountingSetup/Index.cshtml');
    assert.match(razor, /form-guard\.js" asp-append-version="true"[\s\S]*finance\/accounting-setup\.js/);
    for (const name of ['account', 'period', 'bank']) {
        assert.match(source, new RegExp("\\$\\('" + name + "Form'\\)\\.addEventListener\\('submit'[\\s\\S]*?runForm\\(e\\.currentTarget"));
        assert.match(source, new RegExp("confirmDiscard\\('" + name + "Form'\\)"));
        assert.match(source, new RegExp("markClean\\('" + name + "Form'\\)"));
    }
    assert.match(source, /const pendingPeriodCloses = new Set\(\)/);
    assert.match(source, /if \(pendingPeriodCloses\.has\(close\)\) return/);
});

test('Account setup rechecks original tenant and session after awaited API response', () => {
    const before = source.indexOf("if (!sameSession()) throw new Error('Institution session changed.");
    const after = source.indexOf("if (!sameSession()) throw new Error('Institution session changed.", before + 1);
    assert.ok(before >= 0 && after > before);
    assert.ok(source.indexOf('const res = await fetch(base + path, options);') > before);
    assert.ok(source.indexOf('let payload; try { payload = await res.json(); }') < after);
});
