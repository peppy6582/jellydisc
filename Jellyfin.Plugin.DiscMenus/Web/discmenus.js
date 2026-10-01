// Disc Menus web renderer.
//
// Injected into jellyfin-web's index.html by the File Transformation plugin
// (see FileTransformationIntegration.cs). Loads once with the page and never
// reloads on in-app navigation - jellyfin-web is a client-routed, hash-based
// SPA (confirmed: components/router/appRouter.js builds URLs like
// "#/details?id=<guid>"), so this watches the native `hashchange` event to
// know which item's details page is currently showing.
//
// PLAYBACK: playbackManager isn't reachable from an injected script (it's an
// ES module internal to jellyfin-web's bundle), so play actions send a
// PlayNow command to this browser's own session via the Sessions API (see
// playItems). If that fails they fall back to navigating to the item's
// details page. After playback stops the menu reopens where it was left.
//
// KNOWN LIMITATIONS:
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
        lastFocusIndex = {};
        renderOverlay(parentItemId);
    }

    function closeOverlay() {
        var existing = document.getElementById(OVERLAY_ID);
        if (existing) {
            existing.remove();
            document.removeEventListener('keydown', onKeyDown, true);
        }
    }

    // ---- Remote-style navigation -------------------------------------------
    // Directional focus is geometric (nearest entry in the pressed direction by
    // on-screen position), not list-order based, so it keeps working unchanged
    // when menus get authored layouts instead of a plain column.

    var DIRECTION_KEYS = {
        ArrowUp: [0, -1], ArrowDown: [0, 1], ArrowLeft: [-1, 0], ArrowRight: [1, 0],
    };
    var SELECT_KEYS = { Enter: true, NumpadEnter: true, ' ': true };
    // Browsers/TV remotes report "back" in many ways: webOS 461, Tizen 10009.
    var BACK_KEYS = { Escape: true, Backspace: true, BrowserBack: true, GoBack: true };
    var BACK_KEYCODES = { 461: true, 10009: true };

    var lastFocusIndex = {};

    function getEntries() {
        var overlay = document.getElementById(OVERLAY_ID);
        return overlay ? Array.prototype.slice.call(overlay.querySelectorAll('.discMenuEntry')) : [];
    }

    function moveFocus(dx, dy) {
        var entries = getEntries();
        if (entries.length === 0) {
            return;
        }

        var current = entries.indexOf(document.activeElement);
        if (current < 0) {
            entries[0].focus();
            return;
        }

        var from = entries[current].getBoundingClientRect();
        var fx = from.left + from.width / 2;
        var fy = from.top + from.height / 2;
        var best = null;
        var bestScore = Infinity;
        var wrap = null;
        var wrapScore = Infinity;

        entries.forEach(function (el, i) {
            if (i === current) {
                return;
            }

            var r = el.getBoundingClientRect();
            var vx = r.left + r.width / 2 - fx;
            var vy = r.top + r.height / 2 - fy;
            var along = vx * dx + vy * dy;
            var across = Math.abs(vx * dy - vy * dx);
            // Off-axis distance counts double so "down" prefers the entry
            // straight below over a nearer one far off to the side.
            var score = along + 2 * across;
            if (along > 1) {
                if (score < bestScore) {
                    best = el;
                    bestScore = score;
                }
            } else if (score < wrapScore) {
                // Nothing further in this direction: wrap to the far side.
                wrap = el;
                wrapScore = score;
            }
        });

        var target = best || wrap;
        if (target) {
            target.focus();
        }
    }

    function goBack(parentItemId) {
        if (menuStack.length > 1) {
            menuStack.pop();
            renderOverlay(parentItemId);
        } else {
            closeOverlay();
        }
    }

    function selectFocused() {
        if (document.activeElement && document.activeElement.classList.contains('discMenuEntry')) {
            document.activeElement.click();
        }
    }

    function onKeyDown(e) {
        if (!document.getElementById(OVERLAY_ID)) {
            return;
        }

        var dir = DIRECTION_KEYS[e.key];
        var handled = true;
        if (dir) {
            moveFocus(dir[0], dir[1]);
        } else if (SELECT_KEYS[e.key]) {
            selectFocused();
        } else if (BACK_KEYS[e.key] || BACK_KEYCODES[e.keyCode]) {
            goBack(currentParentItemId);
        } else {
            handled = false;
        }

        if (handled) {
            // Capture phase + stop: jellyfin-web's own key handlers (Escape,
            // Backspace, arrows) must not also act on the page behind the menu.
            e.preventDefault();
            e.stopPropagation();
        }
    }

    // Gamepad: d-pad / left stick to move, A to select, B to go back. There is
    // no gamepad "event" for buttons, so poll while the overlay is open.
    var gamepadLoop = 0;
    var gamepadHeld = {};

    function pollGamepad() {
        if (!document.getElementById(OVERLAY_ID)) {
            gamepadLoop = 0;
            return;
        }

        var pads = navigator.getGamepads ? navigator.getGamepads() : [];
        var now = Date.now();
        var pressed = {};
        for (var i = 0; i < pads.length; i++) {
            var pad = pads[i];
            if (!pad) {
                continue;
            }

            var b = pad.buttons;
            var ax = pad.axes[0] || 0;
            var ay = pad.axes[1] || 0;
            if ((b[12] && b[12].pressed) || ay < -0.6) { pressed.up = true; }
            if ((b[13] && b[13].pressed) || ay > 0.6) { pressed.down = true; }
            if ((b[14] && b[14].pressed) || ax < -0.6) { pressed.left = true; }
            if ((b[15] && b[15].pressed) || ax > 0.6) { pressed.right = true; }
            if (b[0] && b[0].pressed) { pressed.select = true; }
            if (b[1] && b[1].pressed) { pressed.back = true; }
        }

        Object.keys(pressed).forEach(function (name) {
            var held = gamepadHeld[name];
            // Fire on press, then auto-repeat directions while held (not select/back).
            var repeat = held && (name === 'up' || name === 'down' || name === 'left' || name === 'right') &&
                now - held.last > (held.repeating ? 120 : 400);
            if (held && !repeat) {
                return;
            }

            gamepadHeld[name] = { last: now, repeating: !!held };
            if (name === 'up') { moveFocus(0, -1); }
            else if (name === 'down') { moveFocus(0, 1); }
            else if (name === 'left') { moveFocus(-1, 0); }
            else if (name === 'right') { moveFocus(1, 0); }
            else if (name === 'select') { selectFocused(); }
            else if (name === 'back') { goBack(currentParentItemId); }
        });

        Object.keys(gamepadHeld).forEach(function (name) {
            if (!pressed[name]) {
                delete gamepadHeld[name];
            }
        });

        if (document.getElementById(OVERLAY_ID)) {
            gamepadLoop = requestAnimationFrame(pollGamepad);
        } else {
            gamepadLoop = 0;
        }
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
        closeBtn.tabIndex = -1;
        closeBtn.addEventListener('click', closeOverlay);
        overlay.appendChild(closeBtn);

        var title = document.createElement('h1');
        title.textContent = menu.Title;
        title.style.cssText = 'margin:0 0 0.75em;font-size:2em;text-shadow:0 2px 8px rgba(0,0,0,0.8);';
        overlay.appendChild(title);

        var list = document.createElement('div');
        list.style.cssText = 'display:flex;flex-direction:column;gap:0.5em;';

        menu.Entries.forEach(function (entry, entryIndex) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'discMenuEntry';
            btn.textContent = entry.Label;
            btn.style.cssText =
                'font-size:1.25em;padding:0.5em 1.25em;background:rgba(0,0,0,0.5);' +
                'border:2px solid transparent;border-radius:0.4em;color:#fff;cursor:pointer;' +
                'text-align:' + align + ';';
            btn.addEventListener('focus', function () {
                lastFocusIndex[menuKey] = entryIndex;
                btn.style.borderColor = accent;
                btn.style.background = 'rgba(0,0,0,0.75)';
            });
            btn.addEventListener('blur', function () {
                btn.style.borderColor = 'transparent';
                btn.style.background = 'rgba(0,0,0,0.5)';
            });
            // Keep mouse and keyboard/remote highlight in sync.
            btn.addEventListener('mouseenter', function () {
                btn.focus();
            });
            btn.addEventListener('click', function () {
                handleEntry(entry, parentItemId);
            });
            list.appendChild(btn);
        });

        overlay.appendChild(list);

        if (!existing) {
            document.body.appendChild(overlay);
            document.addEventListener('keydown', onKeyDown, true);
            if (!gamepadLoop) {
                gamepadLoop = requestAnimationFrame(pollGamepad);
            }
        }

        // Like a disc remembering its highlighted button: returning to a menu
        // lands on the entry you left it from (e.g. 'Special Features' after Back).
        var entryEls = list.querySelectorAll('.discMenuEntry');
        var remembered = entryEls[lastFocusIndex[menuKey]] || entryEls[0];
        if (remembered) {
            remembered.focus();
        }
    }

    function navigateToItem(itemId) {
        closeOverlay();
        window.location.hash = '#/details?id=' + itemId;
    }

    // One-click playback via the Sessions API: find this browser's own session
    // (matched by device id) and send it a PlayNow command - no access to
    // jellyfin-web's internal playbackManager needed. Falls back to navigating
    // to the item's details page if anything about that fails.
    var returnPoll = null;

    function stopReturnPoll() {
        if (returnPoll) {
            clearInterval(returnPoll);
            returnPoll = null;
        }
    }

    function playItems(itemIds, parentItemId) {
        var deviceId = ApiClient.deviceId();
        var savedStack = menuStack.slice();

        ApiClient.getJSON(ApiClient.getUrl('Sessions', { DeviceId: deviceId })).then(function (sessions) {
            var session = sessions && sessions[0];
            if (!session) {
                throw new Error('no session for this device');
            }

            return ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('Sessions/' + session.Id + '/Playing', {
                    PlayCommand: 'PlayNow',
                    ItemIds: itemIds.join(','),
                }),
            }).then(function () {
                closeOverlay();
                watchForPlaybackEnd(session.Id, parentItemId, savedStack);
            });
        }).catch(function (err) {
            console.warn('[Disc Menus] Sessions API playback failed, falling back to details page', err);
            navigateToItem(itemIds[0]);
        });
    }

    // Reopen the menu where the user left it once playback stops, like a disc
    // returning to its menu. Waits for playback to actually start first so the
    // brief gap before NowPlayingItem appears isn't mistaken for "ended".
    function watchForPlaybackEnd(sessionId, parentItemId, savedStack) {
        stopReturnPoll();
        var started = false;
        var ticks = 0;

        returnPoll = setInterval(function () {
            ticks++;
            ApiClient.getJSON(ApiClient.getUrl('Sessions')).then(function (sessions) {
                var s = (sessions || []).filter(function (x) { return x.Id === sessionId; })[0];
                var playing = !!(s && s.NowPlayingItem);
                if (playing) {
                    started = true;
                } else if (started || ticks > 15) {
                    stopReturnPoll();
                    if (started) {
                        window.location.hash = '#/details?id=' + parentItemId;
                        setTimeout(function () {
                            menuStack = savedStack;
                            renderOverlay(parentItemId);
                        }, 600);
                    }
                }
            });
        }, 2000);
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
                playItems([parentItemId], parentItemId);
                break;
            case 'playExtra':
                if (entry.ItemId) {
                    playItems([entry.ItemId], parentItemId);
                } else {
                    alertUnavailable("This extra isn't linked to a local file yet.");
                }

                break;
            case 'playSequence':
                if (entry.ItemIds && entry.ItemIds.length > 0) {
                    playItems(entry.ItemIds, parentItemId);
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
                goBack(parentItemId);
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
