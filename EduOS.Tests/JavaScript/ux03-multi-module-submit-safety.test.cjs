const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const finance = file => read(`EduOS.App/wwwroot/js/finance/${file}.js`);
function functionBody(source, name) {
    const start = source.indexOf(`function ${name}(`);
    assert.ok(start >= 0, `missing function ${name}`);
    const prefix = source.slice(start - 6, start) === 'async ' ? 'async ' : '';
    let first = source.indexOf('{', start), depth = 0;
    for (let i = first; i < source.length; i++) {
        if (source[i] === '{') depth++;
        if (source[i] === '}' && --depth === 0 && source[i] === '}')
            return prefix + source.slice(start, i + 1);
    }
    throw Error(`unclosed function ${name}`);
}
function fakeForm() {
    const button = { disabled: false };
    return { button, querySelector: () => button };
}
function deferred() {
    let release;
    const promise = new Promise(resolve => { release = resolve; });
    return { promise, release };
}

test('finance form runner blocks simultaneous saves and unlocks after completion or rejection', async () => {
    const errors = [];
    const expression = '(() => { const submittingForms = new WeakSet(); ' +
        functionBody(finance('finance-adjustments'), 'run') + ' return run; })()';
    const run = vm.runInNewContext(expression, { notice: (...args) => errors.push(args) });
    const form = fakeForm(), gate = deferred();
    let calls = 0;
    const first = run(form, () => { calls++; return gate.promise; });
    await run(form, () => { calls++; });
    assert.equal(calls, 1);
    assert.equal(form.button.disabled, true);
    gate.release();
    await first;
    assert.equal(form.button.disabled, false);
    await run(form, () => { calls++; throw Error('validation rejected'); });
    assert.equal(calls, 2);
    assert.equal(form.button.disabled, false);
    assert.ok(errors.some(([message]) => message === 'validation rejected'));
});

test('document CRUD/upload runner locks the same form but permits another independent form', async () => {
    const errors = [];
    const expression = '(() => { const submittingForms = new WeakSet(); ' +
        functionBody(read('EduOS.App/wwwroot/js/document/admin.js'), 'run') + ' return run; })()';
    const run = vm.runInNewContext(expression, { msg: (...args) => errors.push(args) });
    const doc = fakeForm(), template = fakeForm(), gate = deferred();
    let calls = 0;
    const pending = run(() => { calls++; return gate.promise; }, doc);
    await run(() => { calls++; }, doc);
    await run(() => { calls++; }, template);
    assert.equal(calls, 2);
    assert.equal(doc.button.disabled, true);
    gate.release();
    await pending;
    await run(() => { calls++; throw Error('server rejected'); }, doc);
    assert.equal(calls, 3);
    assert.equal(doc.button.disabled, false);
    assert.deepEqual(errors[0], ['server rejected', true]);
});

test('hostel and transport form handlers re-enable a rejected edit and keep concurrent loads independent', async () => {
    for (const name of ['hostel-clearances', 'transport-clearances']) {
        const run = vm.runInNewContext('(() => { const submittingForms = new WeakSet(); ' +
            functionBody(finance(name), 'run') + ' return run; })()', { Promise, notice: () => {} });
        const form = fakeForm(), gate = deferred();
        let calls = 0;
        run(() => { calls++; return gate.promise; }, form);
        run(() => { calls++; }, form);
        await Promise.resolve();
        assert.equal(calls, 1, name);
        assert.equal(form.button.disabled, true);
        gate.release();
        await new Promise(setImmediate);
        assert.equal(form.button.disabled, false);
        run(() => { calls++; return Promise.reject(Error('failed')); }, form);
        await new Promise(setImmediate);
        assert.equal(form.button.disabled, false, `${name} failed submit unlock`);
        assert.equal(calls, 2);
    }
});

test('LMS course, lesson, assignment and enrollment bind independently single-flight', async () => {
    const names = ['learningCourseForm', 'learningLessonForm', 'learningAssignmentForm', 'learningEnrollForm'];
    const listeners = new Map();
    const $ = id => ({ addEventListener: (_, handler) => listeners.set(id, handler) });
    const bind = vm.runInNewContext('(() => { const $ = globalSelector; ' +
        functionBody(read('EduOS.App/wwwroot/js/learning/index.js'), 'bindSubmitOnce') +
        ' return bindSubmitOnce; })()', { globalSelector: $ });
    for (const name of names) {
        const gate = deferred();
        let calls = 0;
        bind(name, async () => { calls++; await gate.promise; });
        const submit = listeners.get(name);
        const event = { preventDefault() { this.prevented = true; } };
        const first = submit(event);
        await submit(event);
        assert.equal(calls, 1, name);
        assert.equal(event.prevented, true);
        gate.release();
        await first;
        await submit(event);
        assert.equal(calls, 2, `${name} unlock`);
    }
});

test('Document forms protect unsaved drafts and upload retries reuse the same request ID', () => {
    const markup = read('EduOS.App/Views/DocumentAdministration/Index.cshtml');
    const source = read('EduOS.App/wwwroot/js/document/admin.js');
    assert.match(markup, /form-guard\.js" asp-append-version="true"[\s\S]*js\/document\/admin\.js/);
    for (const guard of ['docGuard', 'typeGuard', 'templateGuard']) {
        assert.match(source, new RegExp(guard + '\\.confirmDiscard\\(\\)'));
        assert.match(source, new RegExp(guard + '\\.markClean\\(\\)'));
    }
    assert.match(source, /uploadRetry=null;el\("docUploadForm"\)\.reset\(\)/);
    const uploadId = vm.runInNewContext('(() => { let uploadRetry = null; const crypto = { randomUUID: () => String(++counter) }; ' +
        functionBody(source, 'uploadRequestId') + ' return uploadRequestId; })()', { counter: 0 });
    const original = { name: 'file.pdf', size: 120, lastModified: 5 };
    const changed = { name: 'file.pdf', size: 121, lastModified: 5 };
    assert.equal(uploadId(original), uploadId(original));
    assert.notEqual(uploadId(original), uploadId(changed));
});

test('Teacher Studio and Adaptive Goals keep single-flight locks on protected writes', () => {
    const studio = read('EduOS.App/wwwroot/js/lms/teacher-studio.js');
    const goals = read('EduOS.App/wwwroot/js/lms/adaptive-goals.js');
    assert.match(studio, /if \(saving\) return;/);
    assert.match(studio, /finally \{ saving = false;/);
    assert.match(goals, /if\(saving\|\|!event\.currentTarget\.reportValidity\(\)\)return/);
    assert.match(goals, /finally\{saving=false;/);
});
