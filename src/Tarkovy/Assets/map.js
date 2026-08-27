(() => {
  const stage = document.getElementById("stage");
  const world = document.getElementById("world");
  const svgHost = document.getElementById("svgHost");
  const markers = document.getElementById("markers");
  const playerEl = document.getElementById("player");
  const hint = document.getElementById("hint");
  const tip = document.getElementById("tip");
  const rotLeftBtn = document.getElementById("rotLeft");
  const rotRightBtn = document.getElementById("rotRight");
  const rotResetBtn = document.getElementById("rotReset");

  const strings = {
    waiting: "WAITING FOR MAP",
    svgUnavailable: "SVG UNAVAILABLE",
    loadFailed: "FAILED TO LOAD MAP",
    rotateLeft: "Rotate 90° counter-clockwise",
    rotateReset: "Reset rotation",
    rotateRight: "Rotate 90° clockwise",
    extract: "EXTRACT",
    mine: "MINE"
  };

  const BASE = 1200;
  const state = {
    map: null,
    worldW: BASE,
    worldH: BASE,
    scale: 1,
    panX: 0,
    panY: 0,
    rotation: 0,
    follow: true,
    player: null,
    extracts: [],
    mines: [],
    showLabels: true,
    dragging: false,
    lastX: 0,
    lastY: 0
  };

  function applyStrings(next) {
    if (!next || typeof next !== "object") return;
    Object.assign(strings, next);
    if (rotLeftBtn) rotLeftBtn.title = strings.rotateLeft;
    if (rotRightBtn) rotRightBtn.title = strings.rotateRight;
    if (rotResetBtn) rotResetBtn.title = strings.rotateReset;
    if (!state.map && hint) hint.textContent = strings.waiting;
  }

  playerEl.innerHTML = '<div class="chevron"></div>';

  function boundsSize(map) {
    const b = map?.svgBounds || map?.bounds;
    if (!b || b.length < 2) return { w: BASE, h: BASE };
    const bw = Math.abs(b[0][0] - b[1][0]) || 1;
    const bh = Math.abs(b[0][1] - b[1][1]) || 1;
    const aspect = bw / bh;
    if (aspect >= 1) return { w: BASE, h: BASE / aspect };
    return { w: BASE * aspect, h: BASE };
  }

  function getViewBox(svg) {
    const raw = svg?.getAttribute("viewBox");
    if (!raw) return null;
    const vb = raw.trim().split(/\s+/).map(Number);
    if (vb.length < 4 || !vb[2] || !vb[3]) return null;
    return { x: vb[0], y: vb[1], w: vb[2], h: vb[3] };
  }

  function applyWorldSize(w, h) {
    state.worldW = w;
    state.worldH = h;
    world.style.width = w + "px";
    world.style.height = h + "px";
  }

  /** Como Sayser: content = viewBox do SVG (marcas em % batem com o desenho). */
  function applyWorldFromSvg(svg, map) {
    const vb = getViewBox(svg);
    if (vb) {
      svg.setAttribute("width", String(vb.w));
      svg.setAttribute("height", String(vb.h));
      svg.removeAttribute("preserveAspectRatio");
      applyWorldSize(vb.w, vb.h);
      return;
    }
    const { w, h } = boundsSize(map);
    applyWorldSize(w, h);
  }

  function applyTransform() {
    const cx = state.worldW / 2;
    const cy = state.worldH / 2;
    const s = state.scale;
    const r = state.rotation;
    // Pan em tela + escala/rotação em torno do centro do mapa.
    world.style.transformOrigin = "0 0";
    world.style.transform =
      `translate(${state.panX}px, ${state.panY}px) ` +
      `translate(${cx * s}px, ${cy * s}px) ` +
      `rotate(${r}deg) ` +
      `scale(${s}) ` +
      `translate(${-cx}px, ${-cy}px)`;
    const inv = s > 0.0001 ? 1 / s : 1;
    world.style.setProperty("--marker-inv", String(inv));
    world.style.setProperty("--map-rot", `${r}deg`);
    if (rotResetBtn) rotResetBtn.textContent = `${((r % 360) + 360) % 360}°`;
    updatePlayerVisual();
  }

  function updatePlayerVisual() {
    const chev = playerEl.querySelector(".chevron");
    if (!chev) return;
    const inv = state.scale > 0.0001 ? 1 / state.scale : 1;
    const yaw = state.player?.yaw || 0;
    // Contra-roda só o anti-zoom; yaw fica no espaço do mapa (pai já gira).
    chev.style.transform = `rotate(${yaw}deg) scale(${inv})`;
  }

  function rotMat() {
    const rad = (state.rotation * Math.PI) / 180;
    return { c: Math.cos(rad), s: Math.sin(rad) };
  }

  function worldToScreen(lx, ly) {
    const cx = state.worldW / 2;
    const cy = state.worldH / 2;
    const sc = state.scale;
    const dx = (lx - cx) * sc;
    const dy = (ly - cy) * sc;
    const { c, s } = rotMat();
    return {
      x: state.panX + cx * sc + (dx * c - dy * s),
      y: state.panY + cy * sc + (dx * s + dy * c)
    };
  }

  function screenToWorld(sx, sy) {
    const cx = state.worldW / 2;
    const cy = state.worldH / 2;
    const sc = state.scale || 1;
    const vx = sx - state.panX - cx * sc;
    const vy = sy - state.panY - cy * sc;
    const { c, s } = rotMat();
    const dx = vx * c + vy * s;
    const dy = -vx * s + vy * c;
    return { x: dx / sc + cx, y: dy / sc + cy };
  }

  function fitToView() {
    const w = Math.max(stage.clientWidth, 1);
    const h = Math.max(stage.clientHeight, 1);
    const rad = (state.rotation * Math.PI) / 180;
    const ac = Math.abs(Math.cos(rad));
    const as = Math.abs(Math.sin(rad));
    const bw = state.worldW * ac + state.worldH * as;
    const bh = state.worldW * as + state.worldH * ac;
    state.scale = Math.min(w / bw, h / bh) * 0.96;
    const cx = state.worldW / 2;
    const cy = state.worldH / 2;
    state.panX = w / 2 - cx * state.scale;
    state.panY = h / 2 - cy * state.scale;
    applyTransform();
  }

  function centerOnPlayer() {
    if (!state.player || !state.map) {
      fitToView();
      return;
    }
    const { pctX, pctY } = gameToPct(state.player.x, state.player.z, state.map);
    const lx = pctX * state.worldW;
    const ly = pctY * state.worldH;
    const w = Math.max(stage.clientWidth, 1);
    const h = Math.max(stage.clientHeight, 1);
    const scr = worldToScreen(lx, ly);
    state.panX += w / 2 - scr.x;
    state.panY += h / 2 - scr.y;
    applyTransform();
  }

  function rotateMap(deltaDeg) {
    const cx = state.worldW / 2;
    const cy = state.worldH / 2;
    const before = worldToScreen(cx, cy);
    state.rotation = ((state.rotation + deltaDeg) % 360 + 360) % 360;
    applyTransform();
    const after = worldToScreen(cx, cy);
    state.panX += before.x - after.x;
    state.panY += before.y - after.y;
    applyTransform();
    state.follow = false;
  }

  function resetRotation() {
    if (state.rotation === 0) {
      fitToView();
      return;
    }
    rotateMap(-state.rotation);
    fitToView();
  }

  function projectionBounds(map) {
    const b = map.svgBounds || map.bounds;
    return b && b.length >= 2 ? b : [[0, 0], [1, 1]];
  }

  function gameToPct(x, z, map) {
    // Sayser/tarkov.dev: lat=gameZ, lng=gameX → rotate → (opcional) Leaflet transform → normaliza.
    const rot = map.coordinateRotation || 0;
    const rad = (rot * Math.PI) / 180;
    const cos = Math.cos(rad);
    const sin = Math.sin(rad);
    const rotate = (gx, gz) => ({
      lng: gx * cos - gz * sin,
      lat: gx * sin + gz * cos
    });

    const b = projectionBounds(map);
    const corners = [
      [b[0][0], b[0][1]],
      [b[1][0], b[0][1]],
      [b[0][0], b[1][1]],
      [b[1][0], b[1][1]]
    ];

    const t = map.transform;
    if (Array.isArray(t) && t.length >= 4) {
      const scaleX = t[0];
      const scaleY = t[2] * -1;
      const marginX = t[1];
      const marginY = t[3];
      const toPx = (gx, gz) => {
        const r = rotate(gx, gz);
        return {
          px: scaleX * r.lng + marginX,
          py: scaleY * r.lat + marginY
        };
      };
      const p = toPx(x, z);
      let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
      for (const [bx, bz] of corners) {
        const c = toPx(bx, bz);
        if (c.px < minX) minX = c.px;
        if (c.px > maxX) maxX = c.px;
        if (c.py < minY) minY = c.py;
        if (c.py > maxY) maxY = c.py;
      }
      return {
        pctX: (p.px - minX) / (maxX - minX || 1),
        pctY: (p.py - minY) / (maxY - minY || 1)
      };
    }

    const p = rotate(x, z);
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (const [bx, bz] of corners) {
      const c = rotate(bx, bz);
      if (c.lng < minX) minX = c.lng;
      if (c.lng > maxX) maxX = c.lng;
      if (c.lat < minY) minY = c.lat;
      if (c.lat > maxY) maxY = c.lat;
    }
    return {
      pctX: (p.lng - minX) / (maxX - minX || 1),
      pctY: (maxY - p.lat) / (maxY - minY || 1)
    };
  }

  function place(el, pctX, pctY) {
    el.style.left = pctX * 100 + "%";
    el.style.top = pctY * 100 + "%";
  }

  async function loadMap(map) {
    state.map = map;
    state.rotation = 0;
    const fallback = boundsSize(map);
    applyWorldSize(fallback.w, fallback.h);
    hint.textContent = map.name || strings.waiting;
    svgHost.innerHTML = "";
    if (map.svgPath) {
      try {
        const res = await fetch(map.svgPath);
        if (res.ok) {
          const text = await res.text();
          svgHost.innerHTML = text;
          const svg = svgHost.querySelector("svg");
          if (svg) applyWorldFromSvg(svg, map);
        } else {
          svgHost.innerHTML = `<div style="color:#bbb;padding:16px">${strings.svgUnavailable}</div>`;
        }
      } catch {
        svgHost.innerHTML = `<div style="color:#bbb;padding:16px">${strings.loadFailed}</div>`;
      }
    }
    renderMarkers();
    requestAnimationFrame(() => {
      if (state.follow && state.player) centerOnPlayer();
      else fitToView();
    });
  }

  function inMap(pctX, pctY) {
    return pctX >= -0.02 && pctX <= 1.02 && pctY >= -0.02 && pctY <= 1.02;
  }

  function inMapPad(pctX, pctY) {
    return pctX >= 0.01 && pctX <= 0.99 && pctY >= 0.01 && pctY <= 0.99;
  }

  function hideTip() {
    tip.hidden = true;
    tip.className = "";
    tip.innerHTML = "";
  }

  function showTip(name, kind, clientX, clientY) {
    const rect = stage.getBoundingClientRect();
    tip.hidden = false;
    tip.className = kind === "mine" ? "mine-tip" : "extract-tip";
    tip.innerHTML =
      `<span class="tip-kind">${kind === "mine" ? strings.mine : strings.extract}</span>` +
      `<span class="tip-name">${name || (kind === "mine" ? strings.mine : strings.extract)}</span>`;

    // Mede depois de preencher pra não sair da tela.
    const pad = 10;
    let x = clientX - rect.left + 14;
    let y = clientY - rect.top + 14;
    tip.style.left = "0px";
    tip.style.top = "0px";
    const tw = tip.offsetWidth;
    const th = tip.offsetHeight;
    if (x + tw + pad > rect.width) x = clientX - rect.left - tw - 14;
    if (y + th + pad > rect.height) y = clientY - rect.top - th - 14;
    tip.style.left = Math.max(pad, x) + "px";
    tip.style.top = Math.max(pad, y) + "px";
  }

  function bindMarkerTip(node, name, kind) {
    node.addEventListener("pointerenter", (e) => showTip(name, kind, e.clientX, e.clientY));
    node.addEventListener("pointermove", (e) => {
      if (!tip.hidden) showTip(name, kind, e.clientX, e.clientY);
    });
    node.addEventListener("pointerleave", hideTip);
  }

  function renderMarkers() {
    hideTip();
    markers.innerHTML = "";
    if (!state.map) return;
    for (const ex of state.extracts) {
      const { pctX, pctY } = gameToPct(ex.x, ex.z, state.map);
      if (!inMap(pctX, pctY)) continue;
      const node = document.createElement("div");
      node.className = "extract " + String(ex.faction || "any").toLowerCase();
      place(node, pctX, pctY);
      const name = ex.name || "EXFIL";
      if (state.showLabels) {
        const lab = document.createElement("div");
        lab.className = "extract-label";
        lab.textContent = name;
        node.appendChild(lab);
      }
      bindMarkerTip(node, name, "extract");
      markers.appendChild(node);
    }
    for (const m of state.mines) {
      const { pctX, pctY } = gameToPct(m.x, m.z, state.map);
      if (!inMapPad(pctX, pctY)) continue;
      const node = document.createElement("div");
      node.className = "mine";
      place(node, pctX, pctY);
      const name = m.name || strings.mine;
      if (state.showLabels) {
        const lab = document.createElement("div");
        lab.className = "extract-label mine-label";
        lab.textContent = name;
        node.appendChild(lab);
      }
      bindMarkerTip(node, name, "mine");
      markers.appendChild(node);
    }
  }

  function setMarkers(extracts, mines, showLabels) {
    state.extracts = Array.isArray(extracts) ? extracts : [];
    state.mines = Array.isArray(mines) ? mines : [];
    if (typeof showLabels === "boolean") state.showLabels = showLabels;
    renderMarkers();
  }

  function setShowLabels(value) {
    state.showLabels = !!value;
    renderMarkers();
  }

  function setPlayer(p) {
    state.player = p;
    if (!p || !state.map) {
      playerEl.hidden = true;
      return;
    }
    playerEl.hidden = false;
    const { pctX, pctY } = gameToPct(p.x, p.z, state.map);
    place(playerEl, pctX, pctY);
    updatePlayerVisual();
    hint.textContent = `${state.map.name}  ${p.x.toFixed(1)}  ${p.z.toFixed(1)}`;
    if (state.follow) centerOnPlayer();
  }

  stage.addEventListener("wheel", (e) => {
    e.preventDefault();
    const rect = stage.getBoundingClientRect();
    const sx = e.clientX - rect.left;
    const sy = e.clientY - rect.top;
    const before = screenToWorld(sx, sy);
    state.scale = Math.min(6, Math.max(0.08, state.scale * (e.deltaY < 0 ? 1.12 : 0.89)));
    applyTransform();
    const after = worldToScreen(before.x, before.y);
    state.panX += sx - after.x;
    state.panY += sy - after.y;
    applyTransform();
  }, { passive: false });

  stage.addEventListener("contextmenu", (e) => e.preventDefault());

  stage.addEventListener("pointerdown", (e) => {
    if (e.target.closest("#mapTools")) return;
    if (e.altKey || e.button === 2) {
      window.chrome?.webview?.postMessage({ type: "dragWindow" });
      return;
    }
    state.dragging = true;
    state.follow = false;
    state.lastX = e.clientX;
    state.lastY = e.clientY;
    stage.classList.add("drag");
    stage.setPointerCapture(e.pointerId);
  });
  stage.addEventListener("pointermove", (e) => {
    if (!state.dragging) return;
    state.panX += e.clientX - state.lastX;
    state.panY += e.clientY - state.lastY;
    state.lastX = e.clientX;
    state.lastY = e.clientY;
    applyTransform();
  });
  stage.addEventListener("pointerup", () => {
    state.dragging = false;
    stage.classList.remove("drag");
  });

  rotLeftBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    rotateMap(-90);
  });
  rotRightBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    rotateMap(90);
  });
  rotResetBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    resetRotation();
  });

  new ResizeObserver(() => {
    if (state.follow && state.player) centerOnPlayer();
    else fitToView();
  }).observe(stage);

  function onMessage(data) {
    if (!data || !data.type) return;
    if (data.type === "lang") applyStrings(data.strings);
    if (data.type === "loadMap") loadMap(data.map);
    if (data.type === "markers") setMarkers(data.extracts, data.mines, data.showLabels);
    if (data.type === "extracts") setMarkers(data.extracts, [], data.showLabels);
    if (data.type === "showLabels") setShowLabels(data.value);
    if (data.type === "player") setPlayer(data.player);
    if (data.type === "follow") {
      state.follow = !!data.value;
      if (state.follow && state.player) centerOnPlayer();
      else fitToView();
    }
    if (data.type === "resetView") fitToView();
  }

  window.chrome?.webview?.addEventListener("message", (ev) => onMessage(ev.data));
  window.addEventListener("message", (ev) => onMessage(ev.data));

  fitToView();
  window.chrome?.webview?.postMessage({ type: "ready" });
})();
