'use strict';
// Actual authorized CSV + XLSX bytes and read-only financial report response.
// All requests use synthetic users and the isolated SQL demo; no mutations.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {chromium}=require('playwright');
const origin='https://qa.eduos.test:7243';
const sha=process.env.GITHUB_SHA||'';
const password=process.env.QA_DEMO_PASSWORD||'';
assert.equal(process.env.EDUOS_QA_ORIGIN,origin,'Only disposable staging origin is permitted');
assert(/^[0-9a-f]{40}$/i.test(sha),'Exact SHA required');
assert(password.length>=16,'Synthetic demo credentials required');
async function signIn(browser,role){
    const context=await browser.newContext({ignoreHTTPSErrors:true,
        serviceWorkers:'block',acceptDownloads:false});
    const page=await context.newPage();
    const response=await page.goto(origin+'/Account/Login',{waitUntil:'domcontentloaded'});
    assert.equal(response.status(),200,'Identity form unavailable');
    await page.locator('#userNameOrEmail').fill('demo.'+role+'@eduos.example.invalid');
    await page.locator('#password').fill(password);
    await page.locator('#loginBtn').click();
    await page.waitForURL(u=>u.pathname.toLowerCase()!=='/account/login',
        {waitUntil:'domcontentloaded',timeout:25000});
    return context;
}
async function get(ctx,p){
    assert(p.startsWith('/')&&!p.startsWith('//'),'Same-origin path required');
    return ctx.request.get(origin+p,{failOnStatusCode:false,timeout:20000});
}
async function main(){
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    const checks=[],denials=[];
    try{
        const admin=await signIn(browser,'tenantadmin');
        try{
            const xlsx=await get(admin,'/api/student-imports/template');
            assert.equal(xlsx.status(),200,'Student XLSX template failed');
            assert.match(String(xlsx.headers()['content-type']||''),
                /application\/vnd\.openxmlformats-officedocument\.spreadsheetml\.sheet/i);
            assert.match(String(xlsx.headers()['content-disposition']||''),/\.xlsx/i);
            const workbook=await xlsx.body();
            assert(workbook.length>=1000&&workbook.length<2*1024*1024,
                'Student XLSX template should be small and nonempty');
            assert(workbook.subarray(0,4).equals(Buffer.from([0x50,0x4b,0x03,0x04])),
                'Invalid OOXML ZIP signature');
            checks.push({route:'/StudentImports/Index',action:'excel',
                proof:'student-xlsx-template-200-zip-mime'});

            const csv=await get(admin,
                '/api/students/export-page?page=1&pageSize=25&includeSection=true');
            assert.equal(csv.status(),200,'Student directory CSV export failed');
            assert.match(String(csv.headers()['content-type']||''),/text\/csv/i);
            assert.match(String(csv.headers()['content-disposition']||''),/\.csv/i);
            const csvText=(await csv.body()).toString('utf8');
            assert(csvText.length>25&&csvText.length<256*1024,
                'Student directory export must be bounded and nonempty');
            assert(csvText.split(/\r?\n/).filter(Boolean).length<=26,
                'More rows exported than the authorized single page');
            assert(/Student Code|শিক্ষার্থী কোড/.test(csvText.split(/\r?\n/,1)[0]),
                'Student directory CSV missing expected header');
            checks.push({route:'/Students/Index',action:'excel',
                proof:'student-directory-csv-paged-200-mime'});

            const trial=await get(admin,
                '/api/finance/accounting/reports/trial-balance?asOfDate=2026-11-15');
            assert.equal(trial.status(),200,'Trial Balance API failed');
            const payload=await trial.json();
            assert.equal(payload?.success,true,'Trial Balance returned unsuccessful report');
            assert(payload?.data!==null&&payload?.data!==undefined,
                'Trial Balance response lacks read model');
            checks.push({route:'/AccountingReports/Index',action:'report',
                proof:'tenant-scoped-trial-balance-json-200'});
        }finally{await admin.close();}

        const student=await signIn(browser,'student');
        try{
            for(const url of [
                '/api/student-imports/template',
                '/api/students/export-page?page=1&pageSize=25',
                '/api/finance/accounting/reports/trial-balance?asOfDate=2026-11-15']){
                const res=await get(student,url);
                const denied=res.status()===401||res.status()===403;
                assert(denied,'Student must not retrieve tenant-admin export/report: '+
                    new URL(origin+url).pathname+' HTTP '+res.status());
                denials.push({path:new URL(origin+url).pathname,status:res.status(),passed:denied});
            }
        }finally{await student.close();}
        assert.equal(checks.length,3);
        assert.equal(denials.length,3);
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-export-report-qa.json',JSON.stringify({
            sha,environment:'disposable SQL HTTPS staging',
            scope:'actual limited CSV, OOXML XLSX and Trial Balance read-model API; NOT all exports, Print or PDF',
            checks,denials,productionReleaseCertified:false
        },null,2));
        console.log('PASS: 3 real export/report reads (CSV, XLSX, GL), 3 unauthorized denials.');
    }finally{await browser.close();}
}
main().catch(e=>{console.error('Read-only export/report QA failed:',String(e?.message||e).slice(0,250));
    process.exitCode=1;});
