'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const script = read('EduOS.App/wwwroot/js/students/guardian-history.js');
const entry = read('EduOS.App/wwwroot/js/students/guardians.js');
const view = read('EduOS.App/Views/StudentGuardians/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/StudentGuardiansController.cs');

assert.match(script, /export function setupGuardianHistory/);
assert.match(script, /pageSize: '20'/);
assert.match(script, /encodeURIComponent\(reference\) \+ '\/history\?'/);
assert.match(script, /textContent/);
assert.doesNotMatch(script, /innerHTML\s*=/);
assert.match(entry, /guardianHistory\.refresh\(\)/);
assert.match(view, /id="guardianHistoryRows"/);
assert.match(api, /\[HttpGet\("students\/\{studentReference:guid\}\/history"\)\]/);
