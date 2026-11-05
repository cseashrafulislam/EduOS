# EduOS Marketing — myeduos.pages.dev

Static public marketing website, wholly separated from the private ASP.NET Education ERP/SQL Server. The official owner-provided domain is **https://myeduos.pages.dev/**. The repo deploys from the EXISTING **Ashraful** branch only.

## Pages, audiences and searchable content

- `/` — English overview, feature cards, representative institution types, Intelligent EduOS introduction and demo gallery.
- `/bn/` — Bengali landing page rendered as static searchable HTML.
- `/solutions/`, `/bn/solutions/` — 11 institution types, from private tutors and coaching centers to schools, universities, madrasas, online learning and vocational training. Clear workflow/use-case details.
- `/modules/`, `/bn/modules/` — detailed ERP module descriptions grouped by academic, finance, campus and platform capabilities.
- `/ai/`, `/bn/ai/` — complete intelligent assistant architecture and sample questions. **Vision and capabilities are described but AI/voice readiness is not falsely claimed as production verified.**
- `/demo/` — five clearly labelled synthetic SVG demo concept previews for Dashboard, Students, Fees, Academics and Reports, with direct **email cseashrafulwork@gmail.com** and **WhatsApp +8801733293938**. No fictitious customers or student data.

Add real, privacy-approved screenshots later to `marketing/public/assets/screenshots/{dashboard,student-management,fees-billing,academics,reports}.webp`. The main landing gallery prefers each matching WebP and falls back to the static concept SVG if unavailable. Update the standalone demo gallery separately when the real captures are approved.

## Cloudflare Pages configuration

Existing Pages project: `myeduos.pages.dev`. The deployment must watch the existing `Ashraful` branch, build root `marketing`, command `npm run build`, output `dist`. A separate new Pages project is **not** needed. Default canonical domain is `https://myeduos.pages.dev`; override `SITE_URL` only after authorizing a domain change. Source changes alone do not prove Cloudflare deployed.

Validate:
```bash
cd marketing
npm run check
npm run build
```
Expected routes: /, /bn/, /solutions/, /bn/solutions/, /modules/, /bn/modules/, /ai/, /bn/ai/, /demo/. Confirm /robots.txt, /sitemap.xml and /_headers browser responses on deployed Pages. Revalidate canonical and alternate language tags after changing the domain.

## Honest product positioning and AI search discovery

Product direction includes AI-enabled dynamic capability registry, model-free question patterns, local ML.NET classification and verified voice entry as they become available; no arbitrary count of AI agents. Public content describes the complete use-case vision; no claim that these functions are already fully implemented/QA certified. Any pricing, claims of production readiness, case studies, customer numbers or demo credentials must be supported by real evidence.

Google and AI search engine recommendations are **not guaranteed** by SEO. Descriptive pages, accessible natural-language text, internal links, valid JSON-LD matching visible content, canonical/hreflang, crawlable robots/sitemap and real brand authority help discovery. Configure and monitor Google Search Console and Bing Webmaster Tools. OAI-SearchBot is allowed by generated robots.txt, but deployed Cloudflare bot/robots settings must independently permit crawling. Do not invent SEO testimonials, reviews, ratings or customer credentials.

## Privacy / security

This public site is static, has no login, analytics cookies, CRM lead forms or external scripts. It exposes only owner-approved contact channels. Cloudflare Pages `_headers` constrains script/connect, microphone permissions, framing and other browser capabilities. No customer records, private source code, app keys, external API secrets or student data belong in the marketing build.
