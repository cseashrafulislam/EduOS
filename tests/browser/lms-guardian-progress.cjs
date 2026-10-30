'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/LmsGuardianProgress/Index.cshtml'),'utf8');
const script=path.join(root,'EduOS.App/wwwroot/js/learning/progress-guardian.js');
const SESSION='B'.repeat(64);
const course={courseEnrollmentId:77,learnerName:'Private Learner',
 courseTitle:'Protected Course',publishedLessons:2,completedLessons:1,
 lessonCompletionPercent:50,totalPages:1};
function html(lang){
 return '<!doctype html><html lang="'+lang+'"><head>'+
  '<meta name="request-verification-token" content="csrf"></head>'+
  '<body data-auth-session-reference="'+SESSION+'" data-auth-tenant-id="41">'+
  view.replace(/<script[^>]*src="~\/[^"]*"[^>]*><\/script>/g,'')+
  '</body></html>';
}
async function scenario(browser,lang){
 const page=await browser.newPage({viewport:{width:lang==='bn'?390:1280,height:800},
  acceptDownloads:true});
 let revoked=false,exportRequests=0,lessonExportRequests=0;const errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://localhost/**',route=>{
  const request=route.request(),url=new URL(request.url()),p=url.pathname;
  if(!p.startsWith('/api/'))
   return route.fulfill({contentType:'text/html',body:html(lang)});
  if(p.endsWith('/lessons/export')){
   lessonExportRequests++;
   return route.fulfill({status:revoked?404:200,contentType:revoked?'application/json':'text/csv',
    body:revoked?JSON.stringify({success:false,message:'Consent revoked'}):
     'Lesson,Completion (%)\r\nPublished lesson,100\r\n'});
  }
  if(p==='/api/lms/progress-sharing/mine/export'){
   exportRequests++;
   return route.fulfill({status:revoked?403:200,contentType:revoked?'application/json':'text/csv',
    body:revoked?JSON.stringify({success:false,message:'Consent revoked'}):
     'Learner,Course\r\nPrivate Learner,Protected Course\r\n'});
  }
  const data=p.endsWith('/lessons')?
   {items:[{lessonTitle:'Published lesson',progressPercent:100,isCompleted:true}],totalPages:1}:
   {items:revoked?[]:[course],totalPages:1};
  return route.fulfill({status:200,contentType:'application/json',
   body:JSON.stringify({success:true,data})});
 });
 try{
  await page.goto('http://localhost/LmsGuardianProgress');
  await page.addScriptTag({path:script});
  await page.locator('#lmsGuardianProgressRows').getByText('Private Learner').waitFor();
  const title=await page.locator('[data-progress-i18n="title"]').innerText();
  assert.equal(title,lang==='bn'?'শেয়ার করা শেখার অগ্রগতি':'Shared learning progress');
  await page.locator('#lmsGuardianProgressRows button').click();
  await page.locator('#lmsGuardianLessonRows').getByText('Published lesson').waitFor();
  const lessonDownloadPromise=page.waitForEvent('download');
  await page.locator('#lmsGuardianLessonExport').click();
  const lessonDownload=await lessonDownloadPromise;
  assert.equal(lessonDownload.suggestedFilename(),'lms-guardian-lessons-visible.csv');
  assert.equal(lessonExportRequests,1,'lesson export must use a fresh server request');
  const downloadPromise=page.waitForEvent('download');
  await page.locator('#lmsGuardianProgressExport').click();
  const download=await downloadPromise;
  assert.equal(download.suggestedFilename(),'lms-guardian-progress-visible.csv');
  assert.equal(exportRequests,1,'export must use the server endpoint');
  await page.evaluate(()=>{document.body.dataset.authTenantId='999';});
  await page.waitForFunction(()=>document.querySelector('#lmsGuardianProgressRows').textContent==='');
  assert.equal(await page.locator('#lmsGuardianProgressExport').isDisabled(),true);
  assert.equal(await page.locator('#lmsGuardianLessonHeading').innerText(),'');
  // A fresh login still cannot export once the server revokes consent.
  revoked=false;
  await page.goto('http://localhost/LmsGuardianProgress');
  await page.addScriptTag({path:script});
  await page.locator('#lmsGuardianProgressRows').getByText('Private Learner').waitFor();
  await page.locator('#lmsGuardianProgressRows button').click();
  await page.locator('#lmsGuardianLessonRows').getByText('Published lesson').waitFor();
  revoked=true;
  await page.locator('#lmsGuardianLessonExport').click();
  await page.locator('#lmsGuardianProgressAlert').getByText(
   lang==='bn'?'পাঠ রপ্তানির অনুমতি নেই। পৃষ্ঠা রিফ্রেশ করুন।':'Lesson export access denied. Refresh the page.').waitFor();
  assert.equal(lessonExportRequests,2);
  assert.equal(await page.locator('#lmsGuardianLessonPanel').evaluate(el=>el.classList.contains('d-none')),true);
  await page.locator('#lmsGuardianProgressExport').click();
  await page.locator('#lmsGuardianProgressAlert').getByText(
   lang==='bn'?'রপ্তানির অনুমতি নেই। পৃষ্ঠা রিফ্রেশ করুন।':'Export access denied. Refresh the page.').waitFor();
  assert.equal(await page.locator('#lmsGuardianProgressExport').isDisabled(),true);
  assert.equal(await page.locator('#lmsGuardianProgressRows').innerText().then(x=>x.includes('Private Learner')),false);
  assert.equal(exportRequests,2);
  assert.deepEqual(errors,[]);
  console.log('LMS guardian progress '+lang+' consent/export/session Chromium PASS');
 }finally{await page.close();}
}
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{for(const lang of ['en','bn'])await scenario(browser,lang);}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
