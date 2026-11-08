'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const progress=require('../../EduOS.App/wwwroot/js/learning/quiz-progress.js');
const plan=require('../../EduOS.App/wwwroot/js/learning/quiz-study-plan.js');
const row=(id,ref,n,passed)=>({attemptId:id,quizReference:ref,quizTitle:ref,attemptNo:n,passed,obtainedMarks:passed===null?null:5});
test('learner journey: graded review first, pending is not failure, no chapter inference',()=>{
 const p=progress.build([[row(1,'Math',1,false),row(2,'English',1,true),row(3,'Science',1,null)]],3,'en-BD');
 const x=plan.build(p,'en-BD');assert.equal(x.status,'ready');
 assert.deepEqual(x.items.map(y=>y.priority),['review','awaiting-evidence','extend']);
 assert.match(x.note,/not teacher goals/);
});
test('latest ungraded attempt is pending only when prior graded evidence exists',()=>{
 const p=progress.build([[
  row(1,'Math',1,false),
  row(2,'Math',2,null),
  row(3,'Science',1,null)
 ]],3,'en-BD');
 const x=plan.build(p,'en-BD');
 assert.equal(x.status,'ready');
 assert.deepEqual(x.items.map(y=>[y.quizReference,y.priority]),[
  ['Science','awaiting-evidence'],['Math','pending']
 ]);
 assert.match(x.items[1].guidance,/prior graded evidence remains unchanged/);
});
test('bilingual and partial evidence are disclosed',()=>{
 const p=progress.build([[row(1,'Math',1,false)]],3,'bn-BD');
 const x=plan.build(p,'bn-BD');assert.equal(x.complete,false);assert.match(x.note,/আংশিক/);
});
test('untrusted or contradictory progress fails closed',()=>{
 const p=progress.build([[row(1,'A',1,true)]],1,'en-BD');
 assert.equal(plan.build({...p,items:[{...p.items[0],failed:1}]},'en-BD').status,'invalid');
 assert.equal(plan.build({...p,items:[p.items[0],p.items[0]]},'en-BD').status,'invalid');
 assert.equal(plan.build(p,'fr-FR').status,'invalid');
});
