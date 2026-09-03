(() => {
    'use strict';

    const sidebar = document.getElementById('sidebarMenu');
    const sidebarToggle = document.querySelector('[data-sidebar-toggle], #btnSidebarToggle');
    const sidebarClosers = document.querySelectorAll('[data-sidebar-close]');
    const desktopMedia = window.matchMedia('(min-width: 992px)');
    let lastFocusedElement = null;

    function setSidebarOpen(isOpen) {
        if (!sidebar || !sidebarToggle) return;

        sidebar.classList.toggle('show', isOpen);
        document.body.classList.toggle('sidebar-open', isOpen);
        sidebarToggle.setAttribute('aria-expanded', String(isOpen));

        if (isOpen) {
            lastFocusedElement = document.activeElement;
            sidebar.querySelector('a, button')?.focus();
        } else if (lastFocusedElement instanceof HTMLElement) {
            lastFocusedElement.focus();
        }
    }

    sidebarToggle?.addEventListener('click', () => {
        setSidebarOpen(!sidebar?.classList.contains('show'));
    });

    sidebarClosers.forEach(element => {
        element.addEventListener('click', () => setSidebarOpen(false));
    });

    sidebar?.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            if (!desktopMedia.matches) setSidebarOpen(false);
        });
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && sidebar?.classList.contains('show')) {
            setSidebarOpen(false);
        }
    });

    desktopMedia.addEventListener('change', event => {
        if (event.matches) setSidebarOpen(false);
    });

    document.querySelector('[data-language-selector]')?.addEventListener('change', event => {
        event.currentTarget.form?.requestSubmit();
    });

    let installPrompt = null;
    const installButton = document.querySelector('[data-install-app]');

    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault();
        installPrompt = event;
        if (installButton) installButton.hidden = false;
    });

    installButton?.addEventListener('click', async () => {
        if (!installPrompt) return;
        installPrompt.prompt();
        await installPrompt.userChoice;
        installPrompt = null;
        installButton.hidden = true;
    });

    window.addEventListener('appinstalled', () => {
        installPrompt = null;
        if (installButton) installButton.hidden = true;
    });

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', () => {
            navigator.serviceWorker.register('/service-worker.js', { scope: '/' })
                .catch(error => console.warn('EduOS service worker registration failed.', error));
        });
    }
})();
