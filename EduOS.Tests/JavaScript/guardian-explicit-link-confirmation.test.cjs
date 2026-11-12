'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root,p),'utf8');
test('guardian creation and sibling reuse require separate explicit confirmations', () => {
 const view=read('EduOS.App/Views/StudentGuardians/Index.cshtml');
 const create=read('EduOS.App/wwwroot/js/students/guardian-save.js');
 const reuse=read('EduOS.App/wwwroot/js/students/guardian-reuse.js');
 for (const id of ['guardianExistingConfirm','guardianCreateConfirm']) assert.match(view,new RegExp('id="'+id+'"[^>]*required'));
 assert.match(create,/guardianCreateConfirm'\)\.checked/);
 assert.match(reuse,/guardianExistingConfirm'\)\.checked/);
 assert.match(reuse,/state\.student\.guardians\?\.some/);
 assert.match(reuse,/guardianReference/);
});
test('student guardian search invalidates stale detail selection and clears stale matches on edit',()=>{
 const search=read('EduOS.App/wwwroot/js/students/guardian-search.js');
 assert.match(search,/\+\+state\.selectionVersion;[\s\S]*const search/);
 assert.match(search,/state\.busy \|\| state\.selecting \|\| !state\.sameSession\(\) \|\| !state\.confirmStudentSwitch\(\)/);
 assert.match(search,/guardianMatches'\)\.replaceChildren\(\)/);
});
