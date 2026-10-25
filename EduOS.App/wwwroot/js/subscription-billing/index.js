(() => {
"use strict";
const $=id=>document.getElementById(id),root=$("subscriptionBillingApp");
if(!root)return;
const status={1:"Trial",2:"Active",3:"Grace",4:"Suspended",5:"Cancelled",6:"Expired",7:"Pending payment"};
const invoiceStatus={1:"Draft",2:"Issued",3:"Partially paid",4:"Paid",5:"Cancelled",6:"Refunded"};
const paymentStatus={1:"Initiated",2:"Awaiting verification",3:"Successful",4:"Failed",5:"Cancelled",6:"Refunded"};
const state={current:null,upcoming:null,page:1,pages:1,invoicePage:1,invoicePages:1,invoiceState:"",invoiceVersion:0,invoiceDetailVersion:0,busy:false,version:0};
const token=root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
 document.querySelector('meta[name="request-verification-token"]')?.content || "";
function message(text,kind="danger"){const e=$("sbAlert");e.textContent=text;e.className="alert alert-"+kind;}
function td(tr,value){const c=document.createElement("td");c.textContent=value==null?"—":String(value);tr.append(c);return c;}
function button(label,fn){const b=document.createElement("button");b.type="button";b.className="btn btn-outline-primary btn-sm";b.textContent=label;b.addEventListener("click",fn);return b;}
function date(v){if(!v)return "—";const d=new Date(v);return Number.isNaN(d.getTime())?"—":d.toLocaleDateString();}
function money(v,c){const n=Number(v);return (Number.isFinite(n)?n.toFixed(2):"0.00")+" "+(c||"BDT");}
function empty(id,count){const tr=document.createElement("tr"),cell=td(tr,"No records.");cell.colSpan=count;$(id).append(tr);}
async function api(path,method="GET",data){
 const opts={method,cache:"no-store",credentials:"same-origin",headers:{Accept:"application/json"}};
 if(method!=="GET"){opts.headers["Content-Type"]="application/json";opts.headers["RequestVerificationToken"]=token;opts.body=JSON.stringify(data);}
 let res;try{res=await fetch(path,opts);}catch{throw Error("Network error. Retry.");}
 const result=await res.json().catch(()=>null);
 if(!res.ok||!result?.success){const e=Error(result?.message||"Request failed ("+res.status+").");e.status=res.status;throw e;}
 return result.data;
}
function renderCurrent(){
 const box=$("sbCurrent"),actions=$("sbActions"),s=state.current;box.replaceChildren();actions.replaceChildren();
 if(!s){box.textContent="No current subscription.";const a=document.createElement("a");a.href="/Account/PlanSelection";a.className="btn btn-primary btn-sm";a.textContent="Choose a plan";actions.append(a);return;}
 box.textContent=s.planName+" · "+(status[s.state]||"Unknown")+" · "+s.billingCycleCode+
  " · "+date(s.startsAt)+" to "+date(s.endsAt)+" · Auto-renew "+(s.autoRenew?"On":"Off");
 if([1,2,3].includes(s.state)&&new Date(s.endsAt)>new Date()&&s.rowVersion){
  const b=button(s.autoRenew?"Disable auto-renew":"Enable auto-renew",async()=>{
   if(state.busy||!confirm((s.autoRenew?"Disable":"Enable")+" auto-renew for "+s.planName+"?"))return;
   state.busy=true;b.disabled=true;
   try{
    await api("/api/subscription/"+encodeURIComponent(s.reference)+"/auto-renew","POST",
      {autoRenew:!s.autoRenew,rowVersion:s.rowVersion});
    message("Renewal preference updated.","success");await refresh();
   }catch(e){message(e.message+(e.status===409?" Refresh to get the latest version.":""));}
   finally{state.busy=false;b.disabled=false;}
  });actions.append(b);
 }
}
function renderLifecycle(info){
 const box=$("sbLifecycle"),actions=$("sbRenewalActions");
 if(!box||!actions)return;
 actions.replaceChildren();
 box.textContent=info?.guidance||"Renewal status is unavailable.";
 if(info?.endsAtUtc){
  box.textContent+=" Period ends: "+date(info.endsAtUtc)+".";
 }
 if(info?.canStartNewSubscription){
  const a=document.createElement("a");a.href="/Account/PlanSelection";
  a.className="btn btn-outline-primary btn-sm";a.textContent="Choose a plan";
  actions.append(a);
 }
}
function renderUpcoming(){
 const box=$("sbUpcoming"),actions=$("sbRenewActions"),s=state.current,u=state.upcoming;
 if(!box||!actions)return;
 actions.replaceChildren();
 if(u){
  box.textContent=(u.state===7?"Renewal invoice awaiting payment. ":u.state===2?"Prepaid renewal scheduled. ":"Renewal scheduled. ")+
   (u.planName||"Subscription")+" · "+date(u.startsAt)+" to "+date(u.endsAt)+
   ". Your current period remains effective until its existing end date.";
  return;
 }
 if(!s||![1,2,3].includes(s.state)||s.billingCycleCode==="Lifetime"){
  box.textContent="No eligible active subscription for early renewal.";return;
 }
 const remaining=new Date(s.endsAt).getTime()-Date.now();
 if(!Number.isFinite(remaining)||remaining<=0||remaining>30*86400000||!s.rowVersion){
  box.textContent="You can request a renewal invoice during the final 30 days of the current period.";return;
 }
 box.textContent=s.autoRenew
  ?"Auto-renew is enabled: an invoice will be scheduled automatically during the final 30 days. You may also request it now. No payment is collected automatically."
  :"Request the next billing period at the plan's current price. No payment is collected automatically.";
 const b=button("Generate renewal invoice",async()=>{
  if(state.busy||!confirm("Create a renewal invoice? The next period begins after your current period. Payment requires separate verification."))return;
  if(!globalThis.crypto?.randomUUID){message("Secure request IDs are unavailable in this browser.");return;}
  const key="eduos:renewal:"+s.reference;
  let requestId=null;
  try{requestId=sessionStorage.getItem(key);}catch{}
  if(!requestId){requestId=crypto.randomUUID();try{sessionStorage.setItem(key,requestId);}catch{}}
  state.busy=true;b.disabled=true;
  try{
   await api("/api/subscription/"+encodeURIComponent(s.reference)+"/renew","POST",
     {clientRequestId:requestId,rowVersion:s.rowVersion});
   try{sessionStorage.removeItem(key);}catch{}
   message("Renewal created. Pay the invoice below to activate the next period.","success");
   await refresh();
  }catch(e){message(e.message+(e.status===409?" Refresh before retrying.":""));}
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
  td(tr,money(inv.dueAmount,inv.currencyCode));td(tr,invoiceStatus[inv.state]||"Unknown");
  const actions=td(tr,"");actions.append(button("Details",()=>showInvoiceDetails(inv.id)));
  actions.append(button("Payments",()=>showPayments(inv.id)));
  if(Number(inv.dueAmount)>0&&[2,3].includes(inv.state)&&
     typeof inv.invoiceNumber==="string"&&inv.invoiceNumber.startsWith("REN-")){
   actions.append(button("Remind owner",()=>remindOwner(inv.id)));
  }
  if(Number(inv.dueAmount)>0&&[2,3].includes(inv.state)){
   const link=document.createElement("a");link.href="/Account/Payment?invoiceId="+encodeURIComponent(inv.id);
   link.className="btn btn-primary btn-sm ms-1";link.textContent="Pay";actions.append(link);
  }
  body.append(tr);
 }
}
async function showInvoiceDetails(id){
 const version=++state.invoiceDetailVersion,box=$("sbInvoiceDetails");
 box.textContent="Loading invoice details…";
 try{
  const inv=await api("/api/subscription/invoices/"+encodeURIComponent(id));
  if(version!==state.invoiceDetailVersion)return;
  box.replaceChildren();
  const heading=document.createElement("h3");heading.className="h6";
  heading.textContent="Invoice "+(inv.invoiceNumber||id);box.append(heading);
  const summary=document.createElement("p");summary.className="small";
  summary.textContent="Issued "+date(inv.invoiceDate)+" · Due "+date(inv.dueDate)+
   " · Status "+(invoiceStatus[inv.state]||"Unknown")+
   " · Subtotal "+money(inv.subtotal,inv.currencyCode)+
   " · Tax "+money(inv.taxAmount,inv.currencyCode)+
   " · Total "+money(inv.totalAmount,inv.currencyCode)+
   " · Paid "+money(inv.paidAmount,inv.currencyCode)+
   " · Outstanding "+money(inv.dueAmount,inv.currencyCode);
  box.append(summary);
  const lines=Array.isArray(inv.lines)?inv.lines:[];
  if(!lines.length){const p=document.createElement("p");p.className="small text-muted";
   p.textContent="No line items recorded for this invoice.";box.append(p);return;}
  const table=document.createElement("table");table.className="table table-sm table-bordered";
  const thead=document.createElement("thead"),header=document.createElement("tr");
  for(const name of ["Description","Quantity","Unit price","Amount"]){
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
  if(!status.hasActiveOwner){message("No active institution owner can receive the reminder.");return;}
  if(!status.canSendToday){message("Today's reminder was already delivered, or this invoice is no longer payable.","info");return;}
  if(!confirm("Send an in-app renewal payment reminder to institution owner(s)? No email, SMS or payment will be initiated."))return;
  await api("/api/subscription/invoices/"+encodeURIComponent(id)+"/remind","POST",{});
  message("In-app renewal reminder processed.","success");
 }catch(e){message(e.message);}
 finally{state.busy=false;}
}
async function showPayments(id){
 const box=$("sbPayments");box.textContent="Loading payment history…";
 try{
  const data=await api("/api/subscription-payment/invoice/"+encodeURIComponent(id));
  box.replaceChildren();const heading=document.createElement("h3");heading.className="h6";heading.textContent="Payments for invoice "+id;box.append(heading);
  if(!Array.isArray(data)||!data.length){const p=document.createElement("p");p.textContent="No payments found.";box.append(p);return;}
  const table=document.createElement("table");table.className="table table-sm";
  const body=document.createElement("tbody");
  for(const pay of data){const tr=document.createElement("tr");td(tr,pay.transactionId);td(tr,money(pay.amount,pay.currencyCode));td(tr,paymentStatus[pay.state]||"Unknown");td(tr,date(pay.initiatedAt));body.append(tr);}
  table.append(body);box.append(table);
 }catch(e){box.textContent=e.message;}
}
function renderHistory(result){
 const body=$("sbHistoryRows");body.replaceChildren();const rows=result?.items||[];
 for(const s of rows){const tr=document.createElement("tr");td(tr,s.planName);td(tr,s.billingCycleCode);
  td(tr,date(s.startsAt));td(tr,date(s.endsAt));td(tr,status[s.state]||"Unknown");td(tr,s.autoRenew?"Yes":"No");body.append(tr);}
 if(!rows.length)empty("sbHistoryRows",6);
 state.pages=Math.max(1,Math.ceil((Number(result?.totalCount)||0)/(Number(result?.pageSize)||10)));
 $("sbPage").textContent="Page "+state.page+" / "+state.pages;
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
  $("sbInvoicePage").textContent="Page "+state.invoicePage+" / "+pages+" · "+(Number(result?.totalCount)||0)+" invoices";
  $("sbInvoicePrev").disabled=state.invoicePage<=1;
  $("sbInvoiceNext").disabled=state.invoicePage>=pages;
 }catch(e){if(version===state.invoiceVersion)message(e.message);}
}

async function refresh(){
 const version=++state.version;
 const invoiceTask=refreshInvoices();
 try{
  const [current,history,lifecycle,upcoming]=await Promise.all([
   api("/api/subscription/current"),
   api("/api/subscription/history?page="+state.page+"&pageSize=10"),
   api("/api/subscription/lifecycle-status"),api("/api/subscription/upcoming")]);
  if(version!==state.version)return;
  state.current=current;state.upcoming=upcoming;renderCurrent();renderLifecycle(lifecycle);renderUpcoming();renderHistory(history);
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
