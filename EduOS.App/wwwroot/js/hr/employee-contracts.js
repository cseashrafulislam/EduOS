(() => {
'use strict';
const root=document.getElementById('employeeContracts');if(!root)return;
const el=id=>document.getElementById(id),token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const state={employee:null,page:1,pages:1,key:crypto.randomUUID(),selected:null,busy:false};
const types={1:'Draft',2:'Approved',3:'Rejected'};
function notice(msg,bad=false){const x=el('contractAlert');x.className='alert '+(bad?'alert-danger':'alert-success');x.textContent=msg;x.focus();}
async function api(url,method='GET',body) {
    const headers={Accept:'application/json'};
    if(method!=='GET') {if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;}
    if(body!==undefined)headers['Content-Type']='application/json';
    const r=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,
        ...(body===undefined?{}:{body:JSON.stringify(body)})});
    const j=await r.json().catch(()=>null);
    if(!r.ok||!j?.success)throw Error(j?.message||'Contract request failed.');
    return j.data;
}
function cell(v){const t=document.createElement('td');t.textContent=v??'';return t;}
function closeReview(){state.selected=null;el('contractReview').classList.add('d-none');}
async function find() {
    try {
        const q=new URLSearchParams({page:'1',pageSize:'50',search:el('contractSearch').value.trim()});
        const d=await api('/api/hr/employees?'+q);
        const select=el('contractEmployee');select.replaceChildren(new Option('Select employee',''));
        for(const e of d.items||[])select.add(new Option(e.employeeCode+' · '+e.fullName,e.reference));
        if(d.totalCount>50)notice('Refine search; more than 50 employees match.',true);
    }catch(e){notice(e.message,true);}
}
async function load() {
    closeReview();if(!state.employee)return;
    try {
        const q=new URLSearchParams({employeeReference:state.employee,page:String(state.page),pageSize:'15'});
        const d=await api('/api/hr/employee-contracts?'+q);
        const body=el('contractRows');body.replaceChildren();
        for(const c of d.items||[]) {
            const tr=document.createElement('tr');
            [c.contractCode,String(c.effectiveFrom).slice(0,10),
                c.effectiveTo?String(c.effectiveTo).slice(0,10):'Open',types[c.state]||c.state].forEach(x=>tr.append(cell(x)));
            const actions=document.createElement('td');
            if(c.canReview) {
                const btn=document.createElement('button');btn.type='button';btn.className='btn btn-sm btn-outline-primary';btn.textContent='Review';
                btn.addEventListener('click',()=>{state.selected=c;el('contractReason').value='';
                    el('contractReviewTitle').textContent='Review '+c.contractCode;
                    el('contractReview').classList.remove('d-none');});
                actions.append(btn);
            }
            tr.append(actions);body.append(tr);
        }
        if(!d.items?.length){const tr=document.createElement('tr'),td=cell('No contracts.');td.colSpan=5;tr.append(td);body.append(tr);}
        state.pages=Math.max(1,Math.ceil((d.totalCount||0)/15));
        el('contractPage').textContent='Page '+state.page+' of '+state.pages;
        el('contractPrev').disabled=state.page<=1;el('contractNext').disabled=state.page>=state.pages;
    }catch(e){notice(e.message,true);}
}
async function create(e) {
    e.preventDefault();if(state.busy||!state.employee||!el('contractForm').reportValidity())return;
    const from=el('contractFrom').value,to=el('contractTo').value;
    if(to&&to<from)return notice('Contract end date is before its start.',true);
    state.busy=true;el('contractCreate').disabled=true;
    try {
        await api('/api/hr/employee-contracts','POST',{clientRequestId:state.key,
            employeeReference:state.employee,contractCode:el('contractCode').value.trim(),
            effectiveFrom:from,effectiveTo:to||null,remarks:el('contractRemarks').value.trim()||null});
        state.key=crypto.randomUUID();el('contractForm').reset();state.page=1;
        notice('Contract draft saved. Another administrator must review.');await load();
    }catch(e){notice(e.message+' Check the register before attempting a new request.',true);}
    finally{state.busy=false;el('contractCreate').disabled=false;}
}
async function review(approve) {
    if(state.busy||!state.selected)return;
    const c=state.selected,reason=el('contractReason').value.trim();
    if(!approve&&!reason)return notice('Rejection reason required.',true);
    if(!confirm((approve?'Approve ':'Reject ')+c.contractCode+'? This review is final.'))return;
    state.busy=true;el('contractApprove').disabled=true;el('contractReject').disabled=true;
    try {await api('/api/hr/employee-contracts/'+encodeURIComponent(c.reference)+(approve?'/approve':'/reject'),
        'POST',{rowVersion:c.rowVersion,reason:reason||null});
        notice('Review saved.');await load();
    }catch(e){notice(e.message,true);}
    finally{state.busy=false;el('contractApprove').disabled=false;el('contractReject').disabled=false;}
}
el('contractFind').addEventListener('submit',e=>{e.preventDefault();find();});
el('contractEmployee').addEventListener('change',()=>{state.employee=el('contractEmployee').value||null;state.page=1;state.key=crypto.randomUUID();load();});
el('contractForm').addEventListener('submit',create);
el('contractApprove').addEventListener('click',()=>review(true));
el('contractReject').addEventListener('click',()=>review(false));
el('contractCancel').addEventListener('click',closeReview);
el('contractPrev').addEventListener('click',()=>{state.page=Math.max(1,state.page-1);load();});
el('contractNext').addEventListener('click',()=>{state.page=Math.min(state.pages,state.page+1);load();});
find();
})();