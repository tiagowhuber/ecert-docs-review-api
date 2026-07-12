/* ecert Document Review — Swagger UI enhancements
   Loaded via UseSwaggerUI(options.InjectJavascript).

   Adds three things to the stock Swagger page:
   1. A live dashboard (Documents / Versions / Events) that polls the API and
      highlights newly-created rows, so the examiner *sees* each call land.
   2. One-click "Register": auto-attaches a valid sample PDF to any empty file
      input, so with Try-it-out on and examples pre-filled they just press Execute.
*/
(function () {
  "use strict";

  var API = "/api/documents";
  var POLL_MS = 3000;

  var state = {
    activeId: null,        // selected document id
    userPicked: false,     // has the examiner clicked a row themselves?
    seen: { docs: {}, versions: {}, events: {} }, // id -> true, for new-row diff
    firstRender: { docs: true, versions: true, events: true },
  };

  // ---------- helpers ----------
  function el(html) {
    var t = document.createElement("template");
    t.innerHTML = html.trim();
    return t.content.firstChild;
  }
  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
  function fmtDate(s) {
    if (!s) return "—";
    var d = new Date(s);
    if (isNaN(d)) return esc(s);
    return d.toLocaleString(undefined, {
      month: "short", day: "numeric", hour: "2-digit", minute: "2-digit",
    });
  }
  function fmtBytes(n) {
    if (n == null) return "—";
    if (n < 1024) return n + " B";
    if (n < 1024 * 1024) return (n / 1024).toFixed(1) + " KB";
    return (n / 1024 / 1024).toFixed(1) + " MB";
  }
  function pill(value, kind) {
    var cls = kind === "type" ? "ecert-pill ecert-pill--type"
      : "ecert-pill ecert-pill--" + value;
    return '<span class="' + cls + '">' + esc(value) + "</span>";
  }
  function getJson(url) {
    return fetch(url, { headers: { Accept: "application/json" } }).then(function (r) {
      if (!r.ok) throw new Error(r.status + " " + url);
      return r.json();
    });
  }

  // ---------- dashboard DOM ----------
  function buildDashboard() {
    var dash = el(
      '<section class="ecert-dash">' +
        '<div class="ecert-dash__head">' +
          "<h2>Live dashboard</h2>" +
          '<span class="ecert-dash__hint">Run an operation below and watch the rows appear. Click a document to see its versions &amp; events.</span>' +
          '<span class="ecert-dash__live">live</span>' +
        "</div>" +
        '<div class="ecert-grid">' +
          card("docs", "Documents", "") +
          '<div class="ecert-grid__row2">' +
            card("versions", "Document versions", "Select a document") +
            card("events", "Events", "Select a document") +
          "</div>" +
        "</div>" +
      "</section>"
    );
    return dash;
  }
  function card(key, title, sub) {
    return (
      '<div class="ecert-card" data-card="' + key + '">' +
        '<div class="ecert-card__head">' +
          '<span class="ecert-card__title">' + title + "</span>" +
          '<span class="ecert-card__count" data-count="' + key + '">0</span>' +
          '<span class="ecert-card__sub" data-sub="' + key + '">' + sub + "</span>" +
        "</div>" +
        '<div class="ecert-card__body"><div class="ecert-empty">Loading…</div></div>' +
      "</div>"
    );
  }

  function bodyOf(key) {
    return document.querySelector('.ecert-card[data-card="' + key + '"] .ecert-card__body');
  }
  function setCount(key, n) {
    var c = document.querySelector('[data-count="' + key + '"]');
    if (c) c.textContent = n;
  }
  function setSub(key, text) {
    var s = document.querySelector('[data-sub="' + key + '"]');
    if (s) s.textContent = text;
  }

  // Diff the current rows against what we rendered last time and return an
  // isNew(id) lookup, so freshly-created rows can flash. Records the ids as
  // seen and clears the first-render flag (the initial paint never flashes).
  function markSeen(key, ids) {
    var seen = state.seen[key];
    var first = state.firstRender[key];
    var isNew = {};
    var nextSeen = {};
    ids.forEach(function (id) {
      isNew[id] = !first && !seen[id];
      nextSeen[id] = true;
    });
    state.seen[key] = nextSeen;
    state.firstRender[key] = false;
    return isNew;
  }

  // Render a table into a card body, flagging rows whose id is newly seen.
  function renderTable(key, columns, rows, opts) {
    opts = opts || {};
    var body = bodyOf(key);
    if (!body) return;
    setCount(key, rows.length);
    if (!rows.length) {
      body.innerHTML = '<div class="ecert-empty">' + (opts.empty || "Nothing yet.") + "</div>";
      return;
    }
    var isNew = markSeen(key, rows.map(function (r) { return r._id; }));
    var head = "<tr>" + columns.map(function (c) { return "<th>" + c.label + "</th>"; }).join("") + "</tr>";
    var trs = rows.map(function (row) {
      var cls = [];
      if (opts.clickable) cls.push("is-clickable");
      if (isNew[row._id]) cls.push("is-new");
      if (opts.activeId != null && row._id === opts.activeId) cls.push("is-active");
      var tds = columns.map(function (c) { return "<td>" + c.cell(row) + "</td>"; }).join("");
      return '<tr class="' + cls.join(" ") + '" data-id="' + esc(row._id) + '">' + tds + "</tr>";
    }).join("");
    body.innerHTML = '<table class="ecert-table"><thead>' + head + "</thead><tbody>" + trs + "</tbody></table>";

    if (opts.clickable) {
      body.querySelectorAll("tbody tr").forEach(function (tr) {
        tr.addEventListener("click", function () { opts.onClick(tr.getAttribute("data-id")); });
      });
    }
  }

  // Render a vertical list into a card body. Narrow half-width cards (versions,
  // events) read better as stacked rows than as many-column tables that scroll
  // sideways. opts.item(row) returns the inner HTML for one <li>.
  function renderList(key, rows, opts) {
    opts = opts || {};
    var body = bodyOf(key);
    if (!body) return;
    setCount(key, rows.length);
    if (!rows.length) {
      body.innerHTML = '<div class="ecert-empty">' + (opts.empty || "Nothing yet.") + "</div>";
      return;
    }
    var isNew = markSeen(key, rows.map(function (r) { return r._id; }));
    var items = rows.map(function (row) {
      return '<li class="ecert-litem' + (isNew[row._id] ? " is-new" : "") +
        '" data-id="' + esc(row._id) + '">' + opts.item(row) + "</li>";
    }).join("");
    body.innerHTML = '<ul class="ecert-list' + (opts.modifier ? " " + opts.modifier : "") + '">' + items + "</ul>";
  }

  // ---------- data loading ----------
  function refreshDocuments() {
    return getJson(API).then(function (docs) {
      // keep a stable active selection; default to the newest document
      if (!state.userPicked || !docs.some(function (d) { return d.id === state.activeId; })) {
        state.activeId = docs.length ? docs[0].id : null;
      }
      renderTable("docs",
        [
          { label: "Title", cell: function (d) { return esc(d.title); } },
          { label: "Type", cell: function (d) { return pill(d.type, "type"); } },
          { label: "Status", cell: function (d) { return pill(d.status); } },
          { label: "Ver.", cell: function (d) { return d.currentVersionNumber == null ? "—" : "v" + d.currentVersionNumber; } },
          { label: "Created", cell: function (d) { return '<span class="ecert-muted">' + fmtDate(d.createdAt) + "</span>"; } },
        ],
        docs.map(function (d) { d._id = d.id; return d; }),
        {
          clickable: true,
          activeId: state.activeId,
          empty: "No documents yet — register one below.",
          onClick: function (id) { state.userPicked = true; state.activeId = id; refreshDetail(); },
        }
      );
      return refreshDetail();
    }).catch(function () {/* API not up yet; next poll retries */});
  }

  function refreshDetail() {
    var id = state.activeId;
    if (!id) {
      renderTable("versions", [], [], { empty: "Select a document." });
      renderTable("events", [], [], { empty: "Select a document." });
      setSub("versions", "Select a document");
      setSub("events", "Select a document");
      return Promise.resolve();
    }
    return Promise.all([
      getJson(API + "/" + id),
      getJson(API + "/" + id + "/history"),
    ]).then(function (res) {
      var doc = res[0], events = res[1];
      var title = doc.title.length > 28 ? doc.title.slice(0, 27) + "…" : doc.title;
      setSub("versions", title);
      setSub("events", title);

      var isCurrent = doc.currentVersionNumber;
      renderList("versions", (doc.versions || []).map(function (v) { v._id = v.id; return v; }), {
        empty: "No versions.",
        item: function (v) {
          var current = v.versionNumber === isCurrent;
          var pages = v.pageCount == null ? "—" : v.pageCount + (v.pageCount === 1 ? " page" : " pages");
          var meta = [pages, fmtBytes(v.fileSizeBytes), esc(v.uploadedBy), fmtDate(v.uploadedAt)]
            .map(function (x) { return "<span>" + x + "</span>"; }).join('<span class="ecert-dot-sep">·</span>');
          return (
            '<span class="ecert-vbadge' + (current ? " is-current" : "") + '">v' + v.versionNumber + "</span>" +
            '<div class="ecert-litem__main">' +
              '<div class="ecert-litem__title ecert-mono">' + esc(v.fileName) +
                (current ? '<span class="ecert-tag-current">current</span>' : "") + "</div>" +
              '<div class="ecert-litem__meta">' + meta + "</div>" +
            "</div>"
          );
        },
      });

      renderList("events", events.map(function (e) { e._id = e.id; return e; }), {
        empty: "No events.",
        modifier: "ecert-list--timeline",
        item: function (e) {
          var transition = "";
          if (e.fromStatus || e.toStatus) {
            transition = '<span class="ecert-transition">' + pill(e.fromStatus || "—") +
              '<span class="ecert-arrow">→</span>' + pill(e.toStatus || "—") + "</span>";
          }
          var detail = e.details ? esc(e.details) : "";
          var meta = e.performedBy
            ? (detail ? detail + '<span class="ecert-dot-sep">·</span>' : "") + esc(e.performedBy)
            : detail;
          return (
            '<span class="ecert-tl__dot ' + eventDotClass(e) + '"></span>' +
            '<div class="ecert-tl__body">' +
              '<div class="ecert-tl__row">' +
                '<span class="ecert-tl__event">' + esc(prettyEvent(e.eventType)) + "</span>" +
                transition +
                '<span class="ecert-tl__when">' + fmtDate(e.occurredAt) + "</span>" +
              "</div>" +
              (meta ? '<div class="ecert-tl__detail">' + meta + "</div>" : "") +
            "</div>"
          );
        },
      });
    }).catch(function () {/* doc may have vanished; next poll re-selects */});
  }

  function prettyEvent(t) {
    return String(t).replace(/([a-z])([A-Z])/g, "$1 $2");
  }

  // Colour the timeline dot by the status the event landed on; fall back to a
  // brand dot for uploads/creation events that don't carry a transition.
  function eventDotClass(e) {
    if (e.toStatus) return "is-" + e.toStatus;
    return "is-neutral";
  }

  // ---------- one-click sample PDF ----------
  // Mirrors DataSeeder.CreateMinimalPdf: a tiny but structurally valid
  // single-page PDF with correct xref offsets, so PdfPigAnalyzer reads it and
  // fills PageCount = 1.
  function buildSamplePdf(text) {
    var content = "BT /F1 18 Tf 72 720 Td (" + text + ") Tj ET";
    var objects = [
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
      "<< /Length " + content.length + " >>\nstream\n" + content + "\nendstream",
      "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ];
    var pdf = "%PDF-1.4\n";
    var offsets = [];
    for (var i = 0; i < objects.length; i++) {
      offsets.push(pdf.length);
      pdf += (i + 1) + " 0 obj\n" + objects[i] + "\nendobj\n";
    }
    var xref = pdf.length;
    pdf += "xref\n0 " + (objects.length + 1) + "\n0000000000 65535 f \n";
    offsets.forEach(function (o) { pdf += String(o).padStart(10, "0") + " 00000 n \n"; });
    pdf += "trailer\n<< /Size " + (objects.length + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF";
    return pdf;
  }

  function attachSampleTo(input) {
    // Make each generated PDF unique (append a timestamp token, ASCII digits
    // only) so uploading a new version is never rejected as a byte-identical
    // duplicate of the current one.
    var text = "ecert sample document " + Date.now();
    var file = new File([buildSamplePdf(text)], "ecert-sample.pdf", {
      type: "application/pdf",
    });
    var dt = new DataTransfer();
    dt.items.add(file);
    input.files = dt.files;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }

  // Fill any empty PDF file input exactly once, so both Register and
  // Upload-version work with a single Execute. The examiner can still pick
  // their own file (we never touch an input that already has one).
  function fillFileInputs() {
    document.querySelectorAll('input[type="file"]:not([data-ecert-filled])').forEach(function (input) {
      input.setAttribute("data-ecert-filled", "1");
      if (input.files && input.files.length) return;
      try { attachSampleTo(input); } catch (e) {/* DataTransfer unsupported */}
    });
  }

  // ---------- boot ----------
  function boot() {
    // Anchor to the rendered info block (title + description). Swagger renders
    // it asynchronously, so wait for that exact node — using a different
    // selector to gate vs. to insert caused the dashboard to mount above the
    // title on some loads.
    var info = document.querySelector(".swagger-ui .information-container");
    if (!info) return setTimeout(boot, 120);
    if (document.querySelector(".ecert-dash")) return; // already mounted

    var dash = buildDashboard();
    info.parentNode.insertBefore(dash, info.nextSibling);

    refreshDocuments();
    setInterval(refreshDocuments, POLL_MS);

    // auto-attach sample PDFs as operations expand
    fillFileInputs();
    new MutationObserver(function () { fillFileInputs(); })
      .observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
