(() => {
    'use strict';
    const root=document.getElementById('readmissionWorkspace');
    if(!root)return;
    const el=id=>document.getElementById(id);
    const val=id=>el(id).value.trim();
    const state={options:null,requestId:null,requestFingerprint:null,saving:false,searchRequest:0,optionsRequest:0,sectionRequest:0,sections:[]};
    const form=el('readmissionForm');
    const guard=window.EduOSFormGuard?.create(form);
    const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value||'';
    const identity=[document.body?.dataset?.authTenantId||'',document.body?.dataset?.authSessionReference||''];
    const sameSession=()=>identity[0]===(document.body?.dataset?.authTenantId||'')&&
        identity[1]===(document.body?.dataset?.authSessionReference||'')&&
        window.EduOSSessionTransitionPending?.()!==true;
    const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    function notice(message,success=false) {
        const box=el('readmissionAlert');
        box.className='alert alert-'+(success?'success':'danger');
        box.textContent=message;box.focus();
    }
    async function api(url,method='GET',body) {
        const headers={Accept:'application/json'};
        if(body!==undefined)headers['Content-Type']='application/json';
        if(method!=='GET')headers.RequestVerificationToken=token;
        const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
            ...(body===undefined?{}:{body:JSON.stringify(body)})});
        const result=await response.json().catch(()=>null);
        if(!response.ok||!result?.success)throw Error(result?.message||'Request failed. Reload and retry.');
        return result.data;
    }
    function reset() {
        state.sections=[];++state.sectionRequest;
        state.options=null;state.requestId=null;state.requestFingerprint=null;
        form.reset();guard?.markClean();
        el('readmissionEditor').classList.add('d-none');
        el('readmissionStudent').textContent='';
    }
    async function loadSections() {
        const levelId=Number(val('readmissionLevel')),campusId=state.options?.campusId,
            ticket=++state.sectionRequest;
        const select=el('readmissionSection');
        state.sections=[];
        select.replaceChildren(new Option('Select academic section',''));
        if(!levelId||!campusId)return;
        const query=new URLSearchParams({campusId:String(campusId),academicLevelId:String(levelId),
            page:'1',pageSize:'100'});
        const data=await api('/api/academic-sections?'+query);
        if(ticket!==state.sectionRequest||!sameSession()||Number(val('readmissionLevel'))!==levelId)return;
        state.sections=(data.items||[]).filter(x=>x.isActive&&x.capacity>0);
        state.sections.forEach(x=>select.add(new Option(x.name,String(x.id))));
        if(state.sections.length===1)select.value=String(state.sections[0].id);
        guard?.markClean();
    }
    async function search(event) {
        event.preventDefault();
        if(!sameSession()||state.saving || (guard?.isDirty() && !guard.confirmDiscard()))return;
        const query=val('readmissionQuery');
        if(query.length<2)return notice('Enter at least two characters.');
        reset();++state.optionsRequest;
        const request=++state.searchRequest;
        const box=el('readmissionMatches');box.textContent='Searching...';
        try {
            const data=await api('/api/students?'+new URLSearchParams({search:query,page:1,pageSize:20}));
            if(request!==state.searchRequest||!sameSession())return;
            box.replaceChildren();
            if(!data?.items?.length){box.textContent='No matching students.';return;}
            for(const item of data.items) {
                const button=document.createElement('button');
                button.type='button';button.className='list-group-item list-group-item-action';
                button.textContent=item.studentCode+' — '+item.fullName+' ('+item.status+')';
                button.addEventListener('click',()=>load(item.reference));box.append(button);
            }
        } catch(error){if(request===state.searchRequest&&sameSession()){box.textContent='';notice(error.message);}}
    }
    async function load(reference) {
        if(!guid.test(reference))return notice('Invalid student reference.');
        if(!sameSession()||state.saving || (guard?.isDirty() && !guard.confirmDiscard()))return;
        reset();
        const request=++state.optionsRequest;
        try {
            const options=await api('/api/students/'+encodeURIComponent(reference)+'/readmission/options');
            if(request!==state.optionsRequest||!sameSession())return;
            if(!options||!Array.isArray(options.years)||!Array.isArray(options.levels))
                throw Error('Readmission academic scopes unavailable.');
            if(options.studentReference!==reference)throw Error('Student reference mismatch. Reload and retry.');
            state.options=options;
            el('readmissionStudent').textContent=options.studentCode+' — '+options.previousStatus;
            const fill=(id,items,placeholder)=>{
                const select=el(id);
                select.replaceChildren(new Option(placeholder,''));
                for(const item of items)select.add(new Option(item.name,String(item.id)));
                if(items.length===1)select.value=String(items[0].id);
            };
            fill('readmissionYear',options.years,'Select academic year');
            fill('readmissionLevel',options.levels,'Select class / grade');
            if(!options.years.length||!options.levels.length)
                return notice('No eligible current academic year or class for readmission.');
            el('readmissionRoll').value='';el('readmissionReason').value='';
            await loadSections();
            guard?.markClean();
            el('readmissionEditor').classList.remove('d-none');
        } catch(error){if(request===state.optionsRequest&&sameSession())notice(error.message);}
    }
    async function submit(event) {
        event.preventDefault();
        if(!sameSession()||state.saving||!state.options)return;
        const yearId=Number(val('readmissionYear')),levelId=Number(val('readmissionLevel')),
            sectionId=Number(val('readmissionSection'))||null,
            roll=val('readmissionRoll'),reason=val('readmissionReason');
        if(!state.options.years.some(x=>x.id===yearId)||
            !state.options.levels.some(x=>x.id===levelId)||
            !state.sections.some(x=>x.id===sectionId))
            return notice('Select an eligible academic year, class and active section.');
        if(!roll||roll.length>50||!reason||reason.length>450)
            return notice('Enter a valid roll and readmission reason.');
        if(!confirm('Readmit '+state.options.studentCode+'? A new enrollment and status history will be created.'))return;
        const request={previousEnrollmentId:state.options.previousEnrollmentId,
            targetAcademicBatchId:null,targetAcademicYearId:yearId,
            targetAcademicLevelId:levelId,targetAcademicSectionId:sectionId,targetRoll:roll,reason,
            studentRowVersion:state.options.studentRowVersion,
            previousEnrollmentRowVersion:state.options.previousEnrollmentRowVersion};
        const fingerprint=JSON.stringify(request);
        if(!state.requestId||state.requestFingerprint!==fingerprint)state.requestId=crypto.randomUUID();
        state.requestFingerprint=fingerprint;
        request.clientRequestId=state.requestId;
        const reference=state.options.studentReference;
        state.saving=true;form.inert=true;el('readmissionSubmit').disabled=true;
        try {
            const result=await api('/api/students/'+encodeURIComponent(reference)+
                '/readmission','POST',request);
            if(!sameSession())return;
            notice(result?.alreadyProcessed?'Readmission already recorded.':'Student readmitted successfully.',true);
            reset();
        } catch(error){if(sameSession())notice(error.message+' Retry unchanged details with the same request ID.');}
        finally{state.saving=false;form.inert=false;el('readmissionSubmit').disabled=false;}
    }
    el('readmissionSearch').addEventListener('submit',search);
    el('readmissionForm').addEventListener('submit',submit);
    el('readmissionLevel').addEventListener('change',()=>{
        loadSections().catch(e=>notice(e.message));
    });
    el('readmissionQuery').addEventListener('input',()=>{++state.searchRequest;});
    const preselected=new URLSearchParams(location.search).get('studentReference');
    if(preselected&&guid.test(preselected))load(preselected);
})();
