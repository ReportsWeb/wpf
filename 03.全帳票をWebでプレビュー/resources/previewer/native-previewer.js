(() => {
  "use strict";
  const cfg = window.ReportsNativeConfig || {},
    root = document.getElementById("nativePreviewer"),
    sampleSelect = document.getElementById("samples");
  const ids = [
    "open",
    "save",
    "print",
    "pdf",
    "svg",
    "navigation",
    "zoom",
    "layout",
    "search",
    "language",
    "settings",
    "about",
  ];
  const defaults = Object.fromEntries(ids.map((x) => [x, true]));
  const state = {
    catalog: [],
    sampleId: "",
    pageIndex: 0,
    pageCount: 0,
    zoom: "auto",
    viewMode: "single",
    outputView: "svg",
    query: "",
    matchIndex: -1,
    matchCount: 0,
    rendering: false,
    error: null,
    chrome: {
      toolbar: true,
      status: true,
      text: true,
      large: false,
      commands: { ...defaults },
    },
  };
  let locale = "ja",
    token = 0,
    abort = null,
    pdfUrl = null,
    searchHits = [],
    openedPrintData = null,
    hostBlank = false;
  const $ = (s) => root.querySelector(s),
    $$ = (s) => [...root.querySelectorAll(s)],
    selected = () => state.catalog.find((x) => x.id === state.sampleId);
  const esc = (s) =>
    String(s).replace(
      /[&<>\"]/g,
      (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c],
    );
  const tr = {
    ja: {
      open: "開く",
      save: "印刷データを保存",
      print: "印刷",
      pdf: "PDF表示",
      svg: "印刷プレビュー",
      first: "先頭",
      prev: "前",
      next: "次",
      last: "末尾",
      page: "ページ",
      fit: "全体",
      width: "幅",
      single: "単一",
      continuous: "連続",
      spread: "見開き",
      find: "検索・次候補",
      previous: "前候補",
      all: "全候補",
      clear: "解除",
      settings: "表示設定",
      about: "情報",
      allOn: "すべて表示",
      allOff: "すべて非表示",
      close: "閉じる",
    },
    en: {
      open: "Open",
      save: "Save print data",
      print: "Print",
      pdf: "View PDF",
      svg: "Print preview",
      first: "First",
      prev: "Previous",
      next: "Next",
      last: "Last",
      page: "Page",
      fit: "Fit",
      width: "Width",
      single: "Single",
      continuous: "Continuous",
      spread: "Facing",
      find: "Find / next",
      previous: "Previous result",
      all: "All results",
      clear: "Clear",
      settings: "View settings",
      about: "About",
      allOn: "Show all",
      allOff: "Hide all",
      close: "Close",
    },
  };
  const t = () => tr[locale];
  const icon = (body) =>
    `<svg viewBox="0 0 24 24" aria-hidden="true">${body}</svg>`;
  const icons = {
    npOpen: icon('<path d="M3 7h7l2 2h9v11H3zM3 7V4h7l2 3"/>'),
    npSave: icon('<path d="M4 3h13l3 3v15H4zM8 3v6h8V3M8 21v-8h8v8"/>'),
    npPrint: icon(
      '<path d="M6 9V3h12v6M6 17H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2M6 14h12v7H6z"/>',
    ),
    npPdf: icon('<path d="M6 2h9l4 4v16H6zM15 2v5h5M8 16h8M8 12h6"/>'),
    npSvg: icon(
      '<path d="M6 2h9l4 4v16H6zM15 2v5h5M9 13l-2 2 2 2m6-4 2 2-2 2"/>',
    ),
    npFirst: icon('<path d="M6 4v16M19 5l-9 7 9 7z"/>'),
    npPrev: icon('<path d="M16 5l-9 7 9 7z"/>'),
    npNext: icon('<path d="M8 5l9 7-9 7z"/>'),
    npLast: icon('<path d="M18 4v16M5 5l9 7-9 7z"/>'),
    npFind: icon('<circle cx="10" cy="10" r="6"/><path d="m15 15 6 6"/>'),
    npClear: icon('<path d="m6 6 12 12M18 6 6 18"/>'),
    npSettings: icon(
      '<path d="m9 3-.5 2-2 1-2-.5-2 3L4 10v3l-1.5 2 2 3 2-.5 2 1L9 21h4l.5-2.5 2-1 2 .5 2-3-1.5-2v-3L19.5 8l-2-3-2 .5-2-1L13 3z"/><circle cx="11" cy="12" r="3"/>',
    ),
    npAbout: icon(
      '<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10v.1"/>',
    ),
  };
  const installIcons = () => {
    $("#npFile").accept = ".prepej,application/json";
    for (const [id, svg] of Object.entries(icons)) {
      const button = $(`#${id}`),
        text = button?.querySelector(".tool-text");
      if (!button) continue;
      button.replaceChildren();
      button.insertAdjacentHTML("beforeend", svg);
      if (text) button.append(text);
    }
    const layoutIcons = [
      '<rect x="6" y="3" width="12" height="18"/>',
      '<rect x="7" y="2" width="10" height="8"/><rect x="7" y="14" width="10" height="8"/>',
      '<rect x="2" y="4" width="9" height="16"/><rect x="13" y="4" width="9" height="16"/>',
    ];
    $$("[data-layout]").forEach((button, index) => {
      const text = button.querySelector(".tool-text");
      button.replaceChildren();
      button.insertAdjacentHTML("beforeend", icon(layoutIcons[index]));
      if (text) button.append(text);
    });
  };
  function markup() {
    const x = t();
    root.innerHTML = `<div class="native-toolbar" role="toolbar"><span class="native-group" data-command="open"><button class="native-tool" id="npOpen">📂 <span class="tool-text">${x.open}</span></button><input id="npFile" type="file" accept=".prepej,.json,application/json" hidden></span><button class="native-tool" id="npSave" data-command="save">💾 <span class="tool-text">${x.save}</span></button><button class="native-tool" id="npPrint" data-command="print">🖨 <span class="tool-text">${x.print}</span></button><button class="native-tool" id="npPdf" data-command="pdf">PDF <span class="tool-text">${x.pdf}</span></button><button class="native-tool" id="npSvg" data-command="svg">▱ <span class="tool-text">${x.svg}</span></button><span class="native-group" data-command="navigation"><button class="native-tool" id="npFirst">|◀ <span class="tool-text">${x.first}</span></button><button class="native-tool" id="npPrev">◀ <span class="tool-text">${x.prev}</span></button><label>${x.page} <input id="npPage" size="3" inputmode="numeric"></label><span id="npTotal">/ 0</span><button class="native-tool" id="npNext"><span class="tool-text">${x.next}</span> ▶</button><button class="native-tool" id="npLast"><span class="tool-text">${x.last}</span> ▶|</button></span><span class="native-group" data-command="zoom"><select id="npZoom"><option value="auto">自動</option><option value="fit">${x.fit}</option><option value="width">${x.width}</option>${[50, 75, 80, 90, 100, 110, 125, 150, 200].map((n) => `<option value="${n}">${n}%</option>`).join("")}</select></span><span class="native-group" data-command="layout"><button class="native-tool" data-layout="single">▯ <span class="tool-text">${x.single}</span></button><button class="native-tool" data-layout="continuous">▯▯ <span class="tool-text">${x.continuous}</span></button><button class="native-tool" data-layout="spread">▯ ▯ <span class="tool-text">${x.spread}</span></button></span><span class="native-spacer"></span><span class="native-group search" data-command="search"><input id="npQuery" type="search" placeholder="${x.find}"><button class="native-tool" id="npFind">⌕ <span class="tool-text">${x.find}</span></button><button class="native-tool" id="npFindPrev">↑ <span class="tool-text">${x.previous}</span></button><button class="native-tool" id="npFindAll">✦ <span class="tool-text">${x.all}</span></button><button class="native-tool" id="npClear">× <span class="tool-text">${x.clear}</span></button></span><button class="native-tool" id="npLanguage" data-command="language">文/A</button><button class="native-tool" id="npSettings" data-command="settings">⚙ <span class="tool-text">${x.settings}</span></button><button class="native-tool" id="npAbout" data-command="about">ⓘ <span class="tool-text">${x.about}</span></button></div><div id="npPages" class="native-pages single" tabindex="0"></div><div id="npStatus" class="native-status" role="status" aria-live="polite">準備しています…</div><dialog id="npSettingsDialog" class="native-dialog"><h2>${x.settings}</h2><div class="native-settings-actions"><button id="npAllOn">${x.allOn}</button><button id="npAllOff">${x.allOff}</button></div><fieldset><legend>画面</legend><label><input type="checkbox" data-chrome="toolbar">ツールバー</label><label><input type="checkbox" data-chrome="status">ステータスバー</label><label><input type="checkbox" data-chrome="text">ボタン文字</label><label><input type="checkbox" data-chrome="large">大きいアイコン</label></fieldset><fieldset><legend>機能</legend>${ids.map((id) => `<label><input type="checkbox" data-command-check="${id}">${{ open: x.open, save: x.save, print: x.print, pdf: x.pdf, svg: x.svg, navigation: x.page, zoom: "拡大・縮小", layout: "ページ配置", search: "検索", language: "言語", settings: x.settings, about: x.about }[id]}</label>`).join("")}</fieldset><button id="npCloseSettings">${x.close}</button></dialog><dialog id="npAboutDialog" class="native-dialog"><h2>Reports Web Previewer</h2><p>${esc(cfg.label)} engine<br>SVG / PDF unified preview</p><button id="npCloseAbout">${x.close}</button></dialog>`;
    const restore = document.createElement("button");
    restore.type = "button";
    restore.id = "npRestoreSettings";
    restore.className = "native-restore-settings";
    restore.setAttribute("aria-label", "表示設定");
    restore.textContent = "⚙ 表示設定";
    root.prepend(restore);
    installIcons();
    bind();
    applyChrome();
  }
  function emit(name, detail = snapshot()) {
    root.dispatchEvent(new CustomEvent(`reports-web:${name}`, { detail }));
    window.dispatchEvent(new CustomEvent(`reports-web:${name}`, { detail }));
  }
  function snapshot() {
    return JSON.parse(JSON.stringify(state));
  }
  function setStatus(s, error = false) {
    $("#npStatus").textContent = s;
    $("#npStatus").classList.toggle("error", error);
    state.error = error ? s : null;
    emit("state-changed");
  }
  function applyChrome() {
    const c = state.chrome;
    $(".native-toolbar").hidden = !c.toolbar;
    $(".native-status").hidden = !c.status;
    root.classList.toggle("native-hide-text", !c.text);
    root.classList.toggle("native-large", c.large);
    root.classList.toggle("native-preview-only", !c.toolbar && !c.status);
    $("#npRestoreSettings").hidden = !!cfg.externalChromeControl || hostBlank || c.toolbar || c.status;
    for (const id of ids)
      $$(`[data-command="${id}"]`).forEach((e) => (e.hidden = !c.commands[id]));
    $$("[data-chrome]").forEach((e) => (e.checked = !!c[e.dataset.chrome]));
    $$("[data-command-check]").forEach(
      (e) => (e.checked = !!c.commands[e.dataset.commandCheck]),
    );
    emit("chrome-changed");
  }
  function pageUrls() {
    const item = selected();
    return item
      ? Array.from(
          { length: item.pages },
          (_, i) => `api/reports/${encodeURIComponent(item.id)}/svg?page=${i}`,
        )
      : [];
  }
  const isOpenedDocument = () => !!openedPrintData;
  const printDataPages = (data) => {
    const pages = data?.Pages ?? data?.pages;
    return Array.isArray(pages) ? pages.length : 0;
  };
  async function fetchPrintData(item) {
    const response = await fetch(`api/reports/${encodeURIComponent(item.id)}/data`, {
      cache: "no-store",
    });
    if (!response.ok) throw Error(`HTTP ${response.status}`);
    return response.text();
  }
  async function renderOpened(format, page, signal) {
    const response = await fetch(
      `api/print-data/${format}${format === "svg" ? `?page=${page}` : ""}`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: openedPrintData.raw,
        signal,
        cache: "no-store",
      },
    );
    if (!response.ok) throw Error(`HTTP ${response.status}`);
    if (response.headers.get("X-Reports-Engine") !== cfg.engineHeader)
      throw Error("帳票エンジンを確認できません");
    return format === "svg" ? response.text() : response.blob();
  }
  async function fetchSvg(i, signal) {
    if (isOpenedDocument()) return renderOpened("svg", i, signal);
    const item = selected();
    if (!item) throw Error("帳票がありません");
    const r = await fetch(
      `api/reports/${encodeURIComponent(item.id)}/svg?page=${i}`,
      { signal, cache: "no-store" },
    );
    if (!r.ok) throw Error(`HTTP ${r.status}`);
    if (r.headers.get("X-Reports-Engine") !== cfg.engineHeader)
      throw Error("帳票エンジンを確認できません");
    return r.text();
  }
  function scale() {
    const box = $("#npPages"),
      pages = $$(".native-page");
    for (const p of pages) {
      const svg = p.querySelector("svg");
      if (!svg) continue;
      const vb = svg.viewBox?.baseVal,
        w = vb?.width || Number(svg.getAttribute("width")) || 794,
        h = vb?.height || Number(svg.getAttribute("height")) || 1123;
      const millimetres = /mm\s*$/i.test(svg.getAttribute("width") || "");
      // Java/Python SVGs use millimetres. The .NET renderer emits PDF-point
      // coordinates (72/inch). CSS uses 96px/inch, so both need a physical
      // 100% conversion instead of treating their numeric coordinates as px.
      const cssUnit = millimetres ? 96 / 25.4 : cfg.engineHeader === "DotNet" ? 96 / 72 : 1;
      const naturalWidth = w * cssUnit,
        naturalHeight = h * cssUnit;
      let mode = state.zoom;
      if (mode === "auto") mode = box.clientWidth < 768 ? "fit" : "100";
      const factor =
        mode === "fit"
          ? Math.min(
              (box.clientWidth - 40) / naturalWidth,
              (box.clientHeight - 40) / naturalHeight,
            )
          : mode === "width"
            ? (box.clientWidth - 40) / naturalWidth
            : Number(mode) / 100;
      p.style.width = `${naturalWidth * factor}px`;
      p.style.height = `${naturalHeight * factor}px`;
    }
  }
  async function render() {
    const item = selected();
    if (!item) return;
    const mine = ++token;
    abort?.abort();
    abort = new AbortController();
    state.rendering = true;
    state.error = null;
    state.pageCount = item.pages;
    $("#npPdf").classList.toggle("is-active", state.outputView === "pdf");
    $("#npSvg").classList.toggle("is-active", state.outputView === "svg");
    $$('[data-layout]').forEach((button) => {
      const active = button.dataset.layout === state.viewMode;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    $("#npPage").value = String(state.pageIndex + 1);
    $("#npTotal").textContent = `/ ${item.pages}`;
    setStatus(`${cfg.label}エンジンで生成しています…`);
    try {
      if (state.outputView === "pdf") {
        const url = isOpenedDocument()
          ? URL.createObjectURL(await renderOpened("pdf", 0, abort.signal))
          : `api/reports/${encodeURIComponent(item.id)}/pdf`;
        if (pdfUrl) URL.revokeObjectURL(pdfUrl);
        pdfUrl = isOpenedDocument() ? url : null;
        $("#npPages").innerHTML =
          `<object class="native-pdf" data="${url}" type="application/pdf"></object>`;
      } else {
        const indexes =
          state.viewMode === "single"
            ? [state.pageIndex]
            : Array.from({ length: item.pages }, (_, i) => i);
        const svgs = await Promise.all(
          indexes.map((i) => fetchSvg(i, abort.signal)),
        );
        if (mine !== token) return;
        $("#npPages").className = `native-pages ${state.viewMode}`;
        $("#npPages").innerHTML = svgs
          .map(
            (s, n) =>
              `<div class="native-page" data-page="${indexes[n]}">${s}</div>`,
          )
          .join("");
        scale();
        findMatches(false);
      }
      state.rendering = false;
      setStatus(
        `${item.name} — ${state.outputView === "pdf" ? "PDFを表示しています。" : "プレビューを表示しています。"}`,
      );
      emit("render-completed");
    } catch (e) {
      if (e.name !== "AbortError" && mine === token) {
        state.rendering = false;
        setStatus(`表示できませんでした。（${e.message}）`, true);
        emit("operation-failed", { error: e.message, state: snapshot() });
      }
    }
  }
  function go(n) {
    state.pageIndex = Math.max(0, Math.min(state.pageCount - 1, n));
    emit("page-changed");
    return render();
  }
  function findMatches(move = true, direction = 1, all = false) {
    const q = $("#npQuery")?.value.trim() || "";
    state.query = q;
    $$(".native-hit,.native-current-hit").forEach((e) =>
      e.classList.remove("native-hit", "native-current-hit"),
    );
    searchHits = q
      ? $$(".native-page text, .native-page [data-report-search-text]").filter((e) =>
          (e.getAttribute("data-report-search-text") ?? e.textContent ?? "").normalize("NFKC").includes(q.normalize("NFKC")))
      : [];
    state.matchCount = searchHits.length;
    if (!searchHits.length) {
      state.matchIndex = -1;
      if (move && q) setStatus("該当する文字はありません");
      emit("search-changed");
      return;
    }
    state.matchIndex = all
      ? -1
      : (state.matchIndex + direction + searchHits.length) % searchHits.length;
    searchHits.forEach((e, i) =>
      e.classList.add(
        all || i !== state.matchIndex ? "native-hit" : "native-current-hit",
      ),
    );
    if (!all)
      searchHits[state.matchIndex].scrollIntoView({
        block: "center",
        inline: "center",
      });
    setStatus(
      `${searchHits.length}件${all ? "を表示" : ` — ${state.matchIndex + 1}件目`}`,
    );
    emit("search-changed");
  }
  async function save() {
    const item = selected();
    if (!item) return;
    const raw = openedPrintData?.raw ?? (await fetchPrintData(item));
    const a = document.createElement("a");
    a.href = URL.createObjectURL(
      new Blob([raw], { type: "application/json" }),
    );
    a.download = openedPrintData?.name || `${item.name}.prepej`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    setStatus(`${a.download} を保存しました`);
  }
  async function printAll() {
    const previousMode = state.viewMode,
      previousOutput = state.outputView;
    state.viewMode = "continuous";
    state.outputView = "svg";
    await render();
    window.addEventListener("afterprint", () => void render(), { once: true });
    window.print();
    state.viewMode = previousMode;
    state.outputView = previousOutput;
  }
  function bind() {
    const on = (s, e, f) => $(s).addEventListener(e, f);
    on("#npOpen", "click", () => $("#npFile").click());
    on("#npFile", "change", async (e) => {
      const f = e.target.files?.[0];
      if (!f) return;
      try {
        if (!f.name.toLowerCase().endsWith(".prepej"))
          throw Error("印刷データ（.prepej）を選択してください");
        const raw = await f.text(), d = JSON.parse(raw), pages = printDataPages(d);
        if (!pages) throw Error("ページを持つPREPEJ印刷データではありません");
        const id = `opened:${Date.now()}`;
        openedPrintData = { raw, name: f.name };
        state.catalog.push({ id, name: f.name, pages, engine: cfg.label });
        sampleSelect.add(new Option(`${f.name}（読み込み）`, id));
        sampleSelect.value = state.sampleId = id;
        state.pageCount = pages;
        state.pageIndex = 0;
        await render();
        emit("document-loaded");
      } catch (x) {
        setStatus(x.message, true);
      }
    });
    on("#npSave", "click", () => void save().catch((e) => setStatus(e.message, true)));
    on("#npPrint", "click", () => void printAll());
    on("#npPdf", "click", () => {
      state.outputView = "pdf";
      emit("output-view-changed");
      render();
    });
    on("#npSvg", "click", () => {
      state.outputView = "svg";
      emit("output-view-changed");
      render();
    });
    on("#npFirst", "click", () => go(0));
    on("#npPrev", "click", () => go(state.pageIndex - 1));
    on("#npNext", "click", () => go(state.pageIndex + 1));
    on("#npLast", "click", () => go(state.pageCount - 1));
    on("#npPage", "change", (e) => go(Number(e.target.value) - 1));
    on("#npZoom", "change", (e) => {
      state.zoom = e.target.value;
      scale();
      emit("zoom-changed");
    });
    $$("[data-layout]").forEach(
      (b) =>
        (b.onclick = () => {
          state.viewMode = b.dataset.layout;
          emit("view-mode-changed");
          render();
        }),
    );
    on("#npFind", "click", () => findMatches(true, 1));
    on("#npFindPrev", "click", () => findMatches(true, -1));
    on("#npFindAll", "click", () => findMatches(true, 1, true));
    on("#npClear", "click", () => {
      $("#npQuery").value = "";
      state.matchIndex = -1;
      findMatches(false);
    });
    on("#npQuery", "keydown", (e) => {
      if (e.key === "Enter") findMatches(true, e.shiftKey ? -1 : 1);
    });
    on("#npLanguage", "click", () => {
      locale = locale === "ja" ? "en" : "ja";
      markup();
      render();
    });
    on("#npSettings", "click", () => $("#npSettingsDialog").showModal());
    on("#npRestoreSettings", "click", () => $("#npSettingsDialog").showModal());
    on("#npAbout", "click", () => $("#npAboutDialog").showModal());
    on("#npCloseSettings", "click", () => $("#npSettingsDialog").close());
    on("#npCloseAbout", "click", () => $("#npAboutDialog").close());
    on("#npAllOn", "click", () => {
      hostBlank = false;
      state.chrome.toolbar = state.chrome.status = state.chrome.text = true;
      Object.keys(state.chrome.commands).forEach(
        (k) => (state.chrome.commands[k] = true),
      );
      applyChrome();
    });
    on("#npAllOff", "click", () => {
      hostBlank = false;
      state.chrome.toolbar = state.chrome.status = state.chrome.text = false;
      Object.keys(state.chrome.commands).forEach(
        (k) => (state.chrome.commands[k] = false),
      );
      applyChrome();
      $("#npSettingsDialog").close();
    });
    $$("[data-chrome]").forEach(
      (e) =>
        (e.onchange = () => {
          state.chrome[e.dataset.chrome] = e.checked;
          applyChrome();
        }),
    );
    $$("[data-command-check]").forEach(
      (e) =>
        (e.onchange = () => {
          state.chrome.commands[e.dataset.commandCheck] = e.checked;
          applyChrome();
        }),
    );
    new ResizeObserver(scale).observe($("#npPages"));
    document.addEventListener("keydown", (e) => {
      if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === "t") {
        state.chrome.toolbar = true;
        state.chrome.commands.settings = true;
        applyChrome();
      }
    });
  }
  markup();
  sampleSelect.addEventListener("change", () => {
    state.sampleId = sampleSelect.value;
    if (!state.sampleId.startsWith("opened:")) openedPrintData = null;
    state.pageIndex = 0;
    render();
  });
  const api = {
    getState: snapshot,
    firstPage: () => go(0),
    previousPage: () => go(state.pageIndex - 1),
    goToPage: (n) => go(Number(n) - 1),
    nextPage: () => go(state.pageIndex + 1),
    lastPage: () => go(state.pageCount - 1),
    setZoom: (v) => {
      state.zoom = String(v);
      $("#npZoom").value = state.zoom;
      scale();
      emit("zoom-changed");
    },
    setViewMode: (v) => {
      if (!["single", "continuous", "spread"].includes(v)) throw RangeError(v);
      state.viewMode = v;
      return render();
    },
    search: (q) => {
      $("#npQuery").value = q;
      state.matchIndex = -1;
      findMatches(true, 1);
    },
    nextMatch: () => findMatches(true, 1),
    previousMatch: () => findMatches(true, -1),
    showAllMatches: () => findMatches(true, 1, true),
    clearSearch: () => {
      $("#npQuery").value = "";
      findMatches(false);
    },
    showSvgPreview: () => {
      state.outputView = "svg";
      return render();
    },
    showPdfPreview: () => {
      state.outputView = "pdf";
      return render();
    },
    print: printAll,
    savePrintData: save,
    openPrintData: () => $("#npFile").click(),
    cancel: () => abort?.abort(),
    setChrome: (o) => {
      if (o.commands) Object.assign(state.chrome.commands, o.commands);
      Object.assign(
        state.chrome,
        Object.fromEntries(Object.entries(o).filter(([k]) => k !== "commands")),
      );
      hostBlank =
        state.chrome.toolbar === false &&
        state.chrome.status === false &&
        Object.values(state.chrome.commands).every((visible) => !visible);
      applyChrome();
    },
    showAll: () => {
      hostBlank = false;
      $("#npAllOn").click();
    },
    hideAll: () => {
      hostBlank = true;
      $$("dialog[open]").forEach((dialog) => dialog.close());
      state.chrome.toolbar = state.chrome.status = state.chrome.text = false;
      Object.keys(state.chrome.commands).forEach(
        (k) => (state.chrome.commands[k] = false),
      );
      applyChrome();
    },
    subscribe: (name, fn) => {
      const n = `reports-web:${name}`,
        w = (e) => fn(e.detail);
      root.addEventListener(n, w);
      return () => root.removeEventListener(n, w);
    },
  };
  Object.defineProperty(window, "ReportsWebNative", {
    value: api,
    configurable: false,
  });
  fetch("api/samples", { cache: "no-store" })
    .then((r) => {
      if (!r.ok) throw Error(`HTTP ${r.status}`);
      return r.json();
    })
    .then((rows) => {
      if (!Array.isArray(rows) || !rows.length) throw Error("帳票がありません");
      state.catalog = rows;
      for (const x of rows) sampleSelect.add(new Option(x.name, x.id));
      state.sampleId = rows.some((x) => x.id === "S06") ? "S06" : rows[0].id;
      sampleSelect.value = state.sampleId;
      sampleSelect.disabled = false;
      render();
    })
    .catch((e) =>
      setStatus(`サンプル一覧を読み込めませんでした。（${e.message}）`, true),
    );
})();
