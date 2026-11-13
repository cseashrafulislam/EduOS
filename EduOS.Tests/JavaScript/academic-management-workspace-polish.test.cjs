'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const base = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(base, file), 'utf8');

const script = read('EduOS.App/wwwroot/js/academic/management-workspace.js');
const css = read('EduOS.App/wwwroot/css/academic/management-workspace.css');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const index = read('EduOS.App/Views/AcademicManagement/Index.cshtml');
const sections = read('EduOS.App/Views/AcademicManagement/SectionRegister.cshtml');
const prerequisites = read('EduOS.App/Views/AcademicManagement/Prerequisites.cshtml');
const controller = read('EduOS.App/Controllers/AcademicManagementController.cs');

test('global app header is the only Academic Management page title on all eleven routes', () => {
    assert.match(layout, /<h1>@pageTitle<\/h1>/);
    for (const [name, view] of [['shared nine routes', index], ['sections', sections], ['prerequisites', prerequisites]]) {
        assert.doesNotMatch(view, /<h1\b/i, name + ' repeats the page title');
        assert.doesNotMatch(view, /Academic Management \/ Settings|Academic Management \/ Operations/i, name + ' contains an extra breadcrumb');
        assert.doesNotMatch(view, /<p\s+class="(?:small text-muted|text-muted small)"\s*>[^<]*<\/p>/i,
            name + ' contains unnecessary static help paragraphs');
        assert.match(view, /ViewData\["Title"\]/, name + ' must provide global page title');
    }
    assert.doesNotMatch(script, /academic-intro|insertAdjacentElement\('afterend'/);
    assert.doesNotMatch(css, /academic-page-top|academic-intro/);
    assert.match(index, /id="academicRefresh"/);
    assert.match(sections, /href="\/AcademicManagement\/Batches"/);
    assert.match(prerequisites, /asp-action="Curricula"/);
});

test('all eleven Academic Management sidebar pages share one scoped progressive UX layer', () => {
    assert.match(layout, /Request\.Path\.StartsWithSegments\("\/AcademicManagement"\)/);
    assert.match(layout, /css\/academic\/management-workspace\.css/);
    assert.match(layout, /js\/academic\/management-workspace\.js/);
    assert.match(css, /#academicManagement/);
    assert.match(css, /#academicSectionsPage/);
    assert.match(css, /#prerequisiteWorkspace/);
    assert.match(css, /prefers-reduced-motion/);
    assert.match(css, /max-width: 767\.98px/);
    for (const [action, section] of Object.entries({
        Departments: 'departments', Years: 'years', Terms: 'terms',
        Classes: 'classes', Batches: 'sections', Rooms: 'rooms',
        Subjects: 'subjects', Curricula: 'curricula', Offerings: 'offerings'
    })) {
        assert.match(controller, new RegExp('IActionResult ' + action + '\\(\\) => Feature\\("' + section + '"\\)'));
        assert.ok(index.includes('data-section="' + section + '"'));
    }
    assert.match(controller, /IActionResult Sections\(\) => View\("SectionRegister"\)/);
    assert.match(controller, /IActionResult Prerequisites\(\) => View\(\)/);
    assert.match(sections, /id="sectionCreateForm"/);
    assert.match(prerequisites, /id="prerequisiteForm"/);
    assert.doesNotMatch(script, /speechRecognition|SpeechRecognition|mediaDevices|microphone/i);
});

test('Academic editor disclosure retains existing DOM form and opens before list edit', () => {
    new Function(script);
    const created = [];
    let editCapture;
    const parent = { insertBefore() {} };
    let focusCount = 0;
    const form = {
        id: 'yearForm', parentNode: parent, hidden: false,
        classList: { add() {} },
        querySelector() { return { focus() { focusCount++; } }; }
    };
    const root = {
        id: 'academicManagement', dataset: { activeFeature: 'years' },
        classList: { add() {} },
        contains(node) { return node === form; },
        querySelector() { return null; },
        addEventListener(type, handler, capture) {
            if (type === 'click' && capture) editCapture = handler;
        },
        querySelectorAll() { return []; }
    };
    function createElement(tag) {
        const node = {
            tag, className: '', textContent: '', attributes: {},
            classList: { add() {}, remove() {} },
            listeners: {},
            setAttribute(key, value) { this.attributes[key] = value; },
            addEventListener(type, cb) { this.listeners[type] = cb; },
            append() {},
            scrollIntoView() {}
        };
        created.push(node);
        return node;
    }
    vm.runInNewContext(script, {
        document: {
            documentElement: { lang: 'en-BD' },
            querySelector() { return root; },
            getElementById(id) { return id === 'yearForm' ? form : null; },
            createElement
        },
        location: { hash: '', search: '' },
        URLSearchParams,
        requestAnimationFrame(cb) { cb(); }
    }, { timeout: 1000 });

    const button = created.find(x => x.tag === 'button');
    assert.ok(button, 'disclosure must be keyboard-operable button');
    assert.equal(button.attributes['aria-controls'], 'yearForm');
    assert.equal(form.hidden, true, 'large one-time entry form is initially collapsed');
    button.listeners.click();
    assert.equal(form.hidden, false);
    assert.equal(button.attributes['aria-expanded'], 'true');
    assert.equal(focusCount, 1);
    button.listeners.click();
    assert.equal(form.hidden, true);

    editCapture({
        target: {
            closest() {
                return { textContent: 'Edit', closest() { return { id: 'yearRows' }; } };
            }
        }
    });
    assert.equal(form.hidden, false, 'edit opens form before existing edit handler runs');
    assert.equal(button.attributes['aria-expanded'], 'true');
});

test('Academic editor does not initialize or alter forms on unrelated pages', () => {
    let accessed = false;
    vm.runInNewContext(script, {
        document: {
            querySelector() { return null; },
            get documentElement() { accessed = true; return { lang: 'en-BD' }; }
        }
    }, { timeout: 1000 });
    assert.equal(accessed, false);
});
