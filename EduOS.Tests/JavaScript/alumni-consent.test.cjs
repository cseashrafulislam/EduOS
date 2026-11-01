const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const script = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/alumni/index.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/Views/Alumni/Index.cshtml'), 'utf8');

test('alumni consent is opt-in, revocable and CSRF protected', () => {
    assert.match(script, /directoryOptIn: el\('alumniOptIn'\)\.checked/);
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(script, /rowVersion: state\.selected\.rowVersion/);
    assert.match(script, /clientRequestId: state\.requestId/);
    assert.match(view, /id="alumniOptIn"/);
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
});

test('event invitation opt-ins are separate, off unless selected and require each channel destination', () => {
    for (const [id, field] of [
        ['alumniEmailInvites', 'invitationEmailOptIn'], ['alumniSmsInvites', 'invitationSmsOptIn']
    ]) {
        assert.match(view, new RegExp(`id="${id}" type="checkbox"`));
        assert.match(script, new RegExp(`${field}: el\\('${id}'\\)\\.checked`));
        assert.match(script, new RegExp(`el\\('${id}'\\)\\.checked = Boolean\\(item\\?\\.${field}\\)`));
    }
    assert.match(script, /el\('alumniEmail'\)\.required = el\('alumniEmailInvites'\)\.checked/);
    assert.match(script, /el\('alumniPhone'\)\.required = el\('alumniSmsInvites'\)\.checked/);
    assert.match(script, /directory \|\| el\('alumniEmailInvites'\)\.checked/);
    assert.match(script, /directory \|\| el\('alumniSmsInvites'\)\.checked/);
    assert.match(script, /emailInvites: \[/);
    assert.match(script, /smsInvites: \[/);
});

test('alumni directory is bounded, accessible, bilingual and uses safe text sinks', () => {
    assert.match(script, /pageSize: 20/);
    assert.match(script, /\.textContent = value/);
    assert.doesNotMatch(script, /innerHTML/);
    assert.match(script, /bn-BD/);
    assert.match(view, /aria-live="polite"/);
    assert.match(view, /table-responsive/);
    assert.match(view, /id="alumniExport"/);
});

test('alumni CSV links retain server-selected bilingual headings and scoped report endpoints', () => {
    const controller = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Controllers/Api/AlumniApiController.cs'), 'utf8');
    const manual = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/platform/page-context-help.js'), 'utf8');
    assert.match(script, /\/api\/alumni\/directory\.csv\?/);
    assert.match(script, /\/api\/alumni\/events\/report\.csv\?/);
    assert.match(controller, /CultureInfo\.CurrentUICulture\.TwoLetterISOLanguageName == "bn"/);
    assert.match(controller, /DirectoryHeader\(bengali\)/);
    assert.match(controller, /EngagementHeader\(bengali\)/);
    assert.match(controller, /Csv\(EventStatusLabel\(item\.Status, bengali\)\)/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(manual, /CSV headings and event status labels follow the selected language/);
    assert.match(manual, /CSV শিরোনাম ও অনুষ্ঠানের অবস্থা নির্বাচিত ভাষায় থাকে/);
});


test('directory search/page races cannot restore stale contacts or CSV scope', async () => {
    const start = script.indexOf('async function loadDirectory()');
    const end = script.indexOf("el('alumniGraduate').addEventListener", start);
    assert.ok(start > 0 && end > start);
    const rows = {
        data: [],
        replaceChildren() { this.data = []; },
        insertRow() {
            const row = { cells: [], insertCell() {
                const cell = { textContent: '', colSpan: 1 };
                this.cells.push(cell); return cell;
            } };
            this.data.push(row); return row;
        }
    };
    const elements = {
        alumniSearch: { value: 'old' }, alumniRows: rows, alumniPage: { textContent: '' },
        alumniPrev: { disabled: false }, alumniNext: { disabled: false },
        alumniExport: { href: '/stale', removeAttribute(name) {
            if (name === 'href') this.href = null;
        } }
    };
    const state = { page: 1, total: 0, directoryRequestSerial: 0 };
    const pending = [];
    const api = url => new Promise((resolve, reject) => pending.push({ url, resolve, reject }));
    const loadDirectory = new Function('state', 'root', 'el', 'api', 'bn', 'URLSearchParams',
        script.slice(start, end) + '; return loadDirectory;')(
        state, { dataset: { admin: 'true' } }, id => elements[id], api, false, URLSearchParams);
    const oldPage = loadDirectory();
    assert.equal(elements.alumniExport.href, null, 'old CSV must be disabled while loading');
    assert.equal(elements.alumniPrev.disabled, true);
    state.page = 2;
    elements.alumniSearch.value = 'new';
    const newPage = loadDirectory();
    pending[1].resolve({ totalCount: 21, items: [{
        fullName: 'New', studentCode: 'N', contactEmail: 'new@example.org',
        completedAtUtc: '2026-10-01T00:00:00Z'
    }] });
    await newPage;
    pending[0].resolve({ totalCount: 1, items: [{
        fullName: 'Old', studentCode: 'O', contactEmail: 'old@example.org',
        completedAtUtc: '2026-10-01T00:00:00Z'
    }] });
    await oldPage;
    assert.equal(rows.data[0].cells[0].textContent, 'New');
    assert.match(elements.alumniExport.href, /page=2/);
    assert.match(elements.alumniExport.href, /search=new/);
    assert.equal(state.total, 21);
    state.page = 3;
    const failedPage = loadDirectory();
    pending[2].reject(new Error('network'));
    await assert.rejects(failedPage, /network/);
    assert.equal(elements.alumniExport.href, null);
    assert.match(rows.data[0].cells[0].textContent, /retry/);
    assert.equal(elements.alumniNext.disabled, true);
});
