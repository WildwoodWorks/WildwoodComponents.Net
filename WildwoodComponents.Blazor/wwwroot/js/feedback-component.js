// feedback-component.js
// ES module companion for the Blazor FeedbackWidgetComponent.
// Ported from WildwoodAdmin/wwwroot/js/feedback-widget.js — provides the browser-only
// behavior that cannot be done from C#: console/error buffering, browser-context
// collection, drag-to-reposition of the floating button, position persistence, and
// screenshot capture (area + full page) with a lightweight annotation editor.
//
// The component drives the form (type/title/description/attachments) in Blazor markup;
// this module only owns the pieces that must run in the browser. A single shared console
// buffer is installed once per page so errors are captured even before the widget opens.

const POSITION_KEY = 'ww-feedback-widget-pos';
const MAX_CONSOLE_ENTRIES = 50;

// ---------- Shared console & error capture (installed once per page) ----------
const _state = (window.__wwFeedbackState = window.__wwFeedbackState || {
    consoleBuffer: [],
    hooksInstalled: false
});

function pushConsoleEntry(level, args) {
    let msg = args.map(function (a) {
        if (typeof a === 'string') return a;
        try { return JSON.stringify(a); } catch (e) { return String(a); }
    }).join(' ');
    if (msg.length > 500) msg = msg.substring(0, 500) + '...';
    _state.consoleBuffer.push({ level: level, message: msg, timestamp: new Date().toISOString() });
    if (_state.consoleBuffer.length > MAX_CONSOLE_ENTRIES) _state.consoleBuffer.shift();
}

function ensureConsoleHooks() {
    if (_state.hooksInstalled) return;
    _state.hooksInstalled = true;

    const origError = console.error;
    const origWarn = console.warn;

    console.error = function () {
        pushConsoleEntry('error', Array.prototype.slice.call(arguments));
        origError.apply(console, arguments);
    };
    console.warn = function () {
        pushConsoleEntry('warn', Array.prototype.slice.call(arguments));
        origWarn.apply(console, arguments);
    };

    window.addEventListener('error', function (e) {
        pushConsoleEntry('exception', [e.message, 'at ' + (e.filename || '') + ':' + (e.lineno || '') + ':' + (e.colno || '')]);
    });

    window.addEventListener('unhandledrejection', function (e) {
        const reason = e.reason;
        const msg = reason instanceof Error
            ? reason.message + (reason.stack ? '\n' + reason.stack.split('\n').slice(0, 3).join('\n') : '')
            : String(reason);
        pushConsoleEntry('unhandledrejection', [msg]);
    });
}

// ---------- Browser context ----------
function collectBrowserContext() {
    const ctx = {
        consoleLog: _state.consoleBuffer.slice(),
        environment: {
            viewportWidth: window.innerWidth,
            viewportHeight: window.innerHeight,
            screenWidth: screen.width,
            screenHeight: screen.height,
            devicePixelRatio: window.devicePixelRatio || 1,
            platform: navigator.platform,
            language: navigator.language,
            languages: navigator.languages ? navigator.languages.slice() : [navigator.language],
            cookiesEnabled: navigator.cookieEnabled,
            online: navigator.onLine,
            colorScheme: window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light',
            touchSupport: 'ontouchstart' in window || navigator.maxTouchPoints > 0,
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone
        }
    };

    if (window.performance) {
        const nav = performance.getEntriesByType && performance.getEntriesByType('navigation')[0];
        if (nav) {
            ctx.performance = {
                pageLoadMs: Math.round(nav.loadEventEnd - nav.startTime),
                domContentLoadedMs: Math.round(nav.domContentLoadedEventEnd - nav.startTime),
                firstPaintMs: null
            };
            const paint = performance.getEntriesByType('paint');
            if (paint && paint.length) {
                const fp = paint.find(function (p) { return p.name === 'first-contentful-paint'; }) || paint[0];
                ctx.performance.firstPaintMs = Math.round(fp.startTime);
            }
        }
        if (performance.memory) {
            ctx.performance = ctx.performance || {};
            ctx.performance.jsHeapUsedMB = Math.round(performance.memory.usedJSHeapSize / 1048576);
            ctx.performance.jsHeapTotalMB = Math.round(performance.memory.totalJSHeapSize / 1048576);
        }
    }

    return JSON.stringify(ctx);
}

// ---------- html2canvas loader ----------
// Self-hosting override: by default html2canvas is loaded from the cdnjs CDN. Hosts that
// cannot (or prefer not to) reach a third-party CDN can serve the library same-origin —
// e.g. from _content/WildwoodComponents.Blazor/js/html2canvas.min.js — by setting
// `window.__WW_HTML2CANVAS_SRC__` to that URL before the first screenshot capture. An
// empty/non-string value falls back to the default CDN URL. Alternatively, pre-register
// `window.html2canvas` yourself and no script is injected at all.
const DEFAULT_HTML2CANVAS_URL = 'https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js';

function resolveHtml2CanvasSrc() {
    const override = window.__WW_HTML2CANVAS_SRC__;
    if (typeof override === 'string' && override.length > 0) return override;
    return DEFAULT_HTML2CANVAS_URL;
}

function ensureHtml2Canvas() {
    return new Promise(function (resolve, reject) {
        if (typeof window.html2canvas !== 'undefined') { resolve(); return; }
        const s = document.createElement('script');
        s.src = resolveHtml2CanvasSrc();
        s.onload = function () { resolve(); };
        s.onerror = function () { reject(new Error('Failed to load screenshot library.')); };
        document.head.appendChild(s);
    });
}

// ---------- A widget controller instance per component ----------
class FeedbackController {
    constructor(button, dotNetRef, options) {
        this.button = button;
        this.dotNetRef = dotNetRef;
        this.options = options || {};
        this.isDragging = false;
        this.hasMoved = false;
        this.dragOffset = { x: 0, y: 0 };

        this._onMouseDown = this.onDragStart.bind(this);
        this._onMouseMove = this.onDragMove.bind(this);
        this._onMouseUp = this.onDragEnd.bind(this);
        this._onTouchStart = this.onTouchDragStart.bind(this);
        this._onTouchMove = this.onTouchDragMove.bind(this);
        this._onTouchEnd = this.onTouchDragEnd.bind(this);
        this._onClickCapture = this.onClickCapture.bind(this);
        this._onResize = this.restorePosition.bind(this);

        if (this.button) {
            this.button.addEventListener('mousedown', this._onMouseDown);
            this.button.addEventListener('touchstart', this._onTouchStart, { passive: false });
            // Capture phase so a post-drag synthetic click is swallowed before Blazor's @onclick sees it.
            this.button.addEventListener('click', this._onClickCapture, true);
        }
        document.addEventListener('mousemove', this._onMouseMove);
        document.addEventListener('mouseup', this._onMouseUp);
        document.addEventListener('touchmove', this._onTouchMove, { passive: false });
        document.addEventListener('touchend', this._onTouchEnd);
        window.addEventListener('resize', this._onResize);

        this.restorePosition();
    }

    dispose() {
        if (this.button) {
            this.button.removeEventListener('mousedown', this._onMouseDown);
            this.button.removeEventListener('touchstart', this._onTouchStart);
            this.button.removeEventListener('click', this._onClickCapture, true);
        }
        document.removeEventListener('mousemove', this._onMouseMove);
        document.removeEventListener('mouseup', this._onMouseUp);
        document.removeEventListener('touchmove', this._onTouchMove);
        document.removeEventListener('touchend', this._onTouchEnd);
        window.removeEventListener('resize', this._onResize);
    }

    // Swallow the native click that a browser synthesizes at the end of a mouse drag so the
    // button is only repositioned, not toggled. A plain (no-move) click is allowed through to
    // Blazor's @onclick, which is the single toggle authority for mouse + keyboard.
    onClickCapture(e) {
        if (this._suppressNextClick) {
            this._suppressNextClick = false;
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }

    notifyToggle() {
        if (this.dotNetRef) {
            try { this.dotNetRef.invokeMethodAsync('OnButtonActivated'); } catch (e) { /* circuit gone */ }
        }
    }

    // ----- drag handlers -----
    onDragStart(e) {
        this.isDragging = true;
        this.hasMoved = false;
        const rect = this.button.getBoundingClientRect();
        this.dragOffset.x = e.clientX - rect.left;
        this.dragOffset.y = e.clientY - rect.top;
        e.preventDefault();
    }
    onDragMove(e) {
        if (!this.isDragging) return;
        this.hasMoved = true;
        this.positionBtnAt(e.clientX - this.dragOffset.x, e.clientY - this.dragOffset.y);
        this.savePosition();
    }
    onDragEnd() {
        // Mouse toggling is handled by Blazor's @onclick (which also covers keyboard and the
        // no-JS case). Here we only need to suppress the synthetic click that follows a drag so
        // repositioning the button does not also open/close the panel.
        if (this.isDragging && this.hasMoved) this._suppressNextClick = true;
        this.isDragging = false;
    }
    onTouchDragStart(e) {
        if (e.touches.length !== 1) return;
        const t = e.touches[0];
        this.isDragging = true;
        this.hasMoved = false;
        const rect = this.button.getBoundingClientRect();
        this.dragOffset.x = t.clientX - rect.left;
        this.dragOffset.y = t.clientY - rect.top;
        e.preventDefault();
    }
    onTouchDragMove(e) {
        if (!this.isDragging || e.touches.length !== 1) return;
        const t = e.touches[0];
        this.hasMoved = true;
        this.positionBtnAt(t.clientX - this.dragOffset.x, t.clientY - this.dragOffset.y);
        this.savePosition();
        e.preventDefault();
    }
    onTouchDragEnd() {
        if (this.isDragging && !this.hasMoved) this.notifyToggle();
        this.isDragging = false;
    }

    positionBtnAt(left, top) {
        if (!this.button) return;
        const vw = window.innerWidth, vh = window.innerHeight;
        const bw = this.button.offsetWidth || 52, bh = this.button.offsetHeight || 52;
        const clampedLeft = Math.max(0, Math.min(left, vw - bw));
        const clampedTop = Math.max(0, Math.min(top, vh - bh));
        this.button.style.left = clampedLeft + 'px';
        this.button.style.top = clampedTop + 'px';
        this.button.style.right = 'auto';
        this.button.style.bottom = 'auto';
    }

    savePosition() {
        try {
            const vw = window.innerWidth;
            const rect = this.button.getBoundingClientRect();
            localStorage.setItem(POSITION_KEY, JSON.stringify({ right: vw - rect.right, top: rect.top }));
        } catch (e) { /* storage unavailable */ }
    }

    restorePosition() {
        try {
            if (!this.button) return;
            const raw = localStorage.getItem(POSITION_KEY);
            if (!raw) return;
            const s = JSON.parse(raw);
            if (s && typeof s.right === 'number' && typeof s.top === 'number') {
                const bw = this.button.offsetWidth || 52;
                const left = window.innerWidth - s.right - bw;
                this.positionBtnAt(left, s.top);
            }
        } catch (e) { /* ignore */ }
    }
}

// ---------- Screenshot helpers ----------
function compressScreenshot(canvas, quality, maxSizeKb) {
    const q = (quality || 80) / 100;
    let dataUrl = canvas.toDataURL('image/jpeg', q);
    if (maxSizeKb && maxSizeKb > 0) {
        const maxBytes = maxSizeKb * 1024;
        let cq = q;
        while (dataUrl.length * 0.75 > maxBytes && cq > 0.1) {
            cq -= 0.1;
            dataUrl = canvas.toDataURL('image/jpeg', cq);
        }
        if (dataUrl.length * 0.75 > maxBytes) {
            const sc = Math.sqrt(maxBytes / (dataUrl.length * 0.75));
            const sc2 = document.createElement('canvas');
            sc2.width = Math.round(canvas.width * sc);
            sc2.height = Math.round(canvas.height * sc);
            sc2.getContext('2d').drawImage(canvas, 0, 0, sc2.width, sc2.height);
            dataUrl = sc2.toDataURL('image/jpeg', 0.7);
        }
    }
    return dataUrl;
}

function drawArrow(ctx, x1, y1, x2, y2, color) {
    ctx.strokeStyle = color; ctx.fillStyle = color; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
    const angle = Math.atan2(y2 - y1, x2 - x1), hl = 12;
    ctx.beginPath(); ctx.moveTo(x2, y2);
    ctx.lineTo(x2 - hl * Math.cos(angle - Math.PI / 6), y2 - hl * Math.sin(angle - Math.PI / 6));
    ctx.lineTo(x2 - hl * Math.cos(angle + Math.PI / 6), y2 - hl * Math.sin(angle + Math.PI / 6));
    ctx.closePath(); ctx.fill();
}

function drawFreehand(ctx, pts, color) {
    if (pts.length < 2) return;
    ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    ctx.beginPath(); ctx.moveTo(pts[0].x, pts[0].y);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
    ctx.stroke();
}

// Lightweight annotation editor; resolves with a compressed data URL, or null if cancelled.
function openAnnotationEditor(sourceCanvas, quality, maxSizeKb) {
    return new Promise(function (resolve) {
        const editorOverlay = document.createElement('div');
        editorOverlay.className = 'ww-feedback-annotation-overlay';

        const toolbar = document.createElement('div');
        toolbar.className = 'ww-feedback-annotation-toolbar';

        const tools = [
            { icon: '↗', label: 'Arrow', tool: 'arrow' },
            { icon: '◯', label: 'Circle', tool: 'circle' },
            { icon: '✎', label: 'Draw', tool: 'draw' },
            { icon: 'T', label: 'Text', tool: 'text' }
        ];
        let currentTool = 'arrow';
        let annotColor = '#FF0000';
        const annotations = [];

        tools.forEach(function (t) {
            const b = document.createElement('button');
            b.textContent = t.icon; b.title = t.label;
            b.className = 'ann-tool-btn' + (t.tool === currentTool ? ' active' : '');
            b.addEventListener('click', function () {
                currentTool = t.tool;
                toolbar.querySelectorAll('.ann-tool-btn').forEach(function (x) { x.classList.remove('active'); });
                b.classList.add('active');
            });
            toolbar.appendChild(b);
        });

        const colorInput = document.createElement('input');
        colorInput.type = 'color'; colorInput.value = annotColor; colorInput.className = 'ann-color-picker'; colorInput.title = 'Color';
        colorInput.addEventListener('input', function () { annotColor = this.value; });
        toolbar.appendChild(colorInput);

        const undoBtn = document.createElement('button');
        undoBtn.textContent = '↶'; undoBtn.title = 'Undo'; undoBtn.className = 'ann-tool-btn';
        undoBtn.addEventListener('click', function () { if (annotations.length) { annotations.pop(); redraw(); } });
        toolbar.appendChild(undoBtn);

        const spacer = document.createElement('span'); spacer.style.flex = '1'; toolbar.appendChild(spacer);

        const doneBtn = document.createElement('button');
        doneBtn.textContent = 'Done'; doneBtn.className = 'ann-done-btn';
        doneBtn.addEventListener('click', function () {
            const fc = document.createElement('canvas'); fc.width = annCanvas.width; fc.height = annCanvas.height;
            const fctx = fc.getContext('2d');
            fctx.drawImage(sourceCanvas, 0, 0, annCanvas.width, annCanvas.height);
            fctx.drawImage(annCanvas, 0, 0);
            cleanup();
            resolve(compressScreenshot(fc, quality, maxSizeKb));
        });
        toolbar.appendChild(doneBtn);

        const skipBtn = document.createElement('button');
        skipBtn.textContent = 'Skip'; skipBtn.className = 'ann-cancel-btn';
        skipBtn.addEventListener('click', function () {
            cleanup();
            resolve(compressScreenshot(sourceCanvas, quality, maxSizeKb));
        });
        toolbar.appendChild(skipBtn);

        editorOverlay.appendChild(toolbar);

        const canvasWrap = document.createElement('div'); canvasWrap.className = 'ww-feedback-annotation-canvas-wrap';
        const maxW = Math.min(window.innerWidth - 40, 900), maxH = Math.min(window.innerHeight - 100, 600);
        const scale = Math.min(maxW / sourceCanvas.width, maxH / sourceCanvas.height, 1);
        const dispW = Math.round(sourceCanvas.width * scale), dispH = Math.round(sourceCanvas.height * scale);

        const bgCanvas = document.createElement('canvas'); bgCanvas.width = dispW; bgCanvas.height = dispH; bgCanvas.className = 'ww-feedback-annotation-bg';
        bgCanvas.getContext('2d').drawImage(sourceCanvas, 0, 0, dispW, dispH);

        const annCanvas = document.createElement('canvas'); annCanvas.width = dispW; annCanvas.height = dispH; annCanvas.className = 'ww-feedback-annotation-draw';
        const annCtx = annCanvas.getContext('2d');

        canvasWrap.appendChild(bgCanvas); canvasWrap.appendChild(annCanvas);
        editorOverlay.appendChild(canvasWrap); document.body.appendChild(editorOverlay);

        let drawing = false, sx, sy, freehandPts = [];

        annCanvas.addEventListener('mousedown', function (e) {
            const r = annCanvas.getBoundingClientRect(); sx = e.clientX - r.left; sy = e.clientY - r.top; drawing = true;
            if (currentTool === 'draw') freehandPts = [{ x: sx, y: sy }];
            if (currentTool === 'text') {
                drawing = false;
                const txt = prompt('Enter text:');
                if (txt) { annotations.push({ tool: 'text', x: sx, y: sy, text: txt, color: annotColor }); redraw(); }
            }
        });
        annCanvas.addEventListener('mousemove', function (e) {
            if (!drawing) return;
            const r = annCanvas.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
            if (currentTool === 'draw') freehandPts.push({ x: mx, y: my });
            redraw();
            if (currentTool === 'arrow') drawArrow(annCtx, sx, sy, mx, my, annotColor);
            else if (currentTool === 'circle') { annCtx.strokeStyle = annotColor; annCtx.lineWidth = 2; annCtx.beginPath(); annCtx.ellipse((sx + mx) / 2, (sy + my) / 2, Math.abs(mx - sx) / 2, Math.abs(my - sy) / 2, 0, 0, Math.PI * 2); annCtx.stroke(); }
            else if (currentTool === 'draw') drawFreehand(annCtx, freehandPts, annotColor);
        });
        annCanvas.addEventListener('mouseup', function (e) {
            if (!drawing) return; drawing = false;
            const r = annCanvas.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
            if (currentTool === 'arrow') annotations.push({ tool: 'arrow', x1: sx, y1: sy, x2: mx, y2: my, color: annotColor });
            else if (currentTool === 'circle') annotations.push({ tool: 'circle', x1: sx, y1: sy, x2: mx, y2: my, color: annotColor });
            else if (currentTool === 'draw') { annotations.push({ tool: 'draw', points: freehandPts.slice(), color: annotColor }); freehandPts = []; }
            redraw();
        });

        function onKey(e) {
            if (e.key === 'Escape') {
                cleanup();
                resolve(null);
            }
        }
        document.addEventListener('keydown', onKey);

        function cleanup() {
            document.removeEventListener('keydown', onKey);
            editorOverlay.remove();
        }

        function redraw() {
            annCtx.clearRect(0, 0, annCanvas.width, annCanvas.height);
            annotations.forEach(function (a) {
                if (a.tool === 'arrow') drawArrow(annCtx, a.x1, a.y1, a.x2, a.y2, a.color);
                else if (a.tool === 'circle') { annCtx.strokeStyle = a.color; annCtx.lineWidth = 2; annCtx.beginPath(); annCtx.ellipse((a.x1 + a.x2) / 2, (a.y1 + a.y2) / 2, Math.abs(a.x2 - a.x1) / 2, Math.abs(a.y2 - a.y1) / 2, 0, 0, Math.PI * 2); annCtx.stroke(); }
                else if (a.tool === 'draw') drawFreehand(annCtx, a.points, a.color);
                else if (a.tool === 'text') { annCtx.fillStyle = a.color; annCtx.font = 'bold 16px sans-serif'; annCtx.fillText(a.text, a.x, a.y); }
            });
        }
    });
}

// Area capture: user drags a selection rectangle, then annotates. Resolves with data URL or null.
async function captureArea(quality, maxSizeKb) {
    await ensureHtml2Canvas();
    return new Promise(function (resolve) {
        const overlay = document.createElement('div'); overlay.className = 'ww-feedback-capture-overlay';
        const selection = document.createElement('div'); selection.className = 'ww-feedback-capture-selection';
        overlay.appendChild(selection); document.body.appendChild(overlay);
        let startX, startY, capturing = false;

        async function handleCaptureEnd(mx, my) {
            capturing = false; overlay.remove();
            const rect = { x: Math.min(mx, startX), y: Math.min(my, startY), width: Math.abs(mx - startX), height: Math.abs(my - startY) };
            if (rect.width < 10 || rect.height < 10) { resolve(null); return; }
            try {
                const canvas = await window.html2canvas(document.body, {
                    x: rect.x + window.scrollX, y: rect.y + window.scrollY,
                    width: rect.width, height: rect.height, useCORS: true, logging: false
                });
                const result = await openAnnotationEditor(canvas, quality, maxSizeKb);
                resolve(result);
            } catch (e) {
                resolve(null);
            }
        }

        overlay.addEventListener('mousedown', function (e) { startX = e.clientX; startY = e.clientY; capturing = true; selection.style.left = startX + 'px'; selection.style.top = startY + 'px'; selection.style.width = '0px'; selection.style.height = '0px'; selection.style.display = 'block'; });
        overlay.addEventListener('mousemove', function (e) { if (!capturing) return; selection.style.left = Math.min(e.clientX, startX) + 'px'; selection.style.top = Math.min(e.clientY, startY) + 'px'; selection.style.width = Math.abs(e.clientX - startX) + 'px'; selection.style.height = Math.abs(e.clientY - startY) + 'px'; });
        overlay.addEventListener('mouseup', function (e) { if (!capturing) return; handleCaptureEnd(e.clientX, e.clientY); });

        overlay.addEventListener('touchstart', function (e) { if (e.touches.length !== 1) return; const t = e.touches[0]; startX = t.clientX; startY = t.clientY; capturing = true; selection.style.left = startX + 'px'; selection.style.top = startY + 'px'; selection.style.width = '0px'; selection.style.height = '0px'; selection.style.display = 'block'; e.preventDefault(); }, { passive: false });
        overlay.addEventListener('touchmove', function (e) { if (!capturing || e.touches.length !== 1) return; const t = e.touches[0]; selection.style.left = Math.min(t.clientX, startX) + 'px'; selection.style.top = Math.min(t.clientY, startY) + 'px'; selection.style.width = Math.abs(t.clientX - startX) + 'px'; selection.style.height = Math.abs(t.clientY - startY) + 'px'; e.preventDefault(); }, { passive: false });
        overlay.addEventListener('touchend', function (e) { if (!capturing) return; const t = e.changedTouches[0]; handleCaptureEnd(t.clientX, t.clientY); });

        function onEscape(e) {
            if (e.key === 'Escape') {
                overlay.remove();
                document.removeEventListener('keydown', onEscape);
                resolve(null);
            }
        }
        document.addEventListener('keydown', onEscape);
    });
}

// Full-page capture: prefer the native Screen Capture API, fall back to html2canvas.
async function captureFullPage(quality, maxSizeKb) {
    if (navigator.mediaDevices && navigator.mediaDevices.getDisplayMedia) {
        try {
            const stream = await navigator.mediaDevices.getDisplayMedia({ video: { displaySurface: 'browser' }, preferCurrentTab: true });
            const track = stream.getVideoTracks()[0];
            const canvas = await new Promise(function (resolve, reject) {
                // Any failure must reject (not hang): the panel is hidden during capture, so a
                // never-settling promise would leave the widget stuck. Rejection falls back below.
                const fail = function (err) { track.stop(); reject(err || new Error('capture failed')); };
                setTimeout(function () {
                    const video = document.createElement('video');
                    video.srcObject = stream;
                    video.onerror = function () { fail(new Error('video error')); };
                    video.onloadedmetadata = function () {
                        video.play().then(function () {
                            const c = document.createElement('canvas');
                            c.width = video.videoWidth;
                            c.height = video.videoHeight;
                            c.getContext('2d').drawImage(video, 0, 0);
                            track.stop();
                            resolve(c);
                        }).catch(fail);
                    };
                }, 200);
            });
            return await openAnnotationEditor(canvas, quality, maxSizeKb);
        } catch (e) {
            // User cancelled or unsupported — fall back to html2canvas.
        }
    }

    try {
        await ensureHtml2Canvas();
        const canvas = await window.html2canvas(document.body, {
            useCORS: true, logging: false, scale: 1,
            width: window.innerWidth, height: window.innerHeight,
            scrollX: -window.scrollX, scrollY: -window.scrollY,
            windowWidth: window.innerWidth, windowHeight: window.innerHeight
        });
        return await openAnnotationEditor(canvas, quality, maxSizeKb);
    } catch (e) {
        return null;
    }
}

// ---------- Exported module API (invoked from C# via IJSObjectReference) ----------
export function initController(button, dotNetRef, options) {
    ensureConsoleHooks();
    return new FeedbackController(button, dotNetRef, options);
}

export function disposeController(controller) {
    if (controller && typeof controller.dispose === 'function') controller.dispose();
}

export function getBrowserContext() {
    ensureConsoleHooks();
    return collectBrowserContext();
}

export function getPageUrl() {
    return window.location.href;
}

// Returns a base64 data URL of the screenshot, or null if cancelled/failed.
// `button` is the floating feedback button element; it is hidden for the duration of the
// capture so it never appears in the screenshot (the panel is already hidden by Blazor).
export async function captureScreenshot(mode, quality, maxSizeKb, button) {
    ensureConsoleHooks();
    const prevVisibility = button ? button.style.visibility : null;
    if (button) button.style.visibility = 'hidden';
    try {
        if (mode === 'full') {
            return await captureFullPage(quality, maxSizeKb);
        }
        return await captureArea(quality, maxSizeKb);
    } finally {
        if (button) button.style.visibility = prevVisibility || '';
    }
}
