(() => {
"use strict";
const root = document.getElementById("subscriptionPlanAdministration");
if (!root) return;
const rows = document.getElementById("planRows");
const status = document.getElementById("planMessage");
const refresh = document.getElementById("planRefresh");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let busy = false;
let editing = null;
const editor = document.getElementById("planConfiguration");
const editorTitle = document.getElementById("planConfigurationTitle");
const editorForm = document.getElementById("planConfigurationForm");
const featureRows = document.getElementById("planFeatureRows");
const configFields = {
    monthlyPrice: "planMonthly", yearlyPrice: "planYearly", trialDays: "planTrial",
    maxStudents: "planStudents", maxEmployees: "planEmployees", maxUsers: "planUsers",
    maxCampuses: "planCampuses", maxStorageMb: "planStorage"
};
const cell = (tr, value) => { const td = document.createElement("td"); td.textContent = value; tr.append(td); return td; };
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
        const error = new Error(payload?.message || "Request failed.");
        error.status = response.status;
        throw error;
    }
    return payload.data;
}
async function change(item, isActive, isPublic) {
    if (busy || !confirm("Update availability for " + item.name + "? Existing subscriptions will not be changed.")) return;
    busy = true;
    refresh.disabled = true;
    try {
        await api("/api/platform/subscription-plans/" + encodeURIComponent(item.id) + "/availability", "POST", {
            requestId: crypto.randomUUID(), isActive, isPublic, rowVersion: item.rowVersion
        });
        status.textContent = "Plan availability updated.";
        await load();
    } catch (error) {
        status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
    } finally { busy = false; refresh.disabled = false; }
}
async function edit(item) {
    if (busy) return;
    busy = true;
    refresh.disabled = true;
    try {
        const config = await api("/api/platform/subscription-plans/" + encodeURIComponent(item.id) + "/configuration");
        editing = config;
        editorTitle.textContent = "Edit " + config.name + " (" + config.currencyCode + ")";
        for (const [key, id] of Object.entries(configFields)) document.getElementById(id).value = config[key];
        featureRows.replaceChildren();
        for (const feature of config.features) {
            const wrap = document.createElement("div"); wrap.className = "col-md-6";
            const box = document.createElement("div"); box.className = "border rounded p-2";
            const label = document.createElement("label"); label.className = "form-check-label d-block";
            const check = document.createElement("input"); check.type = "checkbox";
            check.className = "form-check-input me-2"; check.checked = feature.isEnabled;
            label.append(check, document.createTextNode(feature.name + " (" + feature.code + ")"));
            const limit = document.createElement("input"); limit.type = "number";
            limit.className = "form-control form-control-sm mt-2"; limit.min = "0"; limit.step = "1";
            limit.placeholder = "Optional limit"; limit.value = feature.limitValue ?? "";
            limit.disabled = !check.checked;
            check.addEventListener("change", () => { limit.disabled = !check.checked; if (!check.checked) limit.value = ""; });
            box.append(label, limit); wrap.append(box); featureRows.append(wrap);
            wrap.dataset.featureId = String(feature.featureId);
        }
        editor.hidden = false;
        editor.scrollIntoView({ behavior: "smooth", block: "nearest" });
    } catch (error) { status.textContent = error.message; }
    finally { busy = false; refresh.disabled = false; }
}
editorForm.addEventListener("submit", async event => {
    event.preventDefault();
    if (!editing || busy || !editorForm.reportValidity()) return;
    const request = { requestId: crypto.randomUUID(), rowVersion: editing.rowVersion, features: [] };
    for (const [key, id] of Object.entries(configFields)) request[key] = Number(document.getElementById(id).value);
    for (const wrap of featureRows.children) {
        const check = wrap.querySelector('input[type="checkbox"]');
        const limit = wrap.querySelector('input[type="number"]');
        request.features.push({ featureId: Number(wrap.dataset.featureId), isEnabled: check.checked,
            limitValue: check.checked && limit.value !== "" ? Number(limit.value) : null });
    }
    if (!confirm("Save plan pricing, limits and features? Existing subscription price snapshots remain unchanged.")) return;
    busy = true;
    document.getElementById("planSave").disabled = true;
    try {
        await api("/api/platform/subscription-plans/" + encodeURIComponent(editing.id) + "/configuration", "POST", request);
        editor.hidden = true; editing = null; status.textContent = "Plan configuration saved.";
        await load();
    } catch (error) { status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : ""); }
    finally { busy = false; document.getElementById("planSave").disabled = false; }
});
document.getElementById("planCancel").addEventListener("click", () => {
    if (!busy) { editor.hidden = true; editing = null; }
});
function render(items) {
    rows.replaceChildren();
    if (!Array.isArray(items) || !items.length) {
        const tr = document.createElement("tr"); const td = cell(tr, "No plans found."); td.colSpan = 7; rows.append(tr); return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.name); cell(tr, item.code);
        cell(tr, String(item.monthlyPrice) + " " + item.currencyCode);
        cell(tr, String(item.yearlyPrice) + " " + item.currencyCode);
        cell(tr, item.isActive ? "Accepting subscriptions" : "Inactive");
        cell(tr, item.isPublic ? "Visible" : "Hidden");
        const actions = cell(tr, "");
        const activeButton = document.createElement("button");
        activeButton.type = "button"; activeButton.className = "btn btn-outline-primary btn-sm me-2";
        activeButton.textContent = item.isActive ? "Deactivate" : "Activate";
        activeButton.disabled = !item.rowVersion;
        activeButton.addEventListener("click", () => change(item, !item.isActive, item.isPublic));
        const publicButton = document.createElement("button");
        publicButton.type = "button"; publicButton.className = "btn btn-outline-secondary btn-sm";
        publicButton.textContent = item.isPublic ? "Hide pricing" : "Publish pricing";
        publicButton.disabled = !item.rowVersion;
        publicButton.addEventListener("click", () => change(item, item.isActive, !item.isPublic));
        const editButton = document.createElement("button");
        editButton.type = "button"; editButton.className = "btn btn-outline-success btn-sm ms-2";
        editButton.textContent = "Edit pricing & features";
        editButton.addEventListener("click", () => edit(item));
        actions.append(activeButton, publicButton, editButton); rows.append(tr);
    }
}
async function load() {
    refresh.disabled = true;
    try { render(await api("/api/platform/subscription-plans")); status.textContent = "Plans loaded."; }
    catch (error) { status.textContent = error.message; }
    finally { if (!busy) refresh.disabled = false; }
}
refresh.addEventListener("click", () => { if (!busy) load(); });
load();
})();
