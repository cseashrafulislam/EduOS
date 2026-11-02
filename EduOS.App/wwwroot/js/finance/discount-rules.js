(() => {
'use strict';
// TenantAdmin discount-rule editor. Never mutates issued invoices or the Finance GL.
const root=document.getElementById('discountRulesApp');
if(!root)return;
const el=id=>document.getElementById(id);
const bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
const t=(en,bd)=>bn?bd:en;
const base='/api/finance/fees';
const token=document.querySelector('meta[name="request-verification-token"]')?.content;
const identity=()=>[document.body?.dataset?.authTenantId||'',document.body?.dataset?.authSessionReference||''];
const initial=identity();
const sameSession=()=>{const now=identity();return !!initial[0]&&!!initial[1]&&now[0]===initial[0]&&now[1]===initial[1];};
const state={page:1,pages:1,rows:[],heads:[],editing:null,loading:false,saving:false,locked:false,read:null,sequence:0,headAvailable:true,optionsReady:false};
const date=v=>v?String(v).slice(0,10):'';
const cell=v=>{const td=document.createElement('td');td.textContent=v==null?'':String(v);return td;};
function notice(kind,msg){
 const node=el('discountRuleAlert');node.className='alert alert-'+kind;
 node.textContent=msg;if(kind==='danger')node.focus();
}
function blank(msg){
 const row=document.createElement('tr'),td=cell(msg);
 td.colSpan=7;td.className='text-center text-muted p-3';row.append(td);
 el('discountRuleRows').replaceChildren(row);
 el('discountRulePage').textContent='';
 el('discountRulePrev').disabled=true;el('discountRuleNext').disabled=true;
}
function headOptions(selected){
 const select=el('discountRuleHead');select.replaceChildren(new Option(t('All fee heads','সব ফি খাত'),''));
 for(const head of state.heads)select.add(new Option(head.name,String(head.id)));
 state.headAvailable=!selected||state.heads.some(h=>String(h.id)===String(selected));
 if(selected&&!state.headAvailable)select.add(new Option(t('Unavailable fee head','অনুপলব্ধ ফি খাত'),String(selected),true,true));
 select.value=selected?String(selected):'';
 el('discountRuleSave').disabled=state.locked||state.loading||state.saving||!state.headAvailable||!state.optionsReady;
}
function valueLimit(){
 el('discountRuleValue').max=el('discountRuleType').value==='percentage'?'100':'999999999999';
}
function reset(){
 state.editing=null;el('discountRuleForm').reset();
 el('discountRuleFormTitle').textContent=t('New discount rule','নতুন ফি ছাড়ের নিয়ম');
 el('discountRuleSave').textContent=t('Create rule','নিয়ম তৈরি');
 el('discountRuleActive').checked=true;headOptions(null);valueLimit();
}
function lockSession(){
 if(state.locked||sameSession())return;
 state.locked=true;++state.sequence;
 state.read?.abort();state.read=null;state.rows=[];state.editing=null;
 blank(t('Institution session changed. Reload this page.','প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'));
 for(const control of root.querySelectorAll('input,select,button'))control.disabled=true;
 notice('danger',t('Institution session changed. Reload this page.','প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'));
}
async function api(path,method='GET',body,signal){
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  if(!token)throw Error(t('Security token missing. Reload this page.','নিরাপত্তা টোকেন নেই। পৃষ্ঠাটি রিলোড করুন।'));
  headers.RequestVerificationToken=token;
 }
 if(body!==undefined)headers['Content-Type']='application/json';
 const response=await fetch(base+path,{method,credentials:'same-origin',cache:'no-store',headers,signal,
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
 if(response.status===401||response.status===403)
  throw Error(t('Finance access denied. Check your session.','Finance অনুমতি নেই। সেশন যাচাই করুন।'));
 if(response.status===409)
  throw Error(t('Rule changed, already exists or assigned terms are immutable. Refresh before editing.','নিয়ম পরিবর্তিত, আগে থেকেই আছে অথবা বরাদ্দকৃত শর্ত অপরিবর্তনীয়। সম্পাদনার আগে রিফ্রেশ করুন।'));
 if(!response.ok)
  throw Error(t('Request unavailable. Check values and retry.','অনুরোধ সম্পন্ন হয়নি। তথ্য যাচাই করে আবার চেষ্টা করুন।'));
 let result;
 try{result=await response.json();}catch{throw Error(t('Invalid server response.','সার্ভারের উত্তর সঠিক নয়।'));}
 if(!result?.success)
  throw Error(t('Request rejected. Check values and retry.','অনুরোধ প্রত্যাখ্যাত। তথ্য যাচাই করে আবার চেষ্টা করুন।'));
 return result.data;
}
function validRow(row){
 return row&&Number.isSafeInteger(row.id)&&row.id>0&&typeof row.name==='string'&&
  row.name.trim().length>0&&row.name.length<=150&&
  typeof row.isPercentage==='boolean'&&typeof row.isActive==='boolean'&&
  Number.isFinite(row.value)&&row.value>0&&row.value<=999999999999&&
  (!row.isPercentage||row.value<=100)&&
  (row.feeHeadId==null||(Number.isSafeInteger(row.feeHeadId)&&row.feeHeadId>0))&&
  typeof row.rowVersion==='string'&&
  (!row.feeHeadName||typeof row.feeHeadName==='string')&&
  (!row.effectiveFrom||/^\d{4}-\d{2}-\d{2}/.test(row.effectiveFrom))&&
  (!row.effectiveTo||/^\d{4}-\d{2}-\d{2}/.test(row.effectiveTo));
}
function edit(row){
 if(state.locked||state.loading||state.saving)return;
 if(!row.rowVersion){notice('danger',t('Version missing. Refresh before editing.','সংস্করণ অনুপস্থিত। সম্পাদনার আগে রিফ্রেশ করুন।'));return;}
 state.editing=row;
 el('discountRuleFormTitle').textContent=t('Edit discount rule #','ফি ছাড়ের নিয়ম সম্পাদনা #')+row.id;
 el('discountRuleName').value=row.name;headOptions(row.feeHeadId);
 el('discountRuleType').value=row.isPercentage?'percentage':'fixed';
 el('discountRuleValue').value=String(row.value);
 el('discountRuleFrom').value=date(row.effectiveFrom);el('discountRuleTo').value=date(row.effectiveTo);
 el('discountRuleActive').checked=row.isActive;
 el('discountRuleSave').textContent=t('Update rule','নিয়ম হালনাগাদ');valueLimit();
 if(!state.headAvailable)notice('danger',t('This fee head is no longer available. Create a new eligible rule.','এই ফি খাত আর পাওয়া যাচ্ছে না। নতুন যোগ্য নিয়ম তৈরি করুন।'));
 el('discountRuleName').focus();
}
function render(data,requested){
 const rows=data?.items;
 if(!data||data.page!==requested||data.pageSize!==25||
  !Number.isSafeInteger(data.totalCount)||data.totalCount<0||
  !Array.isArray(rows)||rows.length>25||rows.length>data.totalCount||
  requested>Math.max(1,Math.ceil(data.totalCount/25))||
  (data.totalCount>0&&rows.length===0)||rows.some(row=>!validRow(row))||
  new Set(rows.map(row=>row.id)).size!==rows.length)
  throw Error(t('Register page mismatch. Reload before editing.','তালিকার পৃষ্ঠা মেলেনি। সম্পাদনার আগে রিলোড করুন।'));
 const body=document.createDocumentFragment();
 for(const row of rows){
  const tr=document.createElement('tr'),action=document.createElement('td'),button=document.createElement('button');
  button.type='button';button.className='btn btn-sm btn-outline-primary';
  button.textContent=t('Edit','সম্পাদনা');
  button.setAttribute('aria-label',t('Edit rule ','নিয়ম সম্পাদনা ')+row.name);
  button.addEventListener('click',()=>edit(row));action.append(button);
  const dates=(date(row.effectiveFrom)||t('Any','যেকোনো'))+' → '+(date(row.effectiveTo)||t('Open','উন্মুক্ত'));
  tr.append(cell(row.name),cell(row.feeHeadName||t('All fee heads','সব ফি খাত')),
   cell(row.isPercentage?t('Percentage','শতাংশ'):t('Fixed BDT','নির্দিষ্ট টাকা')),
   cell(String(row.value)+(row.isPercentage?'%':'')),cell(dates),
   cell(row.isActive?t('Active','সক্রিয়'):t('Inactive','নিষ্ক্রিয়')),action);
  body.append(tr);
 }
 if(!rows.length){const tr=document.createElement('tr'),td=cell(t('No discount rules.','ফি ছাড়ের কোনো নিয়ম নেই।'));td.colSpan=7;td.className='text-center text-muted p-3';tr.append(td);body.append(tr);}
 el('discountRuleRows').replaceChildren(body);
 state.rows=rows;state.pages=Math.max(1,Math.ceil(data.totalCount/25));
 el('discountRulePage').textContent=t('Page ','পৃষ্ঠা ')+requested+t(' of ',' / ')+state.pages+
  t(' (',' (')+data.totalCount+t(' rules)','টি নিয়ম)');
 el('discountRulePrev').disabled=requested<=1;el('discountRuleNext').disabled=requested>=state.pages;
}
async function load(){
 if(state.locked||state.saving)return;
 state.read?.abort();const controller=new AbortController();state.read=controller;
 const seq=++state.sequence,requested=state.page;
 state.loading=true;state.rows=[];reset();
 blank(t('Loading discount rules…','ফি ছাড়ের নিয়ম লোড হচ্ছে…'));
 el('discountRuleSave').disabled=true;
 try{
  const data=await api('/discount-rules?page='+requested+'&pageSize=25','GET',undefined,controller.signal);
  if(seq!==state.sequence||controller.signal.aborted||!sameSession()){lockSession();return;}
  render(data,requested);
  return true;
 }catch(err){
  if(seq!==state.sequence||controller.signal.aborted)return;
  blank(t('No verified rules loaded.','যাচাইকৃত কোনো নিয়ম লোড হয়নি।'));
  notice('danger',err.message);
  return false;
 }finally{
  if(seq===state.sequence&&!state.locked){
   state.loading=false;state.read=null;headOptions(null);
  }
 }
}
async function options(){
 const session=identity().join('|');
 try{
  const data=await api('/options');
  if(state.locked||!sameSession()||session!==identity().join('|')){lockSession();return;}
  if(!Array.isArray(data?.feeHeads)||data.feeHeads.some(h=>!Number.isSafeInteger(h.id)||h.id<=0||typeof h.name!=='string'))
   throw Error(t('Invalid fee-head options.','ফি খাতের তালিকা সঠিক নয়।'));
  state.heads=data.feeHeads;state.optionsReady=true;headOptions(state.editing?.feeHeadId);
 }catch(err){
  if(!sameSession()){lockSession();return;}
  state.heads=[];state.optionsReady=false;headOptions(state.editing?.feeHeadId);
  notice('danger',err.message);
 }
}
async function save(event){
 event.preventDefault();
 if(state.locked||state.loading||state.saving||!sameSession()){lockSession();return;}
 if(!event.currentTarget.reportValidity())return;
 if(!state.headAvailable)return notice('danger',t('Choose an available fee head.','উপলব্ধ ফি খাত বেছে নিন।'));
 const raw=el('discountRuleValue').value.trim(),percent=el('discountRuleType').value==='percentage';
 if(!/^\d+(\.\d{1,4})?$/.test(raw)||Number(raw)<=0||Number(raw)>999999999999||
  (percent&&Number(raw)>100))
  return notice('danger',t('Enter a positive value with at most four decimals (percentage at most 100).','সর্বোচ্চ চার দশমিকসহ ধনাত্মক অঙ্ক দিন (শতাংশ সর্বোচ্চ ১০০)।'));
 const from=el('discountRuleFrom').value,to=el('discountRuleTo').value;
 if(from&&to&&to<from)return notice('danger',t('End date must not precede start date.','শেষের তারিখ শুরুর আগে হতে পারবে না।'));
 const body={name:el('discountRuleName').value.trim(),
  feeHeadId:el('discountRuleHead').value?Number(el('discountRuleHead').value):null,
  isPercentage:percent,value:Number(raw),effectiveFrom:from||null,effectiveTo:to||null,
  isActive:el('discountRuleActive').checked,rowVersion:state.editing?.rowVersion||null};
 if(!body.name||body.name.length>150)return notice('danger',t('Rule name is required (up to 150 characters).','নিয়মের নাম আবশ্যক (সর্বোচ্চ ১৫০ অক্ষর)।'));
 if(body.feeHeadId!==null&&!state.heads.some(h=>h.id===body.feeHeadId))
  return notice('danger',t('Choose an available fee head.','উপলব্ধ ফি খাত বেছে নিন।'));
 if(state.editing&&!body.rowVersion)return notice('danger',t('Version missing. Refresh before editing.','সংস্করণ অনুপস্থিত। সম্পাদনার আগে রিফ্রেশ করুন।'));
 if(!confirm(state.editing?t('Save this updated discount rule?','ফি ছাড়ের পরিবর্তিত নিয়ম সংরক্ষণ করবেন?'):t('Create this discount rule?','ফি ছাড়ের নিয়ম তৈরি করবেন?')))return;
 if(!sameSession()){lockSession();return;}
 const id=state.editing?.id;state.saving=true;
 for(const control of root.querySelectorAll('input,select,button'))control.disabled=true;
 try{
  await api('/discount-rules'+(id?'/'+id:''),id?'PUT':'POST',body);
  if(!sameSession()){lockSession();return;}
  state.saving=false;for(const control of root.querySelectorAll('input,select,button'))control.disabled=false;
  state.page=1;const reloaded=await load();
  if(reloaded&&!state.locked)notice('success',t('Discount rule saved.','ফি ছাড়ের নিয়ম সংরক্ষিত হয়েছে।'));
 }catch(err){
  if(!sameSession()){lockSession();return;}
  notice('danger',err.message);
 }finally{
  state.saving=false;
  if(!state.locked){
   for(const control of root.querySelectorAll('input,select,button'))control.disabled=false;
   headOptions(state.editing?.feeHeadId);
   el('discountRulePrev').disabled=state.loading||state.page<=1;
   el('discountRuleNext').disabled=state.loading||state.page>=state.pages;
  }
 }
}
el('discountRuleForm').addEventListener('submit',save);
el('discountRuleReset').addEventListener('click',reset);
el('discountRuleType').addEventListener('change',valueLimit);
el('discountRuleRefresh').addEventListener('click',()=>{state.page=1;load();});
el('discountRulePrev').addEventListener('click',()=>{if(!state.loading&&!state.saving&&state.page>1){--state.page;load();}});
el('discountRuleNext').addEventListener('click',()=>{if(!state.loading&&!state.saving&&state.page<state.pages){++state.page;load();}});
new MutationObserver(lockSession).observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
reset();
if(!sameSession())lockSession();
else{void options();void load();}
})();