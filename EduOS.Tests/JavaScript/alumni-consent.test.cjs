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
