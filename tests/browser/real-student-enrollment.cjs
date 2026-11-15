'use strict';
// Real UI journey on ephemeral GitHub runner: academic prerequisites -> batch
// -> curriculum/required offering -> student+guardian -> idempotent retry.
// Production connections, user accounts and external network are disallowed.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {chromium}=require('playwright');
const origin='https://qa.eduos.test:7243';
const env=process.env;
const names={
  year:env.QA_YEAR_NAME, programme:env.QA_PROGRAM_NAME, level:env.QA_LEVEL_NAME,
  section:env.QA_SECTION_NAME, subject:env.QA_SUBJECT_NAME,
  batch:env.QA_BATCH_NAME, curriculum:env.QA_CURRICULUM_NAME,
  student:env.QA_STUDENT_NAME, guardian:env.QA_GUARDIAN_NAME, roll:env.QA_STUDENT_ROLL
};
assert.equal(env.EDUOS_QA_ORIGIN,origin,'Disposable runner origin required');
assert(/^[a-f0-9]{40}$/i.test(env.GITHUB_SHA||''),'Exact SHA required');
assert((env.QA_DEMO_PASSWORD||'').length>=16,'Generated demo credentials required');
for(const [key,val] of Object.entries(names))
  assert(val && val.startsWith(key==='roll'?'QA-Roll-':'QA '),
    'Synthetic-only fixture required for '+key);

function path(url){return new URL(url).pathname;}
async function postResult(page,target,trigger){
    const pending=page.waitForResponse(r=>r.request().method()==='POST'&&
        path(r.url())===target,{timeout:30000}).catch(()=>null);
    await trigger();
    const response=await pending;
    assert(response,'No POST response from '+target);
    const json=await response.json().catch(()=>null);
    assert(response.status()>=200&&response.status()<300&&json?.success===true,
        'Failed real form '+target+' HTTP '+response.status()+
        ' '+String(json?.message||'unknown').slice(0,210));
    return {data:json.data,request:response.request()};
}
async function academic(page,route,feature){
    const response=await page.goto(origin+'/AcademicManagement/'+route,
        {waitUntil:'domcontentloaded',timeout:25000});
    assert(response&&response.status()===200,'Academic '+route+' unavailable');
    await page.locator('#academicManagement[data-active-feature="'+feature+'"]').waitFor();
    await page.waitForFunction(()=>!!window.eduosAcademicManagement?.state?.catalog,
        null,{timeout:25000});
}
async function catalogRow(page,group,name){
    await page.waitForFunction(([key,label])=>
        window.eduosAcademicManagement?.state?.catalog?.[key]?.some(x=>x.name===label),
        [group,name],{timeout:25000});
    const row=await page.evaluate(([key,label])=>
        window.eduosAcademicManagement.state.catalog[key].find(x=>x.name===label),
        [group,name]);
    assert(Number.isInteger(row.id)&&row.id>0&&row.code,
        'Missing persisted '+group+' code');
    return row;
}
async function settled(page,form){
    await page.waitForFunction(id=>{
        const f=document.getElementById(id);
        return f&&!f.inert&&!f.querySelector('button[type="submit"]')?.disabled;
    },form,{timeout:15000});
}
async function select(page,id,value){
    await page.locator('#'+id+' option[value="'+value+'"]').waitFor({state:'attached'});
    await page.locator('#'+id).selectOption(String(value));
}
async function run(){
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try{
        const ctx=await browser.newContext({ignoreHTTPSErrors:true,
            serviceWorkers:'block',acceptDownloads:false});
        await ctx.route('**/*',route=>
            new URL(route.request().url()).origin===origin?route.continue():
            route.abort('blockedbyclient'));
        const page=await ctx.newPage();
        page.setDefaultTimeout(20000);
        const login=await page.goto(origin+'/Account/Login',{waitUntil:'domcontentloaded'});
        assert(login&&login.status()===200,'Demo login unavailable');
        await page.locator('#userNameOrEmail').fill('demo.tenantadmin@eduos.example.invalid');
        await page.locator('#password').fill(env.QA_DEMO_PASSWORD);
        await page.locator('#loginBtn').click();
        await page.waitForURL(url=>url.pathname.toLowerCase()!=='/account/login',{timeout:25000});
        await academic(page,'Curricula','curricula');
        const catalog=await page.evaluate(()=>({
            programs:window.eduosAcademicManagement.state.catalog.programs,
            levels:window.eduosAcademicManagement.state.catalog.levels,
            subjects:window.eduosAcademicManagement.state.catalog.subjects,
            years:window.eduosAcademicManagement.state.years,
            campuses:window.eduosAcademicManagement.state.campuses
        }));
        const programme=catalog.programs.find(x=>x.name===names.programme);
        const level=catalog.levels.find(x=>x.name===names.level);
        const subject=catalog.subjects.find(x=>x.name===names.subject);
        const year=catalog.years.find(x=>x.name===names.year);
        const campus=catalog.campuses.find(x=>x.code==='DEMO-C01');
        for(const [k,v] of Object.entries({programme,level,subject,year,campus}))
            assert(v?.id>0,'Missing actual prerequisite '+k);
        assert.equal(level.academicProgramId,programme.id);
        console.log('[QA student journey] Create real Curriculum');
        await select(page,'curriculumProgram',programme.id);
        await page.locator('#curriculumName').fill(names.curriculum);
        await page.locator('#curriculumVersion').fill('1');
        await page.locator('#curriculumFrom').fill('2027-01-01');
        await page.locator('#curriculumCurrent').check();
        const creation=await postResult(page,'/api/academic-setup/curricula',
            ()=>page.locator('#curriculumForm button[type="submit"]').click());
        await settled(page,'curriculumForm');
        const curriculum=await catalogRow(page,'curricula',names.curriculum);
        assert.equal(curriculum.academicProgramId,programme.id);
        await select(page,'curriculumSelection',curriculum.id);
        await page.waitForFunction(()=>!document.getElementById('curriculumMappingSubmit').disabled,
            null,{timeout:20000});
        await select(page,'mappingLevel',level.id);
        await select(page,'mappingSubject',subject.id);
        await page.locator('#mappingMarks').fill('100');
        await page.locator('#mappingPassMarks').fill('33');
        console.log('[QA student journey] Map real required Curriculum Subject');
        await postResult(page,'/api/academic-setup/curricula/'+curriculum.id+'/subjects',
            ()=>page.locator('#curriculumMappingSubmit').click());
        await settled(page,'curriculumSubjectForm');
        await page.waitForFunction(([cid,lid,sid])=>
            window.eduosAcademicManagement?.state?.catalog?.curriculumSubjects?.some(x=>
                x.academicCurriculumId===cid&&x.academicLevelId===lid&&x.subjectId===sid),
            [curriculum.id,level.id,subject.id],{timeout:25000});
        const mapping=await page.evaluate(([cid,lid,sid])=>
            window.eduosAcademicManagement.state.catalog.curriculumSubjects.find(x=>
                x.academicCurriculumId===cid&&x.academicLevelId===lid&&x.subjectId===sid),
            [curriculum.id,level.id,subject.id]);
        assert(mapping.id>0&&mapping.isActive);
        console.log('[QA student journey] Create real Batch with existing independent Section');
        await academic(page,'Batches','sections');
        await select(page,'batchCreateCampus',campus.id);
        await select(page,'batchCreateYear',year.id);
        await select(page,'batchCreateProgram',programme.id);
        await select(page,'batchCreateLevel',level.id);
        await page.locator('#batchCreateName').fill(names.batch);
        await page.locator('#batchCreateCapacity').fill('25');
        await page.locator('#batchCreateStart').fill('2027-01-01');
        await page.locator('#batchCreateEnd').fill('2027-12-31');
        await postResult(page,'/api/academic-setup/batches',
            ()=>page.locator('#batchCreateForm button[type="submit"]').click());
        await settled(page,'batchCreateForm');
        const batch=await catalogRow(page,'batches',names.batch);
        assert.equal(batch.academicYearId,year.id);
        assert.equal(batch.academicProgramId,programme.id);
        assert.equal(batch.academicLevelId,level.id);
        assert.equal(batch.campusId,campus.id);
        console.log('[QA student journey] Create one required Subject Offering');
        await academic(page,'Offerings','offerings');
        await select(page,'offeringBatch',batch.id);
        await select(page,'offeringSection',
            (await page.evaluate(()=>[...document.querySelectorAll('#offeringSection option')]
                .find(x=>x.textContent.includes('QA Section A'))?.value))||'');
        await select(page,'offeringCurriculumSubject',mapping.id);
        await page.locator('#offeringCapacity').fill('20');
        const offerResponse=await postResult(page,'/api/academic-setup/offerings',
            ()=>page.locator('#offeringSave').click());
        assert(offerResponse.data,'Offering response must return a persisted entity');
        await page.locator('#offeringRows tr').filter({hasText:names.subject}).first().waitFor();
        console.log('[QA student journey] Save Student + Guardian through real UI');
        const studentView=await page.goto(origin+'/Students/Create',{waitUntil:'domcontentloaded'});
        assert(studentView&&studentView.status()===200,'Student quick entry unavailable');
        await page.locator('#runningStudentForm').waitFor();
        await page.locator('#runningStudentName').fill(names.student);
        await select(page,'runningBatch',batch.id);
        await select(page,'runningSection',
            (await page.evaluate(()=>[...document.querySelectorAll('#runningSection option')]
                .find(x=>x.textContent.includes('QA Section A'))?.value))||'');
        await page.locator('#runningRoll').fill(names.roll);
        await page.locator('#runningAdmissionDate').fill('2027-03-01');
        await page.locator('#runningEnrollDate').fill('2027-03-01');
        await select(page,'runningCurriculum',curriculum.id);
        await page.locator('#runningAddGuardian').check();
        await page.locator('#runningGuardianName').fill(names.guardian);
        await page.locator('#runningGuardianRelation').selectOption('Father');
        const created=await postResult(page,'/api/students/running',
            ()=>page.locator('#runningSave').click());
        assert(created.data?.studentCode?.startsWith('STU-') &&
            created.data?.enrollmentReference &&
            created.data?.guardianLinked===true,'Student+Enrollment+Guardian incomplete');
        await page.locator('#runningStudentSuccess').waitFor({state:'visible'});
        const body=created.request.postData();
        assert(body&&body.length<7000,'Missing bounded synthetic idempotency payload');
        const token=await page.locator('#runningStudentForm input[name="__RequestVerificationToken"]')
            .inputValue();
        const result=await page.evaluate(async ({body,token})=>{
            async function send(data){
                const response=await fetch('/api/students/running',{
                    method:'POST',credentials:'same-origin',cache:'no-store',
                    headers:{'Content-Type':'application/json','RequestVerificationToken':token,
                        'Accept':'application/json'},body:JSON.stringify(data)
                });
                return {status:response.status(),body:await response.json().catch(()=>null)};
            }
            const initial=JSON.parse(body),replay=await send(initial);
            return {replay,conflict:await send({...initial,rollNo:initial.rollNo+'-changed'})};
        },{body,token});
        assert(result.replay.status===200&&result.replay.body?.success===true,
            'Same UUID retry must replay successfully without duplicated student');
        assert(result.replay.body?.data?.studentCode===created.data.studentCode,
            'Idempotent retry returned another student');
        assert(result.conflict.status===409&&result.conflict.body?.success!==true,
            'Changed payload using same UUID must reject');
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-student-enrollment.json',JSON.stringify({
            sha:env.GITHUB_SHA,scope:'actual forms on isolated SQL',
            checks:['curriculum-create','mandatory-subject-mapping','batch-create',
                'section-linked-offering','student-plus-guardian-create',
                'same-request-id-replay','conflicting-request-id-rejected','sql-proof-next'],
            academicProgramId:programme.id,levelId:level.id,curriculumId:curriculum.id,
            mappingId:mapping.id,batchId:batch.id,studentCode:created.data.studentCode,
            enrolled:created.data.enrollmentReference,guardianLinked:true
        },null,2));
        console.log('PASS real Student+Guardian+Enrollment+Offering lifecycle and idempotent retry.');
        await ctx.close();
    }finally{await browser.close();}
}
run().catch(e=>{console.error('Real student onboarding QA failed:',String(e.message||e));
    process.exitCode=1;});
