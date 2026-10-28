(() => {
'use strict';
const root = document.getElementById('payrollApp'); if (!root) return;
const admin = root.dataset.manager === 'true';
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const el = id => document.getElementById(id);
const state = {runPage:1, runPages:1, detailPage:1, detailPages:1, slipPage:1, slipPages:1,
    selected:null, requestId:null, requestPeriod:null, busy:false};
const cell = value => {const td=document.createElement('td');td.textContent=value==null?'':String(value);return td;};
const money = value => Number(value||0).toLocaleString('en-BD',{minimumFractionDigits:2,maximumFractionDigits:2});
const status = value => ({1:'Draft',2:'Calculated',3:'Approved',4:'Posted',5:'Cancelled'}[value] ||
    ({Draft:'Draft',Calculated:'Calculated',Approved:'Approved',Posted:'Posted',Cancelled:'Cancelled'}[value] || String(value)));
function notice(message,error=false) {
    const box=el('payrollAlert');box.className='alert '+(error?'alert-danger':'alert-success');
    box.textContent=message;box.focus();
}
async function api(url,method='GET',body) {
    const headers={Accept:'application/json'};
    if(method!=='GET') {
        if(!token)throw Error('Antiforgery token missing. Reload the page.');
        headers.RequestVerificationToken=token;
    }
    if(body!==undefined)headers['Content-Type']='application/json';
    const response=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers,
        ...(body===undefined?{}:{body:JSON.stringify(body)})});
    const result=await response.json().catch(()=>null);
    if(!response.ok||!result?.success)throw Error(result?.message||'Payroll request failed.');
    return result.data;
}
function pageInfo(prefix,page,pages,count) {
    el(prefix+'PageLabel').textContent='Page '+page+' of '+pages+' · '+count+' records';
    el(prefix+'Prev').disabled=page<=1;el(prefix+'Next').disabled=page>=pages;
}
function empty(body,cols,message) {
    const tr=document.createElement('tr'),td=cell(message);td.colSpan=cols;tr.append(td);body.append(tr);
}
function period() {
    const year=Number(el('payrollYear').value),month=Number(el('payrollMonth').value);
    if(!Number.isInteger(year)||year<2000||year>2200||!Number.isInteger(month)||month<1||month>12)
        throw Error('Select a valid payroll month and year.');
    return {year,month};
}
function action(text,handler,css='btn-outline-primary') {
    const button=document.createElement('button');button.type='button';
    button.className='btn btn-sm '+css;button.textContent=text;
    button.addEventListener('click',handler);return button;
}
async function loadRuns() {
    if(!admin)return;
    try {
        const {year,month}=period();
        const data=await api('/api/hr-payroll/runs?'+new URLSearchParams({
            year:String(year),month:String(month),page:String(state.runPage),pageSize:'10'}));
        const body=el('payrollRunRows');body.replaceChildren();
        state.runPages=Math.max(1,Math.ceil((data.totalCount||0)/10));
        for(const row of data.items||[]) {
            const tr=document.createElement('tr');
            tr.append(cell(row.runNumber),cell(row.month+'/'+row.year),cell(row.campusId??'Institution-wide'),
                cell(row.employeeCount),cell(money(row.grossAmount)),cell(money(row.deductionAmount)),
                cell(money(row.netAmount)),cell(status(row.state)));
            const controls=document.createElement('td');
            controls.append(action('Employees',()=>loadEmployees(row.reference,1)));
            if(status(row.state)==='Calculated')
                controls.append(' ',action('Approve',()=>approve(row),'btn-outline-warning'));
            if(status(row.state)==='Approved')
                controls.append(' ',action('GL posting',()=>{window.location.assign('/PayrollJournalPosting/Index');},'btn-outline-success'));
            if(status(row.state)==='Posted')
                controls.append(' ',action('Payments',()=>{window.location.assign('/PayrollPaymentPosting/Index');},'btn-outline-success'));
            tr.append(controls);body.append(tr);
        }
        if(!data.items?.length)empty(body,9,'No payroll runs for this period.');
        pageInfo('payrollRun',state.runPage,state.runPages,data.totalCount||0);
    } catch(e){notice(e.message,true);}
}
async function generate() {
    if(state.busy)return;
    let selected;try{selected=period();}catch(e){return notice(e.message,true);}
    const campus=el('payrollCampus').value;
    selected.campusId=campus?Number(campus):null;
    if(!confirm('Calculate '+(campus?'campus '+el('payrollCampus').selectedOptions[0].text:'institution-wide')+
        ' payroll for '+selected.month+'/'+selected.year+'? Full-month primary campus assignments and salary structures are required.'))return;
    const key=selected.year+'-'+selected.month+'-'+(campus||'all');
    if(state.requestPeriod!==key){state.requestPeriod=key;state.requestId=crypto.randomUUID();}
    state.busy=true;el('payrollGenerate').disabled=true;
    try {
        const row=await api('/api/hr-payroll/runs','POST',{clientRequestId:state.requestId,...selected});
        state.requestId=null;state.requestPeriod=null;state.runPage=1;
        notice('Payroll '+row.runNumber+' calculated. A different authorized administrator must approve it.');
        await loadRuns();
    } catch(e){notice(e.message+' The request ID is retained for a safe retry; inspect the register first.',true);}
    finally{state.busy=false;el('payrollGenerate').disabled=false;}
}
async function approve(row) {
    if(state.busy)return;
    if(!confirm('Approve '+row.runNumber+'? The payroll creator cannot approve their own run.'))return;
    state.busy=true;
    try {
        const latest=await api('/api/hr-payroll/runs/'+encodeURIComponent(row.reference));
        if(status(latest.state)!=='Calculated')throw Error('Payroll is no longer calculated. Reload the register.');
        await api('/api/hr-payroll/runs/'+encodeURIComponent(row.reference)+'/approve','POST',
            {rowVersion:latest.rowVersion});
        notice('Payroll approved. Post the balanced institutional journal before recording payments.');
        await loadRuns();
    }catch(e){notice(e.message,true);}
    finally{state.busy=false;}
}
async function loadEmployees(reference,page=1) {
    if(!admin)return;
    state.selected=reference;state.detailPage=page;
    try {
        const data=await api('/api/hr-payroll/runs/'+encodeURIComponent(reference)+'/employees?'+
            new URLSearchParams({page:String(page),pageSize:'10'}));
        if(state.selected!==reference)return;
        const body=el('payrollEmployeeRows');body.replaceChildren();
        for(const row of data.items||[]) {
            const tr=document.createElement('tr');
            const breakdown=(row.lines||[]).map(x=>x.salaryComponentName+': '+money(x.amount)).join('; ');
            tr.append(cell(row.employeeCode),cell(row.employeeName),cell(money(row.grossAmount)),
                cell(money(row.deductionAmount)),cell(money(row.netAmount)),cell(breakdown));
            body.append(tr);
        }
        if(!data.items?.length)empty(body,6,'No employees in this payroll run.');
        state.detailPages=Math.max(1,Math.ceil((data.totalCount||0)/10));
        pageInfo('payrollDetail',page,state.detailPages,data.totalCount||0);
        el('payrollDetails').classList.remove('d-none');
    }catch(e){notice(e.message,true);}
}
async function loadSlips() {
    try {
        const data=await api('/api/hr-payroll/my-payslips?'+new URLSearchParams({
            page:String(state.slipPage),pageSize:'10'}));
        const body=el('payrollSlipRows');body.replaceChildren();
        for(const row of data.items||[]) {
            const tr=document.createElement('tr');
            tr.append(cell(row.employeeName),cell(money(row.grossAmount)),cell(money(row.deductionAmount)),
                cell(money(row.netAmount)),
                cell((row.lines||[]).map(x=>x.salaryComponentName+': '+money(x.amount)).join('; ')));
            body.append(tr);
        }
        if(!data.items?.length)empty(body,5,'No posted payslips found.');
        state.slipPages=Math.max(1,Math.ceil((data.totalCount||0)/10));
        pageInfo('payrollSlip',state.slipPage,state.slipPages,data.totalCount||0);
    }catch(e){notice(e.message,true);}
}
if(admin) {
    const now=new Date();el('payrollYear').value=String(now.getFullYear());
    el('payrollMonth').value=String(now.getMonth()+1);
    el('payrollLoad').addEventListener('click',()=>{state.runPage=1;loadRuns();});
    el('payrollGenerate').addEventListener('click',generate);
    api('/api/institution-onboarding/campus-list').then(campuses=>{
        const select=el('payrollCampus');
        for(const campus of campuses||[])
            if(campus.isActive)select.add(new Option(campus.name,campus.id));
    }).catch(e=>notice('Campus selector: '+e.message,true));
    for(const id of ['payrollYear','payrollMonth','payrollCampus'])
        el(id).addEventListener('change',()=>{state.requestId=null;state.requestPeriod=null;state.runPage=1;});
    el('payrollRunPrev').addEventListener('click',()=>{if(state.runPage>1){state.runPage--;loadRuns();}});
    el('payrollRunNext').addEventListener('click',()=>{if(state.runPage<state.runPages){state.runPage++;loadRuns();}});
    el('payrollDetailPrev').addEventListener('click',()=>{if(state.selected&&state.detailPage>1)loadEmployees(state.selected,state.detailPage-1);});
    el('payrollDetailNext').addEventListener('click',()=>{if(state.selected&&state.detailPage<state.detailPages)loadEmployees(state.selected,state.detailPage+1);});
    loadRuns();
}
el('payrollSlipPrev').addEventListener('click',()=>{if(state.slipPage>1){state.slipPage--;loadSlips();}});
el('payrollSlipNext').addEventListener('click',()=>{if(state.slipPage<state.slipPages){state.slipPage++;loadSlips();}});
el('payrollMy').addEventListener('click',()=>{state.slipPage=1;loadSlips();});
if(!admin)loadSlips();
})();