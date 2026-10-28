(() => {
'use strict';
const root=document.getElementById('recruitmentApp');if(!root)return;
const el=id=>document.getElementById(id);
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const stages={1:'Applied',2:'Shortlisted',3:'Interviewed',4:'Offered',5:'Rejected',6:'Hired'};
const allowed={1:[2,5],2:[3,5],3:[4,5],4:[6,5]};
let current=null,page=1,pages=1,key=crypto.randomUUID(),busy=false;
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
    if(!r.ok||!j?.success)throw Error(j?.message||'Recruitment request failed.');
    return j.data;
}
function cell(value){const td=document.createElement('td');td.textContent=value??'—';return td;}
function close(){current=null;el('recruitActionPanel').hidden=true;}
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
    }catch(e){notice(e.message,true);}
}
function clear(){if(busy)return;el('recruitmentCreate').reset();el('recruitAppliedOn').value=today();key=crypto.randomUUID();}
async function list() {
    try {
        const q=new URLSearchParams({page:String(page),pageSize:'15'});
        if(text('recruitSearch'))q.set('search',text('recruitSearch'));
        if(el('recruitStageFilter').value)q.set('stage',el('recruitStageFilter').value);
        const data=await api('/api/hr/recruitment?'+q);
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
    }catch(e){notice(e.message,true);}
}
async function history(ref) {
    try {
        const data=await api('/api/hr/recruitment/'+encodeURIComponent(ref)+'/history?page=1&pageSize=20');
        const body=el('recruitHistoryRows');body.replaceChildren();
        for(const x of data.items||[]){
            const row=document.createElement('tr');
            [stages[x.previousStage]||'—',stages[x.newStage],String(x.transitionAt).slice(0,19).replace('T',' '),x.reason||'—']
                .forEach(t=>row.append(cell(t)));body.append(row);
        }
    }catch(e){notice(e.message,true);}
}
function open(x) {
    current=x;
    el('recruitActionPanel').hidden=false;
    el('recruitActionTitle').textContent=x.fullName+' · '+stages[x.stage];
    el('recruitReason').value='';el('recruitInterviewOn').value='';
    el('recruitEmployee').replaceChildren(new Option('Choose employee',''));
    el('recruitIdentityVerified').checked=false;
    stageOptions(Number(x.stage));
    el('recruitTransition').hidden=!(allowed[x.stage]?.length);
    history(x.reference);
    el('recruitActionPanel').scrollIntoView({block:'nearest'});
}
async function findEmployee() {
    try {
        const q=new URLSearchParams({search:text('recruitEmployeeSearch'),page:'1',pageSize:'50'});
        const d=await api('/api/hr/employees?'+q),sel=el('recruitEmployee');
        sel.replaceChildren(new Option('Choose employee',''));
        for(const x of d.items||[])sel.add(new Option(x.employeeCode+' · '+x.fullName,x.reference));
        if(d.totalCount>50)notice('Refine employee search.',true);
    }catch(e){notice(e.message,true);}
}
async function create(event) {
    event.preventDefault();if(busy||!el('recruitmentCreate').reportValidity())return;
    const payload={clientRequestId:key,campusId:Number(el('recruitCampus').value),
        designationId:Number(el('recruitDesignation').value),fullName:text('recruitName'),
        email:text('recruitEmail')||null,phone:text('recruitPhone')||null,
        appliedOn:text('recruitAppliedOn'),consentConfirmed:el('recruitConsent').checked,
        notes:text('recruitNotes')||null};
    busy=true;el('recruitSave').disabled=true;
    try{
        await api('/api/hr/recruitment','POST',payload);
        notice('Application registered with consent.');
        el('recruitmentCreate').reset();el('recruitAppliedOn').value=today();
        key=crypto.randomUUID();page=1;await list();
    }catch(e){notice(e.message+' Check existing records before retrying.',true);}
    finally{busy=false;el('recruitSave').disabled=false;}
}
async function transition(event){
    event.preventDefault();if(!current||busy||!el('recruitTransition').reportValidity())return;
    const next=Number(el('recruitNextStage').value);
    if(!(allowed[current.stage]||[]).includes(next))return notice('Choose an allowed next stage.',true);
    if(next===5&&!text('recruitReason'))return notice('Rejection reason required.',true);
    if(next===3&&!text('recruitInterviewOn'))return notice('Interview date required.',true);
    if(next===6&&(!el('recruitEmployee').value||!el('recruitIdentityVerified').checked))
        return notice('Verified existing employee is required to confirm hiring.',true);
    if(next===6&&text('recruitReason').length<5)
        return notice('Record the completed identity verification in the decision note.',true);
    if(!confirm('Record '+stages[next]+' decision for '+current.fullName+'?'))return;
    busy=true;el('recruitTransitionSave').disabled=true;
    try{
        await api('/api/hr/recruitment/'+encodeURIComponent(current.reference)+'/transition','POST',{
            nextStage:next,rowVersion:current.rowVersion,
            interviewOn:next===3?text('recruitInterviewOn'):null,
            employeeReference:next===6?el('recruitEmployee').value:null,
            identityVerified:next===6&&el('recruitIdentityVerified').checked,
            reason:text('recruitReason')||null
        });
        notice('Recruitment stage recorded.');close();await list();
    }catch(e){notice(e.message,true);}
    finally{busy=false;el('recruitTransitionSave').disabled=false;}
}
el('recruitNextStage').addEventListener('change',()=>{
    el('recruitHirePanel').hidden=el('recruitNextStage').value!=='6';
});
el('recruitmentCreate').addEventListener('submit',create);
el('recruitReset').addEventListener('click',clear);
el('recruitSearchBtn').addEventListener('click',()=>{page=1;list();});
el('recruitSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();page=1;list();}});
el('recruitStageFilter').addEventListener('change',()=>{page=1;list();});
el('recruitPrev').addEventListener('click',()=>{page=Math.max(1,page-1);list();});
el('recruitNext').addEventListener('click',()=>{page=Math.min(pages,page+1);list();});
el('recruitTransition').addEventListener('submit',transition);
el('recruitTransitionClose').addEventListener('click',close);
el('recruitEmployeeFind').addEventListener('click',findEmployee);
el('recruitAppliedOn').value=today();
options().then(list);
})();