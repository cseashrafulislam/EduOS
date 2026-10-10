export const state = { forms: [], tests: [], test: null, page: 1, pages: 1 };
export const el = id => document.getElementById(id);
export const val = id => el(id)?.value?.trim() || '';
export function notice(text, ok = false) {
 const box = el('assessmentAlert');
 box.className = 'alert alert-' + (ok ? 'success' : 'danger');
 box.textContent = text; box.focus();
}
export async function api(url, method = 'GET', body) {
 const headers = { Accept: 'application/json' };
 const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
 if (body !== undefined) headers['Content-Type'] = 'application/json';
 if (method !== 'GET' && token) headers.RequestVerificationToken = token;
 const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
  ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
 const data = await response.json().catch(() => null);
 if (!response.ok || !data?.success) throw Error(data?.message || 'Request failed. Reload and retry.');
 return data.data;
}
export function option(label, id) {
 const node = document.createElement('option'); node.textContent = label; node.value = String(id); return node;
}
export function cell(value) {
 const node = document.createElement('td'); node.textContent = String(value ?? ''); return node;
}
