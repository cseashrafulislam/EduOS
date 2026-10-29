const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const controller = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/LearningController.cs'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Learning/CertificatePrint.cshtml'), 'utf8');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/learning/certificate-print.js'), 'utf8');

test('certificate print requires a fresh scoped service read and refuses revoked certificates', () => {
    assert.match(controller, /GetCertificateAsync\(reference, ct\)/);
    assert.match(controller, /result\.Data\.IsRevoked/);
    assert.match(controller, /return NotFound\(\)/);
    assert.match(controller, /ResponseCache\(NoStore = true/);
});

test('certificate document uses encoded Razor values and avoids exposing private URLs', () => {
    assert.match(view, /@Model\.StudentName/);
    assert.match(view, /@Model\.Reference/);
    assert.match(view, /noindex,nofollow,noarchive/);
    assert.doesNotMatch(view, /Html\.Raw|innerHTML|data:application|localStorage/);
});

test('print control is user initiated and browser script parses', () => {
    assert.doesNotThrow(() => new vm.Script(script));
    assert.match(script, /addEventListener\('click', \(\) => window\.print\(\)\)/);
    assert.doesNotMatch(script, /onload|setTimeout/);
});
