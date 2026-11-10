(() => {
'use strict';
const root=document.getElementById('bonusWorkspace');if(!root)return;
const el=id=>document.getElementById(id);
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value;
const state={page:1,pages:1,requestId:null,requestKey:null,busy:false,accessRevoked:false,
 employeeRequest:0,registerRequest:0,selectedEmployee:'',acknowledgedCommand:null};
const form=el('bonusCreate'),formGuard=globalThis.EduOSFormGuard?.create(form);
const bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
const t=(en,bd)=>bn?bd:en;
const localDate=()=>{const x=new Date();return [x.getFullYear(),String(x.getMonth()+1).padStart(2,'0'),String(x.getDate()).padStart(2,'0')].join('-');};
const option=(value,label)=>{const o=document.createElement('option');o.value=String(value);o.textContent=label;return o;};
const cell=value=>{const td=document.createElement('td');td.textContent=value==null?'':String(value);return td;};
function notice(message,error=false){const x=el('bonusNotice');x.className='alert '+(error?'alert-danger':'alert-success');x.textContent=message;}
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Antiforgery token missing; reload.');headers.RequestVerificationToken=token;}
 if(body!==undefined)headers['Content-Type']='application/json';
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
   ...(body===undefined?{}:{body:JSON.stringify(body)})});
 const data=await response.json().catch(()=>null);
 if(!response.ok||!data?.success){const error=Error(data?.message||'Bonus request failed.');
  error.statusCode=response.status;throw error;}
 return data.data;
}
function handleError(error){
 if([401,403].includes(error.statusCode)){
  state.accessRevoked=true;++state.employeeRequest;++state.registerRequest;
  el('bonusRows').replaceChildren();el('bonusEmployee').replaceChildren(option('',t('Access unavailable','অ্যাক্সেস নেই')));
  form.reset();el('bonusEmployeeSearch').value='';el('bonusEmployee').disabled=true;
  state.selectedEmployee='';state.requestId=null;state.requestKey=null;
  formGuard?.markClean();root.inert=true;
 }
 notice(error.message,true);
}
function mayDiscard(){return !formGuard?.isDirty()||formGuard.confirmDiscard();}
async function searchEmployees(){
 if(state.busy||state.accessRevoked||!mayDiscard())return;
 const term=el('bonusEmployeeSearch').value.trim();
 if(term.length<2||term.length>100){notice(t('Enter 2–100 characters to search.','খোঁজার জন্য ২–১০০ অক্ষর লিখুন।'),true);return;}
 const draftBefore=JSON.stringify([el('bonusEmployee').value,el('bonusDate').value,
  el('bonusAmount').value,el('bonusReason').value]);
 const request=++state.employeeRequest,select=el('bonusEmployee');
 el('bonusFind').disabled=true;
 try{
  const data=await api('/api/hr-payroll/employee-options?'+new URLSearchParams({search:term,page:'1',pageSize:'50'}));
  if(request!==state.employeeRequest||state.accessRevoked||term!==el('bonusEmployeeSearch').value.trim())return;
  const draftNow=JSON.stringify([select.value,el('bonusDate').value,el('bonusAmount').value,el('bonusReason').value]);
  if(draftNow!==draftBefore&&!mayDiscard())return;
  form.reset();el('bonusDate').value=localDate();state.selectedEmployee='';
  state.requestId=null;state.requestKey=null;formGuard?.markClean();
  select.replaceChildren(option('',t('Select employee','কর্মী নির্বাচন করুন')));
  for(const employee of data.items||[])select.append(option(employee.reference,employee.fullName+' ('+employee.employeeCode+')'));
  select.disabled=!data.items?.length;
  if(data.totalCount>50)notice(t('Showing 50 employees. Narrow the search.','৫০ জন কর্মী দেখানো হচ্ছে। আরও নির্দিষ্ট করে খুঁজুন।'));
 }catch(e){if(request===state.employeeRequest)handleError(e);}
 finally{if(request===state.employeeRequest)el('bonusFind').disabled=false;}
}
function render(data){
 const body=el('bonusRows');body.replaceChildren();
 for(const row of data.items||[]){
  const tr=document.createElement('tr');
  for(const value of [row.id,row.bonusDate,row.employeeName,Number(row.amount).toFixed(2),row.reason])tr.append(cell(value));
  body.append(tr);
 }
 if(!data.items?.length){const tr=document.createElement('tr'),td=cell('No bonuses found.');td.colSpan=5;tr.append(td);body.append(tr);}
 state.pages=Math.max(1,data.totalPages||1);
 el('bonusPage').textContent='Page '+state.page+' / '+state.pages+' · '+data.totalCount+' bonus(es)';
 el('bonusPrev').disabled=state.page<=1;el('bonusNext').disabled=state.page>=state.pages;
}
async function load(){
 if(state.accessRevoked)return false;
 const request=++state.registerRequest;
 const q=new URLSearchParams({page:String(state.page),pageSize:'20'});
 const year=el('bonusYear').value,month=el('bonusMonth').value;
 if(year)q.set('year',year);if(month)q.set('month',month);
 if(el('bonusSelectedOnly').checked){
  const employee=el('bonusEmployee').value;
  if(!employee){notice(t('Select an employee before filtering.','ফিল্টার দেওয়ার আগে কর্মী নির্বাচন করুন।'),true);return false;}
  q.set('employeeReference',employee);
 }
 const scope=q.toString();
 try{
  const result=await api('/api/hr-payroll/bonuses?'+q);
  if(request!==state.registerRequest||scope!==currentFilterScope()||state.accessRevoked)return false;
  render(result);return true;
 }catch(e){if(request===state.registerRequest)handleError(e);return false;}
}
function currentFilterScope(){
 const q=new URLSearchParams({page:String(state.page),pageSize:'20'});
 if(el('bonusYear').value)q.set('year',el('bonusYear').value);
 if(el('bonusMonth').value)q.set('month',el('bonusMonth').value);
 if(el('bonusSelectedOnly').checked){
  if(!el('bonusEmployee').value)return '!invalid-selection';
  q.set('employeeReference',el('bonusEmployee').value);
 }
 return q.toString();
}
el('bonusFind').addEventListener('click',searchEmployees);
el('bonusEmployeeSearchForm').addEventListener('submit',e=>{e.preventDefault();searchEmployees();});
el('bonusEmployeeSearch').addEventListener('input',()=>{
 ++state.employeeRequest;el('bonusFind').disabled=false;
});
el('bonusEmployee').addEventListener('change',()=>{
 const next=el('bonusEmployee').value;
 if(state.busy||state.accessRevoked)return;
 if(state.selectedEmployee&&next!==state.selectedEmployee&&
  (el('bonusAmount').value.trim()||el('bonusReason').value.trim())&&
  !confirm(t('Changing employees will clear the amount and reason. Continue?',
   'কর্মী পরিবর্তন করলে বোনাসের পরিমাণ ও কারণ মুছে যাবে। চালিয়ে যাবেন?'))){
  el('bonusEmployee').value=state.selectedEmployee;return;
 }
 if(next!==state.selectedEmployee){el('bonusAmount').value='';el('bonusReason').value='';}
 state.selectedEmployee=next;
});
el('bonusFilter').addEventListener('submit',e=>{e.preventDefault();if(state.busy||state.accessRevoked)return;
 state.page=1;load();});
el('bonusPrev').addEventListener('click',()=>{if(!state.busy&&!state.accessRevoked&&state.page>1){state.page--;load();}});
el('bonusNext').addEventListener('click',()=>{if(!state.busy&&!state.accessRevoked&&state.page<state.pages){state.page++;load();}});
el('bonusCreate').addEventListener('submit',async e=>{
 e.preventDefault();if(state.busy||state.accessRevoked||!e.currentTarget.reportValidity())return;
 const employee=el('bonusEmployee').value,date=el('bonusDate').value,amountText=el('bonusAmount').value.trim();
 const amount=Number(amountText),reason=el('bonusReason').value.trim()||null;
 if(el('bonusEmployee').disabled||!employee||!/^\d{4}-\d{2}-\d{2}$/.test(date)||
    !/^\d+(?:\.\d{1,2})?$/.test(amountText)||!Number.isFinite(amount)||amount<=0||amount>999999999999){
  notice(t('Select an employee, valid date and positive amount with at most two decimals.','কর্মী, সঠিক তারিখ ও সর্বোচ্চ দুই দশমিকের ধনাত্মক পরিমাণ দিন।'),true);return;
 }
 const key=JSON.stringify([employee,date,amountText,reason]);
 if(state.requestKey!==key){state.requestKey=key;state.requestId=crypto.randomUUID();}
 if(state.acknowledgedCommand===key){notice(t('This bonus was recorded. Clear draft to start another bonus, including an intentional identical entry.',
  'এই বোনাস নথিভুক্ত হয়েছে। নতুন বোনাস দিতে ড্রাফট পরিষ্কার করুন।'),true);return;}
 if(!confirm(t('Record this bonus for a future payroll calculation? Existing payroll runs cannot be changed.','ভবিষ্যৎ বেতন হিসাবের জন্য এই বোনাস নথিভুক্ত করবেন? বিদ্যমান বেতন রান পরিবর্তন হবে না।')))return;
 ++state.employeeRequest;state.busy=true;el('bonusSave').disabled=true;form.inert=true;root.inert=true;
 try{
  await api('/api/hr-payroll/bonuses','POST',{clientRequestId:state.requestId,employeeReference:employee,
    bonusDate:date,amount,reason});
  state.acknowledgedCommand=key;state.requestId=null;state.requestKey=null;
  el('bonusAmount').value='';el('bonusReason').value='';
  formGuard?.markClean();state.page=1;
  if(await load())notice(t('Bonus recorded. Generate payroll after all bonuses are entered.','বোনাস নথিভুক্ত হয়েছে। সব বোনাসের পরে বেতন হিসাব তৈরি করুন।'));
  else if(!state.accessRevoked)notice(t('Bonus was recorded; register refresh unavailable. Reload to verify.','বোনাস নথিভুক্ত হয়েছে; তালিকা হালনাগাদ হয়নি। যাচাই করতে রিলোড করুন।'));
  else notice(t('Bonus recorded; register access has expired. Sign in again.','বোনাস নথিভুক্ত হয়েছে; নিবন্ধনে প্রবেশাধিকার শেষ। আবার সাইন ইন করুন।'));
 }catch(error){handleError(error);if(!state.accessRevoked)notice(error.message+
  t(' Check the bonus register before retrying this exact draft.','একই ড্রাফট পুনরায় পাঠানোর আগে বোনাস তালিকা যাচাই করুন।'),true);}
 finally{state.busy=false;el('bonusSave').disabled=false;form.inert=false;
  if(!state.accessRevoked)root.inert=false;}
});
el('bonusClear').addEventListener('click',()=>{
 if(state.busy||state.accessRevoked||formGuard&&!formGuard.confirmDiscard())return;
 ++state.employeeRequest;form.reset();el('bonusEmployee').replaceChildren(option('',t('Search for an employee','কর্মী খুঁজুন')));
 el('bonusEmployee').disabled=true;el('bonusDate').value=localDate();
 state.selectedEmployee='';state.acknowledgedCommand=null;
 state.requestId=null;state.requestKey=null;formGuard?.markClean();
});
const today=new Date();el('bonusYear').value=String(today.getFullYear());
el('bonusDate').value=localDate();
formGuard?.markClean();
load();
})();
