const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('enrollment history is wired to student selection and uses safe paged rendering', () => {
    const js = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/students/enrollment-history.js'), 'utf8');
    const selection = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/wwwroot/js/students/enrollment-management.js'), 'utf8');
    const view = fs.readFileSync(path.join(__dirname,
        '../../EduOS.App/Views/EnrollmentManagement/Index.cshtml'), 'utf8');
    assert.match(js, /URLSearchParams/);
    assert.match(js, /textContent/);
    assert.match(js, /request !== state.pending/);
    assert.match(js, /encodeURIComponent\(reference\)/);
    assert.match(selection, /eduos:enrollment-student-selected/);
    assert.match(view, /id="enrollmentHistoryPanel"/);
    assert.match(view, /enrollment-history\.js/);
});
