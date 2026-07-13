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
    creating: false,       // "+ New document" request in flight
    seen: { docs: {}, versions: {}, events: {} }, // id -> true, for new-row diff
    firstRender: { docs: true, versions: true, events: true },
    lastDoc: null,         // last fetched detail for the selected document
    flow: { busy: false, error: null, confirmingReject: false, docId: null, trace: null },
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
          '<span class="ecert-dash__hint">Click a document to see its versions &amp; events.</span>' +
          '<span class="ecert-dash__live">live</span>' +
        "</div>" +
        '<div class="ecert-grid">' +
          card("docs", "Documents", "",
            '<button class="ecert-card__action" type="button" data-act="new-doc">+ New document</button>') +
          card("flow", "Review flow", "Select a document") +
          '<div class="ecert-grid__row2">' +
            card("versions", "Document versions", "Select a document") +
            card("events", "Events", "Select a document") +
          "</div>" +
        "</div>" +
      "</section>"
    );
    return dash;
  }
  function card(key, title, sub, actionHtml) {
    return (
      '<div class="ecert-card" data-card="' + key + '">' +
        '<div class="ecert-card__head">' +
          '<span class="ecert-card__title">' + title + "</span>" +
          '<span class="ecert-card__count" data-count="' + key + '">0</span>' +
          (actionHtml || "") +
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
      var rows_ = body.querySelectorAll("tbody tr");
      rows_.forEach(function (tr) {
        tr.addEventListener("click", function () {
          // Move the highlight now, synchronously — don't wait for the next
          // 3s poll tick to confirm which row was picked. The detail panels
          // (versions/events/flow) update separately, on their own fetch.
          rows_.forEach(function (r) { r.classList.remove("is-active"); });
          tr.classList.add("is-active");
          opts.onClick(tr.getAttribute("data-id"));
        });
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
    // A freshly selected document starts with a clean flow panel: no stale
    // busy/error/confirm state left over from the previous one.
    if (state.flow.docId !== id) {
      state.flow = { busy: false, error: null, confirmingReject: false, docId: id, trace: null };
    }
    if (!id) {
      renderTable("versions", [], [], { empty: "Select a document." });
      renderTable("events", [], [], { empty: "Select a document." });
      setSub("versions", "Select a document");
      setSub("events", "Select a document");
      setSub("flow", "Select a document");
      renderFlow(null);
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
      setSub("flow", title);
      renderFlow(doc);

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

  // ---------- review-flow stepper ----------
  // A click-through version of the demo storyline (POST .../status,
  // .../observations, .../versions): submit → take for review → reject (with
  // observation) or approve → upload corrected version → approve, without
  // hand-filling Swagger's Try-it-out forms each time.
  var FLOW_STEPS = ["Created", "PendingReview", "UnderReview", "Approved"];
  var PERSONA = { author: "juan.author", reviewer: "maria.reviewer" };
  // Same wording as the "Paso 6" example in StorylineOpenApi so the two
  // demo paths (guided Swagger form vs. one-click flow) tell one story.
  var REJECT_REASON = "Corregir plazo y precio antes de reenviar.";

  function errorMessage(body, status) {
    if (body && body.errors) {
      var msgs = [];
      Object.keys(body.errors).forEach(function (k) {
        (body.errors[k] || []).forEach(function (m) { msgs.push(m); });
      });
      if (msgs.length) return msgs.join(" ");
    }
    return (body && (body.detail || body.title)) || ("Request failed (" + status + ")");
  }

  // Fires the HTTP call and resolves { status, body } on success; on failure
  // throws an Error carrying .status/.body so the trace panel can show the
  // real response either way (not just the happy path).
  function request(method, url, opts) {
    opts = opts || {};
    var init = { method: method };
    if (opts.json) {
      init.headers = { "Content-Type": "application/json", Accept: "application/json" };
      init.body = JSON.stringify(opts.json);
    } else if (opts.form) {
      init.body = opts.form; // browser sets the multipart boundary itself
    }
    return fetch(url, init).then(function (r) {
      return r.json().catch(function () { return null; }).then(function (body) {
        if (!r.ok) {
          var err = new Error(errorMessage(body, r.status));
          err.status = r.status;
          err.body = body;
          throw err;
        }
        return { status: r.status, body: body };
      });
    });
  }

  // Builds the { method, path, displayBody, exec } descriptor for a status
  // change, so the trace panel can show the exact route/body before exec()
  // is even called.
  function buildStatusAction(id, targetStatus, performedBy, reason) {
    var payload = { targetStatus: targetStatus, performedBy: performedBy };
    if (reason) payload.reason = reason;
    var path = API + "/" + id + "/status";
    return {
      method: "POST",
      path: path,
      displayBody: JSON.stringify(payload, null, 2),
      exec: function () { return request("POST", path, { json: payload }); },
    };
  }

  function buildUploadAction(id, uploadedBy) {
    // Mirrors DataSeeder.CreateMinimalPdf / attachSampleTo: a tiny but valid
    // PDF, unique per call so it's never rejected as a byte-identical dupe.
    var text = "ecert sample document " + Date.now();
    var file = new File([buildSamplePdf(text)], "ecert-sample.pdf", { type: "application/pdf" });
    var form = new FormData();
    form.append("UploadedBy", uploadedBy);
    form.append("File", file);
    var path = API + "/" + id + "/versions";
    return {
      method: "POST",
      path: path,
      displayBody: "UploadedBy: " + uploadedBy + "\nFile: " + file.name + " (" + file.size + " bytes, multipart/form-data)",
      exec: function () { return request("POST", path, { form: form }); },
    };
  }

  // "+ New document" in the Documents card header: registers a fresh sample
  // document (Paso 1, with a unique sample PDF attached) so the examiner can
  // run the whole review flow again without touching the Swagger form.
  function registerNewDocument(btn) {
    if (state.creating) return;
    state.creating = true;
    var originalLabel = btn.textContent;
    btn.disabled = true;
    btn.textContent = "Creating…";

    var text = "ecert sample document " + Date.now();
    var file = new File([buildSamplePdf(text)], "ecert-sample.pdf", { type: "application/pdf" });
    var form = new FormData();
    form.append("Title", "Contrato Demo " + new Date().toLocaleTimeString());
    form.append("Type", "Contract");
    form.append("UploadedBy", "juan.author");
    form.append("File", file);

    request("POST", API, { form: form }).then(function (result) {
      state.userPicked = true;
      state.activeId = result.body.id;
      return refreshDocuments();
    }).catch(function () {/* leave the dashboard as-is; next poll is unaffected */})
      .then(function () {
        state.creating = false;
        btn.disabled = false;
        btn.textContent = originalLabel;
      });
  }

  function runFlowAction(action) {
    if (state.flow.busy) return;
    state.flow.busy = true;
    state.flow.error = null;
    state.flow.trace = {
      method: action.method,
      path: action.path,
      requestBody: action.displayBody,
      status: null,
      responseBody: null,
    };
    rerenderFlow();
    action.exec().then(function (result) {
      state.flow.busy = false;
      state.flow.confirmingReject = false;
      state.flow.trace.status = result.status;
      state.flow.trace.responseBody = result.body ? JSON.stringify(result.body, null, 2) : "(empty body)";
      return refreshDocuments(); // pulls the fresh status into every panel right away
    }).catch(function (err) {
      state.flow.busy = false;
      state.flow.error = err.message || "Request failed.";
      state.flow.trace.status = err.status || null;
      state.flow.trace.responseBody = err.body ? JSON.stringify(err.body, null, 2) : null;
      rerenderFlow();
    });
  }

  function rerenderFlow() {
    renderFlow(state.lastDoc);
  }

  function flowActionsHtml(doc) {
    var disabled = state.flow.busy ? " disabled" : "";
    var html;
    switch (doc.status) {
      case "Created":
        html = '<button class="ecert-flow__btn" data-act="submit"' + disabled + '>Submit for review</button>';
        break;
      case "PendingReview":
        html = '<button class="ecert-flow__btn" data-act="take"' + disabled + '>Take for review</button>';
        break;
      case "UnderReview":
        if (state.flow.confirmingReject) {
          html =
            '<div class="ecert-flow__confirm">' +
              '<span class="ecert-flow__reason">“' + esc(REJECT_REASON) + '”</span>' +
              '<button class="ecert-flow__btn ecert-flow__btn--danger" data-act="reject-confirm"' + disabled + '>Confirm reject</button>' +
              '<button class="ecert-flow__btn ecert-flow__btn--ghost" data-act="reject-cancel"' + disabled + '>Cancel</button>' +
            "</div>";
        } else {
          html =
            '<button class="ecert-flow__btn ecert-flow__btn--primary" data-act="approve"' + disabled + '>Approve</button>' +
            '<button class="ecert-flow__btn ecert-flow__btn--danger" data-act="reject"' + disabled + '>Reject…</button>';
        }
        break;
      case "Rejected":
        html = '<button class="ecert-flow__btn" data-act="upload"' + disabled + '>Upload corrected version</button>';
        break;
      case "Approved":
        html = '<span class="ecert-muted">Approved — flow complete.</span>';
        break;
      default:
        html = '<span class="ecert-muted">' + esc(prettyEvent(doc.status)) + "</span>";
    }
    if (state.flow.error) {
      html += '<div class="ecert-flow__error">' + esc(state.flow.error) + "</div>";
    }
    return html;
  }

  function wireFlowActions(doc) {
    var body = bodyOf("flow");
    if (!body) return;
    var id = doc.id;
    // Each entry builds a fresh action descriptor per click (the upload one
    // needs a new sample file/timestamp every time).
    var actions = {
      submit: function () { return buildStatusAction(id, "PendingReview", PERSONA.author); },
      take: function () { return buildStatusAction(id, "UnderReview", PERSONA.reviewer); },
      approve: function () { return buildStatusAction(id, "Approved", PERSONA.reviewer); },
      "reject-confirm": function () { return buildStatusAction(id, "Rejected", PERSONA.reviewer, REJECT_REASON); },
      upload: function () { return buildUploadAction(id, PERSONA.author); },
    };
    body.querySelectorAll("[data-act]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var act = btn.getAttribute("data-act");
        if (act === "reject") {
          state.flow.confirmingReject = true;
          state.flow.error = null;
          return rerenderFlow();
        }
        if (act === "reject-cancel") {
          state.flow.confirmingReject = false;
          return rerenderFlow();
        }
        if (actions[act]) runFlowAction(actions[act]());
      });
    });
  }

  // Renders the "<method> <path>" line plus the request/response JSON side by
  // side, so the examiner sees exactly what the click just did over HTTP.
  function traceHtml(trace) {
    if (!trace) return "";
    var methodCls = "ecert-flow__method ecert-flow__method--" + trace.method.toLowerCase();
    var statusHtml = trace.status == null
      ? '<span class="ecert-flow__status is-pending">sending…</span>'
      : '<span class="ecert-flow__status' + (trace.status >= 400 ? " is-error" : " is-ok") + '">' + trace.status + "</span>";
    return (
      '<div class="ecert-flow__trace">' +
        '<div class="ecert-flow__route">' +
          '<span class="' + methodCls + '">' + esc(trace.method) + "</span>" +
          '<span class="ecert-mono">' + esc(trace.path) + "</span>" +
        "</div>" +
        '<div class="ecert-flow__io">' +
          '<div class="ecert-flow__block">' +
            '<div class="ecert-flow__blocklabel">Body</div>' +
            '<pre class="ecert-flow__pre">' + esc(trace.requestBody || "—") + "</pre>" +
          "</div>" +
          '<div class="ecert-flow__block">' +
            '<div class="ecert-flow__blocklabel">Response ' + statusHtml + "</div>" +
            '<pre class="ecert-flow__pre">' + esc(trace.responseBody || "") + "</pre>" +
          "</div>" +
        "</div>" +
      "</div>"
    );
  }

  // Renders the step pills (dimmed except the current one) plus the button(s)
  // for whatever transition is valid from here, per DocumentStateMachine.
  function renderFlow(doc) {
    state.lastDoc = doc;
    var body = bodyOf("flow");
    if (!body) return;
    if (!doc) {
      body.innerHTML = '<div class="ecert-empty">Select a document.</div>';
      return;
    }

    var stepsHtml = FLOW_STEPS.map(function (s) {
      var cls = "ecert-pill ecert-pill--" + s + (s === doc.status ? " is-current-step" : " is-dim");
      return '<span class="' + cls + '">' + esc(prettyEvent(s)) + "</span>";
    }).join('<span class="ecert-arrow">→</span>');
    if (doc.status === "Rejected") {
      stepsHtml += '<span class="ecert-arrow">→</span>' +
        '<span class="ecert-pill ecert-pill--Rejected is-current-step">Rejected</span>';
    }

    body.innerHTML =
      '<div class="ecert-flow">' +
        '<div class="ecert-flow__steps">' + stepsHtml + "</div>" +
        '<div class="ecert-flow__actions">' + flowActionsHtml(doc) + "</div>" +
        traceHtml(state.flow.trace) +
      "</div>";

    wireFlowActions(doc);
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
    dash.querySelector('[data-act="new-doc"]').addEventListener("click", function () {
      registerNewDocument(this);
    });

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
