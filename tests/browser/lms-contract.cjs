'use strict';
const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const js=fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/learning/index.js'),'utf8');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/Learning/Index.cshtml'),'utf8');
test('LMS authoring uses canonical course, lesson and assignment DTO fields',()=>{
 for(const field of ['academicProgramId','primaryInstructorReference','code:','contentUrl','displayOrder','maxMarks','instructions','isPublished','rowVersion'])
  assert.ok(js.includes(field),'missing '+field);
 for(const legacy of ['/sync-enrollment','/lessons/complete','videoUrl:','orderNo:','totalMark:','teacherId:','sectionId:'])
  assert.ok(!js.includes(legacy),'legacy contract '+legacy);
});
test('LMS API calls use POST on create and PUT with reference on update',()=>{
 for(const resource of ['courses','lessons','assignments'])
  assert.ok(js.includes("'/api/lms/"+resource+"'+(x?'/'+encodeURIComponent(x.reference):'')"),resource);
 assert.match(js,/x\?'PUT':'POST'/);
 assert.ok(js.includes('RequestVerificationToken'));
 assert.ok(js.includes("credentials:'same-origin'"));
});
test('LMS view and JS have matching unique element ids',()=>{
 const ids=[...view.matchAll(/id="([^"]+)"/g)].map(m=>m[1]);
 assert.equal(ids.length,new Set(ids).size);
 for(const id of [...js.matchAll(/\$\('([^']+)'\)/g)].map(m=>m[1]))
  assert.ok(ids.includes(id),'missing element '+id);
 for(const id of ['learningCode','learningProgram','learningCoursePage','learningLessonPublished','learningAssignmentPublished'])
  assert.ok(ids.includes(id));
});
test('Student progress uses canonical endpoint without sending arbitrary enrollment id',()=>{
 assert.ok(js.includes("'/api/lms/lessons/progress','POST'"));
 assert.ok(js.includes('lessonReference:lesson.reference'));
 assert.ok(!js.includes('courseEnrollmentId:'));
});
