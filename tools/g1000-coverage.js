// G1000 READ-COVERAGE PROBE
//
// Answers one question, for a whole instrument at once: WHAT IS ON THE SCREEN THAT MSFSBA
// NEVER SAYS?
//
// It visits every page the instrument actually has, and for each one subtracts everything the
// display agent emits from everything a sighted pilot can see. What is left is the gap.
//
// ⚠️ WHY THIS EXISTS. Every read-coverage hole found in this project was found by a pilot
// flying into it, one at a time, months apart - the CAS block read off a hidden duplicate, the
// flight plan page that reported itself empty, and the entire LATERAL HALF of the flight mode
// annunciator, which sat in an unclassed element and was never scraped at all. That last one
// meant a NAV mode which was engaged and NOT capturing looked exactly like one that was
// working. None of them were findable by reading code; all of them are findable by this.
//
// ⚠️ IT IS DELIBERATELY AIRCRAFT-AGNOSTIC. The DA40's G1000, the DA40-XLS's and the DA42's are
// different builds with different pages, and a probe hard-wired to one is a probe that has to
// be rewritten to be useful again. The instrument names its own pages (pageMap) and the agent
// names its own global, so both are arguments.
//
//   node tools/g1000-coverage.js [pageTitleRegex] [agentGlobal] [agentFile]
//
//   node tools/g1000-coverage.js AS1000_MFD
//   node tools/g1000-coverage.js AS1000_PFD
//   node tools/g1000-coverage.js AS1000_MFD __MSFSBA_DA42G1000 Resources/coherent-da42-agent.js
//
// Requires the sim running with the aircraft loaded, and NOTHING ELSE holding the display's
// inspector socket - Coherent GT allows exactly one per view, so close MSFSBA's display window
// (and be aware the DA40's background CAS monitor holds the PFD; see docs/tooling.md).

const fs = require('fs');
const path = require('path');
const { get, evalOn } = require(path.join(__dirname, 'g1000-cdp.js'));

const WS = id => 'ws://127.0.0.1:19999/devtools/page/' + id;

const PAGE_RE = process.argv[2] || 'AS1000_MFD';
const GLOBAL = process.argv[3] || '__MSFSBA_DA40G1000';
const AGENT = process.argv[4] ||
    path.join(__dirname, '..', 'MSFSBlindAssist', 'Resources', 'coherent-da40-g1000-agent.js');

/// ⚠️ THE VISIBILITY TEST IS THE WHOLE TOOL, AND THE OBVIOUS ONE IS WRONG.
///
/// Checking only the element's own computed style passes anything sitting inside a hidden
/// PARENT - and this instrument keeps its page selector, its dialogs and a second copy of the
/// CAS block in the DOM at all times. A leaf-only test therefore reported sixty to eighty
/// "unread" items on every single page, almost all of them furniture that was never on screen,
/// which is worse than no tool: a number that large gets skimmed and the three real gaps
/// buried in it are never read.
///
/// So the ancestor chain is walked to the root. The rect check stays as a second filter for
/// zero-sized and scrolled-away elements.
const SWEEP = `(function(){
  var A = window.__GLOBAL__;
  if (!A) return JSON.stringify({error:"agent not installed"});

  function shown(e){
    var n = e;
    while (n && n.nodeType === 1) {
      var s;
      try { s = getComputedStyle(n); } catch (x) { return false; }
      if (s.display === "none" || s.visibility === "hidden") return false;
      if (parseFloat(s.opacity) === 0) return false;
      n = n.parentElement;
    }
    var r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  // Everything the agent SAYS, flattened so formatting differences cannot cause a false gap:
  // the scrape may render "DIS 53.0NM" where the screen shows "53.0NM", and a raw string
  // compare would call that missing.
  function saidNow(){
    try { return A.rows().join(" ").toUpperCase().replace(/[^A-Z0-9]/g,""); }
    catch (e) { return null; }
  }

  function unread(){
    var said = saidNow();
    if (said === null) return {error:"rows() threw"};
    var seen = {}, out = [];
    var all = document.querySelectorAll("*");
    for (var i = 0; i < all.length; i++) {
      var e = all[i];
      if (e.children.length) continue;                 // leaves only - no double counting
      var t = (e.textContent || "").replace(/\\s+/g," ").trim();
      if (!t || t.length < 2 || t.length > 40) continue;
      // Bare numbers are tape graduations and compass ticks. They are not readable content
      // and including them drowns the report - the NUMBER a pilot wants is always labelled.
      if (/^[-+0-9.,:°%\\/]+$/.test(t)) continue;
      if (!shown(e)) continue;
      var key = t.toUpperCase().replace(/[^A-Z0-9]/g,"");
      if (!key || seen[key]) continue;
      seen[key] = 1;
      if (said.indexOf(key) < 0) out.push(t);
    }
    return {items: out};
  }

  // Only pages the instrument ACTUALLY BUILT. A stub carries an empty key, its knob does
  // nothing for a sighted pilot either, and sweeping it measures the page underneath.
  var pages = [];
  try {
    var map = A.M.pageMap();
    for (var g = 0; g < map.length; g++)
      for (var p = 0; p < map[g].pages.length; p++)
        if (map[g].pages[p].key)
          pages.push({label: map[g].group + " / " + map[g].pages[p].name,
                      key: map[g].pages[p].key});
  } catch (e) { return JSON.stringify({error:"pageMap failed: " + e.message}); }

  window.__G1000_COVERAGE = {done:false, pages:{}, order:[], total:pages.length};
  var i = 0, startPage = null;
  try { startPage = A.M.pageKey(); } catch (e) {}

  function step(){
    if (i >= pages.length) {
      if (startPage) { try { A.M.goPage(startPage); } catch (e) {} }
      window.__G1000_COVERAGE.done = true;
      return;
    }
    var pg = pages[i++];
    var r;
    try { A.M.escape(); } catch (e) {}          // a dialog left open measures the dialog
    try { r = A.M.goPage(pg.key); } catch (e) { r = "threw: " + e.message; }

    // The page selector commits about a second after the last turn, and a page read before
    // it has drawn reports the page the probe has just left.
    setTimeout(function(){
      var res = (r === "ok") ? unread() : {error: String(r)};
      window.__G1000_COVERAGE.pages[pg.label] = res;
      window.__G1000_COVERAGE.order.push(pg.label);
      step();
    }, 1100);
  }
  step();
  return JSON.stringify({started: pages.length});
})()`;

const READ = `JSON.stringify(window.__G1000_COVERAGE || {done:false, pages:{}, order:[]})`;

const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
    const list = JSON.parse(await get('/pagelist.json'));
    const pages = list.pages || list;
    const target = pages.find(p => new RegExp(PAGE_RE).test(p.title || ''));
    if (!target) {
        console.error(`No Coherent page matching /${PAGE_RE}/. Is the sim running with the aircraft loaded?`);
        console.error('Available: ' + pages.map(p => p.title).join(', '));
        process.exit(1);
    }
    const url = WS(target.id);

    // Install the agent. It is idempotent - installing over a live one is how MSFSBA itself
    // recovers a socket - and doing it unconditionally means the probe never reports a false
    // "agent not installed" just because nothing had opened the display yet.
    const agentJs = fs.readFileSync(AGENT, 'utf8');
    const installed = await evalOn(url, agentJs, 25000);
    if (!/MSFSBA_DISP_INSTALLED/.test(String(installed))) {
        console.error('Agent did not install. It must return the MSFSBA_DISP_INSTALLED token.');
        console.error('Got: ' + String(installed).slice(0, 300));
        process.exit(1);
    }
    console.log(`agent: ${String(installed).trim()}   page: ${target.title}\n`);

    const started = await evalOn(url, SWEEP.replace('__GLOBAL__', GLOBAL), 20000);
    let meta;
    try { meta = JSON.parse(started); } catch (e) { meta = {}; }
    if (meta.error) { console.error('Sweep refused: ' + meta.error); process.exit(1); }
    console.log(`sweeping ${meta.started} real pages (stubs skipped)...\n`);

    // Poll rather than guess a duration: page count varies by aircraft, and a fixed wait is
    // either a stall or a truncated report.
    let data = null;
    for (let waited = 0; waited < 120000; waited += 1500) {
        await sleep(1500);
        try { data = JSON.parse(await evalOn(url, READ, 10000)); } catch (e) { continue; }
        if (data && data.done) break;
        process.stderr.write('.');
    }
    process.stderr.write('\n');
    if (!data || !data.done) { console.error('Sweep did not finish.'); process.exit(1); }

    // ---- CHROME FILTER -------------------------------------------------------------------
    //
    // ⚠️ THE COUNT IS MEANINGLESS WITHOUT THIS. Softkey labels, the page-selector entries and
    // any dialog left mounted appear on EVERY page, so a raw per-page list is dominated by
    // furniture. Anything present nearly everywhere is chrome by definition - it belongs to
    // the instrument, not the page - and is reported ONCE, separately, rather than repeated
    // twenty times.
    const seenOn = new Map();
    const order = data.order || [];
    for (const label of order) {
        const r = data.pages[label];
        if (!r || !r.items) continue;
        for (const t of new Set(r.items)) seenOn.set(t, (seenOn.get(t) || 0) + 1);
    }
    const n = order.length || 1;
    const CHROME_AT = Math.max(2, Math.ceil(n * 0.6));
    const chrome = [...seenOn.entries()].filter(([, c]) => c >= CHROME_AT).map(([t]) => t).sort();
    const chromeSet = new Set(chrome);

    let gaps = 0;
    console.log('================ PAGE-SPECIFIC UNREAD CONTENT ================');
    for (const label of order) {
        const r = data.pages[label];
        if (!r) continue;
        if (r.error) { console.log(`\n${label}\n   ! ${r.error}`); continue; }
        const own = [...new Set(r.items)].filter(t => !chromeSet.has(t)).sort();
        if (!own.length) continue;
        gaps += own.length;
        console.log(`\n${label}  (${own.length})`);
        for (const t of own) console.log('   ' + t);
    }
    if (!gaps) console.log('\n   nothing - every page\'s own content is read.');

    console.log(`\n================ INSTRUMENT CHROME (on >=${CHROME_AT}/${n} pages) ================`);
    console.log('Softkeys, page-selector entries and mounted dialogs. Reported once, not per page.');
    console.log('Worth a look ONLY if something here should be readable on demand.\n');
    console.log('   ' + (chrome.length ? chrome.join(' | ') : 'none'));
    console.log(`\nSUMMARY: ${gaps} page-specific unread item(s) across ${n} page(s); ${chrome.length} chrome item(s).`);
})().catch(e => { console.error('ERR ' + e.message); process.exit(1); });
