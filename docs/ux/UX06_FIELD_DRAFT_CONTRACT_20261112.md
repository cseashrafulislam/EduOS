# UX-06 F12 shared field drafting — 2026-11-12

## Supported
One existing global Page Assistant remains the only entry point. On-device speech with an installed language pack is explicitly started by the user; the typed transcript is editable and no cloud speech fallback is invoked. The same typed command is available without a microphone. Bilingual reviewed syntax: `fill Public note with Class materials ready` or `বিবরণ লিখুন: আগামী সপ্তাহের পাঠ`.

The shared resolver matches a **unique currently visible label**. It previews the requested value before any mutation. For an unapproved field, it provides a **focus-only** action and explains that manual entry is required. For approved non-sensitive text inputs and textareas, the assistant offers explicit **Confirm field draft**, followed by **Undo field draft**. It emits only a bubbling input event, never submits, posts, approves or saves a business record. Existing canonical form/API validation remains the only business write authority.

## Opt-in contract for page owners (not enabled implicitly)
A domain form owner must review the form and explicitly mark **both** `<form data-assistant-draft="true">` and a safe field `<input data-assistant-draft="true">` (or textarea). No page owner is asked to add per-field microphones. This prevents accidentally enabling payment, grades, attendance, identifiers, dependent selects or other high-impact inputs. This shared change does **not** enable any unreviewed business form. Label and field must be visible, writable, connected and unique; hidden/disabled/read-only, dependent selects, sensitive field identifiers and mismatched maxlength/minlength/pattern are denied. A client-only allowlist is a UX guard, **not authorization**: canonical APIs must still validate tenant/campus/RBAC and domain integrity.

## State and acceptance
- Already supported: authenticated global launcher, opt-in local speech, editable text fallback, sidebar/field locator, search filter draft with preview/undo, protected attendance read-only preview.
- Improvement implemented: shared bilingual generic field-draft parser, explicit opt-in validation, visible preview/confirmation/undo, no implicit form save, safe focus-only fallback, regression contracts.
- Not yet covered: real form-owner opt-ins, canonical server-side typed voice intents for additional domains, dependent-select suggestions from verified APIs, authenticated mobile/SQL browser acceptance, production UAT. These remain pending; do not call all-page F12 complete.
- Security note: no localStorage, service worker data queue, speech recording, provider call, additional database or second AI engine.
