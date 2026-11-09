const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../EduOS.App/wwwroot/js/admissions/offers-journey.js'),'utf8');
(async()=>{
 const m=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
 test('offer journey only progresses for qualified applicants and nonexpired offers',()=>{
  assert.equal(m.nextStage(7,3,null),'review');
  assert.equal(m.nextStage(6,0,null),'issue');
  assert.equal(m.nextStage(6,2,'2020-01-01'),'expired');
  assert.equal(m.nextStage(6,2,'2099-01-01'),'accept');
  assert.equal(m.nextStage(6,3,null),'enroll');
  assert.equal(m.label('enroll',true),'শিক্ষার্থী ভর্তি');
 });
 test('forms reject invalid batch, expiry and roll',()=>{
  assert.equal(m.validateIssue(0,'2099-01-01',0),false);
  assert.equal(m.validateIssue(1,'2099-01-01',0),true);
  assert.equal(m.validateIssue(1,'invalid',0),false);
  assert.equal(m.validateRoll(1,'  '),false);
  assert.equal(m.validateRoll(1,'x'.repeat(51)),false);
  assert.equal(m.validateRoll(1,'12'),true);
 });
})().catch(e=>{console.error(e);process.exitCode=1;});
