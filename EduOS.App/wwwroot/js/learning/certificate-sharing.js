(() => {
'use strict';
const root=document.getElementById('lmsCertificateApp');
const select=document.getElementById('lmsCertificateGuardian');
const list=document.getElementById('lmsCertificateGrants');
const button=document.getElementById('lmsCertificateGrant');
if(!root||!select||!list||!button||root.dataset.studentMode!=='true')return;
const bn=(document.documentElement.lang||'en').toLowerCase().startsWith('bn');
const text=bn?{
 empty:'কোনো অভিভাবকের প্রবেশাধিকার নেই।',none:'যোগ্য অভিভাবকের অ্যাকাউন্ট নেই।',
 remove:'প্রবেশাধিকার বাতিল',share:'এই অভিভাবককে সনদ দেখা ও প্রিন্টের অনুমতি দেবেন?',
 revoke:'এই অভিভাবকের প্রবেশাধিকার বাতিল করবেন?',session:'সেশন পরিবর্তিত হয়েছে। আবার লগইন করুন।',
 error:'অনুরোধ ব্যর্থ হয়েছে।'
}:{
 empty:'No guardian has access.',none:'No eligible guardian account.',
 remove:'Remove access',share:'Allow this guardian to view and print the certificate?',
 revoke:'Remove this guardian’s certificate access?',session:'Session changed. Sign in again.',
 error:'Request failed.'
};
const session=document.body?.dataset.authSessionReference||'';
const tenant=document.body?.dataset.authTenantId||'';
const valid=()=>/^[0-9A-F]{64}$/.test(session)&&
 document.body?.dataset.authSessionReference===session&&document.body?.dataset.authTenantId===tenant&&
 window.EduOSSessionTransitionPending?.()!==true;
let selected='',generation=0,busy=false;
const alert=document.getElementById('lmsCertificateAlert');
function error(message){alert.className='alert alert-danger';alert.textContent=message;alert.focus();}
async function api(path,method='GET',body){
 if(!valid())throw Error(text.session);
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error(text.session);
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';
 }
 const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),15000);
 try{
  const response=await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers,
   signal:controller.signal,...(body===undefined?{}:{body:JSON.stringify(body)})});
  const result=await response.json().catch(()=>null);
  if(!valid())throw Error(text.session);
  if(!response.ok||!result?.success)throw Error(result?.message||text.error);
  return result.data;
 }finally{clearTimeout(timeout);}
}
async function load(reference){
 const seq=++generation;select.replaceChildren();list.replaceChildren();button.disabled=true;
 try{
  const [choices,grants]=await Promise.all([
   api('/api/lms/certificate-sharing/'+encodeURIComponent(reference)+'/guardians'),
   api('/api/lms/certificate-sharing/'+encodeURIComponent(reference)+'/grants')
  ]);
  if(seq!==generation||selected!==reference||!valid())return;
  for(const choice of choices||[]){
   const option=document.createElement('option');
   option.value=String(choice.guardianLinkId);
   option.textContent=choice.guardianName+' ('+choice.relationCode+')';
   select.append(option);
  }
  if(!select.options.length){
   const option=document.createElement('option');option.value='';option.textContent=text.none;select.append(option);
  }
  button.disabled=!choices?.length;
  if(!grants?.length){list.textContent=text.empty;return;}
  for(const grant of grants){
   const line=document.createElement('div');line.className='d-flex flex-wrap gap-2 align-items-center mb-2';
   const label=document.createElement('span');label.textContent=grant.guardianName;
   const revoke=document.createElement('button');revoke.type='button';
   revoke.className='btn btn-sm btn-outline-danger';revoke.textContent=text.remove;
   revoke.addEventListener('click',async()=>{
    if(busy||selected!==reference||!valid()||!window.confirm(text.revoke))return;
    busy=true;revoke.disabled=true;
    try{
     await api('/api/lms/certificate-sharing/grants/'+encodeURIComponent(grant.reference)+'/revoke',
      'POST',{rowVersion:grant.rowVersion});
     if(selected===reference)await load(reference);
    }catch(e){error(e.message);}
    finally{busy=false;revoke.disabled=false;}
   });
   line.append(label,revoke);list.append(line);
  }
 }catch(e){if(seq===generation)error(e.message);}
}
function clearSharing(){
 generation++;selected='';busy=false;
 select.replaceChildren();list.replaceChildren();button.disabled=true;
}
root.addEventListener('lms-certificate-cleared',clearSharing);
const sessionObserver=new MutationObserver(()=>{
 if(!valid()){clearSharing();sessionObserver.disconnect();}
});
sessionObserver.observe(document.body,{attributes:true,
 attributeFilter:['data-auth-session-reference','data-auth-tenant-id']});
window.addEventListener('pagehide',clearSharing);
root.addEventListener('lms-certificate-selected',e=>{
 const reference=e.detail?.reference;
 if(!/^[0-9a-f-]{36}$/i.test(reference||''))return;
 selected=reference;load(reference);
});
button.addEventListener('click',async()=>{
 if(busy||!selected||!select.value||!valid()||!window.confirm(text.share))return;
 const reference=selected;busy=true;button.disabled=true;
 try{
  await api('/api/lms/certificate-sharing/grants','POST',{
   certificateReference:reference,guardianLinkId:Number(select.value),clientRequestId:crypto.randomUUID()
  });
  if(selected===reference)await load(reference);
 }catch(e){error(e.message);}
 finally{busy=false;if(selected===reference&&select.value)button.disabled=false;}
});
})();
