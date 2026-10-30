(() => {
'use strict';
const root = document.getElementById('libraryOverdueNoticeApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
const words = (en, bd) => bn ? bd : en;
const initialSession = document.body?.dataset.authSessionReference || '';
const initialTenant = document.body?.dataset.authTenantId || '';
const active = new Set();
const valid = () => /^[0-9A-F]{64}$/.test(initialSession) &&
 document.body?.dataset.authSessionReference === initialSession &&
 document.body?.dataset.authTenantId === initialTenant &&
 window.EduOSSessionTransitionPending?.() !== true;
let preview = null, requestId = null, previewEpoch = 0;
function message(text, success = false) {
 if (!valid()) return;
 const node = el('libraryNoticeMessage');
 node.className = 'alert alert-' + (success ? 'success' : 'danger') + ' mt-2';
 node.textContent = text;
}
function scrub() {
 for (const controller of active) controller.abort();
 active.clear(); previewEpoch++; preview = null; requestId = null;
 el('libraryNoticeRows').replaceChildren();
 el('libraryNoticeCount').textContent = '';
 el('libraryNoticeSend').disabled = true;
 el('libraryNoticeMessage').className = 'd-none';
 el('libraryNoticeMessage').textContent = '';
}
function campus() {
 const value = el('libraryOverdueCampus').value.trim();
 if (!/^[1-9]\d*$/.test(value)) throw Error(words('Select a campus ID first.', 'প্রথমে ক্যাম্পাস আইডি নির্বাচন করুন।'));
 return Number(value);
}
async function api(url, options = {}) {
 if (!valid()) throw Error(words('Session changed. Reload the page.', 'সেশন পরিবর্তিত হয়েছে। পৃষ্ঠা পুনরায় খুলুন।'));
 const controller = new AbortController();
 active.add(controller);
 const headers = { Accept: 'application/json', ...(options.headers || {}) };
 if ((options.method || 'GET') !== 'GET') {
  const token = document.querySelector('meta[name="request-verification-token"]')?.content;
  if (!token) throw Error(words('Security token missing. Reload the page.', 'নিরাপত্তা টোকেন নেই। পৃষ্ঠা পুনরায় খুলুন।'));
  headers.RequestVerificationToken = token;
 }
 try {
  const response = await fetch(url, { ...options, headers, signal: controller.signal,
   cache: 'no-store', credentials: 'same-origin' });
  if (!valid()) throw Error(words('Session changed.', 'সেশন পরিবর্তিত হয়েছে।'));
  const payload = await response.json().catch(() => null);
  if (!valid()) throw Error(words('Session changed.', 'সেশন পরিবর্তিত হয়েছে।'));
  if (!response.ok || !payload?.success)
   throw Error(payload?.message || words('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
  return payload.data;
 } finally { active.delete(controller); }
}
function cell(row, value) {
 const td = document.createElement('td');
 td.textContent = value == null ? '—' : String(value);
 row.append(td);
}
async function loadPreview() {
 const id = campus();
 const epoch = ++previewEpoch;
 el('libraryNoticeSend').disabled = true;
 const data = await api('/api/library/overdue-notices/preview?campusId=' + encodeURIComponent(id));
 if (epoch !== previewEpoch || !valid() ||
     el('libraryOverdueCampus').value.trim() !== String(id)) return;
 // A lost send acknowledgement must be retried with the same request ID.
 // Only a different campus/date or an acknowledged send may reset the key.
 if (!preview || preview.campusId !== data.campusId || preview.asOfDate !== data.asOfDate)
  requestId = null;
 preview = data;
 el('libraryNoticeRows').replaceChildren();
 for (const item of data.nextRecipients || []) {
  const row = document.createElement('tr');
  [item.studentCode, item.bookTitle, item.dueDate, item.daysOverdue].forEach(v => cell(row, v));
  el('libraryNoticeRows').append(row);
 }
 el('libraryNoticeCount').textContent = String(data.pendingCount) + ' ' +
  words('pending recipient(s); up to 50 per batch', 'জন অপেক্ষমাণ; প্রতি ব্যাচে সর্বোচ্চ ৫০ জন');
 el('libraryNoticeSend').disabled = !(data.pendingCount > 0);
}
el('libraryNoticePreview').addEventListener('click', async () => {
 const button = el('libraryNoticePreview');
 button.disabled = true;
 try { await loadPreview(); }
 catch (error) { if (valid()) message(error.message); }
 finally { button.disabled = false; }
});
el('libraryNoticeSend').addEventListener('click', async () => {
 const button = el('libraryNoticeSend');
 if (!preview || preview.campusId !== campus()) return;
 if (!window.confirm(words('Send in-app overdue reminders to the next 50 eligible borrowers?',
  'পরবর্তী সর্বোচ্চ ৫০ জন যোগ্য পাঠককে অ্যাপের মাধ্যমে স্মরণবার্তা পাঠাবেন?'))) return;
 button.disabled = true;
 requestId ||= crypto.randomUUID();
 try {
  const result = await api('/api/library/overdue-notices/send', {
   method: 'POST', headers: { 'Content-Type': 'application/json' },
   body: JSON.stringify({ clientRequestId: requestId, campusId: preview.campusId,
    asOfDate: preview.asOfDate })
  });
  if (!valid()) return;
  requestId = null;
  message(String(result.sentCount) + ' ' + words('reminder(s) recorded.', 'টি স্মরণবার্তা সংরক্ষিত হয়েছে।'), true);
  await loadPreview();
 } catch (error) {
  if (valid()) message(error.message);
 } finally { if (valid()) button.disabled = !(preview?.pendingCount > 0); }
});
el('libraryOverdueCampus').addEventListener('input', scrub);
const observer = new MutationObserver(() => { if (!valid()) scrub(); });
observer.observe(document.body, { attributes: true,
 attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id'] });
window.addEventListener('pagehide', scrub);
window.addEventListener('focus', () => { if (!valid()) scrub(); });
document.addEventListener('visibilitychange', () => { if (!valid()) scrub(); });
if (!valid()) scrub();
})();
