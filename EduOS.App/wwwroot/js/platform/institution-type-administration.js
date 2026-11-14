(() => {
"use strict";
const root = document.getElementById("institutionTypeAdministration");
if (!root) return;
const rows = document.getElementById("catalogRows");
const status = document.getElementById("catalogMessage");
const retry = document.getElementById("catalogRetry");
const bn = root.dataset.bangla === "true";
const t = (en, banglaText) => bn ? banglaText : en;
const editor = document.getElementById("catalogEditor");
const form = document.getElementById("catalogForm");
const name = document.getElementById("catalogEditName");
const bangla = document.getElementById("catalogEditNameBangla");
const description = document.getElementById("catalogEditDescription");
const cycle = document.getElementById("catalogEditCycle");
const order = document.getElementById("catalogEditOrder");
const save = document.getElementById("catalogSave");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let busy = false, selected = null, requestId = null, loadSequence = 0;
const activationRequests = new Map();
const cell = (tr, value) => { const td = document.createElement("td"); td.textContent = value ?? ""; tr.append(td); return td; };
async function api(path, method = "GET", body) {
    const headers = { Accept: "application/json" };
    const options = { method, credentials: "same-origin", cache: "no-store", headers };
    if (method !== "GET") {
        headers["Content-Type"] = "application/json";
        headers["RequestVerificationToken"] = token;
        options.body = JSON.stringify(body);
    }
    const response = await fetch(path, options);
    const payload = await response.json().catch(() => null);
    if (!response.ok || !payload?.success) {
        const error = new Error(payload?.message || t("Request failed.", "অনুরোধটি সম্পন্ন হয়নি।"));
        error.status = response.status;
        throw error;
    }
    return payload.data;
}
function edit(item) {
    if (busy) return;
    selected = item;
    requestId = crypto.randomUUID();
    document.getElementById("catalogEditCode").textContent = item.code;
    name.value = item.name;
    bangla.value = item.nameBangla ?? "";
    description.value = item.description ?? "";
    cycle.value = item.academicCycleType;
    order.value = item.displayOrder;
    editor.hidden = false;
    name.focus();
}
function render(items) {
    rows.replaceChildren();
    if (!Array.isArray(items) || !items.length) {
        const tr = document.createElement("tr"); cell(tr, t("No institution types found.", "প্রতিষ্ঠানের ধরন পাওয়া যায়নি।")).colSpan = 5; rows.append(tr); return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.name); cell(tr, item.code); cell(tr, item.academicCycleType);
        cell(tr, item.isActive ? t("Available", "উপলব্ধ") : t("Hidden", "লুকানো"));
        const action = cell(tr, "");
        const editButton = document.createElement("button");
        editButton.type = "button"; editButton.className = "btn btn-outline-primary btn-sm me-2";
        editButton.textContent = t("Edit", "সম্পাদনা"); editButton.disabled = !item.rowVersion;
        editButton.addEventListener("click", () => edit(item));
        action.append(editButton);
        const button = document.createElement("button");
        button.type = "button"; button.className = "btn btn-outline-secondary btn-sm";
        button.textContent = item.isActive ? t("Hide from signup", "নিবন্ধন থেকে লুকান") : t("Enable signup", "নিবন্ধনে চালু করুন");
        button.disabled = !item.rowVersion;
        button.addEventListener("click", async () => {
            if (busy || !confirm(t((item.isActive ? "Hide " : "Enable ") + item.name + " for new registrations?", (item.isActive ? "নিবন্ধন থেকে লুকাবেন: " : "নিবন্ধনে চালু করবেন: ") + item.name + "?"))) return;
            const activationKey = item.id + ":" + item.rowVersion + ":" + String(!item.isActive);
            const activationRequestId = activationRequests.get(activationKey) || crypto.randomUUID();
            activationRequests.set(activationKey, activationRequestId);
            busy = true; button.disabled = true; retry.disabled = true;
            try {
                await api("/api/platform/catalog/institution-types/" + encodeURIComponent(item.id) + "/activation", "POST", {
                    requestId: activationRequestId, isActive: !item.isActive, rowVersion: item.rowVersion
                });
                activationRequests.delete(activationKey);
                selected = null; editor.hidden = true;
                await load();
                status.textContent = t("Registration availability updated.", "নিবন্ধনের উপলব্ধতা সংরক্ষিত হয়েছে।");
            } catch (error) {
                if (error.status === 409) { activationRequests.delete(activationKey); await load(); }
                status.textContent = error.message + (error.status === 409 ? t(" Review the latest record and retry.", "সর্বশেষ তথ্য দেখে আবার চেষ্টা করুন।") : "");
            } finally { busy = false; retry.disabled = false; }
        });
        action.append(button); rows.append(tr);
    }
}
async function load() {
    const ticket = ++loadSequence;
    retry.disabled = true;
    try {
        const data = await api("/api/platform/catalog/institution-types");
        if (ticket !== loadSequence) return;
        render(data);
        retry.hidden = true;
        status.textContent = "";
    } catch (error) {
        if (ticket !== loadSequence) return;
        status.textContent = error.message;
        retry.hidden = false;
    } finally { if (ticket === loadSequence && !busy) retry.disabled = false; }
}
form.addEventListener("submit", async event => {
    event.preventDefault();
    if (busy || !selected || !form.reportValidity()) return;
    if (!token) { status.textContent = t("Security token is unavailable. Reload the page.", "নিরাপত্তা টোকেন নেই। পৃষ্ঠা পুনরায় খুলুন।"); return; }
    if (!confirm(t("Save institution type defaults for future registrations?", "পরবর্তী নিবন্ধনের ডিফল্ট তথ্য সংরক্ষণ করবেন?"))) return;
    busy = true; save.disabled = true; retry.disabled = true;
    try {
        await api("/api/platform/catalog/institution-types/" + encodeURIComponent(selected.id) + "/metadata", "POST", {
            requestId, rowVersion: selected.rowVersion,
            name: name.value.trim(), nameBangla: bangla.value.trim(),
            description: description.value.trim(), academicCycleType: cycle.value,
            displayOrder: Number(order.value)
        });
        selected = null; requestId = null; editor.hidden = true;
        await load(); status.textContent = t("Institution type metadata updated.", "প্রতিষ্ঠানের ধরন সংরক্ষিত হয়েছে।");
    } catch (error) {
        if (error.status === 409) { selected = null; requestId = null; editor.hidden = true; await load(); }
        status.textContent = error.message + (error.status === 409 ? t(" Review the latest record before editing again.", "আবার সম্পাদনার আগে সর্বশেষ তথ্য দেখুন।") : "");
    } finally { busy = false; save.disabled = false; retry.disabled = false; }
});
document.getElementById("catalogCancel").addEventListener("click", () => {
    if (!busy) { selected = null; requestId = null; editor.hidden = true; }
});
form.addEventListener("input", () => { if (!busy && selected) requestId = crypto.randomUUID(); });
form.addEventListener("change", () => { if (!busy && selected) requestId = crypto.randomUUID(); });
retry.addEventListener("click", () => { if (!busy) load(); });
load();
})();
