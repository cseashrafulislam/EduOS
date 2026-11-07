'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const progress=require('../../EduOS.App/wwwroot/js/learning/quiz-progress.js');
const plan=require('../../EduOS.App/wwwroot/js/learning/quiz-study-plan.js');
const row=(id,ref,n,passed)=>({attemptId:id,quizReference:ref,quizTitle:ref,attemptNo:n,passed,obtainedMarks:passed===null?null:5});
test('authorized history to bilingual CSV, preserving partial evidence',()=>{
 const p=progress.build([[row(1,'Math',1,false)]],2,'bn-BD');
 const x=plan.build(p,'bn-BD');
 const csv=plan.exportCsv({...x,scanned:p.scanned,totalCount:p.totalCount},'bn-BD');
 assert.ok(csv.startsWith('\uFEFF'));assert.match(csv,/আংশিক|partial/);
 assert.match(csv,/Math/);
});
test('spreadsheet formula injection is escaped, not evaluated',()=>{
 const p=progress.build([[row(1,'=HYPERLINK("evil")',1,false)]],1,'en-BD');
 const x=plan.build(p,'en-BD');
 const csv=plan.exportCsv({...x,scanned:p.scanned,totalCount:p.totalCount},'en-BD');
 assert.match(csv,/"'=HYPERLINK/);assert.ok(!csv.includes('\n=HYPERLINK'));
});
test('untrusted export evidence is rejected',()=>{
 const p=progress.build([[row(1,'Math',1,false)]],1,'en-BD');
 const x=plan.build(p,'en-BD');
 assert.equal(plan.exportCsv({...x,scanned:3,totalCount:1},'en-BD'),null);
 assert.equal(plan.exportCsv({...x,scanned:1,totalCount:1},'fr-FR'),null);
 assert.equal(plan.exportCsv({...x,scanned:1,totalCount:1,items:[x.items[0],x.items[0]]},'en-BD'),null);
});
