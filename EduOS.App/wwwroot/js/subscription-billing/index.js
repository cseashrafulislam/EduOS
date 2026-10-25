(() => {
"use strict";
const $=id=>document.getElementById(id),root=$("subscriptionBillingApp");
if(!root)return;
const status={1:"Trial",2:"Active",3:"Grace",4:"Suspended",5:"Cancelled",6:"Expired",7:"Pending payment"};
const invoiceStatus={1:"Draft",2:"Issued",3:"Partially paid",4:"Paid",5:"Cancelled",6:"Refunded"};
const paymentStatus={1:"Initiated",2:"Awaiting verification",3:"Successful",4:"Failed",5:"Cancelled",6:"Refunded"};
const state={current:null,page:1,pages:1,busy:false,version:0};
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
function renderInvoices(items){
 const body=$("sbInvoiceRows");body.replaceChildren();if(!items.length){empty("sbInvoiceRows",8);return;}
 for(const inv of items){
  const tr=document.createElement("tr");
  td(tr,inv.invoiceNumber);td(tr,inv.invoiceDate);td(tr,inv.dueDate);
  td(tr,money(inv.totalAmount,inv.currencyCode));td(tr,money(inv.paidAmount,inv.currencyCode));
  td(tr,money(inv.dueAmount,inv.currencyCode));td(tr,invoiceStatus[inv.state]||"Unknown");
  const actions=td(tr,"");actions.append(button("Payments",()=>showPayments(inv.id)));
  if(Number(inv.dueAmount)>0&&[2,3].includes(inv.state)){
   const link=document.createElement("a");link.href="/Account/Payment?invoiceId="+encodeURIComponent(inv.id);
   link.className="btn btn-primary btn-sm ms-1";link.textContent="Pay";actions.append(link);
  }
  body.append(tr);
 }
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
async function refresh(){
 const version=++state.version;
 try{
  const [current,invoices,history,lifecycle]=await Promise.all([
   api("/api/subscription/current"),api("/api/subscription/invoices"),
   api("/api/subscription/history?page="+state.page+"&pageSize=10"),
   api("/api/subscription/lifecycle-status")]);
  if(version!==state.version)return;
  state.current=current;renderCurrent();renderLifecycle(lifecycle);renderInvoices(Array.isArray(invoices)?invoices:[]);renderHistory(history);
 }catch(e){if(version===state.version)message(e.message);}
}
$("sbRefresh").addEventListener("click",refresh);
$("sbPrev").addEventListener("click",()=>{if(state.page>1){state.page--;refresh();}});
$("sbNext").addEventListener("click",()=>{if(state.page<state.pages){state.page++;refresh();}});
refresh();
})();
