'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

test('Grade, component and schedule editors load the guard before the owning workflow', () => {
    for (const [view, script] of [
        ['GradeSchemes', 'grade-schemes'], ['AssessmentComponents', 'assessment-components'],
        ['AssessmentSchedules', 'assessment-schedules']
    ]) {
        const razor = read('EduOS.App/Views/AssessmentAdministration/' + view + '.cshtml');
        const js = read('EduOS.App/wwwroot/js/assessment/' + script + '.js');
        assert.match(razor, new RegExp('form-guard\\.js"[\\s\\S]*assessment/' + script + '\\.js"'));
        assert.match(js, /EduOSFormGuard\?\.create\(form\)/);
        assert.match(js, /formGuard\?\.markClean\(\)/);
        assert.match(js, /formGuard\.confirmDiscard\(\)/);
        assert.match(js, /state\.busy/);
        assert.match(js, /generation !== state\.listGeneration/);
        assert.match(js, /register could not refresh/);
    }
});

test('Schedule room lookup is prerequisite to saving and stale response cannot replace new subject rooms', async () => {
    const elements = new Map();
    const get = id => {
        if (elements.has(id)) return elements.get(id);
        const element = {
            id, tagName: id === 'scheduleSubject' || id === 'scheduleRoom' ? 'SELECT' : 'INPUT',
            type: 'text', name: id, value: '', checked: false, disabled: false, options: [], children: [],
            handlers: {}, classList: { add() {}, remove() {} },
            addEventListener(type, callback) { this.handlers[type] = callback; },
            replaceChildren(...children) {
                this.children = children;
                if (this.tagName === 'SELECT') { this.options = [...children]; this.value = children[0]?.value || ''; }
            },
            add(option) { this.options.push(option); },
            append(...children) { this.children.push(...children); },
            scrollIntoView() {}
        };
        elements.set(id, element);
        return element;
    };
    const editorIds = ['scheduleSubject', 'scheduleDate', 'scheduleRoom', 'scheduleStart',
        'scheduleEnd', 'scheduleInstructions'];
    const form = get('scheduleForm');
    form.elements = editorIds.map(get);
    form.querySelectorAll = selector => selector.includes('button')
        ? [...form.elements, get('scheduleSave'), get('scheduleClear')] : form.elements;
    form.reset = () => { for (const field of form.elements) field.value = ''; };
    const workspace = get('assessmentScheduleWorkspace');
    workspace.querySelector = () => ({ value: 'csrf' });
    let resolveOldRoom;
    const requests = [];
    const fetch = async url => {
        requests.push(String(url));
        let data;
        if (String(url).endsWith('/scopes')) data = [
            { assessmentSubjectId: 1, assessmentName: 'Exam', academicBatchName: 'A', subjectName: 'Math' },
            { assessmentSubjectId: 2, assessmentName: 'Exam', academicBatchName: 'A', subjectName: 'English' }
        ];
        else if (String(url).includes('/rooms?assessmentSubjectId=1'))
            data = await new Promise(resolve => { resolveOldRoom = resolve; });
        else if (String(url).includes('/rooms?assessmentSubjectId=2'))
            data = [{ id: 22, name: 'New room', code: 'B' }];
        else if (String(url).includes('/schedules?')) data = { items: [], totalCount: 0 };
        else throw new Error('Unexpected request ' + url);
        return { ok: true, async json() { return { success: true, data }; } };
    };
    const document = { getElementById: get, createElement: tag => ({
        tagName: tag.toUpperCase(), children: [], append(...items) { this.children.push(...items); },
        addEventListener() {}, textContent: '', disabled: false
    }) };
    let allowDiscard = true;
    const window = { EduOSFormGuard: { create: () => ({
        markClean() {}, confirmDiscard: () => allowDiscard
    }) } };
    function Option(name, value) { this.name = name; this.value = value; }
    vm.runInNewContext(read('EduOS.App/wwwroot/js/assessment/assessment-schedules.js'),
        { document, window, fetch, Option, URLSearchParams });
    const tick = () => new Promise(resolve => setImmediate(resolve));
    await tick(); await tick();
    const subject = get('scheduleSubject'), room = get('scheduleRoom');
    assert.equal(get('scheduleSave').disabled, true, 'no subject/room lookup cannot authorize a save');
    subject.value = '1';
    const oldChange = subject.handlers.change();
    await tick();
    assert.equal(get('scheduleSave').disabled, true, 'pending lookup cannot authorize a save');
    subject.value = '2';
    await subject.handlers.change();
    assert.deepEqual(Array.from(room.options, option => option.value), ['', '22']);
    assert.equal(get('scheduleSave').disabled, false);
    resolveOldRoom([{ id: 11, name: 'Old room', code: 'A' }]);
    await oldChange;
    assert.deepEqual(Array.from(room.options, option => option.value), ['', '22']);
    assert.equal(room.disabled, false);
    assert.equal(requests.some(x => x.includes('/rooms?assessmentSubjectId=1')), true);
    get('scheduleDate').value = '2026-11-15';
    allowDiscard = false;
    subject.value = '1';
    await subject.handlers.change();
    assert.equal(subject.value, '2', 'cancelled selection restores the authorized current subject');
    assert.equal(get('scheduleDate').value, '2026-11-15', 'unsaved exam date is preserved');
    assert.deepEqual(Array.from(room.options, option => option.value), ['', '22']);
});
