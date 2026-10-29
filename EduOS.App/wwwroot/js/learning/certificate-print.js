(() => {
    'use strict';
    const button = document.getElementById('lmsPrintNow');
    if (!button) return;
    button.addEventListener('click', () => window.print());
})();
