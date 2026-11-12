const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/academic', name), 'utf8');

for (const [file, prefix, cancel] of [
    ['levels.js', 'level', 'levelEditCancel'],
    ['rooms.js', 'room', 'roomCancel'],
    ['batches.js', 'batch', 'batchEditCancel']
]) {
    test(file + ': cancel invalidates pending edit lookup and save blocks late lookup', () => {
        const source = read(file);
        const cancelSection = source.slice(source.indexOf("el('" + cancel + "').addEventListener"),
            source.indexOf("el('" + prefix + "EditForm').addEventListener('submit'"));
        assert.match(cancelSection, /\+\+editSequence/);
        assert.match(source, /\+\+editSequence;[^\n]*\n\s*editSaving = true/);
        assert.match(source, /editSaving \|\| !mayReplace\('/);
        assert.match(source, /rowVersion: value\('/);
        assert.match(source, /editGuard\?\.markClean\(\)/);
    });
    test(file + ': stale edit responses must be ignored after session or selection changes', () => {
        const source = read(file);
        assert.match(source, /\+\+editSequence/);
        assert.match(source, /(?:ticket|selection) !== editSequence/);
        assert.match(source, /!sameSession\(\)/);
        assert.match(source, /editGuard\?\.isDirty\(\)/);
    });
}
for (const [file, panel] of [['rooms.js', 'rooms'], ['batches.js', 'sections']]) {
    test(file + ': optional legacy tab control on server-routed pages', () => {
        const source = read(file);
        assert.ok(source.includes('document.querySelector(\'button[data-panel="' + panel + '"]\')?.addEventListener'));
        assert.ok(source.includes('document.querySelector(\'button[data-panel="' + panel + '"]\')?.classList.contains'));
    });
}
