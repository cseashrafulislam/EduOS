(() => {
    'use strict';
    const root=document.getElementById('readmissionWorkspace');
    if(!root)return;
    const el=id=>document.getElementById(id);
    const val=id=>el(id).value.trim();
    const state={options:null,requestId:null,saving:false};
    const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value||'';
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
        state.options=null;state.requestId=null;
        el('readmissionEditor').classList.add('d-none');
        el('readmissionStudent').textContent='';
    }
    async function search(event) {
        event.preventDefault();reset();
        const query=val('readmissionQuery');
        if(query.length<2)return notice('Enter at least two characters.');
        const box=el('readmissionMatches');box.textContent='Searching...';
        try {
            const data=await api('/api/students?'+new URLSearchParams({search:query,page:1,pageSize:20}));
            box.replaceChildren();
            if(!data?.items?.length){box.textContent='No matching students.';return;}
            for(const item of data.items) {
                const button=document.createElement('button');
                button.type='button';button.className='list-group-item list-group-item-action';
                button.textContent=item.studentCode+' — '+item.fullName+' ('+item.status+')';
                button.addEventListener('click',()=>load(item.reference));box.append(button);
            }
        } catch(error){box.textContent='';notice(error.message);}
    }
    async function load(reference) {
        reset();
        if(!guid.test(reference))return notice('Invalid student reference.');
        try {
            const options=await api('/api/students/'+encodeURIComponent(reference)+'/readmission/options');
            if(!options||!Array.isArray(options.targets))throw Error('Readmission options unavailable.');
            state.options=options;
            el('readmissionStudent').textContent=options.studentCode+' — '+options.previousStatus;
            const select=el('readmissionBatch');select.replaceChildren();
            for(const target of options.targets) {
                const option=document.createElement('option');
                option.value=String(target.academicBatchId);option.textContent=target.name;select.append(option);
            }
            if(!options.targets.length)return notice('No eligible open batches for this student.');
            el('readmissionRoll').value='';el('readmissionReason').value='';
            state.requestId=crypto.randomUUID();
            el('readmissionEditor').classList.remove('d-none');
        } catch(error){notice(error.message);}
    }
    async function submit(event) {
        event.preventDefault();
        if(state.saving||!state.options||!state.requestId)return;
        const batch=Number(val('readmissionBatch')),roll=val('readmissionRoll'),reason=val('readmissionReason');
        if(!state.options.targets.some(x=>x.academicBatchId===batch)||!roll||roll.length>50||
            !reason||reason.length>450)return notice('Select an eligible batch and enter roll and reason.');
        if(!confirm('Readmit '+state.options.studentCode+'? A new enrollment and status history will be created.'))return;
        const request={clientRequestId:state.requestId,previousEnrollmentId:state.options.previousEnrollmentId,
            targetAcademicBatchId:batch,targetRoll:roll,reason,
            studentRowVersion:state.options.studentRowVersion,
            previousEnrollmentRowVersion:state.options.previousEnrollmentRowVersion};
        state.saving=true;el('readmissionSubmit').disabled=true;
        try {
            const result=await api('/api/students/'+encodeURIComponent(state.options.studentReference)+
                '/readmission','POST',request);
            notice(result?.alreadyProcessed?'Readmission already recorded.':'Student readmitted successfully.',true);
            reset();
        } catch(error){notice(error.message+' Retry unchanged details with the same request ID.');}
        finally{state.saving=false;el('readmissionSubmit').disabled=false;}
    }
    el('readmissionSearch').addEventListener('submit',search);
    el('readmissionForm').addEventListener('submit',submit);
    for(const id of ['readmissionBatch','readmissionRoll','readmissionReason']) {
        el(id).addEventListener('input',()=>{if(!state.saving)state.requestId=crypto.randomUUID();});
        el(id).addEventListener('change',()=>{if(!state.saving)state.requestId=crypto.randomUUID();});
    }
    const preselected=new URLSearchParams(location.search).get('studentReference');
    if(preselected&&guid.test(preselected))load(preselected);
})();
