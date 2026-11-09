export const label=(stage,bn)=>({issue:bn?'অফার প্রদান':'Issue offer',accept:bn?'স্বাক্ষর যাচাই':'Verify signed acceptance',enroll:bn?'শিক্ষার্থী ভর্তি':'Enroll student',expired:bn?'অফারের মেয়াদ শেষ':'Offer expired',review:bn?'আবেদন পর্যালোচনা':'Review application'})[stage]||'Review';
export function nextStage(applicantState,offerState,expiresAt,now=Date.now()){
 if(Number(applicantState)!==6)return 'review';
 if(Number(offerState)===3)return 'enroll';
 if(Number(offerState)===2)return !expiresAt||Date.parse(expiresAt)<=now?'expired':'accept';
 return Number(offerState)===0?'issue':'review';
}
export function validateIssue(batch,expiry,now=Date.now()){
 return Number.isSafeInteger(Number(batch))&&Number(batch)>0&&expiry&&Number.isFinite(Date.parse(expiry))&&Date.parse(expiry)>now;
}
export function validateRoll(batch,roll){
 return Number.isSafeInteger(Number(batch))&&Number(batch)>0&&typeof roll==='string'&&roll.trim().length>0&&roll.trim().length<=50;
}
