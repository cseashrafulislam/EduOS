(() => {
'use strict';
const root=document.getElementById('recruitmentApp');if(!root)return;
const el=id=>document.getElementById(id);
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const stages={1:'Applied',2:'Shortlisted',3:'Interviewed',4:'Offered',5:'Rejected',6:'Hired'};
const allowed={1:[2,5],2:[3,5],3:[4,5],4:[6,5]};
let current=null,page=1,pages=1,key=crypto.randomUUID(),keyFingerprint='',busy=false,
    accessRevoked=false,listSeq=0,historySeq=0,employeeSeq=0;
const createForm=el('recruitmentCreate'),transitionForm=el('recruitTransition');
const guards={create:window.EduOSFormGuard?.create(createForm),
    transition:window.EduOSFormGuard?.create(transitionForm)};
const mayDiscard=name=>!guards[name]?.isDirty()||guards[name].confirmDiscard();
let campuses=new Map(),designations=new Map();
const text=id=>el(id).value.trim();
const today=()=>{const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
function notice(message,bad=false){const n=el('recruitmentNotice');n.className='alert '+(bad?'alert-danger':'alert-success');n.textContent=message;n.focus();}
async function api(url,method='GET',body) {
    const headers={Accept:'application/json'};
    if(method!=='GET'){if(!token)throw Error('Security token missing. Reload this page.');headers.RequestVerificationToken=token;}
    if(body!==undefined)headers['Content-Type']='application/json';
    const r=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,
        ...(body===undefined?{}:{body:JSON.stringify(body)})});
    const j=await r.json().catch(()=>null);
    if(!r.ok||!j?.success){const error=Error(j?.message||'Recruitment request failed.');
        error.statusCode=r.status;throw error;}
    return j.data;
}
function handleError(error){
    if([401,403].includes(error.statusCode)){
        accessRevoked=true;++listSeq;++historySeq;++employeeSeq;current=null;
        el('recruitRows').replaceChildren();el('recruitHistoryRows').replaceChildren();
        el('recruitEmployee').replaceChildren(new Option('Access unavailable',''));
        createForm.reset();transitionForm.reset();
        guards.create?.markClean();guards.transition?.markClean();
        el('recruitActionPanel').hidden=true;root.inert=true;
    }
    notice(error.message,true);
}
function requestKeyFor(command){
    const signature=JSON.stringify(command);
    if(keyFingerprint!==signature){keyFingerprint=signature;key=crypto.randomUUID();}
    return key;
}
function cell(value){const td=document.createElement('td');td.textContent=value??'—';return td;}
function close(force=false){
    if(!force&&(busy||!mayDiscard('transition')))return false;
    current=null;++historySeq;++employeeSeq;
    transitionForm.reset();guards.transition?.markClean();
    el('recruitActionPanel').hidden=true;el('recruitHistoryRows').replaceChildren();
    return true;
}
function stageOptions(stage){
    const next=el('recruitNextStage');next.replaceChildren(new Option('Choose next stage',''));
    for(const n of allowed[stage]||[])next.add(new Option(stages[n],n));
    el('recruitHirePanel').hidden=true;
}
async function options() {
    try {
        const [cs,ds]=await Promise.all([api('/api/institution-onboarding/campus-list'),api('/api/hr/setup/designations')]);
        campuses=new Map((cs||[]).map(x=>[Number(x.id),x.name]));
        designations=new Map((ds||[]).map(x=>[Number(x.id),x.name]));
        const c=el('recruitCampus'),d=el('recruitDesignation');
        for(const x of cs||[])if(x.isActive)c.add(new Option(x.name,x.id));
        for(const x of ds||[])if(x.isActive)d.add(new Option(x.name,x.id));
    }catch(e){handleError(e);}
}
function clear(force=false){
    if(!force&&(busy||!mayDiscard('create')))return;
    createForm.reset();el('recruitAppliedOn').value=today();
    key=crypto.randomUUID();keyFingerprint='';guards.create?.markClean();
}
async function list() {
    if(accessRevoked)return;
    const seq=++listSeq,selectedPage=page,search=text('recruitSearch'),stage=el('recruitStageFilter').value;
    try {
        const q=new URLSearchParams({page:String(selectedPage),pageSize:'15'});
        if(search)q.set('search',search);
        if(stage)q.set('stage',stage);
        const data=await api('/api/hr/recruitment?'+q);
        if(seq!==listSeq||selectedPage!==page||search!==text('recruitSearch')||
            stage!==el('recruitStageFilter').value)return;
        const body=el('recruitRows');body.replaceChildren();
        for(const x of data.items||[]){
            const tr=document.createElement('tr');
            [x.fullName,campuses.get(Number(x.campusId))||('Campus '+x.campusId),
                designations.get(Number(x.designationId))||('Designation '+x.designationId),
                String(x.appliedOn).slice(0,10),stages[x.stage]||x.stage].forEach(t=>tr.append(cell(t)));
            const action=document.createElement('td'),button=document.createElement('button');
            button.type='button';button.className='btn btn-sm btn-outline-primary';button.textContent='Open';
            button.addEventListener('click',()=>open(x));action.append(button);tr.append(action);body.append(tr);
        }
        if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No applicants found.');td.colSpan=6;tr.append(td);body.append(tr);}
        pages=Math.max(1,Math.ceil((data.totalCount||0)/15));
        el('recruitPage').textContent='Page '+page+' of '+pages+' · '+(data.totalCount||0);
        el('recruitPrev').disabled=page<=1;el('recruitNext').disabled=page>=pages;
    }catch(e){if(seq===listSeq){el('recruitRows').replaceChildren();throw e;}}
}
async function history(ref) {
    const seq=++historySeq;
    try {
        const data=await api('/api/hr/recruitment/'+encodeURIComponent(ref)+'/history?page=1&pageSize=20');
        if(seq!==historySeq||current?.reference!==ref)return;
        const body=el('recruitHistoryRows');body.replaceChildren();
        for(const x of data.items||[]){
            const row=document.createElement('tr');
            [stages[x.previousStage]||'—',stages[x.newStage],String(x.transitionAt).slice(0,19).replace('T',' '),x.reason||'—']
                .forEach(t=>row.append(cell(t)));body.append(row);
        }
    }catch(e){if(seq===historySeq&&current?.reference===ref)handleError(e);}
}
function open(x) {
    if(busy||accessRevoked||!close())return;
    current=x;
    el('recruitActionPanel').hidden=false;
    el('recruitActionTitle').textContent=x.fullName+' · '+stages[x.stage];
    el('recruitReason').value='';el('recruitInterviewOn').value='';
    el('recruitEmployee').replaceChildren(new Option('Choose employee',''));
    el('recruitIdentityVerified').checked=false;
    stageOptions(Number(x.stage));
    el('recruitTransition').hidden=!(allowed[x.stage]?.length);
    guards.transition?.markClean();history(x.reference);
    el('recruitActionPanel').scrollIntoView({block:'nearest'});
}
async function findEmployee() {
    if(busy||accessRevoked)return;
    const term=text('recruitEmployeeSearch');
    if(term.length<2||term.length>100)return notice('Enter 2–100 characters to find the verified employee.',true);
    if(el('recruitEmployee').value&&!confirm('Replace the selected verified employee?'))return;
    const seq=++employeeSeq,reference=current?.reference;
    try {
        const q=new URLSearchParams({search:term,page:'1',pageSize:'50'});
        const d=await api('/api/hr/employees?'+q),sel=el('recruitEmployee');
        if(seq!==employeeSeq||current?.reference!==reference||term!==text('recruitEmployeeSearch'))return;
        if(sel.value&&!confirm('Replace the selected verified employee?'))return;
        sel.replaceChildren(new Option('Choose employee',''));
        for(const x of d.items||[])sel.add(new Option(x.employeeCode+' · '+x.fullName,x.reference));
        if(d.totalCount>50)notice('Refine employee search.',true);
    }catch(e){if(seq===employeeSeq)handleError(e);}
}
async function create(event) {
    event.preventDefault();if(busy||accessRevoked||!createForm.reportValidity())return;
    const command={campusId:Number(el('recruitCampus').value),
        designationId:Number(el('recruitDesignation').value),fullName:text('recruitName'),
        email:text('recruitEmail')||null,phone:text('recruitPhone')||null,
        appliedOn:text('recruitAppliedOn'),consentConfirmed:el('recruitConsent').checked,
        notes:text('recruitNotes')||null};
    const payload={clientRequestId:requestKeyFor(command),...command};
    busy=true;root.inert=true;
    try{
        await api('/api/hr/recruitment','POST',payload);
        clear(true);page=1;
        try{await list();notice('Application registered with consent.');}
        catch(readError){if([401,403].includes(readError.statusCode))handleError(readError);
            notice('Application registered with consent. Register refresh unavailable: '+readError.message);}
    }catch(e){handleError(e);
        if(!accessRevoked)notice(e.message+' Retry only the unchanged applicant; altered details create a new request ID.',true);}
    finally{busy=false;root.inert=accessRevoked;}
}
async function transition(event){
    event.preventDefault();if(!current||busy||accessRevoked||!transitionForm.reportValidity())return;
    const next=Number(el('recruitNextStage').value);
    if(!(allowed[current.stage]||[]).includes(next))return notice('Choose an allowed next stage.',true);
    if(next===5&&!text('recruitReason'))return notice('Rejection reason required.',true);
    if(next===3&&!text('recruitInterviewOn'))return notice('Interview date required.',true);
    if(next===6&&(!el('recruitEmployee').value||!el('recruitIdentityVerified').checked))
        return notice('Verified existing employee is required to confirm hiring.',true);
    if(next===6&&text('recruitReason').length<5)
        return notice('Record the completed identity verification in the decision note.',true);
    if(!confirm('Record '+stages[next]+' decision for '+current.fullName+'?'))return;
    const selected=current,command={nextStage:next,rowVersion:selected.rowVersion,
        interviewOn:next===3?text('recruitInterviewOn'):null,
        employeeReference:next===6?el('recruitEmployee').value:null,
        identityVerified:next===6&&el('recruitIdentityVerified').checked,
        reason:text('recruitReason')||null};
    busy=true;root.inert=true;
    try{
        await api('/api/hr/recruitment/'+encodeURIComponent(selected.reference)+'/transition','POST',command);
        close(true);
        try{await list();notice('Recruitment stage recorded.');}
        catch(readError){if([401,403].includes(readError.statusCode))handleError(readError);
            notice('Recruitment stage recorded. Register refresh unavailable: '+readError.message);}
    }catch(e){handleError(e);}
    finally{busy=false;root.inert=accessRevoked;}
}
el('recruitNextStage').addEventListener('change',()=>{
    el('recruitHirePanel').hidden=el('recruitNextStage').value!=='6';
});
el('recruitmentCreate').addEventListener('submit',create);
el('recruitReset').addEventListener('click',()=>clear());
el('recruitSearchBtn').addEventListener('click',()=>{if(busy)return;page=1;list().catch(handleError);});
el('recruitSearch').addEventListener('input',()=>{++listSeq;});
el('recruitSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();page=1;list().catch(handleError);}});
el('recruitStageFilter').addEventListener('change',()=>{page=1;list().catch(handleError);});
el('recruitPrev').addEventListener('click',()=>{page=Math.max(1,page-1);list().catch(handleError);});
el('recruitNext').addEventListener('click',()=>{page=Math.min(pages,page+1);list().catch(handleError);});
el('recruitTransition').addEventListener('submit',transition);
el('recruitTransitionClose').addEventListener('click',()=>close());
el('recruitEmployeeFind').addEventListener('click',()=>findEmployee().catch(handleError));
el('recruitEmployeeSearch').addEventListener('input',()=>{++employeeSeq;});
el('recruitAppliedOn').value=today();guards.create?.markClean();
options().then(()=>list().catch(handleError)).catch(handleError);
})();
