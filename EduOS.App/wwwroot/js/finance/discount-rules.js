(() => {
'use strict';
const app = document.getElementById('discountRulesApp');
if (!app) return;
const e = id => document.getElementById(id);
const base = '/api/finance/fees';
const token = document.querySelector('meta[name="request-verification-token"]')?.content;
const state = {page:1, pages:1, rows:[], editing:null, heads:[]};
const td = value => {const cell=document.createElement('td');cell.textContent=value==null?'':String(value);return cell;};
const date = value => value?String(value).slice(0,10):'';
function alertMessage(kind,message) {
 const box=e('discountRuleAlert');box.className='alert alert-'+kind;box.textContent=message;box.focus();
}
async function api(path,method='GET',body) {
 const headers={Accept:'application/json'};
 if(method!=='GET') {if(!token) throw Error('Antiforgery token missing. Reload the page.');headers.RequestVerificationToken=token;}
 if(body!==undefined)headers['Content-Type']='application/json';
 const response=await fetch(base+path,{method,credentials:'same-origin',cache:'no-store',headers,...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||'Request failed ('+response.status+').');
 return result.data;
}
function headOptions(selected) {
 const select=e('discountRuleHead');select.replaceChildren(new Option('All fee heads',''));
 for(const h of state.heads)select.add(new Option(h.name,String(h.id)));
 if(selected && !state.heads.some(x=>String(x.id)===String(selected)))
  select.add(new Option('Unavailable fee head #'+selected,String(selected)));
 select.value=selected?String(selected):'';
}
function reset() {
 state.editing=null;e('discountRuleForm').reset();e('discountRuleFormTitle').textContent='New discount rule';
 e('discountRuleSave').textContent='Create rule';e('discountRuleActive').checked=true;
 headOptions(null);valueLimit();
}
function valueLimit() {
 const percent=e('discountRuleType').value==='percentage';
 e('discountRuleValue').max=percent?'100':'999999999999';
}
function edit(row) {
 state.editing=row;e('discountRuleFormTitle').textContent='Edit discount rule #'+row.id;
 e('discountRuleName').value=row.name;headOptions(row.feeHeadId);
 e('discountRuleType').value=row.isPercentage?'percentage':'fixed';
 e('discountRuleValue').value=String(row.value);e('discountRuleFrom').value=date(row.effectiveFrom);
 e('discountRuleTo').value=date(row.effectiveTo);e('discountRuleActive').checked=!!row.isActive;
 e('discountRuleSave').textContent='Update rule';valueLimit();
 e('discountRuleName').focus();
}
function render(result) {
 state.rows=result.items||[];state.pages=Math.max(1,Math.ceil(result.totalCount/25));
 e('discountRuleRows').replaceChildren();
 for(const row of state.rows) {
  const tr=document.createElement('tr'),action=document.createElement('td'),button=document.createElement('button');
  button.type='button';button.className='btn btn-sm btn-outline-primary';button.textContent='Edit';button.addEventListener('click',()=>edit(row));action.append(button);
  const dates=(date(row.effectiveFrom)||'Any')+' → '+(date(row.effectiveTo)||'Open');
  tr.append(td(row.name),td(row.feeHeadName||'All fee heads'),td(row.isPercentage?'Percentage':'Fixed BDT'),
   td(row.isPercentage?String(row.value)+'%':Number(row.value).toFixed(2)),td(dates),td(row.isActive?'Active':'Inactive'),action);
  e('discountRuleRows').append(tr);
 }
 if(!state.rows.length){const tr=document.createElement('tr'),cell=td('No discount rules.');cell.colSpan=7;cell.className='text-center text-muted p-3';tr.append(cell);e('discountRuleRows').append(tr);}
 e('discountRulePage').textContent='Page '+state.page+' of '+state.pages+' ('+result.totalCount+' rules)';
 e('discountRulePrev').disabled=state.page<=1;e('discountRuleNext').disabled=state.page>=state.pages;
}
async function load() {render(await api('/discount-rules?page='+state.page+'&pageSize=25'));}
async function options() {
 const result=await api('/options');state.heads=result.feeHeads||[];headOptions(state.editing?.feeHeadId);
}
async function save(ev) {
 ev.preventDefault();if(!ev.currentTarget.reportValidity())return;
 const raw=e('discountRuleValue').value.trim(),percent=e('discountRuleType').value==='percentage';
 if(!/^\d+(\.\d{1,4})?$/.test(raw)||Number(raw)<=0||percent&&Number(raw)>100)
  return alertMessage('danger','Value must be positive with up to four decimals (percentage at most 100).');
 const from=e('discountRuleFrom').value,to=e('discountRuleTo').value;
 if(from&&to&&to<from)return alertMessage('danger','End date must be on or after start date.');
 const body={name:e('discountRuleName').value.trim(),feeHeadId:e('discountRuleHead').value?Number(e('discountRuleHead').value):null,
  isPercentage:percent,value:Number(raw),effectiveFrom:from||null,effectiveTo:to||null,isActive:e('discountRuleActive').checked,
  rowVersion:state.editing?.rowVersion||null};
 if(!body.name)return alertMessage('danger','Rule name is required.');
 if(!confirm(state.editing?'Save this discount rule?':'Create this discount rule?'))return;
 const id=state.editing?.id,button=e('discountRuleSave');button.disabled=true;
 try {
  await api('/discount-rules'+(id?'/'+id:''),id?'PUT':'POST',body);
  reset();state.page=1;await load();alertMessage('success','Discount rule saved.');
 } catch(err) {alertMessage('danger',err.message+' Reload the register before conflicting edits.');}
 finally {button.disabled=false;}
}
e('discountRuleForm').addEventListener('submit',save);
e('discountRuleReset').addEventListener('click',reset);
e('discountRuleType').addEventListener('change',valueLimit);
e('discountRuleRefresh').addEventListener('click',()=>load().catch(err=>alertMessage('danger',err.message)));
e('discountRulePrev').addEventListener('click',()=>{if(state.page>1){state.page--;load().catch(err=>alertMessage('danger',err.message));}});
e('discountRuleNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;load().catch(err=>alertMessage('danger',err.message));}});
reset();
Promise.all([options(),load()]).catch(err=>alertMessage('danger',err.message));
})();
