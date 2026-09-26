// TagTuner website: the working previews, reveal on scroll, and the
// download links of the latest release.
(() => {
  "use strict";

  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const $ = (sel, root = document) => root.querySelector(sel);
  const el = (tag, cls, text) => {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  };
  const icon = (id) => {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("class", "icon");
    const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
    use.setAttribute("href", "#i-" + id);
    svg.appendChild(use);
    return svg;
  };

  // ── Hero: a miniature of the app. The metadata column shows the
  //    selection, "<mixed>" where it disagrees, and nothing without one.
  const album = { album: "Glasshaus", year: "2024", genre: "Indie Pop", art: "assets/cover-glasshaus.jpg" };
  const songs = [
    { title: "Paper Moons", artist: "Mira Voss" },
    { title: "Glasshaus", artist: "Mira Voss" },
    { title: "Salt in the Wound", artist: "Mira Voss" },
    { title: "Northbound", artist: "Mira Voss" },
    { title: "Quiet Hours", artist: "Mira Voss feat. Oda Lindqvist" },
    { title: "Kerosene Summer", artist: "Mira Voss" },
  ].map((s, i) => ({ ...album, ...s, n: i + 1 }));

  const heroRows = $("#hero-rows");
  if (heroRows) {
    const meta = (k) => $(`[data-meta="${k}"]`);
    let selected = new Set([1]);
    let anchor = 1;

    const setField = (k, values, enabled = true) => {
      const box = meta(k);
      const distinct = [...new Set(values)];
      box.classList.toggle("mixed", distinct.length > 1);
      box.classList.toggle("off", !enabled);
      box.textContent = values.length === 0 ? "" : distinct.length > 1 ? "<mixed>" : distinct[0];
    };

    const renderMeta = () => {
      const sel = songs.filter((s) => selected.has(s.n));
      meta("head").textContent = sel.length > 1 ? `METADATA: ${sel.length} TRACKS` : "METADATA";
      meta("art").style.backgroundImage = sel.length ? `url(${album.art})` : "";
      meta("cover").textContent = sel.length ? "jpeg\n700 × 700" : "nothing selected";
      // Titles differ per song by nature; with several songs the field is locked.
      setField("title", sel.map((s) => s.title), sel.length === 1);
      for (const k of ["artist", "album", "year", "genre"]) setField(k, sel.map((s) => s[k]));
      meta("plan").textContent = sel.length
        ? `${sel.length} file${sel.length > 1 ? "s" : ""} selected`
        : "Select songs to edit them.";
    };

    const renderRows = () => {
      heroRows.replaceChildren(...songs.map((s) => {
        const row = el("div", "row" + (selected.has(s.n) ? " sel" : ""));
        row.setAttribute("role", "option");
        row.setAttribute("aria-selected", selected.has(s.n));
        row.tabIndex = 0;
        const art = el("div", "art");
        art.style.backgroundImage = `url(${s.art})`;
        const t = el("div", "t");
        t.append(el("b", null, s.title), el("span", null, s.artist));
        row.append(el("span", "n", String(s.n)), art, t, el("span", "al", s.album), el("span", "f", "MP3"));

        const pick = (e) => {
          if (e.shiftKey) {
            const [a, b] = [anchor, s.n].sort((x, y) => x - y);
            selected = new Set(songs.filter((x) => x.n >= a && x.n <= b).map((x) => x.n));
          } else if (e.ctrlKey || e.metaKey) {
            selected.has(s.n) ? selected.delete(s.n) : selected.add(s.n);
            anchor = s.n;
          } else {
            // A second click on the only selected song clears the selection.
            selected = selected.size === 1 && selected.has(s.n) ? new Set() : new Set([s.n]);
            anchor = s.n;
          }
          renderRows();
          renderMeta();
          heroRows.querySelector(`[role=option]:nth-child(${s.n})`)?.focus();
        };
        row.addEventListener("click", pick);
        row.addEventListener("keydown", (e) => { if (e.key === " " || e.key === "Enter") { e.preventDefault(); pick(e); } });
        return row;
      }));
    };

    renderRows();
    renderMeta();
  }

  // ── Album mode: drag a song, numbers and file names follow ────
  const order = $("#order");
  if (order) {
    const art = "assets/cover-nachtfahrt.jpg";
    let tracks = ["Ufer", "Lichter über Halle", "Nebelfeld", "Nachtfahrt", "Kreidezeit", "Morgengrau"]
      .map((title, i) => ({ title, was: i + 1 }));
    const pad = (n) => String(n).padStart(2, "0");

    const render = (flash = new Set()) => {
      order.replaceChildren(...tracks.map((t, i) => {
        const li = el("li");
        li.dataset.i = i;
        if (flash.has(t.title)) li.classList.add("changed");
        const grip = el("span", "grip");
        grip.append(icon("dots-six-vertical"));
        const cover = el("div", "art");
        cover.style.backgroundImage = `url(${art})`;
        const text = el("div", "t");
        text.append(el("b", null, t.title), el("span", null, "Kollektiv Halle"));
        const file = el("span", "fname", `${pad(i + 1)} ${t.title}.mp3`);
        const moves = el("span", "moves");
        const up = el("button");
        up.type = "button";
        up.setAttribute("aria-label", `Move ${t.title} up`);
        up.append(icon("arrow-right"));
        up.firstChild.style.transform = "rotate(-90deg)";
        up.disabled = i === 0;
        up.addEventListener("click", () => move(i, i - 1, true));
        const down = el("button");
        down.type = "button";
        down.setAttribute("aria-label", `Move ${t.title} down`);
        down.append(icon("arrow-right"));
        down.firstChild.style.transform = "rotate(90deg)";
        down.disabled = i === tracks.length - 1;
        down.addEventListener("click", () => move(i, i + 1, true));
        moves.append(up, down);
        li.append(grip, el("span", "n", String(i + 1)), cover, text, file, el("span", "fmt", "MP3"), moves);
        li.addEventListener("pointerdown", (e) => startDrag(e, li));
        return li;
      }));
    };

    const move = (from, to, keepFocus) => {
      if (to < 0 || to >= tracks.length || from === to) return;
      const before = tracks.map((t) => t.title);
      const [t] = tracks.splice(from, 1);
      tracks.splice(to, 0, t);
      // Everything whose number changed lights up for a moment.
      const flash = new Set(tracks.filter((x, i) => before[i] !== x.title).map((x) => x.title));
      render(flash);
      setTimeout(() => order.querySelectorAll(".changed").forEach((n) => n.classList.remove("changed")), 1400);
      if (keepFocus) order.children[to]?.querySelector(to < from ? ".moves button" : ".moves button:last-child")?.focus();
    };

    // Pointer drag: the row follows the pointer; on release it lands where
    // it stands. Buttons keep their own clicks.
    const startDrag = (e, li) => {
      if (e.button !== 0 || e.target.closest("button")) return;
      const from = +li.dataset.i;
      const rows = [...order.children];
      const step = rows[1] ? rows[1].offsetTop - rows[0].offsetTop : li.offsetHeight + 3;
      const startY = e.clientY;
      let to = from;
      li.setPointerCapture(e.pointerId);
      li.classList.add("dragging");

      const onMove = (ev) => {
        const dy = ev.clientY - startY;
        li.style.transform = `translateY(${dy}px)`;
        to = Math.max(0, Math.min(tracks.length - 1, from + Math.round(dy / step)));
        rows.forEach((r, i) => {
          if (r === li) return;
          let shift = 0;
          if (from < to && i > from && i <= to) shift = -step;
          if (from > to && i < from && i >= to) shift = step;
          r.style.transition = reduce ? "none" : "transform .2s cubic-bezier(.16,1,.3,1)";
          r.style.transform = shift ? `translateY(${shift}px)` : "";
        });
      };
      const onUp = () => {
        li.removeEventListener("pointermove", onMove);
        li.removeEventListener("pointerup", onUp);
        li.removeEventListener("pointercancel", onUp);
        move(from, to);
        if (from === to) render();
      };
      li.addEventListener("pointermove", onMove);
      li.addEventListener("pointerup", onUp);
      li.addEventListener("pointercancel", onUp);
    };

    render();
  }

  // ── Languages, twice in a row so the loop has no seam ─────────
  const langs = $("#langs");
  if (langs) {
    const names = ["English", "Deutsch", "Français", "Español", "Italiano", "Português", "Nederlands",
      "Polski", "Русский", "Українська", "Türkçe", "Čeština", "Svenska"];
    // The one that matches the visitor's browser is marked, as a small nod.
    const mine = { en: 0, de: 1, fr: 2, es: 3, it: 4, pt: 5, nl: 6, pl: 7, ru: 8, uk: 9, tr: 10, cs: 11, sv: 12 }[
      (navigator.language || "en").slice(0, 2)] ?? 0;
    langs.replaceChildren(...names.map((n, i) => el("span", "pill" + (i === mine ? " on" : ""), n)));
  }

  // ── Reveal on scroll, the drop flow plays once in view ────────
  const io = new IntersectionObserver((entries) => {
    for (const en of entries) {
      if (!en.isIntersecting) continue;
      en.target.classList.add(en.target.classList.contains("flow") ? "play" : "in");
      io.unobserve(en.target);
    }
  }, { rootMargin: "0px 0px -10% 0px", threshold: .15 });
  document.querySelectorAll(".reveal, .flow").forEach((n) => io.observe(n));

  // ── Navigation gets its line once the page moves ──────────────
  const nav = $(".nav");
  const top = el("div");
  top.style.cssText = "position:absolute;top:0;height:1px;width:1px";
  document.body.prepend(top);
  new IntersectionObserver(([en]) => nav.classList.toggle("scrolled", !en.isIntersecting)).observe(top);

  // ── Download: version, size and direct links of the latest release.
  //    Without the API the buttons still lead to the release page. ──
  fetch("https://api.github.com/repos/yyusvf/TagTuner/releases/latest", { headers: { Accept: "application/vnd.github+json" } })
    .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
    .then((rel) => {
      const version = (rel.tag_name || "").replace(/^v/, "");
      const mb = (b) => `${(b / 1048576).toFixed(1)} MB`;
      const find = (re) => (rel.assets || []).find((a) => re.test(a.name));
      for (const [key, re] of [["setup", /setup\.exe$/i], ["zip", /\.zip$/i]]) {
        const asset = find(re);
        if (!asset) continue;
        const link = $(`[data-dl="${key}"]`);
        link.href = asset.browser_download_url;
        $(`[data-dl="${key}-meta"]`).textContent = `${version} · ${mb(asset.size)}`;
      }
    })
    .catch(() => {});
})();
