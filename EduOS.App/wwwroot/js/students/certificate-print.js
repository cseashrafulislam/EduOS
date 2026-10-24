(() => {
    'use strict';
    const button = document.getElementById('certificatePrint');
    if (button) button.addEventListener('click', () => window.print());
})();
