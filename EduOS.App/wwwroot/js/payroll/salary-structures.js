(() => {
'use strict';
const root=document.getElementById('salaryWorkspace');if(!root)return;
const el=id=>document.getElementById(id),token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const state={components:[],page:1,pages:1,history:[],requestId:null,busy:false,searchSerial:0};
const option=(value,label)=>{const x=document.createElement('option');x.value=String(value);x.textContent=label;return x;};
const cell=value=>{const x=document.createElement('td');x.textContent=value==null?'':String(value);return x;};
function notice(message,error=false){const x=el('salaryNotice');x.className='alert '+(error?'alert-danger':'alert-success');x.textContent=message;}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Antiforgery token missing. Reload this page.');headers.RequestVerificationToken=token;}
 if(body!==undefined)headers['Content-Type']='application/json';
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
 const json=await response.json().catch(()=>null);
 if(!response.ok||!json?.success)throw Error(json?.message||'Salary request failed.');
 return json.data;
}
function clearRequest(){state.requestId=null;}
function addLine(componentId='',amount='',method='Manual',percentage=null,basedOn=null){
 const tr=document.createElement('tr'),c=document.createElement('td'),m=document.createElement('td'),a=document.createElement('td'),p=document.createElement('td'),b=document.createElement('td'),actions=document.createElement('td');
 const select=document.createElement('select');select.className='form-select form-select-sm';select.required=true;
 select.append(option('','Select component'));
 for(const item of state.components.filter(x=>x.isActive))select.append(option(item.id,item.name+' ('+item.code+') — '+(['','Earning','Deduction','Employer contribution'][item.type]||'Other')));
 select.value=String(componentId);
 const mode=document.createElement('select');mode.className='form-select form-select-sm salary-mode';
 mode.append(option('Manual','Manual (BDT)'),option('Auto','Auto (%)'));mode.value=method==='Auto'?'Auto':'Manual';
 const input=document.createElement('input');input.type='number';input.min='0';input.max='999999999999';input.step='0.01';input.required=true;input.className='form-control form-control-sm salary-amount';input.value=String(amount);
 const rate=document.createElement('input');rate.type='number';rate.min='0';rate.max='100';rate.step='0.0001';rate.className='form-control form-control-sm salary-rate';rate.value=percentage==null?'':String(percentage);rate.placeholder='0–100';
 const basis=document.createElement('select');basis.className='form-select form-select-sm salary-basis';basis.append(option('','Taxable earnings'));
 for(const item of state.components.filter(x=>x.isActive&&x.type===1))basis.append(option(item.id,item.name+' ('+item.code+')'));
 basis.value=basedOn==null?'':String(basedOn);
 function sync(){
  const code=state.components.find(x=>String(x.id)===select.value)?.code;
  const eligible=code==='STAT_TAX'||code==='STAT_PF';
  if(!eligible)mode.value='Manual';
  mode.disabled=!eligible;
  const auto=eligible&&mode.value==='Auto';
  if(auto){tr.dataset.manualAmount??=input.value;input.value='0';}
  else if(tr.dataset.manualAmount!==undefined){input.value=tr.dataset.manualAmount;delete tr.dataset.manualAmount;}
  input.readOnly=auto;rate.disabled=!auto;rate.required=auto;
  basis.disabled=!auto;basis.required=auto&&code==='STAT_PF';
  basis.options[0].disabled=code==='STAT_PF';
  if(code==='STAT_PF'&&!basis.value)basis.value='';
 }
 const remove=document.createElement('button');remove.type='button';remove.className='btn btn-sm btn-outline-danger';remove.textContent='Remove';
 remove.onclick=()=>{tr.remove();clearRequest();};
 select.onchange=()=>{sync();clearRequest();};mode.onchange=()=>{sync();clearRequest();};
 input.oninput=()=>{if(!input.readOnly)tr.dataset.manualAmount=input.value;clearRequest();};
 rate.oninput=clearRequest;basis.onchange=clearRequest;
 c.append(select);m.append(mode);a.append(input);p.append(rate);b.append(basis);
 actions.append(remove);tr.append(c,m,a,p,b,actions);el('salaryLines').append(tr);sync();
}
async function components(){
 state.components=await api('/api/hr-payroll/salary-components');
 if(!state.components.some(x=>x.isActive))notice('No active salary components. Configure them before registering salary structures.',true);
 else addLine();
}
async function find(){
 const term=el('salaryEmployeeSearch').value.trim();
 if(term.length<2){notice('Enter at least two characters to find an employee.',true);return;}
 const serial=++state.searchSerial;
 try{
  const data=await api('/api/hr-payroll/employee-options?'+new URLSearchParams({search:term,page:'1',pageSize:'50'}));
  if(serial!==state.searchSerial)return;
  const select=el('salaryEmployee');select.replaceChildren(option('','Select employee'));
  for(const row of data.items||[])select.append(option(row.reference,row.fullName+' ('+row.employeeCode+')'));
  if(data.totalCount>50)notice('Only the first 50 employees are shown. Narrow your search.');
  el('salaryHistory').replaceChildren();el('salaryPage').textContent='Select an employee.';
 }catch(e){notice(e.message,true);}
}
function render(data){
 const body=el('salaryHistory');body.replaceChildren();state.history=data.items||[];
 state.pages=Math.max(1,Math.ceil((data.totalCount||0)/10));
 for(const row of state.history){
  const tr=document.createElement('tr');
  const details=(row.lines||[]).map(x=>x.salaryComponentName+': '+(x.calculationMethodCode==='Auto'
   ? 'Auto '+Number(x.percentage).toFixed(4)+'%'+(x.basedOnSalaryComponentId?' · base #'+x.basedOnSalaryComponentId:' · taxable earnings')
   : Number(x.amount).toFixed(2)+' BDT')).join('; ');
  tr.append(cell(row.effectiveFrom),cell(row.effectiveTo||'Open'),cell(row.isCurrent?'Current':'Historical'),
   cell(Number(row.grossMonthlyAmount).toFixed(2)),cell(details));
  const action=document.createElement('td'),copy=document.createElement('button');copy.type='button';copy.className='btn btn-sm btn-outline-primary';copy.textContent='Copy components';
  copy.onclick=()=>{
   const missing=(row.lines||[]).filter(x=>!state.components.some(c=>c.id===x.salaryComponentId&&c.isActive));
   if(missing.length){notice('This structure contains inactive components. Configure replacements before copying.',true);return;}
   el('salaryLines').replaceChildren();
   for(const line of row.lines||[])addLine(line.salaryComponentId,line.amount,line.calculationMethodCode,line.percentage,line.basedOnSalaryComponentId);
   clearRequest();notice('Components copied. Select a new effective date before saving.');
  };
  action.append(copy);tr.append(action);body.append(tr);
 }
 if(!state.history.length){const tr=document.createElement('tr'),td=cell('No salary structures for this employee.');td.colSpan=6;tr.append(td);body.append(tr);}
 el('salaryPage').textContent='Page '+state.page+' / '+state.pages+' · '+data.totalCount+' structure(s)';
 el('salaryPrev').disabled=state.page<=1;el('salaryNext').disabled=state.page>=state.pages;
}
async function load(){
 const reference=el('salaryEmployee').value;if(!reference){el('salaryHistory').replaceChildren();return;}
 try{render(await api('/api/hr-payroll/salary-structures/'+encodeURIComponent(reference)+'?'+new URLSearchParams({page:String(state.page),pageSize:'10'})));}
 catch(e){notice(e.message,true);}
}
el('salaryFind').addEventListener('click',find);
el('salaryEmployee').addEventListener('change',()=>{state.page=1;clearRequest();load();});
el('salaryAdd').addEventListener('click',()=>{addLine();clearRequest();});
el('salaryPrev').addEventListener('click',()=>{if(state.page>1){state.page--;load();}});
el('salaryNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;load();}});
el('salaryEffective').addEventListener('change',clearRequest);
el('salaryForm').addEventListener('submit',async event=>{
 event.preventDefault();if(state.busy||!event.currentTarget.reportValidity())return;
 const employee=el('salaryEmployee').value,date=el('salaryEffective').value;
 if(!employee||!/^\d{4}-\d{2}-\d{2}$/.test(date)){notice('Select an employee and effective date.',true);return;}
 const lines=[...el('salaryLines').querySelectorAll('tr')].map(tr=>{
  const method=tr.querySelector('.salary-mode').value;
  const base=tr.querySelector('.salary-basis').value;
  const rate=tr.querySelector('.salary-rate').value;
  return {salaryComponentId:Number(tr.querySelector('select').value),
   calculationMethodCode:method,amount:Number(tr.querySelector('.salary-amount').value),
   percentage:method==='Auto'&&rate!==''?Number(rate):null,
   basedOnSalaryComponentId:method==='Auto'&&base!==''?Number(base):null};
 });
 if(!lines.length||lines.some(x=>!Number.isSafeInteger(x.salaryComponentId)||x.salaryComponentId<=0||
   !Number.isFinite(x.amount)||x.amount<0||x.amount>999999999999||
   Math.abs(x.amount*100-Math.round(x.amount*100))>0.000001||
   x.calculationMethodCode==='Auto'&&(x.amount!==0||x.percentage===null||
    !Number.isFinite(x.percentage)||x.percentage<0||x.percentage>100||
    Math.abs(x.percentage*10000-Math.round(x.percentage*10000))>0.000001))||
   new Set(lines.map(x=>x.salaryComponentId)).size!==lines.length){
  notice('Use distinct active components and nonnegative amounts with at most two decimals.',true);return;
 }
 const types=new Map(state.components.map(x=>[x.id,x.type]));
 const earnings=lines.filter(x=>types.get(x.salaryComponentId)===1).reduce((a,x)=>a+x.amount,0);
 const deductions=lines.filter(x=>types.get(x.salaryComponentId)===2).reduce((a,x)=>a+x.amount,0);
 if(earnings<=0||deductions>earnings){notice('Earnings must be positive and cover deductions.',true);return;}
 if(!confirm('Save a new effective-dated salary structure? An existing current structure will be closed.'))return;
 state.requestId??=crypto.randomUUID();state.busy=true;el('salarySave').disabled=true;
 try{
  await api('/api/hr-payroll/salary-structures','POST',{clientRequestId:state.requestId,
   employeeReference:employee,effectiveFrom:date,effectiveTo:null,lines});
  state.requestId=null;state.page=1;await load();notice('Salary structure saved. Historical entries remain available.');
 }catch(e){notice(e.message+' Check history before retrying an uncertain submission.',true);}
 finally{state.busy=false;el('salarySave').disabled=false;}
});
components().catch(e=>notice(e.message,true));
if(window.bootstrap?.Popover)root.querySelectorAll('[data-bs-toggle="popover"]').forEach(x=>bootstrap.Popover.getOrCreateInstance(x));
})();
