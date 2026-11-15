'use strict';
// Real Chromium render of every role-exclusive sidebar route, with no writes.
// Existing TenantAdmin routes are covered by the preceding page-render gate.
const fs=require('node:fs');
const assert=require('node:assert/strict');
const {chromium}=require('playwright');
const {probePageActions}=require('./page-action-probe.cjs');
const origin='https://qa.eduos.test:7243';
const password=process.env.QA_DEMO_PASSWORD||'';
const sha=process.env.GITHUB_SHA||'';
assert.equal(process.env.EDUOS_QA_ORIGIN,origin,'Disposable HTTPS origin only');
assert(/^[a-f0-9]{40}$/i.test(sha),'Exact SHA required');
assert(password.length>=16,'Random synthetic demo password required');
const roles=['Admin','AdmissionOfficer','Principal','VicePrincipal','Teacher',
    'Student','Parent','Accountant','HR','Librarian','ExamController','Staff'];
const restricted=/\/(?:delete|remove|purge|post|approve|reject|logout|submit)(?:\/|$)/i;
function pathOf(raw){
    if(typeof raw!=='string'||!raw.startsWith('/')||raw.startsWith('//'))return null;
    const url=new URL(raw,origin);
    if(url.origin!==origin||/^(?:\/api|\/account\/logout)/i.test(url.pathname)||
        restricted.test(url.pathname)||/%(?:00|2e|2f|5c)/i.test(url.pathname))return null;
    return url.pathname+url.search;
}
function redact(error){
    return String(error?.message||error).replace(/https?:\/\/[^\s)]+/g,'[url]').slice(0,160);
}
async function main(){
    const input=JSON.parse(fs.readFileSync(
        'qa-artifacts/real-tenantadmin-page-render.json','utf8'));
    assert.equal(input.sha,sha,'TenantAdmin render evidence is for a different commit');
    assert.equal(input.role,'TenantAdmin');
    assert(input.checked>=100,'TenantAdmin navigation inventory is incomplete');
    const seen=new Set(input.pages.map(x=>x.path));
    const baseline=seen.size, results=[],roleCounts=[];
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        for(const role of roles){
            const context=await browser.newContext({
                ignoreHTTPSErrors:true,serviceWorkers:'block',acceptDownloads:false,
                viewport:{width:1366,height:768}
            });
            const page=await context.newPage();
            const login=await page.goto(origin+'/Account/Login',{waitUntil:'domcontentloaded'});
            assert(login&&login.status()===200,'Real login page failed for '+role);
            await page.locator('#userNameOrEmail').fill('demo.'+role.toLowerCase()+
                '@eduos.example.invalid');
            await page.locator('#password').fill(password);
            await page.locator('#loginBtn').click();
            await page.waitForURL(u=>u.pathname.toLowerCase()!=='/account/login',
                {waitUntil:'domcontentloaded', timeout:25000});
            const list=page.locator('#sidebarMenu nav a[href]');
            await list.first().waitFor({timeout:15000});
            const hrefs=await list.evaluateAll(nodes=>nodes.filter(x=>
                x.getAttribute('href')?.startsWith('/')&&
                !x.closest('[hidden],[aria-hidden="true"],.d-none'))
                .map(x=>x.getAttribute('href')));
            const paths=[...new Set(hrefs.map(pathOf).filter(Boolean))];
            assert(paths.length>0,'Authenticated sidebar missing for '+role);
            const unique=paths.filter(x=>!seen.has(x));
            let active='',jsFailures=[],blocked=[];
            await context.route('**/*',route=>{
                const request=route.request(),url=new URL(request.url()),method=request.method();
                if(url.origin!==origin)return route.abort('blockedbyclient');
                if(!['GET','HEAD','OPTIONS'].includes(method)){
                    blocked.push({page:active,endpoint:url.pathname,method});
                    return route.abort('blockedbyclient');
                }
                return route.continue();
            });
            page.on('pageerror',error=>jsFailures.push({page:active,description:redact(error)}));
            for(const path of unique){
                seen.add(path);
                active=path;
                const e=jsFailures.length,m=blocked.length;
                let status=0,content=false,mobile=false,empty=true,redirect='',error='',actionProbe=null,printProof=null;
                const isPrint=/\/print(?:\/|$)/i.test(new URL(origin+path).pathname);
                try{
                    await page.setViewportSize({width:1366,height:768});
                    const response=await page.goto(origin+path,{
                        waitUntil:'domcontentloaded',timeout:20000
                    });
                    status=response?.status()||0;redirect=new URL(page.url()).pathname;
                    const main=page.locator('main').first();

                    const contentNode=await main.count()?main:
                        (isPrint?page.locator('body'):null);
                    if(contentNode){
                        content=true;
                        const state=await contentNode.evaluate(el=>({
                            text:((el.innerText||'').trim().length>4),
                            controls:!!el.querySelector('table,button,input,select,a[href],canvas,svg')
                        }));
                        empty=!state.text&&!state.controls;
                    }
                    await page.setViewportSize({width:390,height:844});
                    mobile=content&&await contentNode.isVisible().catch(()=>false);
                    await page.waitForTimeout(120);
                    actionProbe=await probePageActions(page,isPrint?'body':'main');
                    if(isPrint){
                        const button=page.locator('#printRoster');
                        assert(await button.isVisible(),'Print action button missing');
                        await page.evaluate(()=>{
                            window.__qaPrintCalls=0;
                            window.print=()=>{ window.__qaPrintCalls++; };
                        });
                        await button.click();
                        const calls=await page.evaluate(()=>window.__qaPrintCalls);
                        const pdf=await page.pdf({format:'A4',printBackground:true});
                        const pdfValid=pdf.length>1000 && pdf.length<5*1024*1024 &&
                            pdf.subarray(0,5).toString('ascii')==='%PDF-';
                        await page.emulateMedia({media:'print'});
                        const controlsHidden=await page.locator('.actions').first()
                            .evaluate(el=>getComputedStyle(el).display==='none');
                        await page.emulateMedia({media:'screen'});
                        printProof={buttonInvoked:calls===1,pdfValid,controlsHidden,
                            pdfBytes:pdf.length};
                    }
                }catch(ex){error=redact(ex);}
                const errors=jsFailures.slice(e),mutations=blocked.slice(m);
                const authRedirect=/^\/account\/(?:login|accessdenied|unauthorized)/i.test(redirect);
                const passed=status===200&&content&&mobile&&!empty&&!authRedirect&&
                    (!printProof||(printProof.buttonInvoked&&printProof.pdfValid&&printProof.controlsHidden))&&
                    (!isPrint||!!printProof)&&!error&&!errors.length&&!mutations.length;
                results.push({role,path,status,passed,content,mobile,empty,actionProbe,printProof,
                    redirect:redirect!==new URL(origin+path).pathname?redirect:undefined,
                    errors,blockedMutations:mutations,error:error||undefined});
                if(!passed)console.error('[QA role page FAIL]',role,path,'HTTP',status,
                    'JS',errors.length,'blockedWrites',mutations.length,error);
            }
            roleCounts.push({role,authorizedMenuPaths:paths.length,newDistinctPages:unique.length});
            console.log('[QA role exclusive]',role,'new unique',unique.length);
            await context.close();
        }
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-roleexclusive-page-render.json',JSON.stringify({
            sha,environment:'disposable GitHub runner SQL and HTTPS',
            scope:'all unique actual visible role sidebar pages, desktop/mobile, GET only',
            tenantAdminBaseline:baseline,newDistinctPages:results.length,
            totalDistinctPages:seen.size,roles:roleCounts,
            passed:results.filter(x=>x.passed).length,
            failed:results.filter(x=>!x.passed).length,pages:results
        },null,2));
        const bad=results.filter(x=>!x.passed);
        console.log('[QA role exclusive summary]',results.length-bad.length+'/' +
            results.length,'unique pages; total distinct menu paths',seen.size);
        if(bad.length)throw Error('Role-exclusive render failures: '+bad.slice(0,15)
            .map(x=>x.role+' '+x.path+' HTTP '+x.status).join('; '));
        console.log('PASS all role-exclusive menu pages rendered without mutation.');
    }finally{await browser.close();}
}
main().catch(e=>{console.error('Role-exclusive real browser QA failed: '+redact(e));
    process.exitCode=1;});
