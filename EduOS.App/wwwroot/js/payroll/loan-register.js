(() => {
    'use strict';
    const root=document.getElementById('payrollLoanRegister');if(!root)return;
    const el=id=>document.getElementById(id),token=root.querySelector('input[name="__RequestVerificationToken"]')?.value
        ||document.querySelector('meta[name="request-verification-token"]')?.content;
    let page=1,pages=1,busy=false,submitted=false,accessRevoked=false,
        requestId=crypto.randomUUID(),requestFingerprint='',searchSerial=0,loadSerial=0,lastEmployee='';
    const form=el('loanRegForm'),guard=window.EduOSFormGuard?.create(form);
    const money=x=>Number(x||0).toLocaleString('en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
    const trim=id=>el(id).value.trim();
    const today=()=>{const d=new Date();return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');};
    el('loanRegDate').value=today();
    function notice(text,error=false) {
        const box=el('loanRegNotice');box.className='alert '+(error?'alert-danger':'alert-success');
        box.textContent=text;box.focus();
    }
    async function api(url,method='GET',body) {
        const headers={Accept:'application/json'};
        if(method!=='GET'){if(!token)throw Error('Security token missing. Reload the page.');headers.RequestVerificationToken=token;}
        if(body!==undefined)headers['Content-Type']='application/json';
        const response=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,
            ...(body===undefined?{}:{body:JSON.stringify(body)})});
        const result=await response.json().catch(()=>null);
        if(!response.ok||!result?.success){const error=Error(result?.message||'Request could not be completed.');
            error.statusCode=response.status;throw error;}
        return result.data;
    }
    function handleError(error){
        if([401,403].includes(error.statusCode)){
            accessRevoked=true;++searchSerial;++loadSerial;
            el('loanRegRows').replaceChildren();el('loanRegEmployee').replaceChildren(new Option('Access unavailable',''));
            form.reset();guard?.markClean();root.inert=true;
        }
        notice(error.message,true);
    }
    function hasDraft(){return !!(trim('loanRegEmployee')||trim('loanRegAmount')||
        trim('loanRegInstallment')||trim('loanRegRemarks'));}
    function requestIdFor(command){
        const fingerprint=JSON.stringify(command);
        if(!requestId||fingerprint!==requestFingerprint){requestId=crypto.randomUUID();requestFingerprint=fingerprint;}
        return requestId;
    }
    function cell(value) {const td=document.createElement('td');td.textContent=value===null||value===undefined?'—':String(value);return td;}
    async function employees() {
        if(busy||submitted||accessRevoked)return;
        const term=trim('loanRegSearch');
        if(term.length<2||term.length>100){notice('Enter 2–100 characters to find an employee.',true);return;}
        if(hasDraft()&&!confirm('Discard the current employee and loan draft before searching?'))return;
        const seq=++searchSerial;
        try {
            const qs=new URLSearchParams({page:'1',pageSize:'50',search:term});
            const data=await api('/api/hr-payroll/employee-options?'+qs);
            if(seq!==searchSerial||term!==trim('loanRegSearch')||busy)return;
            if(hasDraft()&&!confirm('Discard the current employee and loan draft?'))return;
            const query=trim('loanRegSearch');newEntry(true);el('loanRegSearch').value=query;guard?.markClean();
            const select=el('loanRegEmployee');select.replaceChildren(new Option('Select an employee',''));
            for(const x of data.items||[])select.add(new Option(x.employeeCode+' · '+x.fullName,x.reference));
            if((data.totalCount||0)>50)notice('More employees match. Refine your search before selecting.',true);
        }catch(e){if(seq===searchSerial)handleError(e);}
    }
    async function load() {
        if(accessRevoked)return;
        const seq=++loadSerial,currentPage=page;
        el('loanRegRows').replaceChildren();el('loanRegPage').textContent='Loading loan register…';
        try {
            const qs=new URLSearchParams({pendingOnly:'false',page:String(currentPage),pageSize:'10'});
            const data=await api('/api/finance/loan-disbursements?'+qs);
            if(seq!==loadSerial||page!==currentPage)return;
            pages=Math.max(1,Math.ceil((data.totalCount||0)/(data.pageSize||10)));
            const body=el('loanRegRows');body.replaceChildren();
            for(const x of data.loans||[]) {
                const tr=document.createElement('tr');
                [x.loanId,x.employeeName,x.issueDate,money(x.principalAmount),money(x.outstandingAmount),
                    money(x.installmentAmount),x.journalReference?'Posted '+x.journalReference:'Not disbursed']
                    .forEach(v=>tr.append(cell(v)));
                body.append(tr);
            }
            if(!data.loans?.length) {
                const tr=document.createElement('tr'),td=cell('No loans in this register.');td.colSpan=7;tr.append(td);body.append(tr);
            }
            el('loanRegPage').textContent='Page '+page+' of '+pages+' · '+(data.totalCount||0)+' loans';
            el('loanRegPrev').disabled=page<=1;el('loanRegNext').disabled=page>=pages;
        }catch(e){if(seq===loadSerial&&page===currentPage)throw e;}
    }
    function newEntry(force=false) {
        if(busy||accessRevoked||!force&&guard?.isDirty()&&!guard.confirmDiscard())return;
        form.reset();el('loanRegDate').value=today();
        el('loanRegSave').disabled=false;submitted=false;requestId=crypto.randomUUID();requestFingerprint='';
        lastEmployee='';guard?.markClean();
        el('loanRegEmployee').replaceChildren(new Option('Select an employee',''));
    }
    async function save(e) {
        e.preventDefault();
        if(busy||submitted||accessRevoked||!form.reportValidity())return;
        const amount=Number(trim('loanRegAmount')),installment=Number(trim('loanRegInstallment'));
        if(!Number.isFinite(amount)||!Number.isFinite(installment)||amount<=0||installment<=0||installment>amount) {
            notice('Installment must be positive and cannot exceed principal.',true);return;
        }
        if(!confirm('Register this employee loan? Registration does not disburse cash.'))return;
        const command={employeeReference:trim('loanRegEmployee'),
            issueDate:trim('loanRegDate'),principalAmount:amount,installmentAmount:installment,
            remarks:trim('loanRegRemarks')||null};
        const body={clientRequestId:requestIdFor(command),...command};
        busy=true;root.inert=true;el('loanRegSave').disabled=true;
        try {
            const registered=await api('/api/hr-payroll/loans','POST',body);
            submitted=true;guard?.markClean();page=1;
            try{await load();notice('Loan #'+registered.id+' registered. Cash disbursement requires separate authorized posting.');}
            catch(readError){if([401,403].includes(readError.statusCode))handleError(readError);
                notice('Loan #'+registered.id+' registered. Register refresh unavailable: '+readError.message);}
        }catch(err){
            handleError(err);
            if(!accessRevoked)notice(err.message+' Retry only the unchanged loan; edited details get a new request ID.',true);
        }finally{busy=false;root.inert=accessRevoked;el('loanRegSave').disabled=submitted||accessRevoked;}
    }
    el('loanRegFind').addEventListener('click',()=>employees().catch(handleError));
    el('loanRegSearch').addEventListener('input',()=>{++searchSerial;});
    el('loanRegSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();employees().catch(handleError);}});
    el('loanRegEmployee').addEventListener('change',()=>{
        if(busy||accessRevoked)return;
        const selected=el('loanRegEmployee').value;
        if(lastEmployee&&selected!==lastEmployee&&hasDraft()&&
            !confirm('Change employee and clear the loan amounts?')){
            el('loanRegEmployee').value=lastEmployee;return;
        }
        if(lastEmployee&&selected!==lastEmployee){
            el('loanRegAmount').value='';el('loanRegInstallment').value='';el('loanRegRemarks').value='';
        }
        lastEmployee=selected;
    });
    el('loanRegForm').addEventListener('submit',save);
    el('loanRegNew').addEventListener('click',()=>newEntry());
    el('loanRegRefresh').addEventListener('click',()=>load().catch(handleError));
    el('loanRegPrev').addEventListener('click',()=>{page=Math.max(1,page-1);load().catch(handleError);});
    el('loanRegNext').addEventListener('click',()=>{page=Math.min(pages,page+1);load().catch(handleError);});
    guard?.markClean();load().catch(handleError);
})();
