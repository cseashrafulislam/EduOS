(() => {
  "use strict";
  // Prefer an approved WebP screenshot when uploaded; otherwise retain the
  // watermarked illustrative SVG concept without disclosing any data.
  document.querySelectorAll('img[src^="/assets/screenshots/"][src$=".svg"]').forEach(img => {
    const path = img.getAttribute("src");
    const match = /^\/assets\/screenshots\/([a-z-]+)\.svg$/.exec(path);
    if (!match) return;
    const real = "/assets/screenshots/" + match[1] + ".webp";
    const probe = new Image();
    probe.onload = () => {
      img.src = real;
      img.alt = match[1].replace(/-/g, " ") + " — approved EduOS application screenshot";
    };
    probe.src = real;
  });
})();
