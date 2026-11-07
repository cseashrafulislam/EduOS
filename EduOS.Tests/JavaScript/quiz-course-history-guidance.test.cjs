'use strict';
const assert=require('node:assert/strict');
const test=require('node:test');
const g=require('../../EduOS.App/wwwroot/js/learning/quiz-study-guidance.js');
const row=(id,passed)=>({attemptId:id,quizTitle:'Math',passed,obtainedMarks:passed===null?null:4});
test('authorized bounded course history aggregates across pages',()=>{
 const x=g.summarize([[row(1,false)],[row(2,true)]],2,'en-BD');
 assert.equal(x.complete,true);assert.equal(x.scanned,2);assert.equal(x.items.length,1);
});
test('partial evidence is explicitly marked',()=>{
 const x=g.summarize([[row(1,false)]],4,'bn-BD');assert.equal(x.complete,false);
});
test('duplicate, inconsistent and malformed history fail closed',()=>{
 assert.equal(g.summarize([[row(1,false)],[row(1,true)]],2,'en-BD').status,'invalid');
 assert.equal(g.summarize([[row(1,false)]],0,'en-BD').status,'invalid');
 assert.equal(g.summarize([[{...row(1,false),obtainedMarks:-1}]],1,'en-BD').status,'invalid');
});
test('no graded evidence is not low proficiency',()=>{
 const x=g.summarize([[row(1,null)]],1,'en-BD');
 assert.equal(x.status,'no-evidence');assert.equal(x.items.length,0);
});
