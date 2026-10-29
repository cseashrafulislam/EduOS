'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const root = resolve(__dirname, '../../');
const read = path => readFileSync(resolve(root, path), 'utf8');

test('grade scheme credit-based GPA mode is explicit, opt-in and persisted', () => {
    const entity = read('EduOS.Core/Entities/Assessment/AssessmentEntities.cs');
    const migration = read('EduOS.Persistence/Migrations/20261029190000_GradeSchemeCreditWeightedGpa.cs');
    const service = read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs');
    const view = read('EduOS.App/Views/AssessmentAdministration/GradeSchemes.cshtml');
    const js = read('EduOS.App/wwwroot/js/assessment/grade-schemes.js');
    assert.match(entity, /public bool UseCreditWeightedGpa/);
    assert.match(migration, /defaultValue: false/);
    assert.match(service, /registration!\.CreditHoursSnapshot \?\? item\.CreditHours/);
    assert.match(service, /creditPoints \+= credits \* subjectGrade\.GradePoint/);
    assert.match(service, /creditPoints \/ earnedCredits/);
    assert.match(view, /gradeSchemeGpaMode/);
    assert.match(js, /useCreditWeightedGpa: el\('gradeSchemeGpaMode'\)\.value === 'credits'/);
});
