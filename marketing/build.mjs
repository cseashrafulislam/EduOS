import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = dirname(fileURLToPath(import.meta.url));
const output = join(root, "dist");

// Exact public domain is not known until a Pages project is created.
// Setting SITE_URL ensures canonical and sitemap use the real deployed address.
const rawUrl = (process.env.SITE_URL || "").trim();
let siteUrl = "";
if (rawUrl) {
  const parsed = new URL(rawUrl);
  if (parsed.protocol !== "https:" || parsed.username || parsed.password || parsed.search || parsed.hash) {
    throw new Error("SITE_URL must be a plain HTTPS origin, e.g. https://your-project.pages.dev");
  }
  siteUrl = parsed.origin;
}

// Replace only the generated marketing/dist folder; never touch application source.
await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
await cp(join(root, "public"), output, { recursive: true, force: true });

const html = await readFile(join(output, "index.html"), "utf8");
const canonical = siteUrl
  ? `<link rel="canonical" href="${siteUrl}/"/><meta property="og:url" content="${siteUrl}/"/>`
  : "";
await writeFile(join(output, "index.html"), html.replace("<!-- CANONICAL_AT_BUILD -->", canonical));
await cp(join(root, "site-config.js"), join(output, "site-config.js"));

const robots = [
  "User-agent: *",
  "Allow: /",
  "",
  "User-agent: OAI-SearchBot",
  "Allow: /",
  ""
];
if (siteUrl) {
  robots.push(`Sitemap: ${siteUrl}/sitemap.xml`, "");
  await writeFile(join(output, "sitemap.xml"),
    `<?xml version="1.0" encoding="UTF-8"?>\n` +
    `<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>${siteUrl}/</loc></url></urlset>\n`);
}
await writeFile(join(output, "robots.txt"), robots.join("\n"));
console.log(`EduOS marketing built. Canonical: ${siteUrl || "not configured (set SITE_URL after Pages URL is known)"}`);
