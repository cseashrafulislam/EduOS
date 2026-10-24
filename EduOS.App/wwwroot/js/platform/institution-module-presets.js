(() => {
"use strict";
const root = document.getElementById("institutionModulePresets");
if (!root) return;
const typeSelect = document.getElementById("presetType");
const rows = document.getElementById("presetRows");
const status = document.getElementById("presetMessage");
const refresh = document.getElementById("presetRefresh");
const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
    document.querySelector('meta[name="request-verification-token"]')?.content || "";
let current = null;
let busy = false;
const cell = (tr, value) => {
    const td = document.createElement("td");
    td.textContent = value ?? "";
    tr.append(td);
    return td;
};
async function api(path, method = "GET", body) {
    const headers = { Accept: "application/json" };
    const options = { method, headers, credentials: "same-origin", cache: "no-store" };
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
function checkbox(checked, disabled) {
    const input = document.createElement("input");
    input.type = "checkbox";
    input.className = "form-check-input";
    input.checked = checked;
    input.disabled = disabled;
    return input;
}
function render(data) {
    rows.replaceChildren();
    for (const item of data.modules || []) {
        const tr = document.createElement("tr");
        cell(tr, item.moduleName + " (" + item.moduleCode + ")");
        cell(tr, item.isCore ? "Yes" : "No");
        cell(tr, item.isActive ? "Yes" : "No");
        const requiredCell = cell(tr, "");
        const enabledCell = cell(tr, "");
        const locked = item.isCore || !item.isActive;
        const required = checkbox(item.isRequired, locked);
        const enabled = checkbox(item.isDefaultEnabled, locked);
        required.setAttribute("aria-label", "Required " + item.moduleName);
        enabled.setAttribute("aria-label", "Default enabled " + item.moduleName);
        required.addEventListener("change", () => { if (required.checked) enabled.checked = true; });
        enabled.addEventListener("change", () => { if (!enabled.checked) required.checked = false; });
        requiredCell.append(required);
        enabledCell.append(enabled);
        const action = cell(tr, "");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-outline-primary btn-sm";
        button.textContent = "Save";
        button.disabled = locked;
        button.addEventListener("click", async () => {
            if (busy || !confirm("Save onboarding preset? Existing institution selections remain unchanged.")) return;
            busy = true;
            button.disabled = true;
            refresh.disabled = true;
            typeSelect.disabled = true;
            const requestId = crypto.randomUUID();
            try {
                await api("/api/platform/catalog/institution-module-presets/" +
                    encodeURIComponent(data.institutionTypeId) + "/modules/" + encodeURIComponent(item.moduleId),
                    "POST", {
                        requestId, institutionTypeRowVersion: data.institutionTypeRowVersion,
                        rowVersion: item.rowVersion, isRequired: required.checked,
                        isDefaultEnabled: enabled.checked
                    });
                status.textContent = "Preset saved.";
                await load();
            } catch (error) {
                status.textContent = error.message + (error.status === 409 ? " Refresh and retry." : "");
            } finally {
                busy = false;
                refresh.disabled = false;
                typeSelect.disabled = false;
                button.disabled = locked;
            }
        });
        action.append(button);
        rows.append(tr);
    }
    if (!data.modules?.length) {
        const tr = document.createElement("tr");
        cell(tr, "No modules found.").colSpan = 6;
        rows.append(tr);
    }
}
async function load() {
    if (busy || !typeSelect.value) {
        rows.replaceChildren();
        const tr = document.createElement("tr");
        cell(tr, "Choose an institution type.").colSpan = 6;
        rows.append(tr);
        return;
    }
    refresh.disabled = true;
    try {
        current = await api("/api/platform/catalog/institution-module-presets/" + encodeURIComponent(typeSelect.value));
        render(current);
        status.textContent = "Preset loaded for " + current.institutionTypeName + ".";
    } catch (error) {
        status.textContent = error.message;
    } finally { refresh.disabled = false; }
}
async function initialize() {
    try {
        const types = await api("/api/platform/catalog/institution-types");
        for (const type of types || []) {
            const option = document.createElement("option");
            option.value = type.id;
            option.textContent = type.name + " (" + type.code + ")";
            typeSelect.append(option);
        }
        if (typeSelect.options.length > 1) {
            typeSelect.selectedIndex = 1;
            await load();
        }
    } catch (error) { status.textContent = error.message; }
}
typeSelect.addEventListener("change", load);
refresh.addEventListener("click", load);
initialize();
})();
