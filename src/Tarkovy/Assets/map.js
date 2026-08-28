(() => {
  const stage = document.getElementById("stage");
  const world = document.getElementById("world");
  const svgHost = document.getElementById("svgHost");
  const routeLayer = document.getElementById("routeLayer");
  const markers = document.getElementById("markers");
  const playerEl = document.getElementById("player");
  const tip = document.getElementById("tip");
  const wpBanner = document.getElementById("wpBanner");
  const rotLeftBtn = document.getElementById("rotLeft");
  const rotRightBtn = document.getElementById("rotRight");
  const rotResetBtn = document.getElementById("rotReset");
  const clearWpBtn = document.getElementById("clearWp");
  const placeWpBtn = document.getElementById("placeWp");
  const floorSep = document.getElementById("floorSep");
  const floorTools = document.getElementById("floorTools");
  const floorUpBtn = document.getElementById("floorUp");
  const floorDownBtn = document.getElementById("floorDown");
  const floorLabelBtn = document.getElementById("floorLabel");
  const floorShortEl = document.getElementById("floorShort");
  const mapToolsDock = document.getElementById("mapToolsDock");
  const toolsToggleBtn = document.getElementById("toolsToggle");
  const sideToolsEl = document.getElementById("sideTools");

  const MARKER_BASE = "https://tarkovy.assets/markers/";
  const MARKER_ICONS = {
    extractPmc: MARKER_BASE + "extract_pmc.png",
    extractScav: MARKER_BASE + "extract_scav.png",
    extractShared: MARKER_BASE + "extract_shared.png",
    hazard: MARKER_BASE + "hazard.png",
    spawnPmc: MARKER_BASE + "spawn_pmc.png",
    quest: MARKER_BASE + "quest_objective.png",
    waypoint: MARKER_BASE + "quest_item.png",
    player: MARKER_BASE + "player-position.png"
  };

  const strings = {
    waiting: "WAITING FOR MAP",
    svgUnavailable: "SVG UNAVAILABLE",
    loadFailed: "FAILED TO LOAD MAP",
    rotateLeft: "Rotate 90° counter-clockwise",
    rotateReset: "Reset rotation",
    rotateRight: "Rotate 90° clockwise",
    extract: "EXTRACT",
    mine: "MINE",
    spawn: "PMC SPAWN",
    quest: "QUEST",
    waypoint: "WAYPOINT",
    clearWaypoint: "Clear waypoint",
    placeWaypoint: "Place waypoint on map",
    placeWaypointActive: "Click the map to place waypoint (Esc to cancel)",
    placeWaypointHint: "CLICK MAP TO PLACE WAYPOINT",
    customWaypoint: "Custom waypoint",
    layerExtracts: "Extracts",
    layerMines: "Mines",
    layerSpawns: "PMC Spawns",
    layerQuests: "Quests",
    layerLabels: "Labels",
    floorUp: "Floor up",
    floorDown: "Floor down",
    floorCurrent: "Floor",
    toolsShow: "Show map tools",
    toolsHide: "Hide map tools"
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
    follow: false,
    followPaused: false,
    followZoomApplied: false,
    followZoomMult: 3.2,
    player: null,
    extracts: [],
    mines: [],
    spawns: [],
    quests: [],
    enabledQuests: new Set(),
    completedQuests: new Set(),
    layers: { extracts: true, mines: true, spawns: true, quests: true, labels: true },
    waypoint: null,
    wpPlaceMode: false,
    dragging: false,
    lastX: 0,
    lastY: 0,
    floorIndex: 0,
    autoFloor: true,
    floorManual: false,
    toolsOpen: true
  };

  function applyStrings(next) {
    if (!next || typeof next !== "object") return;
    Object.assign(strings, next);
    if (rotLeftBtn) rotLeftBtn.title = strings.rotateLeft;
    if (rotRightBtn) rotRightBtn.title = strings.rotateRight;
    if (rotResetBtn) rotResetBtn.title = strings.rotateReset;
    if (clearWpBtn) clearWpBtn.title = strings.clearWaypoint;
    syncPlaceWpButton();
    syncLayerButtons();
    updateFloorUi();
    updateWpBanner();
    syncToolsToggle();
  }

  function syncToolsToggle() {
    if (!toolsToggleBtn || !mapToolsDock) return;
    const open = !!state.toolsOpen;
    mapToolsDock.classList.toggle("tools-open", open);
    mapToolsDock.classList.toggle("tools-collapsed", !open);
    toolsToggleBtn.classList.toggle("tools-off", !open);
    toolsToggleBtn.textContent = open ? "◎" : "◌";
    toolsToggleBtn.title = open ? strings.toolsHide : strings.toolsShow;
    toolsToggleBtn.setAttribute("aria-expanded", open ? "true" : "false");
    layoutToolsDock();
  }

  function layoutToolsDock() {
    if (!mapToolsDock) return;
    mapToolsDock.classList.remove("layout-v", "layout-h", "layout-grid");
    stage.classList.remove("pad-right", "pad-top");

    const w = Math.max(stage.clientWidth, 1);
    const h = Math.max(stage.clientHeight, 1);

    if (!state.toolsOpen) return;

    if (h < 300 || w < 280) mapToolsDock.classList.add("layout-grid");
    else if (h < 400) mapToolsDock.classList.add("layout-h");
    else mapToolsDock.classList.add("layout-v");

    if (mapToolsDock.classList.contains("layout-v")) stage.classList.add("pad-right");
    else stage.classList.add("pad-top");

    if (shouldFollow() && state.player) trackPlayer(false);
    else fitToView();
  }

  function toggleToolsOpen() {
    state.toolsOpen = !state.toolsOpen;
    syncToolsToggle();
  }

  function syncLayerButtons() {
    const titles = {
      extracts: strings.layerExtracts,
      mines: strings.layerMines,
      spawns: strings.layerSpawns,
      quests: strings.layerQuests,
      labels: strings.layerLabels
    };
    document.querySelectorAll(".layer-btn").forEach((btn) => {
      const key = btn.getAttribute("data-layer");
      btn.classList.toggle("active", !!state.layers[key]);
      btn.title = titles[key] || key;
    });
  }

  playerEl.innerHTML =
    '<div class="player-icon-wrap">' +
    `<img class="marker-icon player-icon" src="${MARKER_ICONS.player}" alt="player" draggable="false">` +
    "</div>";

  function extractIconSrc(ex) {
    const f = String(ex.faction || "any").toLowerCase();
    const name = String(ex.name || "").toLowerCase();
    if (f === "scav") return MARKER_ICONS.extractScav;
    if (f === "shared" || name.includes("co-op") || name.includes("co op")) return MARKER_ICONS.extractShared;
    return MARKER_ICONS.extractPmc;
  }

  function createMarkerIcon(src, alt) {
    const img = document.createElement("img");
    img.className = "marker-icon";
    img.src = src;
    img.alt = alt || "";
    img.draggable = false;
    return img;
  }

  function appendMarkerIcon(node, src, alt) {
    const wrap = document.createElement("div");
    wrap.className = "marker-icon-wrap";
    wrap.appendChild(createMarkerIcon(src, alt));
    node.appendChild(wrap);
    return wrap;
  }

  function createMarkerNode(className, pctX, pctY, iconSrc, alt) {
    const node = document.createElement("div");
    node.className = className;
    place(node, pctX, pctY);
    appendMarkerIcon(node, iconSrc, alt);
    return node;
  }

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
    if (routeLayer) {
      routeLayer.setAttribute("width", String(w));
      routeLayer.setAttribute("height", String(h));
      routeLayer.setAttribute("viewBox", `0 0 ${w} ${h}`);
    }
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
    updateRoute();
  }

  function updatePlayerVisual() {
    const icon = playerEl.querySelector(".player-icon");
    if (!icon) return;
    const inv = state.scale > 0.0001 ? 1 / state.scale : 1;
    const yaw = state.player?.yaw || 0;
    icon.style.transform = `rotate(${yaw}deg) scale(${inv})`;
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

  function computeFitScale() {
    const w = Math.max(stage.clientWidth, 1);
    const h = Math.max(stage.clientHeight, 1);
    const rad = (state.rotation * Math.PI) / 180;
    const ac = Math.abs(Math.cos(rad));
    const as = Math.abs(Math.sin(rad));
    const bw = state.worldW * ac + state.worldH * as;
    const bh = state.worldW * as + state.worldH * ac;
    return Math.min(w / bw, h / bh) * 0.96;
  }

  function fitToView() {
    state.scale = computeFitScale();
    const cx = state.worldW / 2;
    const cy = state.worldH / 2;
    const w = Math.max(stage.clientWidth, 1);
    const h = Math.max(stage.clientHeight, 1);
    state.panX = w / 2 - cx * state.scale;
    state.panY = h / 2 - cy * state.scale;
    applyTransform();
  }

  /** Mantém o player no centro com zoom útil (minimapa que acompanha o movimento). */
  function trackPlayer(resetZoom) {
    if (!state.player || !state.map) {
      fitToView();
      return;
    }
    const fit = computeFitScale();
    if (resetZoom || !state.followZoomApplied) {
      state.scale = Math.min(6, Math.max(fit * 1.4, fit * state.followZoomMult));
      state.followZoomApplied = true;
    }
    const { pctX, pctY } = gameToPct(state.player.x, state.player.z, state.map);
    const lx = pctX * state.worldW;
    const ly = pctY * state.worldH;
    const w = Math.max(stage.clientWidth, 1);
    const h = Math.max(stage.clientHeight, 1);
    applyTransform();
    const scr = worldToScreen(lx, ly);
    state.panX += w / 2 - scr.x;
    state.panY += h / 2 - scr.y;
    applyTransform();
  }

  function centerOnPlayer() {
    trackPlayer(false);
  }

  function shouldFollow() {
    return state.follow && !state.followPaused && !state.dragging;
  }

  let followResumeTimer = 0;
  function pauseFollowTemporarily() {
    state.followPaused = true;
    if (followResumeTimer) clearTimeout(followResumeTimer);
    followResumeTimer = setTimeout(() => {
      followResumeTimer = 0;
      if (!state.follow) return;
      state.followPaused = false;
      if (state.player) trackPlayer(false);
    }, 1800);
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
    if (shouldFollow() && state.player) trackPlayer(false);
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

  function pctToGame(pctX, pctY, map) {
    const rot = map.coordinateRotation || 0;
    const rad = (rot * Math.PI) / 180;
    const cos = Math.cos(rad);
    const sin = Math.sin(rad);
    const unrotate = (lng, lat) => ({
      x: lng * cos + lat * sin,
      z: -lng * sin + lat * cos
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
      const rotate = (gx, gz) => ({
        lng: gx * cos - gz * sin,
        lat: gx * sin + gz * cos
      });
      const toPx = (gx, gz) => {
        const r = rotate(gx, gz);
        return {
          px: scaleX * r.lng + marginX,
          py: scaleY * r.lat + marginY
        };
      };
      let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
      for (const [bx, bz] of corners) {
        const c = toPx(bx, bz);
        if (c.px < minX) minX = c.px;
        if (c.px > maxX) maxX = c.px;
        if (c.py < minY) minY = c.py;
        if (c.py > maxY) maxY = c.py;
      }
      const px = pctX * (maxX - minX) + minX;
      const py = pctY * (maxY - minY) + minY;
      const lng = (px - marginX) / scaleX;
      const lat = (py - marginY) / scaleY;
      return unrotate(lng, lat);
    }

    const rotate = (gx, gz) => ({
      lng: gx * cos - gz * sin,
      lat: gx * sin + gz * cos
    });
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (const [bx, bz] of corners) {
      const c = rotate(bx, bz);
      if (c.lng < minX) minX = c.lng;
      if (c.lng > maxX) maxX = c.lng;
      if (c.lat < minY) minY = c.lat;
      if (c.lat > maxY) maxY = c.lat;
    }
    const lng = pctX * (maxX - minX) + minX;
    const lat = maxY - pctY * (maxY - minY);
    return unrotate(lng, lat);
  }

  function hasFloors() {
    return Array.isArray(state.map?.floors) && state.map.floors.length > 1;
  }

  function activeFloor() {
    if (!hasFloors()) return null;
    const idx = Math.min(Math.max(state.floorIndex, 0), state.map.floors.length - 1);
    return state.map.floors[idx];
  }

  function defaultFloorIndex(floors) {
    const ground = floors.findIndex((f) =>
      /ground|térreo|terreo/i.test(f.id || "") ||
      /ground/i.test(f.svgLayer || "")
    );
    if (ground >= 0) return ground;
    return Math.max(0, Math.floor(floors.length / 2) - (floors.length > 2 ? 0 : 0));
  }

  function floorIndexForHeight(y) {
    if (!hasFloors() || y == null || Number.isNaN(y)) return state.floorIndex;
    const floors = state.map.floors;
    for (let i = 0; i < floors.length; i++) {
      const f = floors[i];
      const min = f.minHeight ?? -Infinity;
      const max = f.maxHeight ?? Infinity;
      if (y >= min && y < max) return i;
    }
    let best = 0;
    let bestDist = Infinity;
    for (let i = 0; i < floors.length; i++) {
      const f = floors[i];
      const mid = ((f.minHeight ?? 0) + (f.maxHeight ?? 0)) / 2;
      const d = Math.abs(y - mid);
      if (d < bestDist) {
        bestDist = d;
        best = i;
      }
    }
    return best;
  }

  function markerOnFloor(y) {
    if (!hasFloors()) return true;
    if (y == null || Number.isNaN(y)) return true;
    const f = activeFloor();
    if (!f) return true;
    const min = f.minHeight ?? -Infinity;
    const max = f.maxHeight ?? Infinity;
    return y >= min && y < max;
  }

  function applySvgFloor() {
    const svg = svgHost.querySelector("svg");
    if (!svg || !hasFloors()) return;
    const active = activeFloor();
    let matched = 0;
    for (const fl of state.map.floors) {
      const nodes = svg.querySelectorAll(`[id="${fl.svgLayer}"]`);
      const show = active && fl.id === active.id;
      nodes.forEach((node) => {
        matched++;
        node.style.display = show ? "" : "none";
      });
    }
    if (!matched && active) {
      for (const fl of state.map.floors) {
        if (fl.id !== active.id) continue;
        const loose = svg.querySelectorAll(`[id*="${fl.svgLayer}"], [class*="${fl.svgLayer}"]`);
        loose.forEach((node) => { node.style.display = ""; });
      }
    }
  }

  function updateFloorUi() {
    const show = hasFloors();
    if (floorSep) floorSep.hidden = !show;
    if (floorTools) floorTools.hidden = !show;
    if (!show) return;
    const f = activeFloor();
    const floors = state.map.floors;
    if (floorShortEl && f) floorShortEl.textContent = f.shortLabel || f.short || f.name || "?";
    const floorTitle = f ? `${strings.floorCurrent}: ${f.name || f.short || ""}` : strings.floorCurrent;
    if (floorLabelBtn) floorLabelBtn.title = floorTitle;
    if (floorUpBtn) {
      floorUpBtn.title = strings.floorUp;
      floorUpBtn.disabled = state.floorIndex >= floors.length - 1;
    }
    if (floorDownBtn) {
      floorDownBtn.title = strings.floorDown;
      floorDownBtn.disabled = state.floorIndex <= 0;
    }
  }

  function setFloorIndex(index, manual) {
    if (!hasFloors()) return;
    const max = state.map.floors.length - 1;
    const next = Math.min(Math.max(index, 0), max);
    if (next === state.floorIndex && !!manual === state.floorManual) return;
    state.floorIndex = next;
    if (manual) state.floorManual = true;
    applySvgFloor();
    updateFloorUi();
    renderMarkers();
    updateRoute();
  }

  function shiftFloor(delta) {
    if (!hasFloors()) return;
    setFloorIndex(state.floorIndex + delta, true);
  }

  function maybeAutoFloorFromPlayer() {
    if (!state.autoFloor || state.floorManual || !state.player || !hasFloors()) return;
    const idx = floorIndexForHeight(state.player.y);
    if (idx !== state.floorIndex) setFloorIndex(idx, false);
  }

  function initFloors() {
    if (!hasFloors()) {
      updateFloorUi();
      return;
    }
    if (state.floorIndex < 0 || state.floorIndex >= state.map.floors.length) {
      state.floorIndex = defaultFloorIndex(state.map.floors);
    }
    applySvgFloor();
    updateFloorUi();
    maybeAutoFloorFromPlayer();
  }

  function setAutoFloor(value) {
    state.autoFloor = !!value;
    if (state.autoFloor) {
      state.floorManual = false;
      maybeAutoFloorFromPlayer();
    }
  }

  function place(el, pctX, pctY) {
    el.style.left = pctX * 100 + "%";
    el.style.top = pctY * 100 + "%";
  }

  async function loadMap(map) {
    const prevId = state.map?.id;
    const prevFloor = state.floorIndex;
    const prevManual = state.floorManual;
    state.map = map;
    state.rotation = 0;
    state.followZoomApplied = false;
    state.followPaused = false;
    if (prevId && map?.id === prevId) {
      state.floorIndex = prevFloor;
      state.floorManual = prevManual;
    } else {
      state.floorIndex = defaultFloorIndex(map?.floors || []);
      state.floorManual = false;
    }
    if (typeof map?.autoFloor === "boolean") state.autoFloor = map.autoFloor;
    const fallback = boundsSize(map);
    applyWorldSize(fallback.w, fallback.h);
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
    initFloors();
    renderMarkers();
    updateRoute();
    requestAnimationFrame(() => {
      if (shouldFollow() && state.player) trackPlayer(true);
      else fitToView();
    });
  }

  function inMap(pctX, pctY) {
    return pctX >= -0.02 && pctX <= 1.02 && pctY >= -0.02 && pctY <= 1.02;
  }

  function hideTip() {
    tip.hidden = true;
    tip.className = "";
    tip.innerHTML = "";
  }

  function showTip(name, kind, clientX, clientY) {
    const rect = stage.getBoundingClientRect();
    tip.hidden = false;
    const kindLabel =
      kind === "mine" ? strings.mine :
      kind === "spawn" ? strings.spawn :
      kind === "quest" ? strings.quest :
      strings.extract;
    tip.className =
      kind === "mine" ? "mine-tip" :
      kind === "spawn" ? "spawn-tip" :
      kind === "quest" ? "quest-tip" :
      "extract-tip";
    tip.innerHTML =
      `<span class="tip-kind">${kindLabel}</span>` +
      `<span class="tip-name">${name || kindLabel}</span>`;
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

  function isWp(kind, id) {
    return state.waypoint && state.waypoint.kind === kind && state.waypoint.id === id;
  }

  function setWaypoint(wp) {
    state.waypoint = wp;
    if (wp) setWpPlaceMode(false);
    updateWpBanner();
    renderMarkers();
    updateRoute();
    window.chrome?.webview?.postMessage({ type: "waypoint", waypoint: wp });
  }

  function syncPlaceWpButton() {
    if (!placeWpBtn) return;
    placeWpBtn.classList.toggle("active", state.wpPlaceMode);
    placeWpBtn.title = state.wpPlaceMode ? strings.placeWaypointActive : strings.placeWaypoint;
  }

  function setWpPlaceMode(on) {
    state.wpPlaceMode = !!on;
    stage.classList.toggle("wp-place", state.wpPlaceMode);
    syncPlaceWpButton();
    updateWpBanner();
  }

  function placeWaypointFromPointer(e) {
    if (!state.map) return;
    const rect = stage.getBoundingClientRect();
    const sx = e.clientX - rect.left;
    const sy = e.clientY - rect.top;
    const world = screenToWorld(sx, sy);
    const pctX = world.x / state.worldW;
    const pctY = world.y / state.worldH;
    if (!inMap(pctX, pctY)) return;
    const { x, z } = pctToGame(pctX, pctY, state.map);
    const name = strings.customWaypoint;
    setWaypoint({
      kind: "custom",
      id: `custom-${Math.round(x)},${Math.round(z)}`,
      name,
      x,
      z
    });
  }

  function clearWaypoint() {
    state.waypoint = null;
    updateWpBanner();
    renderMarkers();
    updateRoute();
    window.chrome?.webview?.postMessage({ type: "waypoint", waypoint: null });
  }

  function updateWpBanner() {
    if (!wpBanner) return;
    if (state.wpPlaceMode) {
      wpBanner.hidden = false;
      wpBanner.textContent = strings.placeWaypointHint;
      wpBanner.className = "wp-place-hint";
      return;
    }
    wpBanner.className = "";
    if (!state.waypoint) {
      wpBanner.hidden = true;
      wpBanner.textContent = "";
      return;
    }
    wpBanner.hidden = false;
    wpBanner.textContent = `${strings.waypoint}: ${state.waypoint.name || ""}`;
  }

  function updateRoute() {
    if (!routeLayer) return;
    routeLayer.innerHTML = "";
    if (!state.map || !state.player || !state.waypoint) return;
    const a = gameToPct(state.player.x, state.player.z, state.map);
    const b = gameToPct(state.waypoint.x, state.waypoint.z, state.map);
    const x1 = a.pctX * state.worldW;
    const y1 = a.pctY * state.worldH;
    const x2 = b.pctX * state.worldW;
    const y2 = b.pctY * state.worldH;
    const line = document.createElementNS("http://www.w3.org/2000/svg", "line");
    line.setAttribute("x1", String(x1));
    line.setAttribute("y1", String(y1));
    line.setAttribute("x2", String(x2));
    line.setAttribute("y2", String(y2));
    line.setAttribute("stroke", "#ffd246");
    line.setAttribute("stroke-width", "2");
    line.setAttribute("stroke-dasharray", "8 6");
    line.setAttribute("opacity", "0.9");
    routeLayer.appendChild(line);
  }

  function renderMarkers() {
    hideTip();
    markers.innerHTML = "";
    if (!state.map) return;

    if (state.layers.extracts) {
      for (const ex of state.extracts) {
        if (!markerOnFloor(ex.y)) continue;
        const { pctX, pctY } = gameToPct(ex.x, ex.z, state.map);
        if (!inMap(pctX, pctY)) continue;
        const id = ex.name || `${ex.x},${ex.z}`;
        const name = ex.name || "EXFIL";
        const node = createMarkerNode(
          "marker-wrap extract " + String(ex.faction || "any").toLowerCase(),
          pctX,
          pctY,
          extractIconSrc(ex),
          name
        );
        if (isWp("extract", id)) node.classList.add("waypoint-target");
        if (state.layers.labels) {
          const lab = document.createElement("div");
          lab.className = "extract-label";
          lab.textContent = name;
          node.appendChild(lab);
        }
        bindMarkerTip(node, name, "extract");
        node.addEventListener("click", (e) => {
          e.stopPropagation();
          setWaypoint({ kind: "extract", id, name, x: ex.x, z: ex.z });
        });
        markers.appendChild(node);
      }
    }

    if (state.layers.mines) {
      for (const m of state.mines) {
        if (!markerOnFloor(m.y)) continue;
        const { pctX, pctY } = gameToPct(m.x, m.z, state.map);
        if (!inMap(pctX, pctY)) continue;
        const name = m.name || strings.mine;
        const node = createMarkerNode("marker-wrap mine", pctX, pctY, MARKER_ICONS.hazard, name);
        if (state.layers.labels) {
          const lab = document.createElement("div");
          lab.className = "extract-label mine-label";
          lab.textContent = name;
          node.appendChild(lab);
        }
        bindMarkerTip(node, name, "mine");
        markers.appendChild(node);
      }
    }

    if (state.layers.spawns) {
      for (const sp of state.spawns) {
        if (!markerOnFloor(sp.y)) continue;
        const { pctX, pctY } = gameToPct(sp.x, sp.z, state.map);
        if (!inMap(pctX, pctY)) continue;
        const name = sp.name || strings.spawn;
        const node = createMarkerNode("marker-wrap spawn", pctX, pctY, MARKER_ICONS.spawnPmc, name);
        if (state.layers.labels) {
          const lab = document.createElement("div");
          lab.className = "extract-label";
          lab.textContent = name;
          node.appendChild(lab);
        }
        bindMarkerTip(node, name, "spawn");
        markers.appendChild(node);
      }
    }

    if (state.layers.quests) {
      for (const q of state.quests) {
        if (state.completedQuests.has(q.slug)) continue;
        if (!state.enabledQuests.has(q.slug)) continue;
        for (const obj of q.objectives || []) {
          if (!markerOnFloor(obj.y)) continue;
          const { pctX, pctY } = gameToPct(obj.x, obj.z, state.map);
          if (!inMap(pctX, pctY)) continue;
          const id = obj.id || `${q.slug}-${obj.x}`;
          const name = obj.description || q.name;
          const node = createMarkerNode("marker-wrap quest", pctX, pctY, MARKER_ICONS.quest, q.name);
          if (isWp("quest", id)) node.classList.add("waypoint-target");
          if (state.layers.labels) {
            const lab = document.createElement("div");
            lab.className = "extract-label quest-label";
            lab.textContent = q.name;
            node.appendChild(lab);
          }
          bindMarkerTip(node, name, "quest");
          node.addEventListener("click", (e) => {
            e.stopPropagation();
            setWaypoint({ kind: "quest", id, name: q.name, x: obj.x, z: obj.z });
          });
          markers.appendChild(node);
        }
      }
    }

    if (state.waypoint) {
      const { pctX, pctY } = gameToPct(state.waypoint.x, state.waypoint.z, state.map);
      if (inMap(pctX, pctY)) {
        const pin = createMarkerNode(
          "marker-wrap waypoint-pin-wrap",
          pctX,
          pctY,
          MARKER_ICONS.waypoint,
          state.waypoint.name || strings.waypoint
        );
        markers.appendChild(pin);
      }
    }
  }

  function setMarkers(extracts, mines, spawns, showLabels) {
    state.extracts = Array.isArray(extracts) ? extracts : [];
    state.mines = Array.isArray(mines) ? mines : [];
    state.spawns = Array.isArray(spawns) ? spawns : [];
    if (typeof showLabels === "boolean") state.layers.labels = showLabels;
    syncLayerButtons();
    renderMarkers();
    updateRoute();
  }

  function setQuests(quests, enabledSlugs, completedSlugs) {
    state.quests = Array.isArray(quests) ? quests : [];
    const completed = new Set(Array.isArray(completedSlugs) ? completedSlugs : []);
    for (const q of state.quests) {
      if (q.completed) completed.add(q.slug);
    }
    state.completedQuests = completed;
    const enabled = Array.isArray(enabledSlugs) ? enabledSlugs : [];
    state.enabledQuests = new Set(enabled.filter((s) => !completed.has(s)));
    renderMarkers();
    updateRoute();
  }

  function setLayers(layers) {
    if (!layers || typeof layers !== "object") return;
    Object.assign(state.layers, layers);
    syncLayerButtons();
    renderMarkers();
  }

  function setShowLabels(value) {
    state.layers.labels = !!value;
    syncLayerButtons();
    renderMarkers();
  }

  function setPlayer(p) {
    state.player = p;
    if (!p || !state.map) {
      playerEl.hidden = true;
      updateRoute();
      return;
    }
    playerEl.hidden = false;
    const { pctX, pctY } = gameToPct(p.x, p.z, state.map);
    place(playerEl, pctX, pctY);
    updatePlayerVisual();
    updateRoute();
    maybeAutoFloorFromPlayer();
    if (shouldFollow()) trackPlayer(false);
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
    if (e.target.closest("#mapToolsDock") || e.target.closest("#toolsToggle")) return;
    if (state.wpPlaceMode && e.button === 0 && !e.altKey) {
      e.preventDefault();
      placeWaypointFromPointer(e);
      return;
    }
    if (e.altKey || e.button === 2) {
      window.chrome?.webview?.postMessage({ type: "dragWindow" });
      return;
    }
    state.dragging = true;
    pauseFollowTemporarily();
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
  clearWpBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    clearWaypoint();
  });
  placeWpBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    setWpPlaceMode(!state.wpPlaceMode);
  });

  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && state.wpPlaceMode) setWpPlaceMode(false);
  });

  floorUpBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    shiftFloor(1);
  });
  floorDownBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    shiftFloor(-1);
  });
  floorLabelBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    if (!hasFloors()) return;
    setFloorIndex((state.floorIndex + 1) % state.map.floors.length, true);
  });

  document.querySelectorAll(".layer-btn").forEach((btn) => {
    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      const key = btn.getAttribute("data-layer");
      if (!key) return;
      state.layers[key] = !state.layers[key];
      syncLayerButtons();
      renderMarkers();
      window.chrome?.webview?.postMessage({ type: "layer", key, value: state.layers[key] });
    });
  });

  toolsToggleBtn?.addEventListener("click", (e) => {
    e.stopPropagation();
    toggleToolsOpen();
  });

  new ResizeObserver(() => {
    layoutToolsDock();
    if (shouldFollow() && state.player) trackPlayer(false);
    else fitToView();
  }).observe(stage);

  function onMessage(data) {
    if (!data || !data.type) return;
    if (data.type === "lang") applyStrings(data.strings);
    if (data.type === "loadMap") loadMap(data.map);
    if (data.type === "markers") setMarkers(data.extracts, data.mines, data.spawns, data.showLabels);
    if (data.type === "quests") setQuests(data.quests, data.enabled, data.completed);
    if (data.type === "layers") setLayers(data.layers);
    if (data.type === "extracts") setMarkers(data.extracts, [], [], data.showLabels);
    if (data.type === "showLabels") setShowLabels(data.value);
    if (data.type === "waypoint") {
      state.waypoint = data.waypoint || null;
      updateWpBanner();
      renderMarkers();
      updateRoute();
    }
    if (data.type === "player") setPlayer(data.player);
    if (data.type === "autoFloor") setAutoFloor(data.value);
    if (data.type === "follow") {
      state.follow = !!data.value;
      state.followPaused = false;
      if (state.follow) {
        state.followZoomApplied = false;
        if (state.player) trackPlayer(true);
      } else {
        fitToView();
      }
    }
    if (data.type === "resetView") {
      if (shouldFollow() && state.player) {
        state.followZoomApplied = false;
        trackPlayer(true);
      } else {
        fitToView();
      }
    }
  }

  window.chrome?.webview?.addEventListener("message", (ev) => onMessage(ev.data));
  window.addEventListener("message", (ev) => onMessage(ev.data));

  syncLayerButtons();
  syncToolsToggle();
  fitToView();
  window.chrome?.webview?.postMessage({ type: "ready" });
})();
