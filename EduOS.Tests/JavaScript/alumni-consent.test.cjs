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
