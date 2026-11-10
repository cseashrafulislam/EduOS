(() => {
'use strict';
const root=document.getElementById('employeeContracts');if(!root)return;
const el=id=>document.getElementById(id),token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const state={employee:null,page:1,pages:1,key:crypto.randomUUID(),keyFingerprint:'',
 selected:null,evidence:null,busy:false,accessRevoked:false,searchSeq:0,loadSeq:0,
 uploadKey:null,uploadFile:null,uploadContract:null,stagedReference:null};
const guards={};
for(const [name,id] of Object.entries({draft:'contractForm',review:'contractReview',
 upload:'contractEvidenceUpload',verification:'contractEvidenceReview'}))
 if(window.EduOSFormGuard)guards[name]=window.EduOSFormGuard.create(el(id));
function clean(name){guards[name]?.markClean();}
function mayDiscard(name){return !guards[name]?.isDirty()||guards[name].confirmDiscard();}
function allDiscardable(){return ['draft','review','upload','verification'].every(mayDiscard);}
const types={1:'Draft',2:'Approved',3:'Rejected'};
function notice(msg,bad=false){const x=el('contractAlert');x.className='alert '+(bad?'alert-danger':'alert-success');x.textContent=msg;x.focus();}
async function api(url,method='GET',body) {
    const headers={Accept:'application/json'};
    if(method!=='GET') {if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;}
    if(body!==undefined)headers['Content-Type']='application/json';
    const r=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,
        ...(body===undefined?{}:{body:JSON.stringify(body)})});
    const j=await r.json().catch(()=>null);
    if(!r.ok||!j?.success){const error=Error(j?.message||'Contract request failed.');
        error.statusCode=r.status;throw error;}
    return j.data;
}
function errorNotice(error){
    if([401,403].includes(error.statusCode)){
        state.accessRevoked=true;++state.searchSeq;++state.loadSeq;
        state.employee=null;state.selected=null;state.evidence=null;
        el('contractRows').replaceChildren();el('contractEmployee').replaceChildren(new Option('Access unavailable',''));
        el('contractReview').classList.add('d-none');el('contractEvidence').classList.add('d-none');
        el('contractForm').reset();el('contractReview').reset();el('contractEvidenceUpload').reset();
        el('contractEvidenceReview').reset();
        ['draft','review','upload','verification'].forEach(clean);root.inert=true;
    }
    notice(error.message,true);
}
function requestIdFor(command){
    const fingerprint=JSON.stringify(command);
    if(!state.key||state.keyFingerprint!==fingerprint){state.key=crypto.randomUUID();state.keyFingerprint=fingerprint;}
    return state.key;
}
function clearDraft(){el('contractForm').reset();clean('draft');state.key=crypto.randomUUID();state.keyFingerprint='';}
function cell(v){const t=document.createElement('td');t.textContent=v??'';return t;}
function closeReview(force=false){
    if(!force&&(state.busy||!mayDiscard('review')))return false;
    state.selected=null;el('contractReview').reset();clean('review');
    el('contractReview').classList.add('d-none');return true;
}
function closeEvidence(force=false){
    if(!force&&(state.busy||!mayDiscard('upload')||!mayDiscard('verification')))return false;
    state.evidence=null;el('contractEvidenceUpload').reset();el('contractEvidenceReview').reset();
    clean('upload');clean('verification');
    state.uploadKey=null;state.uploadFile=null;state.uploadContract=null;state.stagedReference=null;
    el('contractEvidence').classList.add('d-none');el('contractEvidenceDownload')?.remove();return true;
}
function openEvidence(contract) {
    if(state.busy||!closeReview()||!closeEvidence())return;
    state.evidence=contract;
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
    if(state.accessRevoked||state.busy||!allDiscardable())return;
    const term=el('contractSearch').value.trim();
    if(term.length<2||term.length>100)return notice('Enter 2–100 characters to search employees.',true);
    const serial=++state.searchSeq;
    try {
        const q=new URLSearchParams({page:'1',pageSize:'50',search:term});
        const d=await api('/api/hr/employees?'+q);
        if(serial!==state.searchSeq||el('contractSearch').value.trim()!==term||
            state.busy||!allDiscardable())return;
        ++state.loadSeq;state.employee=null;state.page=1;state.pages=1;
        clearDraft();closeReview(true);closeEvidence(true);
        el('contractRows').replaceChildren();el('contractPage').textContent='Select employee.';
        const select=el('contractEmployee');select.replaceChildren(new Option('Select employee',''));
        for(const e of d.items||[])select.add(new Option(e.employeeCode+' · '+e.fullName,e.reference));
        if(d.totalCount>50)notice('Refine search; more than 50 employees match.',true);
    }catch(e){if(serial===state.searchSeq)errorNotice(e);}
}
async function load() {
    const serial=++state.loadSeq,employee=state.employee,page=state.page;
    closeReview(true);closeEvidence(true);
    if(!employee){el('contractRows').replaceChildren();return;}
    el('contractRows').replaceChildren();el('contractPage').textContent='Loading contracts…';
    el('contractPrev').disabled=true;el('contractNext').disabled=true;
    try {
        const q=new URLSearchParams({employeeReference:employee,page:String(page),pageSize:'15'});
        const d=await api('/api/hr/employee-contracts?'+q);
        if(serial!==state.loadSeq||employee!==state.employee||page!==state.page)return;
        const body=el('contractRows');body.replaceChildren();
        for(const c of d.items||[]) {
            const tr=document.createElement('tr');
            [c.contractCode,String(c.effectiveFrom).slice(0,10),
                c.effectiveTo?String(c.effectiveTo).slice(0,10):'Open',types[c.state]||c.state].forEach(x=>tr.append(cell(x)));
            const actions=document.createElement('td');
            if(c.canReview) {
                const btn=document.createElement('button');btn.type='button';btn.className='btn btn-sm btn-outline-primary';btn.textContent='Review';
                btn.addEventListener('click',()=>{if(state.busy||!closeReview()||!closeEvidence())return;
                    state.selected=c;el('contractReason').value='';clean('review');
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
    }catch(e){if(serial===state.loadSeq&&employee===state.employee&&page===state.page){
        el('contractPage').textContent='Contract history unavailable. Retry selection.';throw e;}}
}
async function create(e) {
    e.preventDefault();if(state.busy||state.accessRevoked||!state.employee||!el('contractForm').reportValidity())return;
    if(!['review','upload','verification'].every(mayDiscard))return;
    const from=el('contractFrom').value,to=el('contractTo').value;
    if(to&&to<from)return notice('Contract end date is before its start.',true);
    const command={employeeReference:state.employee,contractCode:el('contractCode').value.trim(),
        effectiveFrom:from,effectiveTo:to||null,remarks:el('contractRemarks').value.trim()||null};
    const key=requestIdFor(command);
    state.busy=true;root.inert=true;
    try {
        await api('/api/hr/employee-contracts','POST',{clientRequestId:key,...command});
        clearDraft();state.page=1;
        try{await load();notice('Contract draft saved. Another administrator must review.');}
        catch(readError){if([401,403].includes(readError.statusCode))errorNotice(readError);
            notice('Contract draft saved. Register refresh unavailable: '+readError.message);}
    }catch(error){errorNotice(error);
        if(!state.accessRevoked)notice(error.message+' Check the register before retrying unchanged terms.',true);}
    finally{state.busy=false;root.inert=state.accessRevoked;}
}
async function review(approve) {
    if(state.busy||state.accessRevoked||!state.selected)return;
    const c=state.selected,reason=el('contractReason').value.trim();
    if(!approve&&!reason)return notice('Rejection reason required.',true);
    if(!confirm((approve?'Approve ':'Reject ')+c.contractCode+'? This review is final.'))return;
    state.busy=true;root.inert=true;
    try {await api('/api/hr/employee-contracts/'+encodeURIComponent(c.reference)+(approve?'/approve':'/reject'),
        'POST',{rowVersion:c.rowVersion,reason:reason||null});
        closeReview(true);
        try{await load();notice('Contract review saved.');}
        catch(readError){if([401,403].includes(readError.statusCode))errorNotice(readError);
            notice('Contract review saved. Register refresh unavailable: '+readError.message);}
    }catch(error){errorNotice(error);}
    finally{state.busy=false;root.inert=state.accessRevoked;}
}
async function attachPdf(event) {
    event.preventDefault();if(state.busy||state.accessRevoked||!state.evidence)return;
    const file=el('contractPdf').files?.[0];
    if(!file||file.size<1||file.size>5242880||!file.name.toLowerCase().endsWith('.pdf')||
        file.type!=='application/pdf')return notice('Choose a PDF file no larger than 5 MB.',true);
    if(!confirm('Upload signed PDF for independent verification?'))return;
    const contract=state.evidence.reference;
    if(state.uploadFile!==file||state.uploadContract!==contract){
        state.uploadFile=file;state.uploadContract=contract;state.uploadKey=crypto.randomUUID();
        state.stagedReference=null;
    }
    state.busy=true;root.inert=true;
    try {
        if(!token)throw Error('Security token missing. Reload.');
        if(!state.stagedReference){
            const body=new FormData();body.append('file',file);body.append('requestId',state.uploadKey);
            const result=await fetch('/api/document-admin/files',{method:'POST',credentials:'same-origin',cache:'no-store',
                headers:{RequestVerificationToken:token},body});
            const uploaded=await result.json().catch(()=>null);
            if(!result.ok||!uploaded?.success){const error=Error(uploaded?.message||'Private PDF upload failed.');
                error.statusCode=result.status;throw error;}
            state.stagedReference=uploaded.data.reference;
        }
        await api('/api/hr/employee-contracts/'+encodeURIComponent(contract)+'/signed-pdf',
            'POST',{fileReference:state.stagedReference});
        closeEvidence(true);
        try{await load();notice('Signed PDF attached. A different administrator must verify it.');}
        catch(readError){if([401,403].includes(readError.statusCode))errorNotice(readError);
            notice('Signed PDF attached. Register refresh unavailable: '+readError.message);}
    }catch(error){errorNotice(error);
        if(!state.accessRevoked)notice(error.message+' The same staged file can be retried; inspect unattached files in Document Administration.',true);}
    finally{state.busy=false;root.inert=state.accessRevoked;}
}
async function verifyPdf(approve) {
    if(state.busy||state.accessRevoked||!state.evidence?.signedPdf?.canVerify)return;
    const c=state.evidence,note=el('contractPdfNote').value.trim();
    if(!approve&&!note)return notice('Enter the reason for rejecting the signed PDF.',true);
    if(!confirm((approve?'Verify':'Reject')+' the scanned PDF for '+c.contractCode+'?'))return;
    state.busy=true;root.inert=true;
    try {
        await api('/api/hr/employee-contracts/'+encodeURIComponent(c.reference)+'/signed-pdf/'+(approve?'approve':'reject'),
            'POST',{rowVersion:c.signedPdf.rowVersion,note:note||null});
        closeEvidence(true);
        try{await load();notice('Signed PDF review recorded in audit history.');}
        catch(readError){if([401,403].includes(readError.statusCode))errorNotice(readError);
            notice('Signed PDF review saved. Register refresh unavailable: '+readError.message);}
    }catch(error){errorNotice(error);}
    finally{state.busy=false;root.inert=state.accessRevoked;}
}
el('contractFind').addEventListener('submit',e=>{e.preventDefault();find().catch(errorNotice);});
el('contractSearch').addEventListener('input',()=>{++state.searchSeq;});
el('contractEmployee').addEventListener('change',()=>{
    if(state.accessRevoked||state.busy||!allDiscardable()){
        el('contractEmployee').value=state.employee||'';return;
    }
    ++state.searchSeq;state.employee=el('contractEmployee').value||null;state.page=1;
    clearDraft();load().catch(errorNotice);
});
el('contractForm').addEventListener('submit',create);
el('contractApprove').addEventListener('click',()=>review(true));
el('contractReject').addEventListener('click',()=>review(false));
el('contractCancel').addEventListener('click',closeReview);
el('contractEvidenceClose').addEventListener('click',closeEvidence);
el('contractEvidenceUpload').addEventListener('submit',attachPdf);
el('contractPdfApprove').addEventListener('click',()=>verifyPdf(true));
el('contractPdfReject').addEventListener('click',()=>verifyPdf(false));
function page(delta){
    if(state.accessRevoked||state.busy||!['review','upload','verification'].every(mayDiscard))return;
    const next=Math.max(1,Math.min(state.pages,state.page+delta));
    if(next===state.page)return;
    state.page=next;load().catch(errorNotice);
}
el('contractPrev').addEventListener('click',()=>page(-1));
el('contractNext').addEventListener('click',()=>page(1));
el('contractPage').textContent='Search for an employee to load contracts.';
})();
