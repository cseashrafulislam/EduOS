'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const appRoot = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(appRoot, 'wwwroot/js/shared/current-page-csv.js'), 'utf8');
function loadCsv() {
    const clicked = [];
    const revoked = [];
    const links = [];
    let timer = null;
    const context = {
        window: {}, Blob, URL: {
            createObjectURL: () => 'blob:test',
            revokeObjectURL: value => revoked.push(value)
        }, document: {
            createElement: () => {
                const link = { hidden: false, click: () => clicked.push(link.download), remove: () => {} };
                links.push(link);
                return link;
            }, body: { append: () => {} }
        }, setTimeout: callback => { timer = callback; }
    };
    vm.runInNewContext(script, context, { filename: 'current-page-csv.js' });
    return { csv: context.window.EduOSPageCsv, clicked, revoked, links, runTimer: () => timer?.() };
}
test('CSV cells neutralize formula injection even after whitespace', () => {
    const { csv } = loadCsv();
    for (const expression of ['=SUM(1,1)', '+cmd', '-1+2', '@DDE', '  =HYPERLINK(1)', '\t@import']) {
        assert.equal(csv.csvCell(expression), '"' + "'" + expression + '"');
    }
    assert.equal(csv.csvCell('Applicant'), '"Applicant"');
});
test('CSV output retains Unicode, escapes quotes, and quotes embedded newlines', () => {
    const { csv } = loadCsv();
    assert.equal(csv.serialize(['Applicant', 'Note'], [['আব্দুল', 'Hello "world"\nnext']]),
        '\uFEFF"Applicant","Note"\r\n"আব্দুল","Hello ""world""\nnext"\r\n');
});
test('CSV rejects a column-count mismatch before download', () => {
    const { csv } = loadCsv();
    assert.throws(() => csv.serialize(['A', 'B'], [['only A']]), /columns/);
});
test('CSV refuses unsafe file names and empty page exports', () => {
    const { csv, clicked } = loadCsv();
    assert.equal(csv.download('admissions-page-1.csv', ['A'], []), false);
    assert.throws(() => csv.download('../secrets.csv', ['A'], [['safe']]), /file name/);
    assert.deepEqual(clicked, []);
});
test('CSV download uses a temporary anchor, with URL cleanup', () => {
    const { csv, clicked, revoked, links, runTimer } = loadCsv();
    assert.equal(csv.download('payroll-runs-2026-11-page-1.csv', ['Run'], [['R-1']]), true);
    assert.deepEqual(clicked, ['payroll-runs-2026-11-page-1.csv']);
    assert.equal(links[0].hidden, true);
    runTimer();
    assert.deepEqual(revoked, ['blob:test']);
});
test('Admission export only maps the fetched current page with masked mobile and guards filters', () => {
    const source = fs.readFileSync(path.join(appRoot, 'wwwroot/js/admissions/index.js'), 'utf8');
    assert.match(source, /state\.listPageRows = null;\s*updatePagination\(true\)/);
    assert.match(source, /search === valueOf\('applicationSearch'\) && status === positiveInteger\(valueOf\('statusFilter'\)\)/);
    assert.match(source, /item\.maskedMobile/);
    assert.match(source, /exportButton\.disabled = loading \|\| !state\.listPageRows\?\.length/);
    assert.doesNotMatch(source.slice(source.indexOf('function exportApplicationPage()'), source.indexOf('function renderApplications(')), /fetch\(/);
});
test('Payroll export is admin-only, bounded to current page, and invalidated by filter edits', () => {
    const source = fs.readFileSync(path.join(appRoot, 'wwwroot/js/payroll/index.js'), 'utf8');
    assert.match(source, /if\(!admin \|\| !Array\.isArray\(state\.runPageRows\)/);
    assert.match(source, /state\.runPageRows=null;\s*el\('payrollExportRuns'\)\.disabled=true/);
    assert.match(source, /state\.runPageRows=Array\.isArray\(data\.items\)\?data\.items:\[\]/);
    assert.doesNotMatch(source.slice(source.indexOf('function exportRunPage()'), source.indexOf('function openCancellation(')), /api\(/);
});
