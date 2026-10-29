(() => {
'use strict';
const result=document.getElementById('result');
const button=document.getElementById('verify');
if(!result||!button)return;
const bn=(navigator.language||'').toLowerCase().startsWith('bn');
const message=(en,bd)=>bn?bd:en;
const token=new URLSearchParams(location.hash.slice(1)).get('token')||'';
if(!/^[A-Za-z0-9_-]{43}$/.test(token)){
 button.disabled=true;
 result.textContent=message('Invalid or missing verification link.','যাচাইয়ের লিংকটি সঠিক নয়।');
 return;
}
let busy=false;
async function verify(){
 if(busy)return;
 busy=true;button.disabled=true;
 result.textContent=message('Checking certificate…','সনদ যাচাই করা হচ্ছে…');
 const controller=new AbortController();
 const timer=setTimeout(()=>controller.abort(),12000);
 try{
  const response=await fetch('/api/public/lms-certificate-verification',{
   method:'POST',credentials:'omit',cache:'no-store',referrerPolicy:'no-referrer',
   headers:{'Content-Type':'application/json',Accept:'application/json'},
   body:JSON.stringify({token}),signal:controller.signal
  });
  const body=await response.json().catch(()=>null);
  if(!response.ok||!body?.success||body.data?.status!=='Valid')throw Error('unavailable');
  const item=body.data;
  const date=new Intl.DateTimeFormat(bn?'bn-BD':'en-BD',{dateStyle:'medium',timeZone:'UTC'})
   .format(new Date(item.issuedAt));
  result.textContent=message('VALID · Issued by: ','বৈধ · ইস্যুকারী: ')+
   item.issuerName+' · '+date+' · '+message('Revision ','সংস্করণ ')+item.revision;
 }catch{
  result.textContent=message('Verification unavailable or link expired.','যাচাই সম্ভব নয় অথবা লিংকের মেয়াদ শেষ।');
 }finally{clearTimeout(timer);busy=false;button.disabled=false;}
}
button.addEventListener('click',verify);
verify();
})();
