// Where the "Disc Menu" button goes on the item page: in jellyfin-web's own button row, right after Play, with the same markup; or floating when the
// page has no such row. Runs the real renderer script in jsdom.
const path = require('path'); const fs = require('fs');
const { JSDOM } = require('jsdom');
const ROOT = path.resolve(__dirname, '..', '..');
const src = fs.readFileSync(path.join(ROOT, 'Jellyfin.Plugin.DiscMenus/Web/discmenus.js'), 'utf8');
let fail = 0; const check = (ok, m, e) => { if (!ok) fail++; console.log(ok ? 'PASS' : 'FAIL', m, e === undefined ? '' : e); };
const sleep = ms => new Promise(r => setTimeout(r, ms));
const ID = '4cf4efb9cb2c96b4ec157568bcccdf7f';
const doc = { Root: 'main', Menus: { main: { Title: 'Main', Entries: [{ Action: 'playFeature', Label: 'Play' }] } } };
const ROW = (cls = 'btnPlay') => `<div class="mainDetailButtons focuscontainer-x">
  <button class="button-flat ${cls} detailButton"></button><button class="button-flat btnReplay detailButton"></button>
  <button class="button-flat btnPlayTrailer detailButton"></button><button class="button-flat btnMoreCommands detailButton"></button></div>`;

function boot(body, hash = '#/details?id=' + ID) {
  const dom = new JSDOM('<body>' + body + '</body>', { url: 'http://x/web/' + hash, runScripts: 'outside-only', pretendToBeVisual: true });
  const w = dom.window; w.console.warn = () => {}; w.console.log = () => {};
  w.ApiClient = { getJSON: () => Promise.resolve(doc), getUrl: u => u, getImageUrl: () => 'x', deviceId: () => 'd' };
  w.eval(src); return w;
}
const kids = w => [...w.document.querySelector('.mainDetailButtons').children].map(c => c.className.split(' ').find(x => /^btn/.test(x)));

(async () => {
  let w = boot('<div id="itemDetailPage" class="page">' + ROW() + '</div>'); await sleep(200);
  const b = w.document.getElementById('discMenusButton');
  check(b && b.parentNode.classList.contains('mainDetailButtons'), 'the button is in the item page\'s own button row');
  check(kids(w).join() === 'btnPlay,btnDiscMenu,btnReplay,btnPlayTrailer,btnMoreCommands', 'right after Play, before the others', kids(w).join());
  check(b.classList.contains('detailButton') && b.classList.contains('button-flat') && b.querySelector('.detailButton-content .material-icons.detailButton-icon.album'), 'with the same markup as its neighbours and a disc icon');
  check(b.title === 'Disc Menu' && b.getAttribute('aria-label') === 'Disc Menu' && !b.style.position, 'it has a name for tooltips and screen readers, and is not floating');
  b.click(); await sleep(20);
  check(!!w.document.getElementById('discMenusOverlay'), 'clicking it opens the menu');

  w = boot('<div id="itemDetailPage" class="page hide">' + ROW() + '</div><div id="itemDetailPage" class="page">' + ROW('btnPlay') + '</div>'); await sleep(200);
  const rows = w.document.querySelectorAll('.mainDetailButtons');
  check(!rows[0].querySelector('#discMenusButton') && !!rows[1].querySelector('#discMenusButton'), 'with a hidden cached page, the visible page\'s row gets it');

  w = boot('<div id="itemDetailPage" class="page"><div class="mainDetailButtons"><button class="btnReplay"></button></div></div>'); await sleep(200);
  check(w.document.querySelector('.mainDetailButtons').firstChild.id === 'discMenusButton', 'with no Play button in the row it goes first');

  w = boot('<div id="itemDetailPage" class="page"></div>'); await sleep(100);
  check(!w.document.getElementById('discMenusButton'), 'while the page is still drawing its buttons, nothing floats yet');
  const row = w.document.createElement('div'); row.className = 'mainDetailButtons'; row.innerHTML = '<button class="btnPlay"></button><button class="btnReplay"></button>';
  w.document.getElementById('itemDetailPage').appendChild(row); await sleep(300);
  check(row.children[1] && row.children[1].id === 'discMenusButton', 'a row that appears a moment later still gets it');

  w = boot('<div id="somethingElse"></div>'); await sleep(200);
  const f = w.document.getElementById('discMenusButton');
  check(f && f.parentNode === w.document.body && /position:\s*fixed/.test(f.style.cssText) && f.textContent === 'Disc Menu', 'on a page without such a row a floating button is used so the menu stays reachable');

  w = boot('<div id="itemDetailPage" class="page">' + ROW() + '</div>'); await sleep(200);
  check(w.document.querySelectorAll('#discMenusButton').length === 1, 'exactly one button');
  w.location.hash = '#/details?id=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'; await sleep(1200);
  check(w.document.querySelectorAll('#discMenusButton').length <= 1, 'moving to another item never leaves two buttons', w.document.querySelectorAll('#discMenusButton').length);
  console.log('failures: ' + fail); process.exit(fail ? 1 : 0);
})();
