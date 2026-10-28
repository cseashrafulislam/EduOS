(() => {
'use strict';
const root=document.getElementById('employeeContracts');if(!root)return;
const el=id=>document.getElementById(id),token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const state={employee:null,page:1,pages:1,key:crypto.randomUUID(),selected:null,evidence:null,busy:false};
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
function closeEvidence(){state.evidence=null;el('contractEvidence').classList.add('d-none');}
function openEvidence(contract) {
    closeReview();state.evidence=contract;
    const d=contract.signedPdf, panel=el('contractEvidence');panel.classList.remove('d-none');
    el('contractEvidenceTitle').textContent='Signed PDF · '+contract.contractCode;
    el('contractEvidenceStatus').textContent=d ?
        'Status: '+d.status+' · File: '+d.fileName+' · SHA-256: '+d.sha256 :
        'No signed PDF attached. Contract terms approval alone does not verify signatures.';
    const attachAllowed=!d||d.status==='Rejected';
    el('contractEvidenceUpload').classList.toggle('d-none',!attachAllowed);
    el('contractEvidenceReview').classList.toggle('d-none',!d?.canVerify);
    el('contractPdf').value='';el('contractPdfNote').value='';
    if(d&&d.status==='Approved'||d&&d.canVerify) {
        const link=document.createElement('a');link.className='btn btn-sm btn-outline-primary mt-2';
        link.textContent=d.canVerify?'Open independent reviewer PDF preview':'Download approved signed PDF';
        link.href='/api/hr/employee-contracts/'+encodeURIComponent(contract.reference)+'/signed-pdf/download'+
            (d.canVerify?'?reviewPreview=true':'');
        const old=el('contractEvidenceDownload');if(old)old.remove();
        link.id='contractEvidenceDownload';panel.append(link);
    }else{el('contractEvidenceDownload')?.remove();}
}
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
    closeReview();closeEvidence();if(!state.employee)return;
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
            if(Number(c.state)===2||c.state==='Approved') {
                const evidence=document.createElement('button');evidence.type='button';
                evidence.className='btn btn-sm btn-outline-secondary ms-1';
                evidence.textContent=c.signedPdf?'Signed PDF · '+c.signedPdf.status:'Attach signed PDF';
                evidence.addEventListener('click',()=>openEvidence(c));actions.append(evidence);
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
async function attachPdf(event) {
    event.preventDefault();if(state.busy||!state.evidence)return;
    const file=el('contractPdf').files?.[0];
    if(!file||file.size<1||file.size>5242880||!file.name.toLowerCase().endsWith('.pdf')||
        file.type!=='application/pdf')return notice('Choose a PDF file no larger than 5 MB.',true);
    if(!confirm('Upload signed PDF for independent verification?'))return;
    state.busy=true;el('contractPdfAttach').disabled=true;
    try {
        if(!token)throw Error('Security token missing. Reload.');
        const body=new FormData();body.append('file',file);body.append('requestId',crypto.randomUUID());
        const result=await fetch('/api/document-admin/files',{method:'POST',credentials:'same-origin',cache:'no-store',
            headers:{RequestVerificationToken:token},body});
        const uploaded=await result.json().catch(()=>null);
        if(!result.ok||!uploaded?.success)throw Error(uploaded?.message||'Private PDF upload failed.');
        await api('/api/hr/employee-contracts/'+encodeURIComponent(state.evidence.reference)+'/signed-pdf',
            'POST',{fileReference:uploaded.data.reference});
        notice('Signed PDF attached. A different administrator must verify it.');await load();
    }catch(error){notice(error.message+' Any staged unattached file can be inspected in Document Administration.',true);}
    finally{state.busy=false;el('contractPdfAttach').disabled=false;}
}
async function verifyPdf(approve) {
    if(state.busy||!state.evidence?.signedPdf?.canVerify)return;
    const c=state.evidence,note=el('contractPdfNote').value.trim();
    if(!approve&&!note)return notice('Enter the reason for rejecting the signed PDF.',true);
    if(!confirm((approve?'Verify':'Reject')+' the scanned PDF for '+c.contractCode+'?'))return;
    state.busy=true;el('contractPdfApprove').disabled=true;el('contractPdfReject').disabled=true;
    try {
        await api('/api/hr/employee-contracts/'+encodeURIComponent(c.reference)+'/signed-pdf/'+(approve?'approve':'reject'),
            'POST',{rowVersion:c.signedPdf.rowVersion,note:note||null});
        notice('Signed PDF review recorded in audit history.');await load();
    }catch(error){notice(error.message,true);}
    finally{state.busy=false;el('contractPdfApprove').disabled=false;el('contractPdfReject').disabled=false;}
}
el('contractFind').addEventListener('submit',e=>{e.preventDefault();find();});
el('contractEmployee').addEventListener('change',()=>{state.employee=el('contractEmployee').value||null;state.page=1;state.key=crypto.randomUUID();load();});
el('contractForm').addEventListener('submit',create);
el('contractApprove').addEventListener('click',()=>review(true));
el('contractReject').addEventListener('click',()=>review(false));
el('contractCancel').addEventListener('click',closeReview);
el('contractEvidenceClose').addEventListener('click',closeEvidence);
el('contractEvidenceUpload').addEventListener('submit',attachPdf);
el('contractPdfApprove').addEventListener('click',()=>verifyPdf(true));
el('contractPdfReject').addEventListener('click',()=>verifyPdf(false));
el('contractPrev').addEventListener('click',()=>{state.page=Math.max(1,state.page-1);load();});
el('contractNext').addEventListener('click',()=>{state.page=Math.min(state.pages,state.page+1);load();});
find();
})();
