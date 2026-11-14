(() => {
"use strict";
const root = document.getElementById("platformFeatureAdministration");
if (!root) return;
const byId = id => document.getElementById(id);
const bn = root.dataset.bangla === "true";
const t = (en, bangla) => bn ? bangla : en;
const rows = byId("featureRows"), status = byId("featureMessage"), retry = byId("featureRetry");
const editor = byId("featureEditor"), form = byId("featureForm");
const name = byId("featureEditName"), description = byId("featureEditDescription");
const active = byId("featureEditActive"), save = byId("featureSave");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let selected = null, requestId = null, busy = false, sequence = 0;
const cell = (tr, value) => {
    const td = document.createElement("td");
    td.textContent = String(value ?? "");
    tr.appendChild(td);
    return td;
};
function closeEditor() {
    if (busy) return;
    selected = null; requestId = null; editor.hidden = true;
    form.reset();
}
async function api(route, method = "GET", body) {
    const headers = { Accept: "application/json" };
    const options = { method, credentials: "same-origin", cache: "no-store", headers };
    if (method !== "GET") {
        if (!token) throw new Error(t("Security token unavailable. Reload this page.", "নিরাপত্তা টোকেন নেই। পৃষ্ঠা পুনরায় খুলুন।"));
        headers["Content-Type"] = "application/json";
        headers["RequestVerificationToken"] = token;
        options.body = JSON.stringify(body);
    }
    const response = await fetch(route, options);
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
    byId("featureEditCode").textContent = item.code;
    name.value = item.name;
    description.value = item.description ?? "";
    active.checked = !!item.isActive;
    editor.hidden = false;
    name.focus();
}
function render(items) {
    rows.replaceChildren();
    if (!Array.isArray(items) || !items.length) {
        const tr = document.createElement("tr");
        cell(tr, t("No feature definitions found.", "কোনো ফিচার পাওয়া যায়নি।")).colSpan = 5;
        rows.appendChild(tr);
        return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.code); cell(tr, item.name); cell(tr, item.description);
        cell(tr, item.isActive ? t("Available", "উপলব্ধ") : t("Unavailable", "অনুপলব্ধ"));
        const action = cell(tr, "");
        const button = document.createElement("button");
        button.type = "button"; button.className = "btn btn-outline-primary btn-sm";
        button.textContent = t("Edit", "সম্পাদনা");
        button.disabled = !item.rowVersion;
        button.addEventListener("click", () => edit(item));
        action.appendChild(button);
        rows.appendChild(tr);
    }
}
async function load() {
    const ticket = ++sequence;
    retry.disabled = true;
    try {
        const items = await api("/api/platform/catalog/features");
        if (ticket !== sequence) return false;
        render(items);
        status.textContent = "";
        retry.hidden = true;
        return true;
    } catch (error) {
        if (ticket !== sequence) return false;
        status.textContent = error.message || t("Unable to load features.", "ফিচার আনা যায়নি।");
        retry.hidden = false;
        return false;
    } finally { if (ticket === sequence && !busy) retry.disabled = false; }
}
form.addEventListener("submit", async event => {
    event.preventDefault();
    if (busy || !selected || !form.reportValidity()) return;
    const featureName = name.value.trim();
    if (!featureName) { name.setCustomValidity(t("Enter a feature name.", "ফিচারের নাম লিখুন।")); name.reportValidity(); return; }
    if (!confirm(t("Save feature settings for future plans? Existing assignments remain unchanged.",
        "ভবিষ্যৎ প্ল্যানের ফিচার সেটিংস সংরক্ষণ করবেন? পুরোনো প্ল্যান অপরিবর্তিত থাকবে।"))) return;
    busy = true; save.disabled = true; retry.disabled = true;
    try {
        await api("/api/platform/catalog/features/" + encodeURIComponent(selected.id) + "/configuration", "POST", {
            requestId, rowVersion: selected.rowVersion,
            name: featureName, description: description.value.trim(), isActive: active.checked
        });
        selected = null; requestId = null; editor.hidden = true; form.reset();
        const fresh = await load();
        status.textContent = fresh ? t("Feature updated.", "ফিচার সংরক্ষিত হয়েছে।")
            : t("Saved, but list reload failed. Try again.", "সংরক্ষণ হয়েছে, কিন্তু তালিকা আনা যায়নি। আবার চেষ্টা করুন।");
    } catch (error) {
        if (error.status === 409) {
            selected = null; requestId = null; editor.hidden = true;
            await load();
        }
        status.textContent = error.message + (error.status === 409
            ? t(" Review the latest row before editing again.", "আবার সম্পাদনার আগে সর্বশেষ তথ্য দেখুন।") : "");
    } finally { busy = false; save.disabled = false; retry.disabled = false; }
});
name.addEventListener("input", () => name.setCustomValidity(""));
form.addEventListener("input", () => { if (!busy && selected) requestId = crypto.randomUUID(); });
form.addEventListener("change", () => { if (!busy && selected) requestId = crypto.randomUUID(); });
byId("featureCancel").addEventListener("click", closeEditor);
retry.addEventListener("click", () => { if (!busy) load(); });
load();
})();
