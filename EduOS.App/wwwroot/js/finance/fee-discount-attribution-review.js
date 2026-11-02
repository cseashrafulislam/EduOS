(() => {
'use strict';
// Historical evidence triage only: never posts, reverses or recalculates invoices.
const root = document.getElementById('feeAttributionApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
const context = () => [document.body?.dataset?.authTenantId || '', document.body?.dataset?.authSessionReference || ''].join('|');
const initialContext = context();
let locked = false, page = 1, report = null, pending = null, generation = 0, verifiedQuery = '';
const fields = ['feeAttributionCampus', 'feeAttributionFrom', 'feeAttributionTo', 'feeAttributionSize'];
const moneyFields = /("(?:subtotal|discountAmount|totalAmount|potentiallyMisattributedDiscount|potentialAmountOnPage)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
const statusLabels = {
 'Header/line integrity exception': ['Header/line integrity exception','ইনভয়েস হেডার/লাইনে অসামঞ্জস্য'],
 'No discount recorded': ['No discount recorded','কোনো ছাড় নেই'],
 'Historical award evidence unavailable': ['Historical award evidence unavailable','পুরোনো ছাড়ের প্রমাণ পাওয়া যায়নি'],
 'Global/mixed awards: manual review': ['Global/mixed awards: manual review','সমন্বিত ছাড়: হাতে যাচাই প্রয়োজন'],
 'Possible cross-head attribution': ['Possible cross-head attribution','ভিন্ন ফি খাতে ছাড়ের সম্ভাবনা'],
 'No cross-head signal in surviving metadata': ['No cross-head signal in surviving metadata','বিদ্যমান তথ্যে ভিন্ন খাতের ইঙ্গিত নেই']
};
function dateLocal(d) {
 return d.getFullYear() + '-' + String(d.getMonth()+1).padStart(2,'0') + '-' + String(d.getDate()).padStart(2,'0');
}
const today = new Date();
el('feeAttributionFrom').value = dateLocal(new Date(today.getFullYear(),today.getMonth(),today.getDate()-29));
el('feeAttributionTo').value = dateLocal(today);
function td(value) { const node=document.createElement('td'); node.textContent=String(value ?? ''); return node; }
function notice(message, error) {
 const node=el('feeAttributionNotice');
 node.className='alert '+(error?'alert-danger':'alert-success')+' d-print-none';
 node.textContent=message;
}
function cents(value) {
 const raw=String(value ?? '');
 if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error(t('Invalid financial precision.','টাকার দশমিক নির্ভুলতা সঠিক নয়।'));
 const negative=raw.startsWith('-'), parts=(negative?raw.slice(1):raw).split('.');
 const n=BigInt(parts[0])*100n+BigInt((parts[1]||'').padEnd(2,'0'));
 return negative?-n:n;
}
function money(value) {
 const n=cents(value), abs=n<0n?-n:n;
 return (n<0n?'-':'')+String(abs/100n)+'.'+String(abs%100n).padStart(2,'0');
}
function parseDate(value) {
 if (typeof value!=='string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
 const [y,m,d]=value.split('-').map(Number);
 if (y<2000 || y>2199) return null;
 const ms=Date.UTC(y,m-1,d), actual=new Date(ms);
 return actual.getUTCFullYear()===y && actual.getUTCMonth()+1===m && actual.getUTCDate()===d ? ms : null;
}
function filters() {
 const campus=Number(el('feeAttributionCampus').value);
 const from=el('feeAttributionFrom').value, to=el('feeAttributionTo').value;
 const size=Number(el('feeAttributionSize').value), a=parseDate(from), b=parseDate(to);
 if (!Number.isSafeInteger(campus) || campus<=0 || a===null || b===null ||
     b<a || (b-a)/86400000>=90 || ![25,50,100].includes(size))
  throw Error(t('Choose an authorized campus, valid dates (up to 90 days) and rows.','অনুমোদিত ক্যাম্পাস, সঠিক তারিখ (সর্বোচ্চ ৯০ দিন) ও সারি বেছে নিন।'));
 return new URLSearchParams({campusId:String(campus),fromDate:from,toDate:to,
  page:String(page),pageSize:String(size)});
}
function clear() {
 report=null; verifiedQuery='';
 for (const id of ['feeAttributionCsv','feeAttributionPrint','feeAttributionPrev','feeAttributionNext'])
  el(id).disabled=true;
 el('feeAttributionScope').textContent=t('No verified report loaded','কোনো যাচাইকৃত প্রতিবেদন লোড হয়নি');
 el('feeAttributionCount').textContent='';
 el('feeAttributionPage').textContent='';
 const row=document.createElement('tr'), cell=td(t('Run the review.','যাচাই প্রতিবেদন চালান।'));
 cell.colSpan=10; cell.className='text-center text-muted p-3';row.append(cell);
 el('feeAttributionRows').replaceChildren(row);
}
function invalidate() {
 ++generation;
 if (pending) pending.abort();
 pending=null;page=1;clear();
 el('feeAttributionRun').disabled=locked;
}
function lockContext() {
 if (locked || context()===initialContext) return;
 locked=true;invalidate();
 for (const control of el('feeAttributionFilters').querySelectorAll('input,select,button')) control.disabled=true;
 notice(t('Institution session changed. Reload this page.','প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'),true);
}
async function api(url, signal) {
 const response=await fetch(url,{credentials:'same-origin',cache:'no-store',
  headers:{Accept:'application/json'},signal});
 if (response.status===401 || response.status===403)
  throw Error(t('Finance access denied. Check your session and campus.','Finance অনুমতি নেই। সেশন ও ক্যাম্পাস যাচাই করুন।'));
 if (!response.ok) throw Error(t('Report unavailable. Check scope and retry.','প্রতিবেদন পাওয়া যায়নি। পরিসর দেখে আবার চেষ্টা করুন।'));
 const raw=await response.text();
 let json;
 try {json=JSON.parse(raw.replace(moneyFields,'$1"$2"'));}
 catch {throw Error(t('Invalid server response.','সার্ভারের উত্তর সঠিক নয়।'));}
 if (!json?.success) throw Error(t('Report unavailable. Check scope and retry.','প্রতিবেদন পাওয়া যায়নি। পরিসর দেখে আবার চেষ্টা করুন।'));
 return json.data;
}
function render(data,query) {
 const invoices=data?.invoices, rows=invoices?.items;
 const size=Number(query.get('pageSize')), requested=Number(query.get('page'));
 if (!data || Number(data.campusId)!==Number(query.get('campusId')) ||
     String(data.fromDate).slice(0,10)!==query.get('fromDate') ||
     String(data.toDate).slice(0,10)!==query.get('toDate') ||
     !invoices || invoices.page!==requested || invoices.pageSize!==size ||
     !Number.isSafeInteger(invoices.totalCount) || invoices.totalCount<0 ||
     !Array.isArray(rows) || rows.length>size || rows.length>100 ||
     rows.length>invoices.totalCount ||
     requested>Math.max(1,Math.ceil(invoices.totalCount/size)) ||
     (invoices.totalCount>0 && rows.length===0) ||
     !Number.isSafeInteger(data.reviewRowsOnPage) ||
     data.reviewRowsOnPage<0 || data.reviewRowsOnPage>rows.length ||
     typeof data.campusName!=='string')
  throw Error(t('Report scope or page mismatch. Export blocked.','প্রতিবেদনের পরিসর বা পৃষ্ঠা মেলেনি। এক্সপোর্ট বন্ধ।'));
 const seen=new Set(), body=document.createDocumentFragment();
 let reviewCount=0, candidateSum=0n;
 const currencies=new Set();
 for (const item of rows) {
  if (!item || typeof item.invoiceReference!=='string' ||
      !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(item.invoiceReference) ||
      seen.has(item.invoiceReference) || typeof item.invoiceNumber!=='string' ||
      typeof item.studentCode!=='string' || typeof item.currencyCode!=='string' ||
      !/^[A-Z]{3}$/.test(item.currencyCode) ||
      typeof item.hasJournalLink!=='boolean' || typeof item.needsReview!=='boolean' ||
      !Object.hasOwn(statusLabels,item.evidenceStatus) ||
      parseDate(String(item.invoiceDate).slice(0,10))===null ||
      String(item.invoiceDate).slice(0,10)<query.get('fromDate') ||
      String(item.invoiceDate).slice(0,10)>query.get('toDate'))
   throw Error(t('Invalid invoice evidence. Export blocked.','ইনভয়েসের প্রমাণ সঠিক নয়। এক্সপোর্ট বন্ধ।'));
  seen.add(item.invoiceReference);currencies.add(item.currencyCode);
  const candidate=cents(item.potentiallyMisattributedDiscount), discount=cents(item.discountAmount);
  if (candidate<0n || discount<0n || candidate>discount ||
      cents(item.subtotal)<0n || cents(item.totalAmount)<0n)
   throw Error(t('Invalid financial evidence. Export blocked.','আর্থিক প্রমাণ সঠিক নয়। এক্সপোর্ট বন্ধ।'));
  candidateSum+=candidate;if(item.needsReview)reviewCount++;
  const tr=document.createElement('tr');
  const values=[item.invoiceNumber,item.studentCode,String(item.invoiceDate).slice(0,10),item.currencyCode,
   money(item.subtotal),money(item.discountAmount),money(item.totalAmount),
   money(item.potentiallyMisattributedDiscount),
   item.hasJournalLink?t('Yes','হ্যাঁ'):t('No','না'),
   t(...statusLabels[item.evidenceStatus])];
  values.forEach((v,i)=>{const cell=td(v);if(i>=4&&i<=7)cell.className='text-end';tr.append(cell);});
  body.append(tr);
 }
 if (reviewCount!==data.reviewRowsOnPage ||
     cents(data.potentialAmountOnPage)!==(currencies.size<=1?candidateSum:0n))
  throw Error(t('Report totals mismatch. Export blocked.','প্রতিবেদনের মোট মিলছে না। এক্সপোর্ট বন্ধ।'));
 if (!rows.length) {
  const tr=document.createElement('tr'),cell=td(t('No invoices in this scope.','এই পরিসরে কোনো ইনভয়েস নেই।'));
  cell.colSpan=10;cell.className='text-center text-muted p-3';tr.append(cell);body.append(tr);
 }
 el('feeAttributionRows').replaceChildren(body);
 el('feeAttributionScope').textContent=data.campusName+' · '+query.get('fromDate')+' '+t('to','থেকে')+' '+query.get('toDate');
 el('feeAttributionCount').textContent=reviewCount+t(' flagged on page · ','টি এই পৃষ্ঠায় যাচাইযোগ্য · ')+
  invoices.totalCount+t(' invoices in scope','টি ইনভয়েস এই পরিসরে');
 el('feeAttributionPage').textContent=t('Page ','পৃষ্ঠা ')+requested+t(' of ',' / ')+Math.max(1,Math.ceil(invoices.totalCount/size));
 el('feeAttributionPrev').disabled=requested<=1;
 el('feeAttributionNext').disabled=requested*size>=invoices.totalCount;
 report=data;verifiedQuery=query.toString();
 el('feeAttributionCsv').disabled=!rows.length;
 el('feeAttributionPrint').disabled=!rows.length;
}
async function load() {
 if (locked) return;
 if (pending) pending.abort();
 const controller=new AbortController();pending=controller;
 const id=++generation, identity=context();
 clear();el('feeAttributionRun').disabled=true;
 try {
  const query=filters();
  const data=await api('/api/finance/fees/discount-attribution-review?'+query,controller.signal);
  if (id!==generation || controller.signal.aborted || identity!==context() ||
      query.toString()!==filters().toString()) {lockContext();return;}
  render(data,query);
  notice(t('Historical evidence loaded; candidates are not proven errors.','পুরোনো প্রমাণ লোড হয়েছে; সম্ভাব্য সমস্যা নিশ্চিত ভুল নয়।'),false);
 } catch(error) {
  if(id===generation && error.name!=='AbortError'){clear();notice(error.message,true);}
 } finally {
  if(id===generation)el('feeAttributionRun').disabled=locked;
 }
}
function csvCell(value,numeric=false) {
 let s=String(value??'');
 if(!numeric && /^[\s\u0000-\u001f\u007f\u200b-\u200f\u202a-\u202e\u2060-\u206f\ufeff]*[=+\-@]/.test(s))
  s="'"+s;
 return '"'+s.replace(/"/g,'""')+'"';
}
function verified() {
 if(!report || locked || context()!==initialContext){lockContext();return false;}
 try{return filters().toString()===verifiedQuery;}catch{return false;}
}
function exportCsv() {
 if(!verified())return;
 const rows=[[t('Invoice','ইনভয়েস'),t('Student code','শিক্ষার্থী কোড'),t('Invoice date','ইনভয়েসের তারিখ'),
  t('Currency','মুদ্রা'),t('Subtotal','উপমোট'),t('Discount','ছাড়'),t('Total','মোট'),
  t('Candidate amount (unverified)','সম্ভাব্য অঙ্ক (যাচাইকৃত নয়)'),t('GL linked','GL সংযুক্ত'),
  t('Needs review','যাচাই প্রয়োজন'),t('Evidence status','প্রমাণের অবস্থা')]];
 const numericColumns=new Set([4,5,6,7]);
 for(const r of report.invoices.items)rows.push([r.invoiceNumber,r.studentCode,String(r.invoiceDate).slice(0,10),
  r.currencyCode,money(r.subtotal),money(r.discountAmount),money(r.totalAmount),
  money(r.potentiallyMisattributedDiscount),r.hasJournalLink?t('Yes','হ্যাঁ'):t('No','না'),
  r.needsReview?t('Yes','হ্যাঁ'):t('No','না'),t(...statusLabels[r.evidenceStatus])]);
 const csv='\uFEFF'+rows.map((row,index)=>row.map((v,i)=>csvCell(v,index>0&&numericColumns.has(i))).join(',')).join('\r\n')+'\r\n';
 const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
 const a=document.createElement('a');a.href=url;a.download='fee-discount-evidence-page.csv';
 document.body.append(a);a.click();a.remove();
 setTimeout(()=>URL.revokeObjectURL(url),1000);
}
el('feeAttributionFilters').addEventListener('input',invalidate);
el('feeAttributionFilters').addEventListener('change',invalidate);
el('feeAttributionFilters').addEventListener('submit',event=>{event.preventDefault();page=1;load();});
el('feeAttributionPrev').addEventListener('click',()=>{if(verified()&&page>1){page--;load();}});
el('feeAttributionNext').addEventListener('click',()=>{if(verified()&&page*report.invoices.pageSize<report.invoices.totalCount){page++;load();}});
el('feeAttributionCsv').addEventListener('click',exportCsv);
el('feeAttributionPrint').addEventListener('click',()=>{if(verified())window.print();});
window.addEventListener('pageshow',event=>{if(event.persisted)invalidate();lockContext();});
window.addEventListener('focus',lockContext);
if(typeof MutationObserver==='function')
 new MutationObserver(lockContext).observe(document.body,{attributes:true,
  attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
const optionsContext=context();
api('/api/finance/fees/options').then(data=>{
 if(optionsContext!==context()){lockContext();return;}
 const select=el('feeAttributionCampus');select.replaceChildren(new Option(t('Select campus','ক্যাম্পাস বাছুন'),''));
 for(const campus of data?.campuses||[])
  if(Number.isSafeInteger(campus.id)&&campus.id>0)
   select.add(new Option(campus.name,String(campus.id)));
}).catch(error=>{if(optionsContext===context())notice(error.message,true);});
})();
