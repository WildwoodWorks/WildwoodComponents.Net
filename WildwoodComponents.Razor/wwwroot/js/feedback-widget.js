/**
 * WildwoodComponents.Razor - Feedback Widget JavaScript
 * Wires the server-rendered floating button + slide-out feedback form: panel toggle,
 * drag-to-reposition, debounced duplicate detection + upvote, optional screenshot capture
 * (area + full page, with a lightweight annotation editor), optional attachments, and
 * browser console-context capture.
 *
 * Razor model: this script POSTs/GETs against a server-side proxy base URL
 * (data-proxy-url, default /api/wildwood-feedback) which the consuming app maps onto the
 * WildwoodFeedbackService. The Bearer token stays server-side; the browser never sees it.
 * The widget configuration is rendered server-side into data-* attributes, so no config
 * fetch happens here. Ported from WildwoodAdmin/wwwroot/js/feedback-widget.js.
 */
(function () {
    'use strict';

    var POSITION_KEY = 'ww-feedback-widget-pos';
    var MAX_CONSOLE_ENTRIES = 50;

    // ===== Shared console & error capture (installed once per page) =====
    var sharedState = (window.__wwRazorFeedbackState = window.__wwRazorFeedbackState || {
        consoleBuffer: [],
        hooksInstalled: false
    });

    function pushConsoleEntry(level, args) {
        var msg = args.map(function (a) {
            if (typeof a === 'string') return a;
            try { return JSON.stringify(a); } catch (e) { return String(a); }
        }).join(' ');
        if (msg.length > 500) msg = msg.substring(0, 500) + '...';
        sharedState.consoleBuffer.push({ level: level, message: msg, timestamp: new Date().toISOString() });
        if (sharedState.consoleBuffer.length > MAX_CONSOLE_ENTRIES) sharedState.consoleBuffer.shift();
    }

    function ensureConsoleHooks() {
        if (sharedState.hooksInstalled) return;
        sharedState.hooksInstalled = true;

        var origError = console.error;
        var origWarn = console.warn;

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
            var reason = e.reason;
            var msg = reason instanceof Error
                ? reason.message + (reason.stack ? '\n' + reason.stack.split('\n').slice(0, 3).join('\n') : '')
                : String(reason);
            pushConsoleEntry('unhandledrejection', [msg]);
        });
    }

    function collectBrowserContext() {
        var ctx = {
            consoleLog: sharedState.consoleBuffer.slice(),
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
            var nav = performance.getEntriesByType && performance.getEntriesByType('navigation')[0];
            if (nav) {
                ctx.performance = {
                    pageLoadMs: Math.round(nav.loadEventEnd - nav.startTime),
                    domContentLoadedMs: Math.round(nav.domContentLoadedEventEnd - nav.startTime),
                    firstPaintMs: null
                };
                var paint = performance.getEntriesByType('paint');
                if (paint && paint.length) {
                    var fp = paint.find(function (p) { return p.name === 'first-contentful-paint'; }) || paint[0];
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

    // ===== Shared helpers =====
    function escapeHtml(str) {
        var d = document.createElement('div');
        d.textContent = str == null ? '' : str;
        return d.innerHTML;
    }

    function showToast(msg, type) {
        var t = document.createElement('div');
        t.className = 'ww-feedback-toast ' + (type === 'error' ? 'ww-error' : 'ww-success');
        t.textContent = msg;
        document.body.appendChild(t);
        setTimeout(function () {
            t.style.opacity = '0';
            t.style.transition = 'opacity 0.3s';
            setTimeout(function () { t.remove(); }, 300);
        }, 3000);
    }

    function ensureHtml2Canvas() {
        return new Promise(function (resolve, reject) {
            if (typeof window.html2canvas !== 'undefined') { resolve(); return; }
            var s = document.createElement('script');
            s.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js';
            s.onload = function () { resolve(); };
            s.onerror = function () { reject(new Error('Failed to load screenshot library.')); };
            document.head.appendChild(s);
        });
    }

    // ===== Screenshot helpers =====
    function compressScreenshot(canvas, quality, maxSizeKb) {
        var q = (quality || 80) / 100;
        var dataUrl = canvas.toDataURL('image/jpeg', q);
        if (maxSizeKb && maxSizeKb > 0) {
            var maxBytes = maxSizeKb * 1024;
            var cq = q;
            while (dataUrl.length * 0.75 > maxBytes && cq > 0.1) {
                cq -= 0.1;
                dataUrl = canvas.toDataURL('image/jpeg', cq);
            }
            if (dataUrl.length * 0.75 > maxBytes) {
                var sc = Math.sqrt(maxBytes / (dataUrl.length * 0.75));
                var sc2 = document.createElement('canvas');
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
        var angle = Math.atan2(y2 - y1, x2 - x1), hl = 12;
        ctx.beginPath(); ctx.moveTo(x2, y2);
        ctx.lineTo(x2 - hl * Math.cos(angle - Math.PI / 6), y2 - hl * Math.sin(angle - Math.PI / 6));
        ctx.lineTo(x2 - hl * Math.cos(angle + Math.PI / 6), y2 - hl * Math.sin(angle + Math.PI / 6));
        ctx.closePath(); ctx.fill();
    }

    function drawFreehand(ctx, pts, color) {
        if (pts.length < 2) return;
        ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
        ctx.beginPath(); ctx.moveTo(pts[0].x, pts[0].y);
        for (var i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
        ctx.stroke();
    }

    // Lightweight annotation editor; resolves with a compressed data URL, or null if cancelled.
    function openAnnotationEditor(sourceCanvas, quality, maxSizeKb) {
        return new Promise(function (resolve) {
            var editorOverlay = document.createElement('div');
            editorOverlay.className = 'ww-feedback-annotation-overlay';

            var toolbar = document.createElement('div');
            toolbar.className = 'ww-feedback-annotation-toolbar';

            var tools = [
                { icon: '↗', label: 'Arrow', tool: 'arrow' },
                { icon: '◯', label: 'Circle', tool: 'circle' },
                { icon: '✎', label: 'Draw', tool: 'draw' },
                { icon: 'T', label: 'Text', tool: 'text' }
            ];
            var currentTool = 'arrow';
            var annotColor = '#FF0000';
            var annotations = [];

            tools.forEach(function (t) {
                var b = document.createElement('button');
                b.textContent = t.icon; b.title = t.label;
                b.className = 'ann-tool-btn' + (t.tool === currentTool ? ' active' : '');
                b.addEventListener('click', function () {
                    currentTool = t.tool;
                    toolbar.querySelectorAll('.ann-tool-btn').forEach(function (x) { x.classList.remove('active'); });
                    b.classList.add('active');
                });
                toolbar.appendChild(b);
            });

            var colorInput = document.createElement('input');
            colorInput.type = 'color'; colorInput.value = annotColor; colorInput.className = 'ann-color-picker'; colorInput.title = 'Color';
            colorInput.addEventListener('input', function () { annotColor = this.value; });
            toolbar.appendChild(colorInput);

            var undoBtn = document.createElement('button');
            undoBtn.textContent = '↶'; undoBtn.title = 'Undo'; undoBtn.className = 'ann-tool-btn';
            undoBtn.addEventListener('click', function () { if (annotations.length) { annotations.pop(); redraw(); } });
            toolbar.appendChild(undoBtn);

            var spacer = document.createElement('span'); spacer.style.flex = '1'; toolbar.appendChild(spacer);

            var doneBtn = document.createElement('button');
            doneBtn.textContent = 'Done'; doneBtn.className = 'ann-done-btn';
            doneBtn.addEventListener('click', function () {
                var fc = document.createElement('canvas'); fc.width = annCanvas.width; fc.height = annCanvas.height;
                var fctx = fc.getContext('2d');
                fctx.drawImage(sourceCanvas, 0, 0, annCanvas.width, annCanvas.height);
                fctx.drawImage(annCanvas, 0, 0);
                cleanup();
                resolve(compressScreenshot(fc, quality, maxSizeKb));
            });
            toolbar.appendChild(doneBtn);

            var skipBtn = document.createElement('button');
            skipBtn.textContent = 'Skip'; skipBtn.className = 'ann-cancel-btn';
            skipBtn.addEventListener('click', function () {
                cleanup();
                resolve(compressScreenshot(sourceCanvas, quality, maxSizeKb));
            });
            toolbar.appendChild(skipBtn);

            editorOverlay.appendChild(toolbar);

            var canvasWrap = document.createElement('div'); canvasWrap.className = 'ww-feedback-annotation-canvas-wrap';
            var maxW = Math.min(window.innerWidth - 40, 900), maxH = Math.min(window.innerHeight - 100, 600);
            var scale = Math.min(maxW / sourceCanvas.width, maxH / sourceCanvas.height, 1);
            var dispW = Math.round(sourceCanvas.width * scale), dispH = Math.round(sourceCanvas.height * scale);

            var bgCanvas = document.createElement('canvas'); bgCanvas.width = dispW; bgCanvas.height = dispH; bgCanvas.className = 'ww-feedback-annotation-bg';
            bgCanvas.getContext('2d').drawImage(sourceCanvas, 0, 0, dispW, dispH);

            var annCanvas = document.createElement('canvas'); annCanvas.width = dispW; annCanvas.height = dispH; annCanvas.className = 'ww-feedback-annotation-draw';
            var annCtx = annCanvas.getContext('2d');

            canvasWrap.appendChild(bgCanvas); canvasWrap.appendChild(annCanvas);
            editorOverlay.appendChild(canvasWrap); document.body.appendChild(editorOverlay);

            var drawing = false, sx, sy, freehandPts = [];

            annCanvas.addEventListener('mousedown', function (e) {
                var r = annCanvas.getBoundingClientRect(); sx = e.clientX - r.left; sy = e.clientY - r.top; drawing = true;
                if (currentTool === 'draw') freehandPts = [{ x: sx, y: sy }];
                if (currentTool === 'text') {
                    drawing = false;
                    var txt = prompt('Enter text:');
                    if (txt) { annotations.push({ tool: 'text', x: sx, y: sy, text: txt, color: annotColor }); redraw(); }
                }
            });
            annCanvas.addEventListener('mousemove', function (e) {
                if (!drawing) return;
                var r = annCanvas.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
                if (currentTool === 'draw') freehandPts.push({ x: mx, y: my });
                redraw();
                if (currentTool === 'arrow') drawArrow(annCtx, sx, sy, mx, my, annotColor);
                else if (currentTool === 'circle') { annCtx.strokeStyle = annotColor; annCtx.lineWidth = 2; annCtx.beginPath(); annCtx.ellipse((sx + mx) / 2, (sy + my) / 2, Math.abs(mx - sx) / 2, Math.abs(my - sy) / 2, 0, 0, Math.PI * 2); annCtx.stroke(); }
                else if (currentTool === 'draw') drawFreehand(annCtx, freehandPts, annotColor);
            });
            annCanvas.addEventListener('mouseup', function (e) {
                if (!drawing) return; drawing = false;
                var r = annCanvas.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
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
    function captureArea(quality, maxSizeKb) {
        return ensureHtml2Canvas().then(function () {
            return new Promise(function (resolve) {
                var overlay = document.createElement('div'); overlay.className = 'ww-feedback-capture-overlay';
                var selection = document.createElement('div'); selection.className = 'ww-feedback-capture-selection';
                overlay.appendChild(selection); document.body.appendChild(overlay);
                var startX, startY, capturing = false;

                function handleCaptureEnd(mx, my) {
                    capturing = false; overlay.remove();
                    var rect = { x: Math.min(mx, startX), y: Math.min(my, startY), width: Math.abs(mx - startX), height: Math.abs(my - startY) };
                    if (rect.width < 10 || rect.height < 10) { resolve(null); return; }
                    window.html2canvas(document.body, {
                        x: rect.x + window.scrollX, y: rect.y + window.scrollY,
                        width: rect.width, height: rect.height, useCORS: true, logging: false
                    }).then(function (canvas) {
                        return openAnnotationEditor(canvas, quality, maxSizeKb);
                    }).then(function (result) {
                        resolve(result);
                    }).catch(function () {
                        resolve(null);
                    });
                }

                overlay.addEventListener('mousedown', function (e) { startX = e.clientX; startY = e.clientY; capturing = true; selection.style.left = startX + 'px'; selection.style.top = startY + 'px'; selection.style.width = '0px'; selection.style.height = '0px'; selection.style.display = 'block'; });
                overlay.addEventListener('mousemove', function (e) { if (!capturing) return; selection.style.left = Math.min(e.clientX, startX) + 'px'; selection.style.top = Math.min(e.clientY, startY) + 'px'; selection.style.width = Math.abs(e.clientX - startX) + 'px'; selection.style.height = Math.abs(e.clientY - startY) + 'px'; });
                overlay.addEventListener('mouseup', function (e) { if (!capturing) return; handleCaptureEnd(e.clientX, e.clientY); });

                overlay.addEventListener('touchstart', function (e) { if (e.touches.length !== 1) return; var t = e.touches[0]; startX = t.clientX; startY = t.clientY; capturing = true; selection.style.left = startX + 'px'; selection.style.top = startY + 'px'; selection.style.width = '0px'; selection.style.height = '0px'; selection.style.display = 'block'; e.preventDefault(); }, { passive: false });
                overlay.addEventListener('touchmove', function (e) { if (!capturing || e.touches.length !== 1) return; var t = e.touches[0]; selection.style.left = Math.min(t.clientX, startX) + 'px'; selection.style.top = Math.min(t.clientY, startY) + 'px'; selection.style.width = Math.abs(t.clientX - startX) + 'px'; selection.style.height = Math.abs(t.clientY - startY) + 'px'; e.preventDefault(); }, { passive: false });
                overlay.addEventListener('touchend', function (e) { if (!capturing) return; var t = e.changedTouches[0]; handleCaptureEnd(t.clientX, t.clientY); });

                function onEscape(e) {
                    if (e.key === 'Escape') {
                        overlay.remove();
                        document.removeEventListener('keydown', onEscape);
                        resolve(null);
                    }
                }
                document.addEventListener('keydown', onEscape);
            });
        });
    }

    // Full-page capture: prefer the native Screen Capture API, fall back to html2canvas.
    function captureFullPage(quality, maxSizeKb) {
        if (navigator.mediaDevices && navigator.mediaDevices.getDisplayMedia) {
            return navigator.mediaDevices.getDisplayMedia({ video: { displaySurface: 'browser' }, preferCurrentTab: true })
                .then(function (stream) {
                    var track = stream.getVideoTracks()[0];
                    return new Promise(function (resolve, reject) {
                        // Any failure must reject (not hang): the panel is hidden during capture, so a
                        // never-settling promise would leave the widget stuck. Rejection falls back below.
                        var fail = function (err) { track.stop(); reject(err || new Error('capture failed')); };
                        setTimeout(function () {
                            var video = document.createElement('video');
                            video.srcObject = stream;
                            video.onerror = function () { fail(new Error('video error')); };
                            video.onloadedmetadata = function () {
                                video.play().then(function () {
                                    var c = document.createElement('canvas');
                                    c.width = video.videoWidth;
                                    c.height = video.videoHeight;
                                    c.getContext('2d').drawImage(video, 0, 0);
                                    track.stop();
                                    resolve(c);
                                }).catch(fail);
                            };
                        }, 200);
                    });
                })
                .then(function (canvas) {
                    return openAnnotationEditor(canvas, quality, maxSizeKb);
                })
                .catch(function () {
                    return captureFullPageFallback(quality, maxSizeKb);
                });
        }
        return captureFullPageFallback(quality, maxSizeKb);
    }

    function captureFullPageFallback(quality, maxSizeKb) {
        return ensureHtml2Canvas().then(function () {
            return window.html2canvas(document.body, {
                useCORS: true, logging: false, scale: 1,
                width: window.innerWidth, height: window.innerHeight,
                scrollX: -window.scrollX, scrollY: -window.scrollY,
                windowWidth: window.innerWidth, windowHeight: window.innerHeight
            }).then(function (canvas) {
                return openAnnotationEditor(canvas, quality, maxSizeKb);
            });
        }).catch(function () {
            return null;
        });
    }

    // ===== Per-instance widget controller =====
    function initFeedback(root) {
        var cid = root.dataset.componentId;
        var proxyUrl = (root.dataset.proxyUrl || '/api/wildwood-feedback').replace(/\/+$/, '');
        var appId = root.dataset.appId || '';
        var cfg = {
            requireScreenshot: root.dataset.requireScreenshot === 'true',
            screenshotQuality: parseInt(root.dataset.screenshotQuality || '80', 10),
            screenshotMaxKb: parseInt(root.dataset.screenshotMaxKb || '500', 10),
            enableDuplicateDetection: root.dataset.enableDuplicateDetection === 'true',
            allowAttachments: root.dataset.allowAttachments === 'true',
            maxAttachmentKb: parseInt(root.dataset.maxAttachmentKb || '2048', 10),
            allowedAttachmentTypes: root.dataset.allowedAttachmentTypes || ''
        };

        var btn = root.querySelector('.ww-feedback-btn');
        var panel = root.querySelector('.ww-feedback-panel');
        if (!btn || !panel) return;

        var closeBtn = panel.querySelector('.ww-feedback-close');
        var typeEl = panel.querySelector('.ww-feedback-type');
        var titleEl = panel.querySelector('.ww-feedback-title');
        var descEl = panel.querySelector('.ww-feedback-desc');
        var emailEl = panel.querySelector('.ww-feedback-email');
        var nameEl = panel.querySelector('.ww-feedback-name');
        var dupWarning = panel.querySelector('.ww-feedback-duplicate-warning');
        var screenshotPreview = panel.querySelector('.ww-feedback-screenshot-preview');
        var captureAreaBtn = panel.querySelector('.ww-feedback-capture-area');
        var captureFullBtn = panel.querySelector('.ww-feedback-capture-full');
        var attachmentDrop = panel.querySelector('.ww-feedback-attachment-drop');
        var attachmentInput = panel.querySelector('.ww-feedback-attachment-input');
        var attachmentBrowse = panel.querySelector('.ww-feedback-attachment-browse');
        var attachmentList = panel.querySelector('.ww-feedback-attachment-list');
        var formError = panel.querySelector('.ww-feedback-form-error');
        var submitBtn = panel.querySelector('.ww-feedback-submit-btn');

        var panelOpen = false;
        var screenshotData = null;
        var attachments = []; // [{name, contentType, size, data}]

        // ----- panel toggle -----
        function openPanel() {
            panelOpen = true;
            positionPanelNearButton();
            panel.style.display = '';
            setTimeout(function () { var f = panel.querySelector('select, input, textarea'); if (f) f.focus(); }, 80);
        }
        function closePanel() {
            panelOpen = false;
            panel.style.display = 'none';
            if (btn) btn.focus();
        }
        function togglePanel() { if (panelOpen) closePanel(); else openPanel(); }

        function positionPanelNearButton() {
            var btnRect = btn.getBoundingClientRect();
            var pw = 370, ph = 520, vw = window.innerWidth, vh = window.innerHeight;
            if (btn.style.left && btn.style.left !== 'auto') {
                var pl = btnRect.left + btnRect.width + 10;
                if (pl + pw > vw) pl = btnRect.left - pw - 10;
                if (pl < 8) pl = 8;
                var pt = btnRect.top - ph + btnRect.height;
                if (pt < 8) pt = 8;
                if (pt + ph > vh - 8) pt = vh - ph - 8;
                panel.style.left = pl + 'px'; panel.style.top = pt + 'px'; panel.style.right = 'auto'; panel.style.bottom = 'auto';
            } else {
                panel.style.left = ''; panel.style.top = ''; panel.style.right = ''; panel.style.bottom = '';
            }
        }

        // ----- drag to reposition the floating button -----
        var isDragging = false, hasMoved = false, dragOffset = { x: 0, y: 0 };

        function positionBtnAt(left, top) {
            var vw = window.innerWidth, vh = window.innerHeight, bw = btn.offsetWidth || 52, bh = btn.offsetHeight || 52;
            btn.style.left = Math.max(0, Math.min(left, vw - bw)) + 'px';
            btn.style.top = Math.max(0, Math.min(top, vh - bh)) + 'px';
            btn.style.right = 'auto'; btn.style.bottom = 'auto';
        }
        function savePosition() {
            try {
                var vw = window.innerWidth;
                var rect = btn.getBoundingClientRect();
                localStorage.setItem(POSITION_KEY, JSON.stringify({ right: vw - rect.right, top: rect.top }));
            } catch (e) { /* storage unavailable */ }
        }
        function restorePosition() {
            try {
                var raw = localStorage.getItem(POSITION_KEY);
                if (!raw) return;
                var s = JSON.parse(raw);
                if (s && typeof s.right === 'number' && typeof s.top === 'number') {
                    var bw = btn.offsetWidth || 52;
                    positionBtnAt(window.innerWidth - s.right - bw, s.top);
                }
            } catch (e) { /* ignore */ }
        }

        btn.addEventListener('mousedown', function (e) { isDragging = true; hasMoved = false; var r = btn.getBoundingClientRect(); dragOffset.x = e.clientX - r.left; dragOffset.y = e.clientY - r.top; e.preventDefault(); });
        document.addEventListener('mousemove', function (e) { if (!isDragging) return; hasMoved = true; positionBtnAt(e.clientX - dragOffset.x, e.clientY - dragOffset.y); savePosition(); });
        document.addEventListener('mouseup', function () { if (isDragging && !hasMoved) togglePanel(); isDragging = false; });

        btn.addEventListener('touchstart', function (e) { if (e.touches.length !== 1) return; var t = e.touches[0]; isDragging = true; hasMoved = false; var r = btn.getBoundingClientRect(); dragOffset.x = t.clientX - r.left; dragOffset.y = t.clientY - r.top; e.preventDefault(); }, { passive: false });
        document.addEventListener('touchmove', function (e) { if (!isDragging || e.touches.length !== 1) return; var t = e.touches[0]; hasMoved = true; positionBtnAt(t.clientX - dragOffset.x, t.clientY - dragOffset.y); savePosition(); e.preventDefault(); }, { passive: false });
        document.addEventListener('touchend', function () { if (isDragging && !hasMoved) togglePanel(); isDragging = false; });

        btn.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); togglePanel(); }
        });

        window.addEventListener('resize', function () {
            if (!btn.style.left || btn.style.left === 'auto') return;
            restorePosition();
        });

        if (closeBtn) closeBtn.addEventListener('click', closePanel);
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && panelOpen) closePanel(); });

        // Focus trap inside the panel.
        panel.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;
            var focusable = panel.querySelectorAll('input, select, textarea, button, a[href]');
            if (focusable.length === 0) return;
            var first = focusable[0], last = focusable[focusable.length - 1];
            if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
            else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
        });

        // ----- duplicate detection -----
        if (cfg.enableDuplicateDetection && titleEl) {
            var dupTimer = null;
            titleEl.addEventListener('input', function () {
                titleEl.classList.remove('ww-feedback-invalid');
                clearTimeout(dupTimer);
                dupTimer = setTimeout(function () { checkDuplicate(titleEl.value); }, 600);
            });
        }

        function checkDuplicate(title) {
            if (!dupWarning) return;
            if (!title || title.trim().length < 5) { dupWarning.style.display = 'none'; return; }
            var url = proxyUrl + '/duplicate-check?title=' + encodeURIComponent(title.trim()) + (appId ? '&appId=' + encodeURIComponent(appId) : '');
            fetch(url)
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (data && data.hasPotentialDuplicate) {
                        var voteInfo = data.duplicateVoteCount > 0 ? ' (' + data.duplicateVoteCount + ' votes)' : '';
                        dupWarning.innerHTML = '&#9888; Similar feedback exists: <strong>' + escapeHtml(data.duplicateTitle) + '</strong>' + voteInfo +
                            '<br><button type="button" class="ww-feedback-vote-btn" data-id="' + escapeHtml(data.duplicateId) + '">&#128077; Me too! Upvote instead</button>';
                        dupWarning.style.display = 'block';
                        var voteBtn = dupWarning.querySelector('.ww-feedback-vote-btn');
                        if (voteBtn) voteBtn.addEventListener('click', function () { voteFeedback(this.dataset.id, this); });
                    } else {
                        dupWarning.style.display = 'none';
                    }
                })
                .catch(function () { dupWarning.style.display = 'none'; });
        }

        function voteFeedback(feedbackId, btnEl) {
            if (!feedbackId) return;
            if (btnEl) btnEl.disabled = true;
            fetch(proxyUrl + '/' + encodeURIComponent(feedbackId) + '/vote', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            })
                .then(function (r) {
                    if (r.ok) {
                        return r.json().then(function (d) {
                            showToast('Vote recorded! (' + (d && d.voteCount != null ? d.voteCount : 0) + ' total)', 'success');
                            closePanel();
                        });
                    }
                    showToast('Could not record your vote.', 'error');
                    if (btnEl) btnEl.disabled = false;
                })
                .catch(function () {
                    showToast('Network error while voting.', 'error');
                    if (btnEl) btnEl.disabled = false;
                });
        }

        // ----- screenshot -----
        function showScreenshotPreview() {
            if (!screenshotPreview || !screenshotData) return;
            screenshotPreview.innerHTML = '<img alt="Screenshot"><button type="button" class="ww-feedback-remove-screenshot" title="Remove">&times;</button>';
            screenshotPreview.querySelector('img').src = screenshotData;
            screenshotPreview.style.display = 'inline-block';
            screenshotPreview.querySelector('.ww-feedback-remove-screenshot').addEventListener('click', function () {
                screenshotData = null;
                screenshotPreview.style.display = 'none';
                screenshotPreview.innerHTML = '';
            });
        }

        function runCapture(promiseFactory) {
            // Hide the panel and button so neither appears in the capture.
            var prevPanelOpen = panelOpen;
            panel.style.display = 'none';
            btn.style.visibility = 'hidden';
            if (captureAreaBtn) captureAreaBtn.disabled = true;
            if (captureFullBtn) captureFullBtn.disabled = true;

            promiseFactory()
                .then(function (data) {
                    if (data) { screenshotData = data; showScreenshotPreview(); }
                })
                .catch(function () {
                    showToast('Failed to capture screenshot.', 'error');
                })
                .finally(function () {
                    btn.style.visibility = '';
                    if (captureAreaBtn) captureAreaBtn.disabled = false;
                    if (captureFullBtn) captureFullBtn.disabled = false;
                    if (prevPanelOpen) openPanel();
                });
        }

        if (captureAreaBtn) {
            captureAreaBtn.addEventListener('click', function () {
                runCapture(function () { return captureArea(cfg.screenshotQuality, cfg.screenshotMaxKb); });
            });
        }
        if (captureFullBtn) {
            captureFullBtn.addEventListener('click', function () {
                runCapture(function () { return captureFullPage(cfg.screenshotQuality, cfg.screenshotMaxKb); });
            });
        }

        // ----- attachments -----
        function renderAttachmentList() {
            if (!attachmentList) return;
            attachmentList.innerHTML = '';
            attachments.forEach(function (att, idx) {
                var item = document.createElement('div'); item.className = 'ww-feedback-attachment-item';
                var sizeStr = att.size < 1024 ? att.size + 'B' : Math.round(att.size / 1024) + 'KB';
                item.innerHTML = '<span class="ww-feedback-att-name">' + escapeHtml(att.name) + ' <small>(' + sizeStr + ')</small></span>' +
                    '<button type="button" class="ww-feedback-att-remove" data-idx="' + idx + '" title="Remove">&times;</button>';
                attachmentList.appendChild(item);
            });
            attachmentList.querySelectorAll('.ww-feedback-att-remove').forEach(function (b) {
                b.addEventListener('click', function () { attachments.splice(parseInt(this.dataset.idx, 10), 1); renderAttachmentList(); });
            });
        }

        function handleFiles(files) {
            var maxSize = (cfg.maxAttachmentKb || 2048) * 1024;
            var allowedTypes = (cfg.allowedAttachmentTypes || '').split(',').map(function (t) { return t.trim().toLowerCase(); });
            for (var i = 0; i < files.length; i++) {
                var file = files[i];
                var ext = '.' + file.name.split('.').pop().toLowerCase();
                if (allowedTypes.length > 0 && allowedTypes[0] !== '' && allowedTypes.indexOf(ext) === -1) {
                    showToast('File type ' + ext + ' is not allowed.', 'error'); continue;
                }
                if (file.size > maxSize) {
                    showToast(file.name + ' exceeds the ' + (cfg.maxAttachmentKb || 2048) + 'KB limit.', 'error'); continue;
                }
                (function (f) {
                    var reader = new FileReader();
                    reader.onload = function (e) { attachments.push({ name: f.name, contentType: f.type, size: f.size, data: e.target.result }); renderAttachmentList(); };
                    reader.readAsDataURL(f);
                })(file);
            }
        }

        if (cfg.allowAttachments && attachmentDrop && attachmentInput) {
            if (attachmentBrowse) attachmentBrowse.addEventListener('click', function (e) { e.preventDefault(); attachmentInput.click(); });
            attachmentInput.addEventListener('change', function () { handleFiles(this.files); this.value = ''; });
            attachmentDrop.addEventListener('dragover', function (e) { e.preventDefault(); attachmentDrop.classList.add('ww-dragover'); });
            attachmentDrop.addEventListener('dragleave', function () { attachmentDrop.classList.remove('ww-dragover'); });
            attachmentDrop.addEventListener('drop', function (e) { e.preventDefault(); attachmentDrop.classList.remove('ww-dragover'); handleFiles(e.dataTransfer.files); });
        }

        // ----- submit -----
        function showFormError(msg) {
            if (!formError) return;
            formError.textContent = msg;
            formError.style.display = msg ? 'block' : 'none';
        }

        function resetForm() {
            if (titleEl) { titleEl.value = ''; titleEl.classList.remove('ww-feedback-invalid'); }
            if (descEl) { descEl.value = ''; descEl.classList.remove('ww-feedback-invalid'); }
            if (typeEl) typeEl.selectedIndex = 0;
            if (emailEl) emailEl.value = '';
            if (nameEl) nameEl.value = '';
            screenshotData = null;
            attachments = [];
            if (screenshotPreview) { screenshotPreview.style.display = 'none'; screenshotPreview.innerHTML = ''; }
            if (dupWarning) dupWarning.style.display = 'none';
            if (attachmentList) attachmentList.innerHTML = '';
            showFormError('');
        }

        if (submitBtn) {
            submitBtn.addEventListener('click', function () {
                showFormError('');
                if (titleEl) titleEl.classList.remove('ww-feedback-invalid');
                if (descEl) descEl.classList.remove('ww-feedback-invalid');

                if (titleEl && !titleEl.value.trim()) { titleEl.classList.add('ww-feedback-invalid'); titleEl.focus(); return; }
                if (descEl && !descEl.value.trim()) { descEl.classList.add('ww-feedback-invalid'); descEl.focus(); return; }
                if (cfg.requireScreenshot && !screenshotData) { showFormError('A screenshot is required.'); return; }

                var body = {
                    appId: appId || null,
                    title: titleEl ? titleEl.value.trim() : '',
                    description: descEl ? descEl.value.trim() : '',
                    feedbackType: typeEl ? typeEl.value : '',
                    pageUrl: window.location.href,
                    screenshotData: screenshotData,
                    attachments: attachments.length > 0 ? JSON.stringify(attachments) : null,
                    browserContext: collectBrowserContext(),
                    submitterEmail: emailEl && emailEl.value.trim() ? emailEl.value.trim() : null,
                    submitterName: nameEl && nameEl.value.trim() ? nameEl.value.trim() : null
                };

                submitBtn.disabled = true;
                submitBtn.textContent = 'Submitting...';

                fetch(proxyUrl + '/submit', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                })
                    .then(function (r) {
                        if (r.ok) {
                            showToast('Thank you! Your feedback has been submitted.', 'success');
                            resetForm();
                            closePanel();
                            root.dispatchEvent(new CustomEvent('ww-feedback-submitted', { bubbles: true }));
                        } else if (r.status === 429) {
                            showToast('Too many submissions. Please try again later.', 'error');
                        } else {
                            return r.text().then(function (text) {
                                var msg = 'Failed to submit feedback (status ' + r.status + ').';
                                try { var d = JSON.parse(text); msg = (d && (d.error || d.title || d.errorMessage)) || msg; } catch (e) { /* not JSON */ }
                                showFormError(msg);
                            });
                        }
                    })
                    .catch(function (err) {
                        console.error('Feedback submit error:', err);
                        showFormError('Could not reach the server. Please check your connection and try again.');
                    })
                    .finally(function () {
                        submitBtn.disabled = false;
                        submitBtn.textContent = 'Submit Feedback';
                    });
            });
        }

        // Initialize button position from any saved drag position.
        restorePosition();
    }

    // ===== Bootstrap all instances on the page =====
    function initAll() {
        ensureConsoleHooks();
        var roots = document.querySelectorAll('.ww-feedback');
        for (var i = 0; i < roots.length; i++) {
            if (roots[i].dataset.wwFeedbackInit === 'true') continue;
            roots[i].dataset.wwFeedbackInit = 'true';
            initFeedback(roots[i]);
        }
    }

    // Capture console early even before DOM is ready.
    ensureConsoleHooks();

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }

    // ===== Public API =====
    window.wwFeedback = {
        /** Programmatically open the first feedback panel on the page. */
        open: function () {
            var root = document.querySelector('.ww-feedback');
            if (!root) return;
            var btn = root.querySelector('.ww-feedback-btn');
            if (btn) btn.click();
        }
    };
})();
