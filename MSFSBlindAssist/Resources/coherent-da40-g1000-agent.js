// MSFSBA in-page agent for the COWS DA40's Working Title G1000 PFD.
//
// SCOPE, deliberately narrow: this scrapes ONLY what the display alone knows — the CAS
// window, the FMA, the navigation source and the softkey labels. Airspeed, altitude,
// vertical speed, heading and the barometric setting are NOT scraped, because they are
// stock SimVars MSFSBA already reads, and the DOM is a far worse source for them: the
// airspeed box is a SCROLLER, so its textContent is the whole digit strip
// ("210987654321 123456789012-2109876543210...") rather than the value. Scraping a number
// that SimConnect already answers correctly would be choosing the fragile source.
//
// Installed once per connection under window.__MSFSBA_DA40G1000.
(function () {
    var A = {};

    A.VERSION = 1;

    function visible(el) {
        if (!el) return false;
        var s = window.getComputedStyle(el);
        if (s.display === "none" || s.visibility === "hidden") return false;
        var r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }
    A.visible = visible;

    function text(el) {
        return el ? (el.textContent || "").replace(/\s+/g, " ").trim() : "";
    }

    // textContent CONCATENATES with no separator, so a pane full of little spans reads
    // back as "Timer0:00:00UpStart?VNE172KT On". Collecting the text NODES and joining
    // them with a space is what turns that into "Timer 0:00:00 Up Start? VNE 172 KT On".
    function spacedText(el) {
        if (!el) return "";
        var parts = [];
        var walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, null, false);
        var n;
        while ((n = walker.nextNode())) {
            var t = (n.nodeValue || "").replace(/\s+/g, " ").trim();
            if (t) parts.push(t);
        }

        // Join with a space BETWEEN WORDS but not inside a number. The G1000 renders a
        // clock and a scrolling readout as one span per character, so a blanket space
        // turns 0:00:00 into "0 : 0 0 : 0 0" - unreadable as a time. Two adjacent
        // digit-ish characters belong to one value; anything else gets the space.
        var out = "";
        for (var i = 0; i < parts.length; i++) {
            if (!out) { out = parts[i]; continue; }
            var a = out.charAt(out.length - 1);
            var b = parts[i].charAt(0);
            var glue = /[0-9.:]/.test(a) && /[0-9.:]/.test(b);
            out += (glue ? "" : " ") + parts[i];
        }

        // A field the pilot has not filled in renders as a row of underscores. "blank"
        // is what that means, and it is one word instead of five.
        return out.replace(/(?:_\s*){2,}/g, "blank ").replace(/\s+/g, " ").trim();
    }

    function classList(el) {
        var cn = el.className;
        if (cn && cn.baseVal !== undefined) cn = cn.baseVal;
        return typeof cn === "string" ? cn.split(/\s+/) : [];
    }

    // ---------------------------------------------------------------- CAS
    //
    // The CAS window PRE-ALLOCATES its rows: 32 of them on this aircraft, and a cleared
    // message leaves its slot in the DOM with display:none rather than removing it. So a
    // row counts only when it is BOTH visible and non-empty — reading the node list alone
    // would report every message the flight has ever raised.
    //
    // Severity comes from the CLASS LIST and never from colour. The rows carry a "new"
    // class while the message is flashing, which changes the rendered colour underneath
    // it, so a colour test reports the same caution differently depending on when it is
    // sampled.
    A.cas = function () {
        var host = document.querySelector(".cas-display");
        if (!host) return [];

        var rows = host.querySelectorAll(".annunciation");
        var out = [];

        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            var t = text(row);
            if (!t || !visible(row)) continue;

            var cls = classList(row);
            var severity = "advisory";
            if (cls.indexOf("warning") >= 0) severity = "warning";
            else if (cls.indexOf("caution") >= 0) severity = "caution";
            else if (cls.indexOf("safe-op") >= 0) severity = "status";

            out.push({ text: t, severity: severity, isNew: cls.indexOf("new") >= 0 });
        }

        return out;
    };

    // ---------------------------------------------------------------- FMA
    A.fma = function () {
        function one(sel) {
            var e = document.querySelector(sel);
            return e && visible(e) ? text(e) : "";
        }
        return {
            autopilot: one(".fma-ap-label"),
            yawDamper: one(".fma-yd-label"),
            vertical: one(".fma-ap-vertical-modes")
        };
    };

    // ------------------------------------------------------- navigation source
    A.nav = function () {
        function one(sel) {
            var e = document.querySelector(sel);
            return e && visible(e) ? text(e) : "";
        }
        return {
            source: one(".hsi-nav-source") || one(".hsi-map-nav-src"),
            sensitivity: one(".hsi-nav-sensitivity") || one(".hsi-map-nav-sensitivity"),
            suspended: (one(".hsi-nav-susp") || one(".hsi-map-nav-susp")) !== "",
            crossTrack: one(".hsi-gps-xtrack-number"),
            message: one(".hsi-gps-msg")
        };
    };

    // ---------------------------------------------------------------- softkeys
    //
    // The bezel's twelve softkeys, in order, with the label the display is CURRENTLY
    // showing — they change per page, so they have to be read live rather than laid out
    // as a fixed list. A blank slot is a real state and is reported as such: the pilot
    // needs to know a key does nothing here, not that it is missing.
    A.softkeys = function () {
        var host = document.querySelector(".softkeys-container");
        if (!host) return [];

        var tabs = host.querySelectorAll(".softkey-tab");
        var out = [];

        for (var i = 0; i < tabs.length; i++) {
            var tab = tabs[i];
            var label = text(tab.querySelector(".softkey-tab-label"));
            var value = text(tab.querySelector(".softkey-tab-value"));
            var cls = classList(tab);

            out.push({
                index: i + 1,
                label: label,
                value: value,
                active: cls.indexOf("active") >= 0 || cls.indexOf("highlight") >= 0
            });
        }

        return out;
    };

    // ------------------------------------------------------------ popout panes
    //
    // Several softkeys do not change the softkey row at all - they open a DIALOG over the
    // display. Tmr/Ref and Nearest are the two on the PFD, and until this existed they
    // were pressable but unreadable: the key worked, and nothing could be read back.
    //
    // A dialog is open when its OPACITY is not zero, and by nothing else. All four of the
    // PFD's dialogs are permanently display:block, visibility:visible, 310 by 220, and
    // carrying the class "quickclosed" whether open or shut - measured. A closed one is
    // simply transparent. Filtering on display, visibility, size or that class name finds
    // four windows that are not on screen; filtering on opacity finds the one that is.
    //
    // The four are Nearest Airports, Alerts (the full text behind the CAS abbreviations),
    // Timer and References, and ADF/DME Tuning.
    A.panes = function () {
        var out = [];
        var dialogs = document.querySelectorAll(".popout-dialog");

        for (var i = 0; i < dialogs.length; i++) {
            var d = dialogs[i];
            var cls = classList(d).join(" ");
            if (!visible(d)) continue;
            if (parseFloat(window.getComputedStyle(d).opacity || "1") < 0.05) continue;

            var title = text(d.querySelector(".popout-dialog-title")) ||
                        (/nearest-airport/.test(cls) ? "Nearest Airports" : "");
            var lines = [];

            // The nearest-airport list has NAMED fields, so it is read as fields rather
            // than as its own textContent - which runs together into "VCBI0200.4 NMILS".
            var items = d.querySelectorAll(".nearest-airport-item");
            if (items.length) {
                for (var k = 0; k < items.length; k++) {
                    var it = items[k];
                    var parts = [
                        text(it.querySelector(".nearest-airport-name")),
                        text(it.querySelector(".nearest-airport-bearing")),
                        text(it.querySelector(".nearest-airport-distance")),
                        text(it.querySelector(".nearest-airport-approach")),
                        text(it.querySelector(".nearest-airport-freqtype")),
                        text(it.querySelector(".nearest-airport-frequency")),
                        text(it.querySelector(".nearest-airport-rwy-number"))
                    ].filter(function (x) { return x; });
                    if (parts.length) lines.push(parts.join(", "));
                }
            } else {
                // One line per row of the pane, so a reader can arrow through it, with
                // the text nodes spaced rather than run together.
                var kids = d.children.length === 1 ? d.children[0].children : d.children;
                for (var c = 0; c < kids.length; c++) {
                    var line = spacedText(kids[c]);
                    if (line) lines.push(line);
                }
                if (!lines.length) {
                    var whole = spacedText(d);
                    if (whole) lines.push(whole);
                }
            }

            // No dialog carries a title element, so the pane names itself with its own
            // first line - "Timer", "Alerts", "ADF/DME TUNING" - which is what a sighted
            // pilot reads at the top of it anyway.
            if (!title && lines.length) title = lines.shift();
            if (lines.length || title) out.push({ title: title || "Window", lines: lines });
        }

        return out;
    };

    // ---------------------------------------------------------------- pressing
    //
    // ONE SOFTKEY IS ONE BUTTON. There is no such thing as several items behind one key.
    // What a press does is either CYCLE A VALUE in place (CDI steps GPS, VOR1, VOR2, and
    // the current one shows in softkey-tab-value) or REPLACE ALL TWELVE KEYS with a
    // sub-menu — verified live: pressing 4, "PFD Opt", turned the row into
    // SVT / blank / Wind / DME / Bearing 1 / blank / Bearing 2 / blank / ALT Units /
    // STD Baro / Back / Alerts, and pressing 11, "Back", restored it. So the menu is a
    // TREE, and the way out is always a Back key somewhere in the row rather than a
    // separate gesture.
    //
    // Blank slots are real and are reported as blank: on that sub-page keys 2, 6 and 8 do
    // nothing, and a pilot needs to know that rather than wonder whether the key is
    // missing.
    A.press = function (index, side) {
        if (!(index >= 1 && index <= 12)) return "range";
        var evt = "H:AS1000_" + (side === "MFD" ? "MFD" : "PFD") + "_SOFTKEYS_" + index;
        try {
            SimVar.SetSimVarValue(evt, "number", 1);
            return "ok";
        } catch (e) {
            return "error " + e;
        }
    };

    A.snapshot = function () {
        return JSON.stringify({
            v: A.VERSION,
            cas: A.cas(),
            panes: A.panes(),
            fma: A.fma(),
            nav: A.nav(),
            softkeys: A.softkeys()
        });
    };

    // ------------------------------------------------- the shared display contract
    //
    // CoherentDisplayClient is generic: it resolves a view by title needle, installs an
    // agent, and polls `__MSFSBA_DISP.scrape()` for a JSON {ok, rows}. Exposing that
    // contract here means the DA40 needs no client of its own and inherits every piece of
    // connection handling that client already gets right - re-installing on a still-open
    // socket, the connect lock, the reconnect backoff.
    A.rows = function () {
        var rows = [];

        var cas = A.cas();
        rows.push("CAS messages: " + (cas.length === 0 ? "none" : cas.length));
        for (var i = 0; i < cas.length; i++) {
            var label = cas[i].severity === "warning" ? "WARNING"
                : cas[i].severity === "caution" ? "Caution"
                : cas[i].severity === "status" ? "Status" : "Advisory";
            rows.push("  " + label + ": " + cas[i].text);
        }

        var f = A.fma();
        var modes = [f.autopilot, f.vertical, f.yawDamper].filter(function (x) { return x; });
        rows.push("Autopilot: " + (modes.length ? modes.join(", ") : "off"));

        var n = A.nav();
        rows.push("Navigation source: " + (n.source || "not shown")
            + (n.sensitivity ? ", " + n.sensitivity : "")
            + (n.suspended ? ", SUSPENDED" : ""));
        if (n.crossTrack) rows.push("Cross track: " + n.crossTrack);
        if (n.message) rows.push("GPS message: " + n.message);

        var panes = A.panes();
        for (var p = 0; p < panes.length; p++) {
            rows.push(panes[p].title + ":");
            for (var q = 0; q < panes[p].lines.length; q++) rows.push("  " + panes[p].lines[q]);
        }

        // "Softkey N:" is a CONTRACT with CowsDA40DisplayForm, which matches that prefix
        // to know which rows can be pressed and which key each one is. Change the wording
        // here and Enter stops working there.
        var keys = A.softkeys();
        for (var k = 0; k < keys.length; k++) {
            var key = keys[k];
            rows.push("Softkey " + key.index + ": "
                + (key.label || "blank")
                + (key.value ? " " + key.value : "")
                + (key.active ? ", selected" : ""));
        }

        return rows;
    };

    window.__MSFSBA_DISP = {
        scrape: function () {
            try {
                return JSON.stringify({ ok: true, rows: A.rows() });
            } catch (e) {
                return JSON.stringify({ ok: false, error: String(e) });
            }
        }
    };

    window.__MSFSBA_DA40G1000 = A;

    // MUST return a string containing MSFSBA_DISP_INSTALLED. CoherentDisplayClient tests
    // for that exact token to decide the agent is in place; anything else leaves
    // _agentInstalled false, so EnsureConnected returns false and the poll loop retries
    // for ever WITHOUT raising an error - a window stuck on "Connecting..." with nothing
    // in the log. This agent returned its own version string at first and did exactly
    // that. The token is the contract; the version rides alongside it.
    return "MSFSBA_DISP_INSTALLED da40-g1000 v" + A.VERSION;
})();
