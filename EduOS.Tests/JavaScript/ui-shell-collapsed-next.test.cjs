'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { contextualNextLink, breadcrumbSelection } = require('../../EduOS.App/wwwroot/js/platform/ui-shell.js');
function links(currentAttrs = {}, currentRects = [], nextAttrs = {}, nextRects = [1]) {
    let items;
    const nav = { querySelectorAll: () => items };
    function link(href, attrs, rects) {
        return {
            getAttribute: key => key === 'href' ? href : (Object.hasOwn(attrs, key) ? attrs[key] : null),
            closest: selector => selector.includes('nav[data-sidebar-section]') ? nav : null,
            getClientRects: () => rects,
            classList: { contains: () => false }
        };
    }
    items = [link('/Students', { 'aria-current': 'page', ...currentAttrs }, currentRects),
        link('/Admissions', nextAttrs, nextRects)];
    return items;
}
test('collapsed active page keeps breadcrumb and authorized next action', () => {
    const items = links();
    assert.equal(breadcrumbSelection(items), items[0]);
    assert.equal(contextualNextLink(items[0]), items[1]);
});
test('explicitly disabled, hidden or inert current page never offers next action', () => {
    for (const attrs of [{ hidden: '' }, { inert: '' }, { disabled: '' },
        { 'aria-hidden': 'true' }, { 'aria-disabled': 'true' }]) {
        const items = links(attrs);
        assert.equal(contextualNextLink(items[0]), null);
    }
});
test('next destination must remain visible and server-provided', () => {
    const collapsed = links({}, [], {}, []);
    assert.equal(contextualNextLink(collapsed[0]), collapsed[1]);
    assert.equal(contextualNextLink(links({}, [], { 'aria-disabled': 'true' })[0]), null);
    assert.equal(contextualNextLink(links({}, [], { hidden: '' })[0]), null);
});

test('closed mobile drawer still offers a permitted next page', () => {
    const items = links({}, [], {}, []);
    assert.equal(contextualNextLink(items[0]), items[1]);
});
test('explicitly inaccessible next destinations remain excluded even when drawer is closed', () => {
    for (const attrs of [{ hidden: '' }, { inert: '' }, { disabled: '' },
        { 'aria-hidden': 'true' }, { 'aria-disabled': 'true' }]) {
        const items = links({}, [], attrs, []);
        assert.equal(contextualNextLink(items[0]), null);
    }
});
