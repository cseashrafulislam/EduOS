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
    assert.match(script,/clientRequestId:clientId\(\)/);
    assert.match(script,/random\.getRandomValues\(new Uint8Array\(16\)\)/);
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
test('teacher timetable uses authorized SQL pages and exports only the displayed page',()=>{
    assert.match(script,/teachers\/.*\/page\?/);
    assert.match(script,/pageSize:'50'/);
    assert.match(script,/state\.teacherEntries=data\.items\|\|\[\]/);
    assert.match(script,/routineTeacherPrev/);
    assert.match(script,/routineTeacherNext/);
    assert.match(script,/page-'\+state\.teacherPage/);
    assert.match(view,/id="routineTeacherPage"/);
    assert.match(view,/Routine field guide/);
});
test('timetable readback exposes CSV reports with spreadsheet formula protection',()=>{
    assert.match(script,/function exportBatch\(/);
    assert.match(script,/function exportTeacher\(/);
    assert.match(script,/\[=\+@-\]/);
    assert.match(view,/id="routineExportBatch"/);
    assert.match(view,/id="routineExportTeacher"/);
});

test('instructor assignment list uses server-side scoped pages instead of a 500-row cap',()=>{
    assert.match(script,/batches\/.*\/assignments\/page\?/);
    assert.match(script,/state\.assignments=data\.items\|\|\[\]/);
    assert.match(script,/routineAssignmentsPrev/);
    assert.match(script,/routineAssignmentsNext/);
    assert.match(view,/id="routineAssignmentSearch"/);
    assert.match(view,/id="routineAssignmentsPage"/);
    assert.doesNotMatch(script,/Showing at most 500 assignments/);
});

test('batch timetable is SQL-paged, guards stale batch responses and exports only visible rows',()=>{
    assert.match(script,/academic-routines\/batches\/.*\/page\?/);
    assert.match(script,/state\.entries=data\.items\|\|\[\]/);
    assert.match(script,/batchRequestSequence/);
    assert.match(script,/routineBatchPrev/);
    assert.match(script,/routineBatchNext/);
    assert.match(script,/routine-batch-'\+batch\.id\+'-page-'\+state\.batchPage/);
    assert.doesNotMatch(script,/const entries=await api/);
    assert.match(view,/id="routineBatchPage"/);
    assert.match(view,/Export current page CSV/);
});
