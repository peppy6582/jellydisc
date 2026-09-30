// Disc Menus web renderer.
//
// Injected into jellyfin-web's index.html by the File Transformation plugin
// (see FileTransformationIntegration.cs). Loads once with the page and never
// reloads on in-app navigation - jellyfin-web is a client-routed, hash-based
// SPA (confirmed: components/router/appRouter.js builds URLs like
// "#/details?id=<guid>"), so this watches the native `hashchange` event to
// know which item's details page is currently showing.
//
// KNOWN v1 LIMITATIONS (see README "Web menu renderer" for why):
// - playbackManager isn't reachable from an externally-injected script (it's
//   an ES module internal to jellyfin-web's own bundle, not a global). So
//   "play" actions navigate to the target item's own details page instead of
//   starting playback directly - the user clicks Jellyfin's own native Play
//   button there. Not a one-click DVD-menu experience yet.
// - playSequence plays only the first resolved item, not the whole queue.
// - "chapters" (scene selection) isn't implemented - no chapter data is
//   fetched by this plugin yet.
// - background.source "tmdb"/"fanart" aren't implemented - falls back to a
//   plain dark background. Only "jellyfin" (the parent item's own images)
//   and "color" work.
(function () {
    'use strict';

    var OVERLAY_ID = 'discMenusOverlay';
    var BUTTON_ID = 'discMenusButton';

    var currentParentItemId = null;
    var menuDoc = null;
    var menuStack = [];

    function getItemIdFromHash() {
        var hash = window.location.hash || '';
        var match = /[#&?]id=([0-9a-fA-F-]{32,36})/.exec(hash);
        return match ? match[1] : null;
    }

    function waitForApiClient(callback, attempts) {
        attempts = attempts || 0;
        if (window.ApiClient) {
            callback();
            return;
        }

        if (attempts > 100) {
            return;
        }

        setTimeout(function () {
            waitForApiClient(callback, attempts + 1);
        }, 100);
    }

    function checkForMenu() {
        var itemId = getItemIdFromHash();
        if (itemId === currentParentItemId) {
            return;
        }

        currentParentItemId = itemId;
        removeButton();

        if (!itemId || !window.ApiClient) {
            return;
        }

        ApiClient.getJSON(ApiClient.getUrl('DiscMenus/' + itemId + '/Menu')).then(
            function (doc) {
                if (currentParentItemId !== itemId) {
                    return;
                }

                menuDoc = doc;
                addButton(itemId);
            },
            function () {
                // No disc menu bound to this item (404), or not authenticated
                // yet - either way, just don't show the button.
            }
        );
    }

    function addButton(itemId) {
        var btn = document.createElement('button');
        btn.id = BUTTON_ID;
        btn.type = 'button';
        btn.textContent = 'Disc Menu';
        btn.style.cssText =
            'position:fixed;bottom:2em;right:2em;z-index:9998;padding:0.75em 1.25em;' +
            'border-radius:2em;border:none;background:#3ddc84;color:#101010;font-weight:600;' +
            'font-size:1em;cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,0.4);';
        btn.addEventListener('click', function () {
            openOverlay(itemId);
        });
        document.body.appendChild(btn);
    }

    function removeButton() {
        var existing = document.getElementById(BUTTON_ID);
        if (existing) {
            existing.remove();
        }

        closeOverlay();
    }

    function openOverlay(parentItemId) {
        if (!menuDoc) {
            return;
        }

        menuStack = [menuDoc.Root];
        renderOverlay(parentItemId);
    }

    function closeOverlay() {
        var existing = document.getElementById(OVERLAY_ID);
        if (existing) {
            existing.remove();
            document.removeEventListener('keydown', onKeyDown);
        }
    }

    function onKeyDown(e) {
        var overlay = document.getElementById(OVERLAY_ID);
        if (!overlay) {
            return;
        }

        if (e.key === 'Escape') {
            closeOverlay();
            return;
        }

        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') {
            return;
        }

        var focusable = Array.prototype.slice.call(overlay.querySelectorAll('.discMenuEntry'));
        if (focusable.length === 0) {
            return;
        }

        var idx = focusable.indexOf(document.activeElement);
        e.preventDefault();
        var next = e.key === 'ArrowDown' ? idx + 1 : idx - 1;
        focusable[(next + focusable.length) % focusable.length].focus();
    }

    function backgroundStyle(background, parentItemId) {
        if (!background) {
            return 'background-color:#101010;';
        }

        var dim = background.Dim != null ? background.Dim : 0.4;

        if (background.Source === 'color' && background.Color) {
            return 'background-color:' + background.Color + ';';
        }

        if (background.Source === 'jellyfin' && window.ApiClient) {
            var url = ApiClient.getImageUrl(parentItemId, {
                type: background.ImageType || 'Backdrop',
                index: background.Index || 0,
            });
            return (
                'background-image:linear-gradient(rgba(0,0,0,' + dim + '),rgba(0,0,0,' + dim + ')),url(' + url + ');' +
                'background-size:cover;background-position:center;'
            );
        }

        // tmdb/fanart sources: not implemented yet, see file header.
        return 'background-color:#101010;';
    }

    function renderOverlay(parentItemId) {
        var menuKey = menuStack[menuStack.length - 1];
        var menu = menuDoc.Menus[menuKey];
        if (!menu) {
            return;
        }

        var existing = document.getElementById(OVERLAY_ID);
        var overlay = existing || document.createElement('div');
        overlay.id = OVERLAY_ID;
        overlay.innerHTML = '';

        var theme = menu.Theme || menuDoc.Theme || {};
        var accent = theme.Accent || '#3ddc84';
        var align = theme.Align || 'left';
        var alignItems = align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start';
        var background = menu.Background || menuDoc.Background;

        overlay.style.cssText =
            'position:fixed;inset:0;z-index:9999;display:flex;flex-direction:column;' +
            'justify-content:center;align-items:' + alignItems + ';padding:4em;color:#fff;' +
            backgroundStyle(background, parentItemId);

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.textContent = '✕';
        closeBtn.setAttribute('aria-label', 'Close disc menu');
        closeBtn.style.cssText =
            'position:absolute;top:1.5em;right:1.5em;background:none;border:none;color:#fff;' +
            'font-size:1.5em;cursor:pointer;line-height:1;';
        closeBtn.addEventListener('click', closeOverlay);
        overlay.appendChild(closeBtn);

        var title = document.createElement('h1');
        title.textContent = menu.Title;
        title.style.cssText = 'margin:0 0 0.75em;font-size:2em;text-shadow:0 2px 8px rgba(0,0,0,0.8);';
        overlay.appendChild(title);

        var list = document.createElement('div');
        list.style.cssText = 'display:flex;flex-direction:column;gap:0.5em;';

        menu.Entries.forEach(function (entry) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'discMenuEntry';
            btn.textContent = entry.Label;
            btn.style.cssText =
                'font-size:1.25em;padding:0.5em 1.25em;background:rgba(0,0,0,0.5);' +
                'border:2px solid transparent;border-radius:0.4em;color:#fff;cursor:pointer;' +
                'text-align:' + align + ';';
            btn.addEventListener('focus', function () {
                btn.style.borderColor = accent;
                btn.style.background = 'rgba(0,0,0,0.75)';
            });
            btn.addEventListener('blur', function () {
                btn.style.borderColor = 'transparent';
                btn.style.background = 'rgba(0,0,0,0.5)';
            });
            btn.addEventListener('click', function () {
                handleEntry(entry, parentItemId);
            });
            list.appendChild(btn);
        });

        overlay.appendChild(list);

        if (!existing) {
            document.body.appendChild(overlay);
            document.addEventListener('keydown', onKeyDown);
        }

        var first = list.querySelector('.discMenuEntry');
        if (first) {
            first.focus();
        }
    }

    function navigateToItem(itemId) {
        closeOverlay();
        window.location.hash = '#/details?id=' + itemId;
    }

    function alertUnavailable(message) {
        if (window.Dashboard && Dashboard.alert) {
            Dashboard.alert(message);
        } else {
            window.alert(message);
        }
    }

    function handleEntry(entry, parentItemId) {
        switch (entry.Action) {
            case 'playFeature':
                navigateToItem(parentItemId);
                break;
            case 'playExtra':
                if (entry.ItemId) {
                    navigateToItem(entry.ItemId);
                } else {
                    alertUnavailable("This extra isn't linked to a local file yet.");
                }

                break;
            case 'playSequence':
                // Queueing the whole sequence isn't implemented yet - see file header.
                if (entry.ItemIds && entry.ItemIds.length > 0) {
                    navigateToItem(entry.ItemIds[0]);
                } else {
                    alertUnavailable("None of these extras are linked to a local file yet.");
                }

                break;
            case 'submenu':
                if (entry.Menu && menuDoc.Menus[entry.Menu]) {
                    menuStack.push(entry.Menu);
                    renderOverlay(parentItemId);
                }

                break;
            case 'back':
                if (menuStack.length > 1) {
                    menuStack.pop();
                    renderOverlay(parentItemId);
                } else {
                    closeOverlay();
                }

                break;
            case 'chapters':
                alertUnavailable("Scene selection isn't implemented yet.");
                break;
            default:
                break;
        }
    }

    window.addEventListener('hashchange', checkForMenu);
    waitForApiClient(checkForMenu);

    console.log('[Disc Menus] renderer script loaded');
})();
