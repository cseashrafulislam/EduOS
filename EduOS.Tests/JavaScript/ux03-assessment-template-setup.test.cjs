'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('Assessment setup and certificate templates protect drafts, in-flight writes and confirmed saves', () => {
    const pages = [
        ['Setup', 'setup'], ['CertificateTemplates', 'certificate-templates']
    ];
    for (const [page, script] of pages) {
        const view = read('EduOS.App/Views/AssessmentAdministration/' + page + '.cshtml');
        const js = read('EduOS.App/wwwroot/js/assessment/' + script + '.js');
        assert.match(view, new RegExp('form-guard\\.js"[\\s\\S]*assessment/' + script + '\\.js"'));
        assert.match(js, /EduOSFormGuard\?\.create\(form\)/);
        assert.match(js, /formGuard\.confirmDiscard\(\)/);
        assert.match(js, /formGuard\?\.markClean\(\)/);
        assert.match(js, /form\.inert = true/);
        assert.match(js, /saved, but .*register|saved, but details\/register/i);
    }
    const setup = read('EduOS.App/wwwroot/js/assessment/setup.js');
    assert.match(setup, /generation !== state\.detailGeneration/);
    assert.match(setup, /generation !== state\.batchGeneration/);
    assert.match(setup, /generation !== state\.offeringGeneration/);
    assert.match(setup, /state\.requestKey \|\|= crypto\.randomUUID\(\)/);
});

test('Stale assessment details cannot overwrite the newer selected assessment', async () => {
    const elements = new Map();
    const get = id => {
        if (elements.has(id)) return elements.get(id);
        const e = { id, value: '', checked: false, disabled: false, inert: false, options: [],
            children: [], handlers: {},
            addEventListener(type, callback) { this.handlers[type] = callback; },
            replaceChildren(...children) { this.children = children; this.options = [...children]; this.value = children[0]?.value || ''; },
            add(option) { this.options.push(option); }, append(child) { this.children.push(child); },
            reset() { for (const x of editorFields) { get(x).value = ''; get(x).checked = false; } }
        };
        elements.set(id, e); return e;
    };
    const editorFields = ['setupName', 'setupCode', 'setupCampus', 'setupYear', 'setupTerm', 'setupScheme',
        'setupType', 'setupAttemptMode', 'setupStart', 'setupEnd', 'setupRemarks', 'setupCgpa'];
    get('assessmentSetupWorkspace').querySelector = () => ({ value: 'csrf' });
    const form = get('setupDefinition');
    const detail = (reference, name) => ({ reference, name, code: name.toUpperCase(), campusId: 1,
        academicYearId: 10, academicTermId: null, gradeSchemeId: null, type: 1,
        attemptMode: 0, startDate: '2026-11-15', endDate: '2026-11-16', remarks: '',
        countsTowardCgpa: false, state: 1, rowVersion: 'v', subjects: [] });
    let resolveOld;
    let failDetailRefresh = false;
    const fetch = async (url, options = {}) => {
        const request = String(url);
        let data;
        if (request.endsWith('/setup/references')) data = {
            campuses: [{ id: 1, name: 'Main' }],
            academicYears: [{ id: 10, name: '2026', campusId: 1 }], academicTerms: []
        };
        else if (request.includes('/grade-schemes?')) data = { items: [] };
        else if (request.includes('/assessments/page?')) data = { items: [
            { reference: '111', name: 'Old', code: 'OLD', state: 1 },
            { reference: '222', name: 'Newest', code: 'NEWEST', state: 1 }
        ], totalCount: 2 };
        else if (request.includes('/setup/batches?')) data = { items: [] };
        else if (request.endsWith('/assessments/111'))
            data = await new Promise(resolve => { resolveOld = resolve; });
        else if (request.endsWith('/assessments/222') && options.method === 'PUT')
            data = detail('222', 'Changed');
        else if (request.endsWith('/assessments/222')) {
            if (failDetailRefresh) return {
                ok: false, async json() { return { success: false, message: 'Details temporarily unavailable' }; }
            };
            data = detail('222', 'Newest');
        }
        else throw Error('Unexpected API request ' + request);
        return { ok: true, async json() { return { success: true, data }; } };
    };
    const document = { getElementById: get, createElement: tag => ({ tagName: tag.toUpperCase(),
        children: [], append(child) { this.children.push(child); } }) };
    function Option(label, value) { this.label = label; this.value = value; }
    const window = { EduOSFormGuard: { create: () => ({
        confirmDiscard: () => true, markClean() {}, isDirty: () => false
    }), fingerprint: () => 'unchanged' } };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/assessment/setup.js'),
        { document, window, fetch, Option, URLSearchParams, crypto: { randomUUID: () => 'key' } });
    const tick = () => new Promise(resolve => setImmediate(resolve));
    await tick(); await tick();
    assert.equal(form.inert, false, 'reference options are ready before the editor unlocks');
    const selected = get('setupExisting');
    selected.value = '111'; selected.handlers.change();
    await tick();
    selected.value = '222'; selected.handlers.change();
    await tick(); await tick();
    assert.equal(get('setupName').value, 'Newest');
    resolveOld(detail('111', 'Old'));
    await tick();
    assert.equal(get('setupName').value, 'Newest');
    assert.equal(selected.value, '222');
    get('setupName').value = 'Changed';
    failDetailRefresh = true;
    form.handlers.submit({ preventDefault() {} });
    await tick(); await tick(); await tick();
    assert.match(get('setupAlert').textContent, /Assessment saved, but details\/register could not refresh/);
    assert.equal(form.inert, false, 'confirmed write never leaves the draft permanently locked');
});
