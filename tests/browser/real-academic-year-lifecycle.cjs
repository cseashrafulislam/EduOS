'use strict';
// Isolated, disposable SQL academic-year Create -> Edit -> Archive -> Reload.
// The original Year/Term acceptance fixtures remain untouched.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {chromium}=require('playwright');
const origin='https://qa.eduos.test:7243';
const sha=process.env.GITHUB_SHA||'',pwd=process.env.QA_DEMO_PASSWORD||'';
assert.equal(process.env.EDUOS_QA_ORIGIN,origin);
assert(/^[0-9a-f]{40}$/i.test(sha)&&pwd.length>=16,
    'Exact commit and private synthetic QA identity required');
const original=process.env.QA_YEAR_NAME||'';
assert(original.startsWith('QA Academic Year '),'Only a synthetic test year is permitted');
const createName='QA Workflow Archive '+original.slice('QA Academic Year '.length);
const editedName=createName+' Edited';
async function login(page){
    const res=await page.goto(origin+'/Account/Login',{waitUntil:'domcontentloaded'});
    assert.equal(res?.status(),200);
    await page.locator('#userNameOrEmail').fill('demo.tenantadmin@eduos.example.invalid');
    await page.locator('#password').fill(pwd);
    await page.locator('#loginBtn').click();
    await page.waitForURL(u=>u.pathname.toLowerCase()!=='/account/login',
        {waitUntil:'domcontentloaded',timeout:25000});
}
async function yearRow(page,name){
    return page.locator('#yearRows tr').filter({hasText:name});
}
async function waitYear(page,name,exists=true){
    await page.waitForFunction(({name,exists})=>
        !!window.eduosAcademicManagement?.state?.years?.some(y=>y.name===name)===exists,
        {name,exists},{timeout:25000});
}
async function openForm(page){
    if(!(await page.locator('#yearForm').isVisible()))
        await page.locator('[aria-controls="yearForm"]').click();
}
async function main(){
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try{
        const ctx=await browser.newContext({ignoreHTTPSErrors:true,
            serviceWorkers:'block',acceptDownloads:false});
        const page=await ctx.newPage();
        const rejected=[];
        page.on('response',res=>{
            const url=new URL(res.url());
            if(url.origin===origin&&url.pathname.startsWith('/api/institution-onboarding/academic-year')
                &&res.request().method()!=='GET'&&res.status()>=400)
                rejected.push({method:res.request().method(),status:res.status()});
        });
        await login(page);
        const response=await page.goto(origin+'/AcademicManagement/Years',
            {waitUntil:'domcontentloaded'});
        assert.equal(response?.status(),200);
        await page.waitForFunction(()=>Array.isArray(window.eduosAcademicManagement?.state?.years),
            null,{timeout:25000});
        await waitYear(page,createName,false);
        await openForm(page);
        await page.locator('#yearName').fill(createName);
        await page.locator('#yearStart').fill('2028-01-01');
        await page.locator('#yearEnd').fill('2028-12-31');
        await page.locator('#yearForm button[type="submit"]').click();
        await waitYear(page,createName);
        const record=await page.evaluate(name=>window.eduosAcademicManagement.state.years
            .find(y=>y.name===name),createName);
        assert(record?.id>0&&record?.rowVersion,'Created year must contain DB key and version');

        await (await yearRow(page,createName)).getByRole('button',{name:'Edit'}).click();
        assert.equal(Number(await page.locator('#yearId').inputValue()),record.id,
            'UI Edit must target the generated row');
        assert(await page.locator('#yearVersion').inputValue(),
            'Edit must carry optimistic concurrency token');
        await page.locator('#yearName').fill(editedName);
        await page.locator('#yearForm button[type="submit"]').click();
        await waitYear(page,editedName);
        await page.reload({waitUntil:'domcontentloaded'});
        await waitYear(page,editedName);
        const current=await page.evaluate(name=>window.eduosAcademicManagement.state.years
            .find(y=>y.name===name),editedName);
        assert.equal(current.id,record.id,'Edit must update original row, not create duplicate');
        assert.equal(await (await yearRow(page,editedName)).count(),1);

        page.once('dialog',async dialog=>dialog.accept());
        await (await yearRow(page,editedName)).getByRole('button',{name:'Archive'}).click();
        await waitYear(page,editedName,false);
        await page.reload({waitUntil:'domcontentloaded'});
        await waitYear(page,editedName,false);
        assert.deepEqual(rejected,[],'Create/Edit/Archive API rejected a valid synthetic request');
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-academic-year-lifecycle.json',JSON.stringify({
            sha,role:'TenantAdmin',scope:'isolated synthetic academic year; safe archive, not physical delete',
            checks:['year-isolated-create','year-ui-edit-saved-and-reloaded',
                'year-ui-archive-and-reloaded'],resourceId:record.id,productionReleaseCertified:false
        },null,2));
        console.log('PASS year Create, Edit, safe Archive, reloaded readback.');
        await ctx.close();
    }finally{await browser.close();}
}
main().catch(e=>{console.error('Year UI mutation QA failed:',String(e?.message||e).slice(0,230));
    process.exitCode=1;});
