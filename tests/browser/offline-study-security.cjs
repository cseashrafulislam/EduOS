'use strict';
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');

const script = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/learning/offline-library.js');
const passphrase = 'Correct horse battery 2026!';
const reference = '11111111-2222-4333-8444-555555555551';

function fixture(language) {
    return '<!doctype html><html lang="' + language + '"><body>' +
        '<main id="offlineStudyReader">' +
        '<div id="offlineReaderList"></div>' +
        '<div id="offlineReaderStatus" role="status" aria-live="polite"></div>' +
        '<input id="offlineReaderPassphrase" type="password" aria-label="Passphrase">' +
        '<button id="offlineReaderClear" type="button">Clear</button>' +
        '<div id="offlineReaderContent"></div></main></body></html>';
}

async function seed(page) {
    await page.evaluate(async ({ reference, passphrase }) => {
        const bytes = Uint8Array.from(atob(
            'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL/nwAAAABJRU5ErkJggg=='),
        c => c.charCodeAt(0));
        const salt = crypto.getRandomValues(new Uint8Array(16));
        const passwordKey = await crypto.subtle.importKey('raw',
            new TextEncoder().encode(passphrase), 'PBKDF2', false, ['deriveKey']);
        const key = await crypto.subtle.deriveKey({
            name: 'PBKDF2', salt, iterations: 210000, hash: 'SHA-256'
        }, passwordKey, { name: 'AES-GCM', length: 256 }, false, ['encrypt']);
        const seal = async content => {
            const iv = crypto.getRandomValues(new Uint8Array(12));
            return { iv: Array.from(iv), body: await crypto.subtle.encrypt({
                name: 'AES-GCM', iv
            }, key, content) };
        };
        const sha256 = Array.from(new Uint8Array(await crypto.subtle.digest(
            'SHA-256', bytes)), b => b.toString(16).padStart(2, '0')).join('').toUpperCase();
        const id = '101:7:' + reference.toLowerCase();
        const record = {
            id, account: '101:7', reference, salt: Array.from(salt),
            sha256, sizeBytes: bytes.length, contentType: 'image/png',
            expiresAt: Date.now() + 3600000,
            check: await seal(new TextEncoder().encode('EduOS offline study v1')),
            title: await seal(new TextEncoder().encode('Licensed worksheet')),
            parts: [await seal(bytes)], complete: true,
            progress: null, progressVersion: null, progressPage: 1
        };
        const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('eduos-offline-study-v1', 1);
            request.onupgradeneeded = () => request.result.createObjectStore(
                'resources', { keyPath: 'id' });
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        await new Promise((resolve, reject) => {
            const tx = db.transaction('resources', 'readwrite');
            tx.objectStore('resources').put(record);
            tx.oncomplete = resolve; tx.onerror = () => reject(tx.error);
        });
        db.close();
        window.__offlineFixtureId = id;
        window.dispatchEvent(new Event('visibilitychange'));
    }, { reference, passphrase });
    await page.locator('#offlineReaderList button').waitFor();
}

async function countSaved(page) {
    return page.evaluate(async () => {
        const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('eduos-offline-study-v1', 1);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        const count = await new Promise((resolve, reject) => {
            const request = db.transaction('resources').objectStore('resources').count();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        db.close();
        return count;
    });
}

async function setup(browser, language) {
    const page = await browser.newPage({ viewport: { width: 390, height: 780 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('http://localhost/**', route => route.fulfill({
        contentType: 'text/html', body: fixture(language)
    }));
    await page.goto('http://localhost/offline-study.html');
    await page.addScriptTag({ path: script });
    await seed(page);
    return { page, errors };
}

async function run() {
    const browser = await chromium.launch({
        headless: true, executablePath: process.env.EDUOS_CHROMIUM_PATH || undefined,
        args: ['--no-sandbox']
    });
    try {
        {
            const { page, errors } = await setup(browser, 'en-BD');
            try {
                await page.locator('#offlineReaderPassphrase').fill(passphrase);
                await page.locator('#offlineReaderList button').click();
                await page.locator('#offlineReaderContent img').waitFor();
                await page.route('http://localhost/api/**', route => route.fulfill({
                    status: 403, contentType: 'application/json',
                    body: '{"success":false,"message":"Revoked"}'
                }));
                await page.evaluate(() => window.dispatchEvent(new Event('online')));
                await page.waitForFunction(() =>
                    document.querySelector('#offlineReaderContent').children.length === 0);
                assert.equal(await countSaved(page), 0, 'revoked content remains in vault');
                assert.match(await page.locator('#offlineReaderStatus').textContent(), /revoked/);
                assert.deepEqual(errors, []);
            } finally { await page.close(); }
        }
        {
            const { page, errors } = await setup(browser, 'bn-BD');
            try {
                await page.locator('#offlineReaderPassphrase').fill(passphrase);
                await page.evaluate(() => {
                    document.querySelector('#offlineReaderList button').click();
                    document.querySelector('#offlineReaderClear').click();
                });
                await page.waitForFunction(() =>
                    document.querySelector('#offlineReaderList').textContent.includes('অফলাইন ফাইল নেই'));
                assert.equal(await page.locator('#offlineReaderContent img').count(), 0,
                    'in-flight decrypt restored a cleared reader');
                assert.equal(await countSaved(page), 0, 'cleared vault was repopulated');
                assert.deepEqual(errors, []);
            } finally { await page.close(); }
        }
    } finally { await browser.close(); }
}

run().catch(error => { console.error(error); process.exitCode = 1; });
