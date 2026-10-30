'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const script=fs.readFileSync(path.resolve(__dirname,
  '../../EduOS.App/wwwroot/js/learning/lms-public-verification.js'),'utf8');
const token='Abcdefghijklmnopqrstuvwxyz0123456789_-ABCDE';
assert.equal(token.length,43);
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 const page=await browser.newPage();
 const calls=[],errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://localhost/**',route=>{
  const request=route.request(),url=new URL(request.url());
  if(url.pathname==='/api/public/lms-certificate-verification'){
   calls.push({method:request.method(),path:url.pathname,body:request.postDataJSON()});
   return route.fulfill({status:200,contentType:'application/json',
    body:JSON.stringify({success:true,data:{status:'Valid',
     issuerName:'Example Institute',issuedAt:'2026-10-29T12:00:00Z',revision:1}})});
  }
  return route.fulfill({status:200,contentType:'text/html',
   body:'<!doctype html><html lang="en"><body><button id="verify">Verify</button>'+
    '<div id="result" role="status"></div></body></html>'});
 });
 try{
  await page.goto('http://localhost/LmsPublicVerification#token='+token);
  await page.addScriptTag({content:script});
  await page.waitForFunction(()=>document.getElementById('result').textContent.includes('VALID'));
  assert.equal(new URL(page.url()).hash,'','bearer must be removed from browser history');
  assert.equal(calls.length,1);
  assert.equal(calls[0].method,'POST');
  assert.equal(calls[0].path,'/api/public/lms-certificate-verification');
  assert.equal(calls[0].body.token,token);
  assert.ok(!(await page.locator('#result').innerText()).includes(token));
  await page.reload();
  await page.addScriptTag({content:script});
  await page.waitForFunction(()=>document.getElementById('verify').disabled);
  assert.equal(calls.length,1,'refresh must not replay a consumed bearer');
  assert.deepEqual(errors,[]);
  console.log('LMS anonymous verification bearer/history browser PASS');
 }finally{await page.close();await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
