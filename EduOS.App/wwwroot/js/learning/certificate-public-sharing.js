(() => {
'use strict';
const root=document.getElementById('lmsCertificateApp');
const section=document.getElementById('lmsCertificatePublicSharing');
if(!root||!section)return;
const $=id=>document.getElementById(id);
const bn=(document.documentElement.lang||'').toLowerCase().startsWith('bn');
const msg=(en,bd)=>bn?bd:en;
const initialSession=document.body?.dataset.authSessionReference||'';
const initialTenant=document.body?.dataset.authTenantId||'';
const validSession=()=>/^[0-9A-F]{64}$/.test(initialSession)&&
 document.body?.dataset.authSessionReference===initialSession&&
 document.body?.dataset.authTenantId===initialTenant&&
 window.EduOSSessionTransitionPending?.()!==true;
const state={reference:'',rowVersion:'',request:0,controller:null,busy:false};
const tokenMeta=()=>document.querySelector('meta[name="request-verification-token"]')?.content;
function scrub(){
 state.request++;state.controller?.abort();state.reference='';state.rowVersion='';
 section.classList.add('d-none');
 $('lmsPublicLink').value='';$('lmsPublicLinkWrap').classList.add('d-none');
 $('lmsPublicStatus').textContent='';
}
async function api(path,method='GET',body,signal){
 if(!validSession())throw Error('session');
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  if(!tokenMeta())throw Error('session');
  headers.RequestVerificationToken=tokenMeta();
  headers['Content-Type']='application/json';
 }
 const response=await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers,
  signal,...(body?{body:JSON.stringify(body)}:{})});
 const payload=await response.json().catch(()=>null);
 if(!validSession())throw Error('session');
 if(!response.ok||!payload?.success)throw Error(payload?.message||'request');
 return payload.data;
}
function status(data){
 state.rowVersion=data.rowVersion;
 $('lmsPublicStatus').textContent=data.enabled?
  msg('Verification enabled until ','যাচাই চালু থাকবে ')+new Date(data.expiresAt).toLocaleDateString(bn?'bn-BD':'en-BD'):
  msg('Public verification is off.','সর্বজনীন যাচাই বন্ধ আছে।');
 $('lmsPublicDisable').disabled=!data.enabled;
}
async function select(reference){
 scrub();if(!reference||!validSession())return;
 state.reference=reference;section.classList.remove('d-none');
 const seq=++state.request;state.controller=new AbortController();
 try{
  const data=await api('/api/lms/certificate-verification/'+encodeURIComponent(reference),'GET',null,state.controller.signal);
  if(seq===state.request&&validSession())status(data);
 }catch{if(seq===state.request){scrub();}}
}
async function change(enable){
 if(state.busy||!state.reference||!state.rowVersion||!validSession())return;
 state.busy=true;$('lmsPublicEnable').disabled=true;$('lmsPublicDisable').disabled=true;
 const ref=state.reference;
 try{
  const data=await api('/api/lms/certificate-verification/'+encodeURIComponent(ref)+
   (enable?'/enable':'/disable'),'POST',{rowVersion:state.rowVersion});
  if(ref!==state.reference||!validSession())return;
  if(enable){
   status({enabled:true,expiresAt:data.expiresAt,rowVersion:data.rowVersion});
   const url=new URL('/LmsPublicVerification',location.origin);
   url.hash='token='+encodeURIComponent(data.token);
   $('lmsPublicLink').value=url.toString();
   $('lmsPublicLinkWrap').classList.remove('d-none');
  }else{
   status(data);$('lmsPublicLink').value='';$('lmsPublicLinkWrap').classList.add('d-none');
  }
 }catch{
  $('lmsPublicStatus').textContent=msg('Link changed. Reload the certificate and retry.','লিংক পরিবর্তিত হয়েছে। সনদ আবার খুলুন।');
 }finally{
  state.busy=false;$('lmsPublicEnable').disabled=false;
  if(ref===state.reference&&state.rowVersion)$('lmsPublicDisable').disabled=
   $('lmsPublicStatus').textContent.includes(msg('off.','বন্ধ আছে।'));
 }
}
$('lmsPublicEnable').addEventListener('click',()=>change(true));
$('lmsPublicDisable').addEventListener('click',()=>change(false));
$('lmsPublicCopy').addEventListener('click',async()=>{
 const input=$('lmsPublicLink');
 if(!input.value)return;
 try{await navigator.clipboard.writeText(input.value);}catch{input.focus();input.select();}
});
root.addEventListener('lms-certificate-selected',e=>select(e.detail?.reference));
root.addEventListener('lms-certificate-cleared',scrub);
window.addEventListener('pagehide',scrub);
window.addEventListener('pageshow',e=>{if(e.persisted) scrub();});
const observer=new MutationObserver(()=>{if(!validSession()){scrub();observer.disconnect();}});
observer.observe(document.body,{attributes:true,attributeFilter:['data-auth-session-reference','data-auth-tenant-id']});
})();
