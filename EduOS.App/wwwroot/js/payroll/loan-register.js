(() => {
    'use strict';
    const root=document.getElementById('payrollLoanRegister');if(!root)return;
    const el=id=>document.getElementById(id),token=root.querySelector('input[name="__RequestVerificationToken"]')?.value
        ||document.querySelector('meta[name="request-verification-token"]')?.content;
    let page=1,pages=1,busy=false,submitted=false,requestId=crypto.randomUUID();
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
        if(!response.ok||!result?.success)throw Error(result?.message||'Request could not be completed.');
        return result.data;
    }
    function cell(value) {const td=document.createElement('td');td.textContent=value===null||value===undefined?'—':String(value);return td;}
    async function employees() {
        try {
            const qs=new URLSearchParams({page:'1',pageSize:'50',search:trim('loanRegSearch')});
            const data=await api('/api/hr-payroll/employee-options?'+qs);
            const select=el('loanRegEmployee');select.replaceChildren(new Option('Select an employee',''));
            for(const x of data.items||[])select.add(new Option(x.employeeCode+' · '+x.fullName,x.reference));
            if((data.totalCount||0)>50)notice('More employees match. Refine your search before selecting.',true);
        }catch(e){notice(e.message,true);}
    }
    async function load() {
        try {
            const qs=new URLSearchParams({pendingOnly:'false',page:String(page),pageSize:'10'});
            const data=await api('/api/finance/loan-disbursements?'+qs);
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
        }catch(e){notice(e.message,true);}
    }
    function newEntry() {
        if(busy)return;
        el('loanRegForm').reset();el('loanRegDate').value=today();
        el('loanRegSave').disabled=false;submitted=false;requestId=crypto.randomUUID();
        el('loanRegEmployee').replaceChildren(new Option('Select an employee',''));
    }
    async function save(e) {
        e.preventDefault();
        if(busy||submitted||!el('loanRegForm').reportValidity())return;
        const amount=Number(trim('loanRegAmount')),installment=Number(trim('loanRegInstallment'));
        if(!Number.isFinite(amount)||!Number.isFinite(installment)||amount<=0||installment<=0||installment>amount) {
            notice('Installment must be positive and cannot exceed principal.',true);return;
        }
        if(!confirm('Register this employee loan? Registration does not disburse cash.'))return;
        const body={clientRequestId:requestId,employeeReference:trim('loanRegEmployee'),
            issueDate:trim('loanRegDate'),principalAmount:amount,installmentAmount:installment,
            remarks:trim('loanRegRemarks')||null};
        busy=true;submitted=true;el('loanRegSave').disabled=true;
        try {
            const registered=await api('/api/hr-payroll/loans','POST',body);
            notice('Loan #'+registered.id+' registered. Cash disbursement requires a separate authorized posting.');
            page=1;await load();
        }catch(err){
            notice(err.message+' Check the loan register before starting a new entry. Do not retry this request blindly.',true);
            await load();
        }finally{busy=false;}
    }
    el('loanRegFind').addEventListener('click',employees);
    el('loanRegSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();employees();}});
    el('loanRegForm').addEventListener('submit',save);
    el('loanRegNew').addEventListener('click',newEntry);
    el('loanRegRefresh').addEventListener('click',load);
    el('loanRegPrev').addEventListener('click',()=>{page=Math.max(1,page-1);load();});
    el('loanRegNext').addEventListener('click',()=>{page=Math.min(pages,page+1);load();});
    load();
})();
