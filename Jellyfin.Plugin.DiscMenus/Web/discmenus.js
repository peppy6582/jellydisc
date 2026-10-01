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
            window.removeEventListener('keydown', onKeyDown, true);
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
    var lastMouse = { x: -1, y: -1 };

    function getEntries() {
        var overlay = document.getElementById(OVERLAY_ID);
        return overlay ? Array.prototype.slice.call(overlay.querySelectorAll('.discMenuEntry')) : [];
    }

    // How far candidate rect `r` lies in direction (dx,dy) from `from`, judged by
    // edges rather than centres, or null if it isn't in that direction at all.
    // A button on the same row is not "below" you even if its centre is a few
    // pixels lower, so up/down in a single row of buttons never hops sideways.
    function distanceAlong(from, r, dx, dy) {
        var tol = 0.25 * (dx !== 0 ? from.width : from.height);
        var gap = dx === 1 ? r.left - from.right
            : dx === -1 ? from.left - r.right
            : dy === 1 ? r.top - from.bottom
            : from.top - r.bottom;
        if (gap < -tol) {
            return null;
        }

        var cx = r.left + r.width / 2 - (from.left + from.width / 2);
        var cy = r.top + r.height / 2 - (from.top + from.height / 2);
        return cx * dx + cy * dy;
    }

    function overlapsAcross(from, r, dx) {
        return dx !== 0
            ? from.top < r.bottom && r.top < from.bottom
            : from.left < r.right && r.left < from.right;
    }

    function acrossOffset(from, r, dx) {
        var cx = r.left + r.width / 2 - (from.left + from.width / 2);
        var cy = r.top + r.height / 2 - (from.top + from.height / 2);
        return Math.abs(dx !== 0 ? cy : cx);
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
        var best = null;
        var bestKey = null;
        var wrap = null;
        var wrapKey = null;

        entries.forEach(function (el, i) {
            if (i === current) {
                return;
            }

            var r = el.getBoundingClientRect();
            var ahead = distanceAlong(from, r, dx, dy);
            var across = acrossOffset(from, r, dx);
            if (ahead !== null) {
                // Same row/column as you first (nearest wins); otherwise the
                // closest diagonal, weighting sideways drift double.
                var key = overlapsAcross(from, r, dx) ? [0, ahead] : [1, ahead + 2 * across];
                if (!bestKey || key[0] < bestKey[0] || (key[0] === bestKey[0] && key[1] < bestKey[1])) {
                    best = el;
                    bestKey = key;
                }

                return;
            }

            // Not ahead of you. Only buttons genuinely *behind* you are wrap
            // targets; same-row neighbours for an up/down press are ignored.
            var behind = distanceAlong(from, r, -dx, -dy);
            if (behind === null) {
                return;
            }

            // Farthest behind wins (so wrapping reverses the opposite press
            // exactly and every button stays reachable); ties go to the
            // straightest one.
            var far = -behind;
            if (!wrapKey || far < wrapKey[0] - 20 || (far < wrapKey[0] + 20 && across < wrapKey[1])) {
                wrap = el;
                wrapKey = [far, across];
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
            e.stopImmediatePropagation();
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

        if (background.Source === 'image') {
            var imageUrl = safeImage(background.Image);
            if (imageUrl) {
                return (
                    'background-image:linear-gradient(rgba(0,0,0,' + dim + '),rgba(0,0,0,' + dim + ')),url(' + imageUrl + ');' +
                    'background-size:cover;background-position:center;background-color:#101010;'
                );
            }

            return 'background-color:#101010;';
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

    // ---- Authored layout ---------------------------------------------------

    var ANCHOR_SHIFT = {
        'top-left': [0, 0], top: [-50, 0], 'top-right': [-100, 0],
        left: [0, -50], center: [-50, -50], right: [-100, -50],
        'bottom-left': [0, -100], bottom: [-50, -100], 'bottom-right': [-100, -100],
    };

    // Position an element at percentages of the menu screen; x/y refer to the
    // chosen anchor point of the element (so "bottom" centres it horizontally
    // on x and sits its bottom edge on y).
    function place(el, pos) {
        var shift = ANCHOR_SHIFT[pos.Anchor || 'top-left'] || ANCHOR_SHIFT['top-left'];
        el.style.position = 'absolute';
        el.style.left = pos.X + '%';
        el.style.top = pos.Y + '%';
        if (pos.W != null) {
            el.style.width = pos.W + '%';
        }

        if (pos.H != null) {
            el.style.height = pos.H + '%';
        }

        el.style.transform = 'translate(' + shift[0] + '%,' + shift[1] + '%)';
    }

    // Defense in depth: the server already rejects anything else, but menu JSON
    // can come from anywhere, so never hand the browser a URL we wouldn't vouch for.
    var SAFE_IMAGE = /^(https:\/\/[^\s"'()<>\\]+|data:image\/(png|jpeg|webp);base64,[A-Za-z0-9+\/=]+|asset:[A-Za-z0-9][A-Za-z0-9._-]{0,63}(\/[A-Za-z0-9][A-Za-z0-9._-]{0,63}){0,3})$/;

    // Returns a URL that is safe to hand to an <img>/CSS url(), or null. An
    // "asset:<folder>/<file>" reference becomes this server's asset endpoint.
    function safeImage(url) {
        if (typeof url !== 'string' || !SAFE_IMAGE.test(url)) {
            return null;
        }

        if (url.indexOf('asset:') === 0) {
            if (!window.ApiClient) {
                return null;
            }

            return ApiClient.getUrl('DiscMenus/Assets/' + url.slice(6).split('/').map(encodeURIComponent).join('/'));
        }

        return url;
    }

    // Built-in font stacks only; the menu JSON picks one by name and never
    // supplies a font-family string of its own.
    var FONT_STACKS = {
        sans: 'system-ui,-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif',
        serif: 'Georgia,"Times New Roman",Times,serif',
        condensed: '"Arial Narrow","Roboto Condensed","Helvetica Neue",Arial,sans-serif',
        wide: 'Verdana,"DejaVu Sans","Trebuchet MS",Geneva,sans-serif',
        mono: 'ui-monospace,Menlo,Consolas,"DejaVu Sans Mono",monospace',
    };

    // Typography declared by the theme, as inline CSS. Sizes are in vh so text
    // scales with the screen the same way positions do.
    function typographyCss(theme) {
        var css = '';
        if (theme.Font && FONT_STACKS[theme.Font]) {
            css += 'font-family:' + FONT_STACKS[theme.Font] + ';';
        }

        if (typeof theme.FontSize === 'number') {
            css += 'font-size:' + theme.FontSize + 'vh;';
        }

        if (theme.Uppercase) {
            css += 'text-transform:uppercase;';
        }

        if (theme.Bold) {
            css += 'font-weight:700;';
        }

        if (typeof theme.LetterSpacing === 'number') {
            css += 'letter-spacing:' + theme.LetterSpacing + 'em;';
        }

        return css;
    }

    var HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

    function safeColor(c, fallback) {
        return typeof c === 'string' && HEX_COLOR.test(c) ? c : fallback;
    }

    function num(v, fallback) {
        return typeof v === 'number' && isFinite(v) ? v : fallback;
    }

    // Decorative layers: drawn behind the buttons, never focusable or clickable.
    function renderLayer(layer, overlay) {
        var el;
        if (layer.Type === 'panel') {
            el = document.createElement('div');
            el.style.boxSizing = 'border-box';
            el.style.background = safeColor(layer.Fill, 'transparent');
            if (layer.BorderColor && num(layer.BorderWidth, 0) > 0) {
                el.style.border = num(layer.BorderWidth, 0) + 'vh solid ' + safeColor(layer.BorderColor, 'transparent');
            }

            if (layer.Radius != null) {
                el.style.borderRadius = num(layer.Radius, 0) + 'vh';
            }
        } else if (layer.Type === 'image' && safeImage(layer.Image)) {
            el = document.createElement('img');
            el.src = safeImage(layer.Image);
            el.alt = '';
            el.draggable = false;
            el.style.objectFit = layer.Fit === 'fill' || layer.Fit === 'cover' ? layer.Fit : 'contain';
        } else {
            return;
        }

        if (layer.Opacity != null) {
            el.style.opacity = String(num(layer.Opacity, 1));
        }

        el.style.pointerEvents = 'none';
        el.setAttribute('aria-hidden', 'true');
        place(el, layer.Position);
        overlay.appendChild(el);
    }

    function buildEntryButton(entry, defaultStyle, theme, align) {
        var accent = safeColor(theme.Accent, '#3ddc84');
        var textColor = safeColor(theme.TextColor, '#fff');
        var style = entry.Style || defaultStyle;
        var image = safeImage(entry.Image);
        var imageFocus = safeImage(entry.ImageFocus);
        var sized = !!(entry.Position && (entry.Position.W != null || entry.Position.H != null));

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'discMenuEntry';
        btn.setAttribute('aria-label', entry.Label);
        btn.style.cssText =
            'font-size:1.25em;padding:0.5em 1.25em;color:' + textColor + ';cursor:pointer;background:transparent;' +
            'border:2px solid transparent;border-radius:0.4em;text-align:' + align + ';' +
            'text-shadow:0 2px 6px rgba(0,0,0,0.8);outline:none;' + typographyCss(theme);

        var arrow = null;
        var img = null;

        if (image) {
            // Artwork button: the label is only an accessible name, not drawn.
            btn.title = entry.Label;
            btn.style.padding = '0';
            btn.style.border = 'none';
            img = document.createElement('img');
            img.src = image;
            img.alt = entry.Label;
            img.draggable = false;
            img.style.cssText = sized
                ? 'display:block;width:100%;height:100%;object-fit:contain;'
                : 'display:block;max-width:40vw;max-height:25vh;';
            btn.appendChild(img);
        } else {
            if (style === 'arrow') {
                arrow = document.createElement('span');
                arrow.textContent = '▶';
                arrow.setAttribute('aria-hidden', 'true');
                arrow.style.cssText = 'color:' + accent + ';margin-right:0.5em;visibility:hidden;';
                btn.appendChild(arrow);
            }

            btn.appendChild(document.createTextNode(entry.Label));
            if (style === 'frame') {
                btn.style.background = 'rgba(0,0,0,0.5)';
            }
        }

        function highlight(on) {
            if (img) {
                if (imageFocus) {
                    img.src = on ? imageFocus : image;
                } else {
                    img.style.filter = on ? 'drop-shadow(0 0 10px ' + accent + ') brightness(1.15)' : 'none';
                }

                return;
            }

            switch (style) {
                case 'frame':
                    btn.style.borderColor = on ? accent : 'transparent';
                    btn.style.background = on ? 'rgba(0,0,0,0.75)' : 'rgba(0,0,0,0.5)';
                    break;
                case 'glow':
                    btn.style.textShadow = on
                        ? '0 0 10px ' + accent + ',0 0 22px ' + accent
                        : '0 2px 6px rgba(0,0,0,0.8)';
                    break;
                case 'arrow':
                    arrow.style.visibility = on ? 'visible' : 'hidden';
                    break;
                default: // text
                    btn.style.color = on ? accent : textColor;
                    break;
            }
        }

        btn.addEventListener('focus', function () {
            highlight(true);
        });
        btn.addEventListener('blur', function () {
            highlight(false);
        });
        return btn;
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
        var layout = menu.Layout || {};
        var defaultStyle = layout.ButtonStyle || 'frame';
        // The server guarantees all-or-none, so one check is enough.
        var positioned = menu.Entries.length > 0 && menu.Entries.every(function (e) { return !!e.Position; });

        overlay.style.cssText =
            'position:fixed;inset:0;z-index:9999;color:#fff;overflow:hidden;' +
            (positioned
                ? ''
                : 'display:flex;flex-direction:column;justify-content:center;align-items:' + alignItems + ';padding:4em;') +
            backgroundStyle(background, parentItemId);

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.textContent = '✕';
        closeBtn.setAttribute('aria-label', 'Close disc menu');
        closeBtn.style.cssText =
            'position:absolute;top:1.5em;right:1.5em;background:none;border:none;color:#fff;' +
            'font-size:1.5em;cursor:pointer;line-height:1;z-index:1;';
        closeBtn.tabIndex = -1;
        closeBtn.addEventListener('click', closeOverlay);
        overlay.appendChild(closeBtn);

        (layout.Layers || []).forEach(function (layer) {
            renderLayer(layer, overlay);
        });

        if (!layout.HideTitle) {
            var title = document.createElement('h1');
            title.textContent = menu.Title;
            title.style.cssText =
                'margin:0 0 0.75em;font-size:2em;text-shadow:0 2px 8px rgba(0,0,0,0.8);color:' +
                safeColor(theme.TextColor, '#fff') + ';' + typographyCss(theme);
            if (typeof theme.FontSize === 'number') {
                title.style.fontSize = theme.FontSize * 1.6 + 'vh';
            }
            if (layout.TitlePosition) {
                title.style.margin = '0';
                place(title, layout.TitlePosition);
            }

            overlay.appendChild(title);
        }

        var list = positioned ? overlay : document.createElement('div');
        if (!positioned) {
            list.style.cssText = 'display:flex;flex-direction:column;gap:0.5em;';
        }

        menu.Entries.forEach(function (entry, entryIndex) {
            var btn = buildEntryButton(entry, defaultStyle, theme, align);
            if (positioned) {
                place(btn, entry.Position);
            }

            btn.addEventListener('focus', function () {
                lastFocusIndex[menuKey] = entryIndex;
            });
            // Keep mouse and keyboard/remote highlight in sync.
            // Only on real pointer movement: a menu appearing under a resting
            // cursor fires mouseenter without the user moving, which would
            // steal focus from the keyboard/remote.
            btn.addEventListener('mousemove', function (e) {
                if (e.screenX === lastMouse.x && e.screenY === lastMouse.y) {
                    return;
                }

                lastMouse.x = e.screenX;
                lastMouse.y = e.screenY;
                if (document.activeElement !== btn) {
                    btn.focus();
                }
            });
            btn.addEventListener('click', function () {
                handleEntry(entry, parentItemId);
            });
            list.appendChild(btn);
        });

        if (!positioned) {
            overlay.appendChild(list);
        }

        if (!existing) {
            document.body.appendChild(overlay);
            window.addEventListener('keydown', onKeyDown, true);
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
