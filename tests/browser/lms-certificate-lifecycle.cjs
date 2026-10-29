'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/Learning/Certificates.cshtml'),'utf8');
const certScript=path.join(root,'EduOS.App/wwwroot/js/learning/certificates.js');
const shareScript=path.join(root,'EduOS.App/wwwroot/js/learning/certificate-sharing.js');
const SESSION='A'.repeat(64);
const FIRST='11111111-2222-4333-8444-555555555551';
const SECOND='11111111-2222-4333-8444-555555555552';
const COURSE='aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const issued='2026-10-29T12:00:00Z';
function certificate(reference=FIRST,revoked=false){
 return {reference,courseEnrollmentId:41,courseReference:COURSE,
  studentName:'Private Learner Name',studentCode:'PRIVATE-STUDENT-001',
  courseTitle:'Protected Course',courseCode:'LMS-01',revision:reference===FIRST?1:2,
  issuedAt:issued,completedAt:issued,isRevoked:revoked,rowVersion:'AQIDBA==',
  requiredLessons:1,completedLessons:1,requiredAssignments:0,gradedAssignments:0,
  requiredQuizzes:0,passedQuizzes:0};
}
function documentFor(mode){
 return '<!doctype html><html lang="en"><head><meta name="request-verification-token" content="csrf"></head>'+
  '<body data-auth-session-reference="'+SESSION+'" data-auth-tenant-id="101">'+
  view.replace(/data-student-mode="[^"]*"/,'data-student-mode="'+(mode==='student')+'"')
   .replace(/data-guardian-mode="[^"]*"/,'data-guardian-mode="'+(mode==='guardian')+'"')
   // Razor asset URLs are resolved by ASP.NET in production. Do not load them as
   // literal "~/" paths in this isolated fixture; scripts are injected explicitly.
   .replace(/<script[^>]*src="~\/[^"]*"[^>]*><\/script>/g,'')+
  '</body></html>';
}
async function scenario(browser,mode){
 const page=await browser.newPage({viewport:{width:mode==='student'?390:1280,height:850}});
 const errors=[],writes=[];let revoked=false,reissued=false;
 page.on('pageerror',error=>errors.push(error.message));
 page.on('dialog',dialog=>dialog.accept(dialog.type()==='prompt'?'Incorrect award':undefined));
 await page.route('http://localhost/**',route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  if(!p.startsWith('/api/'))return route.fulfill({contentType:'text/html',body:documentFor(mode)});
  if(req.method()==='POST'){
   writes.push({path:p,csrf:req.headers()['requestverificationtoken']});
   if(p.endsWith('/revoke'))revoked=true;
   else if(p==='/api/lms/certificates')reissued=true;
  }
  let data;
  if(p==='/api/lms/courses')data={items:[{reference:COURSE,title:'Protected Course',code:'LMS-01'}],totalPages:1};
  else if(p.endsWith('/mine')||p.endsWith('/course/'+COURSE))
   data={items:[certificate(reissued?SECOND:FIRST,revoked&&!reissued)],totalPages:1};
  else if(p.endsWith('/guardians'))data=[{guardianLinkId:71,guardianName:'Private Guardian Name',relationCode:'Parent'}];
  else if(p.endsWith('/grants'))data=[{reference:SECOND,guardianLinkId:71,guardianName:'Private Guardian Name',rowVersion:'AQIDBA=='}];
  else if(p.endsWith('/history'))data=[];
  else if(p.endsWith('/revoke'))data=certificate(FIRST,true);
  else if(p==='/api/lms/certificates')data=certificate(SECOND,false);
  else if(p==='/api/lms/certificates/'+FIRST||p==='/api/lms/certificate-sharing/'+FIRST)
   data=certificate(FIRST,revoked);
  else if(p==='/api/lms/certificates/'+SECOND)data=certificate(SECOND,false);
  else data=[];
  return route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({success:true,data})});
 });
 try{
  await page.goto('http://localhost/Learning/Certificates');
  await page.addScriptTag({path:certScript});
  if(mode==='student')await page.addScriptTag({path:shareScript});
  if(mode==='instructor'){
   await page.locator('#lmsCertificateCourse option').nth(1).waitFor({state:'attached'});
   await page.locator('#lmsCertificateCourse').selectOption(COURSE);
  }
  await page.locator('#lmsCertificateRows tr button').first().click();
  await page.locator('#lmsCertificateStudent').getByText('Private Learner Name').waitFor();
  const print=page.locator('#lmsCertificatePrint');
  assert.equal(await print.getAttribute('href'),
   (mode==='guardian'?'/LmsGuardianCertificates/Print?reference=':
    '/Learning/CertificatePrint?reference=')+FIRST);
  if(mode==='student'){
   await page.locator('#lmsCertificateGrants').getByText('Private Guardian Name').waitFor();
   assert.equal(await page.locator('#lmsCertificateGuardian option').count(),1);
  }
  if(mode==='instructor'){
   await page.locator('#lmsCertificateRevoke').click();
   await page.waitForFunction(()=>document.getElementById('lmsCertificateStatus').textContent==='REVOKED');
   assert.equal(await print.getAttribute('href'),null);
   await page.locator('#lmsCertificateReissue').click();
   await page.waitForFunction(()=>document.getElementById('lmsCertificatePrint').getAttribute('href')?.includes('555555555552'));
   assert.equal(writes.length,2);
   assert.ok(writes.every(x=>x.csrf==='csrf'),'mutation requires CSRF');
  }
  await page.evaluate(()=>{document.body.dataset.authTenantId='202';});
  await page.waitForFunction(()=>document.getElementById('lmsCertificateStudent').textContent==='');
  assert.equal(await page.locator('#lmsCertificatePrint').getAttribute('href'),null);
  assert.equal(await page.locator('#lmsCertificateRows').innerText().then(s=>s.includes('PRIVATE-STUDENT-001')),false);
  if(mode==='student'){
   assert.equal(await page.locator('#lmsCertificateGrants').innerText(),'');
   assert.equal(await page.locator('#lmsCertificateGuardian option').count(),0);
   assert.equal(await page.locator('#lmsCertificateGrant').isDisabled(),true);
  }
  assert.deepEqual(errors,[]);
  console.log('LMS certificate '+mode+' session/print/ownership browser PASS');
 }finally{await page.close();}
}
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{for(const mode of ['student','guardian','instructor'])await scenario(browser,mode);}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
