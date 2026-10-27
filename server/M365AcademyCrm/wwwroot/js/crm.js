// M365 Academy CRM - front-end behaviour
// No build step, no dependencies: plain DOM APIs only (same as the website).

(function () {
  "use strict";

  document.addEventListener("DOMContentLoaded", function () {
    initNav();
    initConfirm();
    initAutoSubmit();
    initBoard();
    initQuoteLines();
    initDealForm();
    initLeadConvert();
  });

  function initNav() {
    var toggle = document.querySelector(".nav-toggle");
    var links = document.querySelector(".nav-links");
    if (!toggle || !links) return;

    toggle.addEventListener("click", function () {
      var isOpen = links.classList.toggle("is-open");
      toggle.setAttribute("aria-expanded", isOpen ? "true" : "false");
    });
  }

  // <form data-confirm="Delete this?"> - asks before submitting.
  function initConfirm() {
    document.querySelectorAll("form[data-confirm]").forEach(function (form) {
      form.addEventListener("submit", function (e) {
        if (!window.confirm(form.getAttribute("data-confirm"))) {
          e.preventDefault();
        }
      });
    });
  }

  // <select data-autosubmit> - filter dropdowns apply as soon as they change.
  function initAutoSubmit() {
    document.querySelectorAll("[data-autosubmit]").forEach(function (el) {
      el.addEventListener("change", function () {
        if (el.form) el.form.submit();
      });
    });
  }

  function antiforgeryToken() {
    var input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
  }

  function postStage(dealId, stage, lostReason) {
    var body = new URLSearchParams();
    body.set("stage", stage);
    if (lostReason) body.set("lostReason", lostReason);
    body.set("__RequestVerificationToken", antiforgeryToken());
    return fetch("/deals/" + encodeURIComponent(dealId) + "/stage", {
      method: "POST",
      headers: { "Accept": "application/json", "Content-Type": "application/x-www-form-urlencoded" },
      body: body.toString(),
      credentials: "same-origin",
    }).then(function (res) {
      if (!res.ok) throw new Error("HTTP " + res.status);
      return res.json();
    });
  }

  // Pipeline board: drag a card to another column to change its stage.
  // Dropping on Lost asks for a reason first. Column totals are recomputed
  // client-side from each card's data-value/data-probability, and the page
  // reloads if the save fails so the board never shows a state that isn't saved.
  function initBoard() {
    var board = document.querySelector("[data-board]");
    if (!board) return;

    var dragged = null;
    var lostDialog = document.getElementById("lost-dialog");

    board.querySelectorAll(".board-card").forEach(function (card) {
      card.addEventListener("dragstart", function (e) {
        dragged = card;
        card.classList.add("is-dragging");
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/plain", card.getAttribute("data-deal-id"));
      });
      card.addEventListener("dragend", function () {
        card.classList.remove("is-dragging");
        dragged = null;
      });

      // Keyboard/touch fallback: the per-card stage picker.
      var select = card.querySelector(".stage-select select");
      if (select) {
        select.addEventListener("change", function () {
          var col = board.querySelector('.board-col[data-stage="' + select.value + '"]');
          if (col) moveCard(card, col);
        });
      }
    });

    board.querySelectorAll(".board-col").forEach(function (col) {
      col.addEventListener("dragover", function (e) {
        if (!dragged) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        col.classList.add("is-drop-target");
      });
      col.addEventListener("dragleave", function (e) {
        if (!col.contains(e.relatedTarget)) col.classList.remove("is-drop-target");
      });
      col.addEventListener("drop", function (e) {
        e.preventDefault();
        col.classList.remove("is-drop-target");
        if (dragged && dragged.closest(".board-col") !== col) moveCard(dragged, col);
      });
    });

    function moveCard(card, col) {
      var stage = col.getAttribute("data-stage");
      var fromCol = card.closest(".board-col");
      var dealId = card.getAttribute("data-deal-id");

      var commit = function (reason) {
        col.querySelector(".board-cards").prepend(card);
        var select = card.querySelector(".stage-select select");
        if (select) select.value = stage;
        recalc(fromCol);
        recalc(col);
        postStage(dealId, stage, reason).then(function (result) {
          card.setAttribute("data-probability", result.probability);
          recalc(col);
        }).catch(function () {
          alert("Couldn't save that stage change - reloading the board.");
          location.reload();
        });
      };

      if (stage === "Lost" && lostDialog && typeof lostDialog.showModal === "function") {
        var form = lostDialog.querySelector("form");
        form.onsubmit = function (e) {
          e.preventDefault();
          var reason = form.querySelector("select").value;
          lostDialog.close();
          commit(reason);
        };
        lostDialog.querySelector("[data-cancel]").onclick = function () {
          lostDialog.close();
          var select = card.querySelector(".stage-select select");
          if (select) select.value = fromCol.getAttribute("data-stage");
        };
        lostDialog.showModal();
      } else {
        commit(null);
      }
    }

    function recalc(col) {
      var total = 0;
      var weighted = 0;
      var cards = col.querySelectorAll(".board-card");
      cards.forEach(function (c) {
        var value = parseFloat(c.getAttribute("data-value")) || 0;
        var stage = col.getAttribute("data-stage");
        var probability = stage === "Won" ? 100 : stage === "Lost" ? 0 : parseFloat(c.getAttribute("data-probability")) || 0;
        total += value;
        weighted += value * probability / 100;
      });
      var fmt = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", maximumFractionDigits: 0 });
      var count = col.querySelector("[data-count]");
      var sum = col.querySelector("[data-total]");
      var w = col.querySelector("[data-weighted]");
      if (count) count.textContent = cards.length;
      if (sum) sum.textContent = fmt.format(total);
      if (w) w.textContent = fmt.format(weighted) + " weighted";
      var empty = col.querySelector("[data-empty]");
      if (empty) empty.hidden = cards.length > 0;
    }
  }

  // Quote editor: add/remove line rows, renumber field names so model
  // binding sees a contiguous Lines[0..n], and keep the totals live.
  function initQuoteLines() {
    var table = document.querySelector("[data-quote-lines]");
    if (!table) return;

    var tbody = table.querySelector("tbody");
    var template = document.getElementById("quote-line-template");
    var fmt = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP" });

    document.querySelector("[data-add-line]").addEventListener("click", function () {
      tbody.appendChild(template.content.cloneNode(true));
      renumber();
      recalc();
    });

    tbody.addEventListener("click", function (e) {
      var btn = e.target.closest("[data-remove-line]");
      if (!btn) return;
      if (tbody.rows.length > 1) {
        btn.closest("tr").remove();
      } else {
        btn.closest("tr").querySelectorAll("input").forEach(function (i) { i.value = i.name.indexOf("Quantity") >= 0 ? "1" : ""; });
      }
      renumber();
      recalc();
    });

    // Picking a catalogue course fills in a description and list price.
    tbody.addEventListener("change", function (e) {
      if (!e.target.matches("[data-course]")) return;
      var option = e.target.selectedOptions[0];
      var row = e.target.closest("tr");
      if (!option || !option.value) return;
      var desc = row.querySelector("[data-desc]");
      var price = row.querySelector("[data-unit-price]");
      if (desc && !desc.value) desc.value = option.getAttribute("data-title");
      if (price && (!price.value || parseFloat(price.value) === 0)) price.value = option.getAttribute("data-price");
      recalc();
    });

    table.closest("form").addEventListener("input", recalc);

    function renumber() {
      Array.prototype.forEach.call(tbody.rows, function (row, i) {
        row.querySelectorAll("[name]").forEach(function (el) {
          el.name = el.name.replace(/Lines\[\d+\]/, "Lines[" + i + "]");
        });
      });
    }

    function recalc() {
      var subtotal = 0;
      Array.prototype.forEach.call(tbody.rows, function (row) {
        var qty = parseFloat(row.querySelector("[data-qty]").value) || 0;
        var price = parseFloat(row.querySelector("[data-unit-price]").value) || 0;
        var line = qty * price;
        subtotal += line;
        row.querySelector("[data-line-total]").textContent = fmt.format(line);
      });
      var discountPct = parseFloat(document.getElementById("DiscountPercent").value) || 0;
      var vatPct = parseFloat(document.getElementById("VatRate").value) || 0;
      var discount = Math.round(subtotal * discountPct) / 100;
      var net = subtotal - discount;
      var vat = Math.round(net * vatPct) / 100;
      document.querySelector("[data-subtotal]").textContent = fmt.format(subtotal);
      document.querySelector("[data-discount]").textContent = "-" + fmt.format(discount);
      document.querySelector("[data-net]").textContent = fmt.format(net);
      document.querySelector("[data-vat]").textContent = fmt.format(vat);
      document.querySelector("[data-gross]").textContent = fmt.format(net + vat);
    }

    recalc();
  }

  // Deal form: stage sets the default probability, Lost shows the reason
  // field, and the catalogue price is offered for course x delegates.
  function initDealForm() {
    var form = document.querySelector("[data-deal-form]");
    if (!form) return;

    var defaults = { New: 10, Qualified: 30, ProposalSent: 50, Negotiation: 75, Won: 100, Lost: 0 };
    var stage = form.querySelector("#Stage");
    var probability = form.querySelector("#Probability");
    var lostRow = form.querySelector("[data-lost-reason]");

    function syncLost() {
      if (lostRow) lostRow.hidden = stage.value !== "Lost";
    }

    stage.addEventListener("change", function () {
      if (stage.value in defaults) probability.value = defaults[stage.value];
      syncLost();
    });
    syncLost();

    var course = form.querySelector("#CourseId");
    var format = form.querySelector("#DeliveryFormat");
    var delegates = form.querySelector("#Delegates");
    var hint = form.querySelector("[data-price-hint]");
    var value = form.querySelector("#Value");
    var fmt = new Intl.NumberFormat("en-GB", { style: "currency", currency: "GBP", maximumFractionDigits: 0 });

    function catalogueValue() {
      var option = course.selectedOptions[0];
      if (!option || !option.value) return null;
      var perDelegate = parseFloat(option.getAttribute("data-price")) || 0;
      var cohort = parseFloat(option.getAttribute("data-private-price")) || 0;
      var n = parseInt(delegates.value, 10) || 1;
      if (format.value === "PublicScheduled" || cohort === 0) return perDelegate * n;
      return cohort;
    }

    function updateHint() {
      var v = catalogueValue();
      if (!hint) return;
      if (v === null || v === 0) {
        hint.textContent = "";
        return;
      }
      hint.innerHTML = "";
      hint.append("Catalogue price: " + fmt.format(v) + " ");
      var use = document.createElement("button");
      use.type = "button";
      use.className = "btn btn-ghost btn-xs";
      use.textContent = "Use this";
      use.addEventListener("click", function () { value.value = v.toFixed(2); });
      hint.append(use);
    }

    [course, format, delegates].forEach(function (el) { if (el) el.addEventListener("change", updateHint); });
    if (delegates) delegates.addEventListener("input", updateHint);
    updateHint();
  }

  // Lead conversion: hide the deal fields when "create a deal" is unticked.
  function initLeadConvert() {
    var toggle = document.querySelector("[data-toggle-deal]");
    if (!toggle) return;
    var fields = document.querySelector("[data-deal-fields]");
    var sync = function () { fields.hidden = !toggle.checked; };
    toggle.addEventListener("change", sync);
    sync();
  }
})();
