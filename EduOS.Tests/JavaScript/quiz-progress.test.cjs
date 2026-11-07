'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const p=require('../../EduOS.App/wwwroot/js/learning/quiz-progress.js');
const row=(id,ref,attemptNo,passed)=>({attemptId:id,quizReference:ref,quizTitle:ref,attemptNo,passed,obtainedMarks:passed===null?null:4});
test('groups distinct canonical quiz references without conflating attempts',()=>{
 const x=p.build([[row(1,'A',1,false),row(2,'B',1,true)],[row(3,'A',2,true)]],3,'en-BD');
 assert.equal(x.status,'ready');assert.equal(x.items.length,2);assert.equal(x.items.find(y=>y.quizReference==='A').latestPassed,true);
});
test('missing grades remain pending rather than low proficiency',()=>{
 const x=p.build([[row(1,'A',1,null)]],1,'bn-BD');assert.equal(x.items[0].graded,0);assert.equal(x.items[0].failed,0);
});
test('partial evidence is disclosed',()=>{
 const x=p.build([[row(1,'A',1,false)]],4,'en-BD');assert.equal(x.complete,false);assert.equal(x.scanned,1);
});
test('duplicate, malformed and contradictory records fail closed',()=>{
 assert.equal(p.build([[row(1,'A',1,false),row(1,'B',1,true)]],2,'en-BD').status,'invalid');
 assert.equal(p.build([[{...row(1,'A',1,false),quizReference:''}]],1,'en-BD').status,'invalid');
 assert.equal(p.build([[{...row(1,'A',1,false),obtainedMarks:-1}]],1,'en-BD').status,'invalid');
 assert.equal(p.build([[row(1,'A',1,false)]],0,'en-BD').status,'invalid');
});
