const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '../../', p), 'utf8');
const ui = read('EduOS.App/wwwroot/js/assessment/grade-schemes.js');
const page = read('EduOS.App/Views/AssessmentAdministration/GradeSchemes.cshtml');
const calculation = read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.CumulativeGpa.cs');
const setup = read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.Setup.cs');

test('repeat-course policy is explicitly opted in on a new immutable credit scheme', () => {
    assert.match(page, /gradeSchemeRepeatPolicy/);
    assert.match(page, /Reject until policy approved/);
    assert.match(page, /Use latest attempt/);
    assert.match(page, /Use highest grade point/);
    assert.match(ui, /repeatCourseGradePolicy: Number/);
    assert.match(ui, /field\.disabled = el\('gradeSchemeGpaMode'\)\.value !== 'credits'/);
    assert.match(setup, /Enum\.IsDefined\(request\.RepeatCourseGradePolicy\)/);
    assert.match(setup, /RepeatCourseGradePolicy = request\.RepeatCourseGradePolicy/);
});

test('cumulative GPA selects latest or highest grade per curriculum course', () => {
    assert.match(calculation, /RepeatCourseGradePolicy\.Reject/);
    assert.match(calculation, /RepeatCourseGradePolicy\.HighestGradePoint/);
    assert.match(calculation, /GroupBy\(x => x\.CurriculumSubjectId\)/);
    assert.match(calculation, /ThenByDescending\(x => x\.AssessmentEndDate\)/);
    assert.match(calculation, /numerator \+= selected\.CreditHours \* selected\.GradePoint/);
});
