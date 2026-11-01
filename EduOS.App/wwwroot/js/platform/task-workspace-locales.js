/* Strictly static labels in the existing task workspaces. This does not
 * translate server-provided records, identifiers, validation, or API states.
 * The server-selected culture controls the whole shell; no browser language
 * override or persistent/tenant-independent setting is introduced. */
(() => {
    'use strict';
    if (!(document.documentElement.lang || '').toLowerCase().startsWith('bn')) return;
    const root = document.querySelector('#hostelWorkspace, #transportWorkspace, #inventoryMovements');
    if (!root) return;
    const labels = {
        'Find student': 'শিক্ষার্থী খুঁজুন',
        'Find': 'খুঁজুন',
        'Find available bed': 'খালি শয্যা খুঁজুন',
        'Student enrollment': 'শিক্ষার্থীর ভর্তি',
        'Unoccupied bed': 'খালি শয্যা',
        'Start date': 'শুরুর তারিখ',
        'Checkout date (optional)': 'ছাড়ার তারিখ (ঐচ্ছিক)',
        'End date (optional)': 'শেষের তারিখ (ঐচ্ছিক)',
        'Monthly rent (optional)': 'মাসিক ভাড়া (ঐচ্ছিক)',
        'Monthly fare (optional)': 'মাসিক ভাড়া (ঐচ্ছিক)',
        'Allocate a bed': 'শয্যা বরাদ্দ করুন',
        'My current allocation': 'আমার বর্তমান বরাদ্দ',
        'My hostel stay history': 'আমার আবাসিক ইতিহাস',
        'My current assignment': 'আমার বর্তমান বরাদ্দ',
        'Authorized student hostel stays': 'অনুমোদিত শিক্ষার্থীর আবাসিক অবস্থান',
        'Status': 'অবস্থা',
        'Student': 'শিক্ষার্থী',
        'Name': 'নাম',
        'Code': 'কোড',
        'Campus': 'ক্যাম্পাস',
        'Hostel': 'হোস্টেল',
        'Room': 'কক্ষ',
        'Bed': 'শয্যা',
        'Room / Bed': 'কক্ষ / শয্যা',
        'Start': 'শুরু',
        'End': 'শেষ',
        'Date': 'তারিখ',
        'Action': 'কার্যক্রম',
        'Active': 'সক্রিয়',
        'Inactive': 'নিষ্ক্রিয়',
        'Previous': 'আগের',
        'Next': 'পরের',
        'Next page': 'পরের পৃষ্ঠা',
        'Search': 'খুঁজুন',
        'Refresh': 'রিফ্রেশ',
        'Save': 'সংরক্ষণ',
        'Cancel': 'বাতিল',
        'Clear': 'মুছুন',
        'Print': 'প্রিন্ট',
        'Export page': 'পৃষ্ঠা রপ্তানি',
        'All': 'সব',
        'Available routes and vehicles': 'উপলব্ধ রুট ও গাড়ি',
        'Routes': 'রুট',
        'Vehicles': 'গাড়ি',
        'Route': 'রুট',
        'Vehicle': 'গাড়ি',
        'Route name': 'রুটের নাম',
        'Route code': 'রুট কোড',
        'Stops in travel order (one per line)': 'যাত্রার ক্রমানুসারে স্টপ (প্রতি লাইনে একটি)',
        'Route and ordered stop setup': 'রুট ও স্টপ সেটআপ',
        'Create route with stops': 'স্টপসহ রুট তৈরি',
        'Campus route inventory': 'ক্যাম্পাস রুট তালিকা',
        'All authorized campuses': 'সব অনুমোদিত ক্যাম্পাস',
        'Assign transport': 'পরিবহন বরাদ্দ',
        'Pickup stop': 'ওঠার স্টপ',
        'Drop stop': 'নামার স্টপ',
        'Search eligible students': 'যোগ্য শিক্ষার্থী খুঁজুন',
        'Select route': 'রুট বাছুন',
        'Select vehicle': 'গাড়ি বাছুন',
        'Assign': 'বরাদ্দ দিন',
        'Close': 'বন্ধ করুন',
        'Capacity': 'ধারণক্ষমতা',
        'Available': 'উপলব্ধ',
        'Occupied': 'দখলকৃত',
        'Occupied today': 'আজ দখলকৃত',
        'Create hostel room': 'হোস্টেল কক্ষ তৈরি',
        'Find facility': 'আবাসিক ভবন খুঁজুন',
        'Facility': 'ভবন',
        'Facility name': 'ভবনের নাম',
        'Room number': 'কক্ষ নম্বর',
        'Create room': 'কক্ষ তৈরি',
        'Create hostel bed': 'হোস্টেল শয্যা তৈরি',
        'Bed number': 'শয্যা নম্বর',
        'Create bed': 'শয্যা তৈরি',
        'Room inventory and controlled closure': 'কক্ষ তালিকা ও নিয়ন্ত্রিত বন্ধ',
        'Bed inventory and controlled closure': 'শয্যা তালিকা ও নিয়ন্ত্রিত বন্ধ',
        'Hostel facilities and controlled closure': 'হোস্টেল ভবন ও নিয়ন্ত্রিত বন্ধ',
        'Find room': 'কক্ষ খুঁজুন',
        'Correct room number': 'কক্ষ নম্বর সংশোধন',
        'Save changes': 'পরিবর্তন সংরক্ষণ',
        'More': 'আরও',
        'Room capacity': 'কক্ষ ধারণক্ষমতা',
        'Stock position': 'স্টক অবস্থা',
        'Item': 'পণ্য',
        'Quantity': 'পরিমাণ',
        'Location': 'স্থান',
        'Reference': 'রেফারেন্স',
        'Reason': 'কারণ',
        'Receive in-transit stock': 'পথের স্টক গ্রহণ',
        'Receive stock': 'স্টক গ্রহণ করুন',
        'Dispatch number or UUID': 'ডিসপ্যাচ নম্বর বা UUID'
    };
    const safe = value => Object.prototype.hasOwnProperty.call(labels, value) ? labels[value] : null;
    for (const node of root.querySelectorAll('label, h3, h4, th, button, option')) {
        // Never replace markup, child controls, interactive button icons or
        // dynamically generated records; exact static strings only.
        if (node.children.length) continue;
        const original = node.textContent.trim();
        const translated = safe(original);
        if (translated) node.textContent = translated;
    }
    for (const node of root.querySelectorAll('[placeholder], [aria-label]')) {
        for (const attribute of ['placeholder', 'aria-label']) {
            const original = node.getAttribute(attribute);
            const translated = original && safe(original.trim());
            if (translated) node.setAttribute(attribute, translated);
        }
    }
})();
