import { el, canManage, canCategory } from './assets-api.js';

const create = (id, enabled) => enabled ? globalThis.EduOSFormGuard.create(el(id)) : null;
export const registerGuard = create('assetForm', canManage);
export const assignmentGuard = create('assetAssignForm', canManage);
export const transferGuard = create('assetTransferForm', canManage);
export const maintenanceGuard = create('assetMaintenanceForm', canManage);
export const categoryGuard = create('assetCategoryForm', canCategory);

export function confirmDraftDiscard(guards) {
    const dirty = guards.filter(guard => guard?.isDirty());
    if (!dirty.length) return true;
    if (dirty.length === 1) return dirty[0].confirmDiscard();
    return window.confirm(document.documentElement.lang?.toLowerCase().startsWith('bn') ?
        'এই সম্পদের অসংরক্ষিত ফর্মের পরিবর্তন বাদ দেবেন?' :
        'Discard unsaved asset form changes?');
}

export function resetGuardedForm(id, guard) {
    el(id).reset();
    guard?.markClean();
}
