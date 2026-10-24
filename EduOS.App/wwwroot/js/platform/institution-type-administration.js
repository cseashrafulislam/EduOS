(() => {
"use strict";
const root = document.getElementById("institutionTypeAdministration");
if (!root) return;
const rows = document.getElementById("catalogRows");
const status = document.getElementById("catalogMessage");
const refresh = document.getElementById("catalogRefresh");
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
let busy = false, selected = null, requestId = null;
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
        const error = new Error(payload?.message || "Request failed.");
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
        const tr = document.createElement("tr"); cell(tr, "No institution types found.").colSpan = 5; rows.append(tr); return;
    }
    for (const item of items) {
        const tr = document.createElement("tr");
        cell(tr, item.name); cell(tr, item.code); cell(tr, item.academicCycleType);
        cell(tr, item.isActive ? "Available" : "Hidden");
        const action = cell(tr, "");
        const editButton = document.createElement("button");
        editButton.type = "button"; editButton.className = "btn btn-outline-primary btn-sm me-2";
        editButton.textContent = "Edit"; editButton.disabled = !item.rowVersion;
        editButton.addEventListener("click", () => edit(item));
        action.append(editButton);
        const button = document.createElement("button");
        button.type = "button"; button.className = "btn btn-outline-secondary btn-sm";
        button.textContent = item.isActive ? "Hide from signup" : "Enable signup";
        button.disabled = !item.rowVersion;
        button.addEventListener("click", async () => {
            if (busy || !confirm((item.isActive ? "Hide " : "Enable ") + item.name + " for new registrations?")) return;
            busy = true; button.disabled = true; refresh.disabled = true;
            try {
                await api("/api/platform/catalog/institution-types/" + encodeURIComponent(item.id) + "/activation", "POST", {
                    requestId: crypto.randomUUID(), isActive: !item.isActive, rowVersion: item.rowVersion
                });
                selected = null; editor.hidden = true;
                await load();
                status.textContent = "Registration availability updated.";
            } catch (error) {
                status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
            } finally { busy = false; refresh.disabled = false; }
        });
        action.append(button); rows.append(tr);
    }
}
async function load() {
    refresh.disabled = true;
    try { render(await api("/api/platform/catalog/institution-types")); status.textContent = "Catalog loaded."; }
    catch (error) { status.textContent = error.message; }
    finally { if (!busy) refresh.disabled = false; }
}
form.addEventListener("submit", async event => {
    event.preventDefault();
    if (busy || !selected || !form.reportValidity()) return;
    if (!confirm("Save institution type defaults for future registrations?")) return;
    busy = true; save.disabled = true; refresh.disabled = true;
    try {
        await api("/api/platform/catalog/institution-types/" + encodeURIComponent(selected.id) + "/metadata", "POST", {
            requestId, rowVersion: selected.rowVersion,
            name: name.value.trim(), nameBangla: bangla.value.trim(),
            description: description.value.trim(), academicCycleType: cycle.value,
            displayOrder: Number(order.value)
        });
        selected = null; requestId = null; editor.hidden = true;
        await load(); status.textContent = "Institution type metadata updated.";
    } catch (error) {
        status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
    } finally { busy = false; save.disabled = false; refresh.disabled = false; }
});
document.getElementById("catalogCancel").addEventListener("click", () => {
    if (!busy) { selected = null; requestId = null; editor.hidden = true; }
});
refresh.addEventListener("click", () => {
    if (!busy) { selected = null; requestId = null; editor.hidden = true; load(); }
});
load();
})();
