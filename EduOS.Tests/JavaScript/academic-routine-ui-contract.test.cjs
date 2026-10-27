'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const app=path.resolve(__dirname,'../../EduOS.App');
const script=fs.readFileSync(path.join(app,'wwwroot/js/academic/routines.js'),'utf8');
const view=fs.readFileSync(path.join(app,'Views/AcademicRoutines/Index.cshtml'),'utf8');

test('routine assignment and entry submit canonical offering and instructor references with effective dates',()=>{
    assert.match(script,/employeeReference:employee/);
    assert.match(script,/subjectOfferingReference:offering/);
    assert.match(script,/subjectOfferingReference:assignment\.subjectOfferingReference/);
    assert.match(script,/effectiveFrom:from,effectiveTo:to\|\|null/);
    assert.match(script,/clientRequestId:crypto\.randomUUID\(\)/);
    assert.doesNotMatch(script,/academicBatchId:Number\(batch\.id\),subjectId/);
    assert.doesNotMatch(script,/remarks:null/);
    assert.match(view,/id="routineAssignFrom"/);
    assert.match(view,/id="routineEntryFrom"/);
});
test('routine subject picker uses authorized paged offering lookup and teacher public reference',()=>{
    assert.match(script,/academic-instruction\/batches\/.*lesson-offerings/);
    assert.match(script,/state\.offerings=data\.items\|\|\[\]/);
    assert.match(script,/id:x\.employeeReference/);
    assert.match(view,/id="routineOfferingSearch"/);
    assert.match(view,/id="routineOfferingsNext"/);
});
test('timetable readback exposes CSV reports with spreadsheet formula protection',()=>{
    assert.match(script,/function exportBatch\(/);
    assert.match(script,/function exportTeacher\(/);
    assert.match(script,/\[=\+@-\]/);
    assert.match(view,/id="routineExportBatch"/);
    assert.match(view,/id="routineExportTeacher"/);
});
