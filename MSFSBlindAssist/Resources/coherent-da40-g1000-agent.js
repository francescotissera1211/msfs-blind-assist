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

    A.snapshot = function () {
        return JSON.stringify({
            v: A.VERSION,
            cas: A.cas(),
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
    return "DA40 G1000 agent v" + A.VERSION;
})();
