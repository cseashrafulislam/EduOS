(() => {
"use strict";
const $=id=>document.getElementById(id),root=$("subscriptionBillingApp");
if(!root)return;
const bn=(document.documentElement.lang||"").toLowerCase().startsWith("bn");
const t=(en,bangla)=>bn?bangla:en;
const status={1:t("Trial","পরীক্ষামূলক"),2:t("Active","সক্রিয়"),3:t("Grace","অতিরিক্ত সময়"),4:t("Suspended","স্থগিত"),5:t("Cancelled","বাতিল"),6:t("Expired","মেয়াদোত্তীর্ণ"),7:t("Pending payment","পেমেন্টের অপেক্ষায়")};
const invoiceStatus={1:t("Draft","খসড়া"),2:t("Issued","ইস্যুকৃত"),3:t("Partially paid","আংশিক পরিশোধিত"),4:t("Paid","পরিশোধিত"),5:t("Cancelled","বাতিল"),6:t("Refunded","ফেরত দেওয়া")};
const paymentStatus={1:t("Initiated","শুরু হয়েছে"),2:t("Awaiting verification","যাচাইয়ের অপেক্ষায়"),3:t("Successful","সফল"),4:t("Failed","ব্যর্থ"),5:t("Cancelled","বাতিল"),6:t("Refunded","ফেরত দেওয়া")};
const state={current:null,upcoming:null,plans:[],page:1,pages:1,invoicePage:1,invoicePages:1,invoiceState:"",invoiceVersion:0,invoiceDetailVersion:0,busy:false,version:0};
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
 document.querySelector('meta[name="request-verification-token"]')?.content || "";
function message(text,kind="danger"){const e=$("sbAlert");e.textContent=text;e.className="alert alert-"+kind;}
function td(tr,value){const c=document.createElement("td");c.textContent=value==null?"—":String(value);tr.append(c);return c;}
function button(label,fn){const b=document.createElement("button");b.type="button";b.className="btn btn-outline-primary btn-sm";b.textContent=label;b.addEventListener("click",fn);return b;}
function date(v){if(!v)return "—";const d=new Date(v);return Number.isNaN(d.getTime())?"—":d.toLocaleDateString();}
function money(v,c){const n=Number(v);return (Number.isFinite(n)?n.toFixed(2):"0.00")+" "+(c||"BDT");}
function empty(id,count){const tr=document.createElement("tr"),cell=td(tr,t("No records.","কোনো রেকর্ড নেই।"));cell.colSpan=count;$(id).append(tr);}
async function api(path,method="GET",data){
 const opts={method,cache:"no-store",credentials:"same-origin",headers:{Accept:"application/json"}};
 if(method!=="GET"){opts.headers["Content-Type"]="application/json";opts.headers["RequestVerificationToken"]=token;opts.body=JSON.stringify(data);}
 let res;try{res=await fetch(path,opts);}catch{throw Error(t("Network error. Retry.","নেটওয়ার্ক সমস্যা। আবার চেষ্টা করুন।"));}
 const result=await res.json().catch(()=>null);
 if(!res.ok||!result?.success){const e=Error(result?.message||t("Request failed (","অনুরোধ ব্যর্থ (")+res.status+").");e.status=res.status;throw e;}
 return result.data;
}
function renderCurrent(){
 const box=$("sbCurrent"),actions=$("sbActions"),s=state.current;box.replaceChildren();actions.replaceChildren();
 if(!s){box.textContent=t("No current subscription.","কোনো বর্তমান সাবস্ক্রিপশন নেই।");const a=document.createElement("a");a.href="/Account/PlanSelection";a.className="btn btn-primary btn-sm";a.textContent=t("Choose a plan","প্ল্যান বাছুন");actions.append(a);return;}
 box.textContent=s.planName+" · "+(status[s.state]||t("Unknown","অজানা"))+" · "+s.billingCycleCode+
  " · "+date(s.startsAt)+t(" to "," থেকে ")+date(s.endsAt)+t(" · Auto-renew "," · স্বয়ংক্রিয় নবায়ন ")+(s.autoRenew?t("On","চালু"):t("Off","বন্ধ"));
 if([1,2,3].includes(s.state)&&new Date(s.endsAt)>new Date()&&s.rowVersion){
  const b=button(s.autoRenew?t("Disable auto-renew","স্বয়ংক্রিয় নবায়ন বন্ধ করুন"):t("Enable auto-renew","স্বয়ংক্রিয় নবায়ন চালু করুন"),async()=>{
   if(state.busy||!confirm(t((s.autoRenew?"Disable":"Enable")+" auto-renew for "+s.planName+"?",s.planName+" প্ল্যানের স্বয়ংক্রিয় নবায়ন "+(s.autoRenew?"বন্ধ":"চালু")+" করবেন?")))return;
   state.busy=true;b.disabled=true;
   try{
    await api("/api/subscription/"+encodeURIComponent(s.reference)+"/auto-renew","POST",
      {autoRenew:!s.autoRenew,rowVersion:s.rowVersion});
    message(t("Renewal preference updated.","নবায়নের পছন্দ সংরক্ষিত হয়েছে।"),"success");await refresh();
   }catch(e){message(e.message+(e.status===409?t(" Refresh to get the latest version."," সর্বশেষ অবস্থা রিফ্রেশ করুন।"):""));}
   finally{state.busy=false;b.disabled=false;}
  });actions.append(b);
 }
 if([1,2,3].includes(s.state)&&new Date(s.startsAt)<=new Date()&&
    new Date(s.endsAt)>new Date()&&s.rowVersion&&!state.upcoming){
  const endButton=button(t("End subscription now","এখনই সাবস্ক্রিপশন শেষ করুন"),async()=>{
   if(state.busy)return;
   const typed=window.prompt(t("Ending this subscription immediately removes access. No automatic refund will be issued. Type END to continue:","এখনই সাবস্ক্রিপশন শেষ করলে প্রবেশাধিকার বন্ধ হবে, স্বয়ংক্রিয় রিফান্ড হবে না। চালিয়ে যেতে END লিখুন:"));
   if(typed!=="END")return;
   if(!window.confirm(t("Confirm immediate cancellation of "+s.planName+"? This ends the current paid/trial period now and cannot be undone.",s.planName+" প্ল্যান এখনই বাতিল করবেন? বর্তমান মেয়াদ শেষ হবে; পূর্বাবস্থায় নেওয়া যাবে না।")))return;
   state.busy=true;endButton.disabled=true;
   try{
    await api("/api/subscription/"+encodeURIComponent(s.reference)+"/cancel","POST",
      {atPeriodEnd:false,rowVersion:s.rowVersion});
    message(t("Subscription ended immediately. No automatic refund was initiated.","সাবস্ক্রিপশন শেষ হয়েছে। কোনো স্বয়ংক্রিয় রিফান্ড শুরু হয়নি।"),"success");
    await refresh();
   }catch(e){message(e.message+(e.status===409?t(" Reload the latest subscription state and retry."," সর্বশেষ সাবস্ক্রিপশন অবস্থা লোড করে আবার চেষ্টা করুন।"):""));}
   finally{state.busy=false;endButton.disabled=false;}
  });
  endButton.className="btn btn-outline-danger btn-sm";actions.append(endButton);
 }
}
function renderLifecycle(info){
 const box=$("sbLifecycle"),actions=$("sbRenewalActions");
 if(!box||!actions)return;
 actions.replaceChildren();
 box.textContent=info?.guidance||t("Renewal status is unavailable.","নবায়নের অবস্থা পাওয়া যায়নি।");
 if(info?.endsAtUtc){
  box.textContent+=t(" Period ends: "," মেয়াদ শেষ: ")+date(info.endsAtUtc)+".";
 }
 if(info?.canStartNewSubscription){
  const a=document.createElement("a");a.href="/Account/PlanSelection";
  a.className="btn btn-outline-primary btn-sm";a.textContent=t("Choose a plan","প্ল্যান বাছুন");
  actions.append(a);
 }
}
function renderUpcoming(){
 const box=$("sbUpcoming"),actions=$("sbRenewActions"),s=state.current,u=state.upcoming;
 if(!box||!actions)return;
 actions.replaceChildren();
 if(u){
  box.textContent=(u.state===7?t("Renewal invoice awaiting payment. ","নবায়নের ইনভয়েস পরিশোধের অপেক্ষায়। "):u.state===2?t("Prepaid renewal scheduled. ","অগ্রিম পরিশোধিত নবায়ন নির্ধারিত। "):t("Renewal scheduled. ","নবায়ন নির্ধারিত। "))+
   (u.planName||t("Subscription","সাবস্ক্রিপশন"))+" · "+date(u.startsAt)+t(" to "," থেকে ")+date(u.endsAt)+
   t(". Your current period remains effective until its existing end date.","। বর্তমান মেয়াদ আগের নির্ধারিত তারিখ পর্যন্ত চালু থাকবে।");
  if(u.state===7){
   const version=state.version;
   api("/api/subscription/invoices/unpaid").then(invoices=>{
    if(version!==state.version||state.upcoming?.id!==u.id)return;
    const invoice=Array.isArray(invoices)?invoices.find(i=>
     Number(i.tenantSubscriptionId)===Number(u.id)&&Number(i.dueAmount)>0&&[2,3].includes(Number(i.state))):null;
    if(invoice){const a=document.createElement("a");a.href="/Account/Payment?invoiceId="+encodeURIComponent(invoice.id);
     a.className="btn btn-primary btn-sm";a.textContent=t("Pay next period","পরবর্তী মেয়াদের পেমেন্ট");actions.append(a);}
   }).catch(()=>{});
  }
  return;
 }
 if(!s||![1,2,3].includes(s.state)||s.billingCycleCode==="Lifetime"){
  box.textContent=t("No eligible active subscription for early renewal.","আগাম নবায়নের জন্য যোগ্য সক্রিয় সাবস্ক্রিপশন নেই।");return;
 }
 const remaining=new Date(s.endsAt).getTime()-Date.now();
 if(!Number.isFinite(remaining)||remaining<=0||!s.rowVersion||
   (s.billingCycleCode!=="Monthly"&&remaining>30*86400000)){
  box.textContent=t("Monthly plans may renew during their active period. Other plans can request renewal in the last 30 days.","মাসিক প্ল্যান সক্রিয় থাকাকালে পরবর্তী মাসের নবায়ন করা যায়। অন্য প্ল্যানের জন্য শেষ ৩০ দিনে নবায়ন করা যাবে।");return;
 }
 box.textContent=s.autoRenew
  ?t("Auto-renew is enabled: an invoice will be scheduled automatically during the final 30 days. You may also request it now. No payment is collected automatically.","স্বয়ংক্রিয় নবায়ন চালু: শেষ ৩০ দিনে ইনভয়েস নির্ধারিত হবে। এখনই চাইতে পারেন; স্বয়ংক্রিয়ভাবে টাকা কাটা হবে না।")
  :t("Request the next billing period at the plan's current price. No payment is collected automatically.","প্ল্যানের বর্তমান মূল্যে পরবর্তী মেয়াদের অনুরোধ করুন। স্বয়ংক্রিয়ভাবে টাকা কাটা হয় না।");
 // Only public, active, same-currency plans can be scheduled; the server revalidates.
 const eligiblePlans=state.plans.filter(p=>p&&p.isActive&&p.isPublic&&
   String(p.currencyCode||"").toUpperCase()===String(s.currencyCode||"").toUpperCase());
 let planSelect=null;
 if(eligiblePlans.length){
  const label=document.createElement("label");label.className="small";
  label.textContent=t("Plan for next period ","পরবর্তী মেয়াদের প্ল্যান ");
  planSelect=document.createElement("select");planSelect.className="form-select form-select-sm";
  planSelect.setAttribute("aria-label",t("Plan for next subscription period","পরবর্তী সাবস্ক্রিপশন মেয়াদের প্ল্যান"));
  for(const plan of eligiblePlans){
   const opt=document.createElement("option");opt.value=String(plan.id);
   opt.textContent=plan.name+" ("+plan.currencyCode+")";
   opt.selected=Number(plan.id)===Number(s.subscriptionPlanId);
   planSelect.append(opt);
  }
  label.append(planSelect);actions.append(label);
 }
 const b=button(t("Generate renewal invoice","নবায়নের ইনভয়েস তৈরি"),async()=>{
  if(state.busy||!confirm(t("Create a renewal invoice? The next period begins after your current period. Payment requires separate verification.","নবায়নের ইনভয়েস তৈরি করবেন? নতুন মেয়াদ বর্তমান মেয়াদ শেষে শুরু হবে। পেমেন্ট আলাদাভাবে যাচাই করতে হবে।")))return;
  if(!globalThis.crypto?.randomUUID){message(t("Secure request IDs are unavailable in this browser.","এই ব্রাউজারে নিরাপদ অনুরোধের আইডি পাওয়া যাচ্ছে না।"));return;}
  const nextPlanId=planSelect?Number(planSelect.value):Number(s.subscriptionPlanId);
  if(!Number.isSafeInteger(nextPlanId)||nextPlanId<=0){message(t("Select a valid next-period plan.","পরবর্তী মেয়াদের জন্য বৈধ প্ল্যান বাছুন।"));return;}
  const key="eduos:renewal:"+s.reference+":"+nextPlanId;
  let requestId=null;
  try{requestId=sessionStorage.getItem(key);}catch{}
  if(!requestId){requestId=crypto.randomUUID();try{sessionStorage.setItem(key,requestId);}catch{}}
  state.busy=true;b.disabled=true;
  try{
   await api("/api/subscription/"+encodeURIComponent(s.reference)+"/renew","POST",
     {clientRequestId:requestId,rowVersion:s.rowVersion,
      nextPlanId:nextPlanId===Number(s.subscriptionPlanId)?null:nextPlanId});
   try{sessionStorage.removeItem(key);}catch{}
   message(t("Renewal created. Pay the invoice below to activate the next period.","নবায়ন তৈরি হয়েছে। পরবর্তী মেয়াদ চালু করতে নিচের ইনভয়েস পরিশোধ করুন।"),"success");
   await refresh();
  }catch(e){message(e.message+(e.status===409?t(" Refresh before retrying."," আবার চেষ্টা করার আগে রিফ্রেশ করুন।"):""));}
  finally{state.busy=false;b.disabled=false;}
 });
 actions.append(b);
}
function renderInvoices(items){
 const body=$("sbInvoiceRows");body.replaceChildren();if(!items.length){empty("sbInvoiceRows",8);return;}
 for(const inv of items){
  const tr=document.createElement("tr");
  td(tr,inv.invoiceNumber);td(tr,inv.invoiceDate);td(tr,inv.dueDate);
  td(tr,money(inv.totalAmount,inv.currencyCode));td(tr,money(inv.paidAmount,inv.currencyCode));
  td(tr,money(inv.dueAmount,inv.currencyCode));td(tr,invoiceStatus[inv.state]||t("Unknown","অজানা"));
  const actions=td(tr,"");actions.append(button(t("Details","বিবরণ"),()=>showInvoiceDetails(inv.id)));
  actions.append(button(t("Payments","পেমেন্ট"),()=>showPayments(inv.id)));
  const exportButton=button(t("Download CSV","CSV ডাউনলোড"),()=>downloadInvoiceCsv(inv.id,exportButton));
  actions.append(exportButton);
  if(Number(inv.dueAmount)>0&&[2,3].includes(inv.state)&&
     typeof inv.invoiceNumber==="string"&&inv.invoiceNumber.startsWith("REN-")){
   actions.append(button(t("Remind owner","মালিককে স্মরণ করান"),()=>remindOwner(inv.id)));
  }
  if(Number(inv.dueAmount)>0&&[2,3].includes(inv.state)){
   const link=document.createElement("a");link.href="/Account/Payment?invoiceId="+encodeURIComponent(inv.id);
   link.className="btn btn-primary btn-sm ms-1";link.textContent=t("Pay","পরিশোধ করুন");actions.append(link);
  }
  body.append(tr);
 }
}

async function downloadInvoiceCsv(id,trigger){
 if(trigger.disabled)return;
 trigger.disabled=true;
 try{
  const response=await fetch("/api/subscription/invoices/"+encodeURIComponent(id)+"/export",{
   method:"GET",credentials:"same-origin",cache:"no-store",headers:{Accept:"text/csv"}
  });
  if(!response.ok){
   const error=await response.json().catch(()=>null);
   throw Error(error?.message||t("Invoice export failed (","ইনভয়েস রপ্তানি ব্যর্থ (")+response.status+").");
  }
  const csv=await response.blob();
  if(!csv.size||csv.size>10*1024*1024)throw Error(t("Invoice export is empty or exceeds 10 MB.","ইনভয়েস রপ্তানি খালি বা ১০ MB-এর বেশি।"));
  const url=URL.createObjectURL(csv),link=document.createElement("a");
  link.href=url;link.download="subscription-invoice-"+id+".csv";
  document.body.append(link);link.click();link.remove();
  window.setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(e){message(e.message||t("Unable to download invoice.","ইনভয়েস ডাউনলোড করা যায়নি।"));}
 finally{trigger.disabled=false;}
}
async function showInvoiceDetails(id){
 const version=++state.invoiceDetailVersion,box=$("sbInvoiceDetails");
 box.textContent=t("Loading invoice details…","ইনভয়েসের বিবরণ লোড হচ্ছে…");
 try{
  const inv=await api("/api/subscription/invoices/"+encodeURIComponent(id));
  if(version!==state.invoiceDetailVersion)return;
  box.replaceChildren();
  const heading=document.createElement("h3");heading.className="h6";
  heading.textContent=t("Invoice ","ইনভয়েস ")+(inv.invoiceNumber||id);box.append(heading);
  const summary=document.createElement("p");summary.className="small";
  summary.textContent=t("Issued ","ইস্যু ")+date(inv.invoiceDate)+t(" · Due "," · শেষ তারিখ ")+date(inv.dueDate)+
   t(" · Status "," · অবস্থা ")+(invoiceStatus[inv.state]||t("Unknown","অজানা"))+
   t(" · Subtotal "," · উপমোট ")+money(inv.subtotal,inv.currencyCode)+
   t(" · Tax "," · কর ")+money(inv.taxAmount,inv.currencyCode)+
   t(" · Total "," · মোট ")+money(inv.totalAmount,inv.currencyCode)+
   t(" · Paid "," · পরিশোধ ")+money(inv.paidAmount,inv.currencyCode)+
   t(" · Outstanding "," · বকেয়া ")+money(inv.dueAmount,inv.currencyCode);
  box.append(summary);
  const lines=Array.isArray(inv.lines)?inv.lines:[];
  if(!lines.length){const p=document.createElement("p");p.className="small text-muted";
   p.textContent=t("No line items recorded for this invoice.","এই ইনভয়েসে কোনো লাইন আইটেম নেই।");box.append(p);return;}
  const table=document.createElement("table");table.className="table table-sm table-bordered";
  const thead=document.createElement("thead"),header=document.createElement("tr");
  for(const name of [t("Description","বিবরণ"),t("Quantity","পরিমাণ"),t("Unit price","একক মূল্য"),t("Amount","টাকা")]){
   const th=document.createElement("th");th.scope="col";th.textContent=name;header.append(th);
  }
  thead.append(header);table.append(thead);
  const tbody=document.createElement("tbody");
  for(const line of lines){
   const tr=document.createElement("tr");td(tr,line.description);
   const qty=Number(line.quantity);td(tr,Number.isFinite(qty)?String(qty):"—");
   td(tr,money(line.unitPrice,inv.currencyCode));td(tr,money(line.amount,inv.currencyCode));
   tbody.append(tr);
  }
  table.append(tbody);const wrapper=document.createElement("div");
  wrapper.className="table-responsive";wrapper.append(table);box.append(wrapper);
 }catch(e){if(version===state.invoiceDetailVersion)box.textContent=e.message;}
}
async function remindOwner(id){
 if(state.busy)return;
 state.busy=true;
 try{
  const status=await api("/api/subscription/invoices/"+encodeURIComponent(id)+"/reminder-status");
  if(!status.hasActiveOwner){message(t("No active institution owner can receive the reminder.","স্মরণবার্তা গ্রহণের জন্য কোনো সক্রিয় প্রতিষ্ঠানের মালিক নেই।"));return;}
  if(!status.canSendToday){message(t("Today's reminder was already delivered, or this invoice is no longer payable.","আজকের স্মরণবার্তা পাঠানো হয়েছে অথবা ইনভয়েসটি আর পরিশোধযোগ্য নয়।"),"info");return;}
  if(!confirm(t("Send an in-app renewal payment reminder to institution owner(s)? No email, SMS or payment will be initiated.","প্রতিষ্ঠানের মালিককে অ্যাপের মধ্যে নবায়নের স্মরণবার্তা পাঠাবেন? ইমেইল, SMS বা পেমেন্ট হবে না।")))return;
  await api("/api/subscription/invoices/"+encodeURIComponent(id)+"/remind","POST",{});
  message(t("In-app renewal reminder processed.","অ্যাপের নবায়ন স্মরণবার্তা প্রক্রিয়াকৃত হয়েছে।"),"success");
 }catch(e){message(e.message);}
 finally{state.busy=false;}
}
async function showPayments(id){
 const box=$("sbPayments");box.textContent=t("Loading payment history…","পেমেন্টের ইতিহাস লোড হচ্ছে…");
 try{
  const data=await api("/api/subscription-payment/invoice/"+encodeURIComponent(id));
  box.replaceChildren();const heading=document.createElement("h3");heading.className="h6";heading.textContent=t("Payments for invoice ","ইনভয়েসের পেমেন্ট ")+id;box.append(heading);
  if(!Array.isArray(data)||!data.length){const p=document.createElement("p");p.textContent=t("No payments found.","কোনো পেমেন্ট পাওয়া যায়নি।");box.append(p);return;}
  const table=document.createElement("table");table.className="table table-sm";
  const body=document.createElement("tbody");
  for(const pay of data){
   const tr=document.createElement("tr");
   td(tr,pay.transactionId);td(tr,money(pay.amount,pay.currencyCode));
   td(tr,paymentStatus[pay.state]||t("Unknown","অজানা"));td(tr,date(pay.initiatedAt));
   const action=td(tr,"");
   if(Number(pay.state)===3 && pay.completedAt && Number(pay.id)>0){
    const link=document.createElement("a");
    link.href="/SubscriptionBilling/Receipt/"+encodeURIComponent(pay.id);
    link.target="_blank";link.rel="noopener noreferrer";
    link.className="btn btn-outline-primary btn-sm";link.textContent=t("View receipt","রসিদ দেখুন");
    action.append(link);
   }
   body.append(tr);
  }
  table.append(body);box.append(table);
 }catch(e){box.textContent=e.message;}
}
function renderHistory(result){
 const body=$("sbHistoryRows");body.replaceChildren();const rows=result?.items||[];
 for(const s of rows){const tr=document.createElement("tr");td(tr,s.planName);td(tr,s.billingCycleCode);
  td(tr,date(s.startsAt));td(tr,date(s.endsAt));td(tr,status[s.state]||t("Unknown","অজানা"));td(tr,s.autoRenew?t("Yes","হ্যাঁ"):t("No","না"));body.append(tr);}
 if(!rows.length)empty("sbHistoryRows",6);
 state.pages=Math.max(1,Math.ceil((Number(result?.totalCount)||0)/(Number(result?.pageSize)||10)));
 $("sbPage").textContent=t("Page ","পৃষ্ঠা ")+state.page+" / "+state.pages;
 $("sbPrev").disabled=state.page<=1;$("sbNext").disabled=state.page>=state.pages;
}

async function refreshInvoices(){
 const version=++state.invoiceVersion;
 ++state.invoiceDetailVersion;$("sbInvoiceDetails").replaceChildren();
 try{
  const filter=state.invoiceState?"&state="+encodeURIComponent(state.invoiceState):"";
  const result=await api("/api/subscription/invoices/paged?page="+state.invoicePage+"&pageSize=10"+filter);
  if(version!==state.invoiceVersion)return;
  const pages=Math.max(1,Math.ceil((Number(result?.totalCount)||0)/(Number(result?.pageSize)||10)));
  if(state.invoicePage>pages){state.invoicePage=pages;return refreshInvoices();}
  renderInvoices(Array.isArray(result?.items)?result.items:[]);
  state.invoicePages=pages;
  $("sbInvoicePage").textContent=t("Page ","পৃষ্ঠা ")+state.invoicePage+" / "+pages+" · "+(Number(result?.totalCount)||0)+t(" invoices","টি ইনভয়েস");
  $("sbInvoicePrev").disabled=state.invoicePage<=1;
  $("sbInvoiceNext").disabled=state.invoicePage>=pages;
 }catch(e){if(version===state.invoiceVersion)message(e.message);}
}

async function refresh(){
 const version=++state.version;
 const invoiceTask=refreshInvoices();
 try{
  const [current,history,lifecycle,upcoming,plans]=await Promise.all([
   api("/api/subscription/current"),
   api("/api/subscription/history?page="+state.page+"&pageSize=10"),
   api("/api/subscription/lifecycle-status"),api("/api/subscription/upcoming"),
   api("/api/subscription-plans").catch(()=>[])]);
  if(version!==state.version)return;
  state.current=current;state.upcoming=upcoming;state.plans=Array.isArray(plans)?plans:[];renderCurrent();renderLifecycle(lifecycle);renderUpcoming();renderHistory(history);
 }catch(e){if(version===state.version)message(e.message);}
 await invoiceTask;
}
$("sbInvoiceState").addEventListener("change",e=>{state.invoiceState=e.target.value;state.invoicePage=1;refreshInvoices();});
$("sbInvoicePrev").addEventListener("click",()=>{if(state.invoicePage>1){state.invoicePage--;refreshInvoices();}});
$("sbInvoiceNext").addEventListener("click",()=>{if(state.invoicePage<state.invoicePages){state.invoicePage++;refreshInvoices();}});
$("sbRefresh").addEventListener("click",refresh);
$("sbPrev").addEventListener("click",()=>{if(state.page>1){state.page--;refresh();}});
$("sbNext").addEventListener("click",()=>{if(state.page<state.pages){state.page++;refresh();}});
refresh();
})();
