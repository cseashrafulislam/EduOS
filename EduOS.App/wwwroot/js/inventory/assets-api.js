import { api as inventoryApi } from './movements-api.js';
export { el, td } from './movements-api.js';
export const api = (path, method, body) => inventoryApi('assets' + path, method, body);
export const catalog = path => inventoryApi('catalog' + path);
export const root = document.getElementById('assetWorkspace');
export const canManage = root?.dataset.canManage === 'true';
export const canCategory = root?.dataset.canCategory === 'true';
const pending = new Map();
export function requestKey(name) {
    if (!pending.has(name)) pending.set(name, crypto.randomUUID());
    return pending.get(name);
}
export function clearKey(name) { pending.delete(name); }
export function notice(message, success = false) {
    const box = document.getElementById('assetAlert');
    box.className = 'alert alert-' + (success ? 'success' : 'danger');
    box.textContent = message;
}
export function empty(id, span, message) {
    const row = document.createElement('tr'), cell = document.createElement('td');
    cell.colSpan = span; cell.textContent = message; row.append(cell);
    document.getElementById(id).replaceChildren(row);
}
export function addButton(cell, label, onClick) {
    const button = document.createElement('button');
    button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
    button.textContent = label; button.addEventListener('click', onClick); cell.append(button);
}
export const dateToday = () => new Date().toISOString().slice(0, 10);
export function money(value) {
    if (value === '') return null;
    if (!/^(0|[1-9]\d{0,8})(\.\d{1,2})?$/.test(String(value)))
        throw new Error('Enter a valid BDT amount (up to 2 decimals).');
    return Number(value);
}
