// Duvar görüntüleyici (spec 1.6.3) - tek modül: tam sayfa, modal (iframe ?modal=1), gömülü (embed)
// ve karşılaştırma sayfası bunu kullanır. Sunucu render'ı (WallPreviewRenderer.cs) ile AYNI homografi,
// AYNI yerleşim kuralları (WallLayout) ve AYNI katman sırası: taban → poster (perspektif) → gölge
// (multiply, yalnızca poster) → ön plan maskesi. WebGL varsa shader ile, yoksa Canvas 2D ile çizer.
(function () {
    'use strict';

    // =====================================================================
    // Matematik - Domain.WallCovering.Homography / WallLayout ile birebir
    // =====================================================================
    function homographyFromUnitSquare(q) {
        var x0 = q[0], y0 = q[1], x1 = q[2], y1 = q[3], x2 = q[4], y2 = q[5], x3 = q[6], y3 = q[7];
        var dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
        var sx = x0 - x1 + x2 - x3, sy = y0 - y1 + y2 - y3;
        var g = 0, h = 0;
        if (Math.abs(sx) > 1e-12 || Math.abs(sy) > 1e-12) {
            var den = dx1 * dy2 - dx2 * dy1;
            g = (sx * dy2 - dx2 * sy) / den;
            h = (dx1 * sy - sx * dy1) / den;
        }
        return [x1 - x0 + g * x1, x3 - x0 + h * x3, x0, y1 - y0 + g * y1, y3 - y0 + h * y3, y0, g, h, 1];
    }
    function mapPoint(m, x, y) {
        var w = m[6] * x + m[7] * y + m[8];
        return [(m[0] * x + m[1] * y + m[2]) / w, (m[3] * x + m[4] * y + m[5]) / w];
    }
    function invert(m) {
        var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        return [
            (m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
            (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
            (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det
        ];
    }
    function subQuad(q, u0, v0, u1, v1) {
        var h = homographyFromUnitSquare(q);
        return [].concat(mapPoint(h, u0, v0), mapPoint(h, u1, v0), mapPoint(h, u1, v1), mapPoint(h, u0, v1));
    }
    function place(wallW, wallH, w, h, align) {
        var clipH = w > wallW, clipV = h > wallH;
        var uw = Math.min(w, wallW) / wallW, vh = Math.min(h, wallH) / wallH;
        var u0 = align === 'left' ? 0 : align === 'right' ? 1 - uw : (1 - uw) / 2;
        var v0 = (1 - vh) / 2;
        return { u0: u0, v0: v0, u1: u0 + uw, v1: v0 + vh, clipH: clipH, clipV: clipV };
    }
    function quadWidth(q) {
        return (Math.hypot(q[2] - q[0], q[3] - q[1]) + Math.hypot(q[4] - q[6], q[5] - q[7])) / 2;
    }
    function defaultCrop(imgW, imgH, targetRatio) {
        var ir = imgW / imgH;
        return targetRatio > ir ? [0, (1 - ir / targetRatio) / 2, 1, ir / targetRatio] : [(1 - targetRatio / ir) / 2, 0, targetRatio / ir, 1];
    }

    // =====================================================================
    // Poster dokusu - WallPreviewRenderer.BuildPosterTexture ile aynı kurallar
    // =====================================================================
    var supportsCtxFilter = (function () { try { return typeof document.createElement('canvas').getContext('2d').filter === 'string'; } catch (e) { return false; } })();
    var noiseCanvas = null;

    function materialNoise(strength, lines) {
        noiseCanvas = noiseCanvas || {};
        var key = strength + (lines ? 'l' : 'n');
        if (noiseCanvas[key]) return noiseCanvas[key];
        var c = document.createElement('canvas');
        c.width = c.height = 128;
        var ctx = c.getContext('2d');
        var data = ctx.createImageData(128, 128);
        var seed = 42;
        function rnd() { seed = (seed * 16807) % 2147483647; return seed / 2147483647; }
        for (var i = 0; i < data.data.length; i += 4) {
            var y = Math.floor(i / 4 / 128);
            var v = 128 + (rnd() - 0.5) * strength * 4 - (lines && y % 3 === 0 ? strength * 2 : 0);
            data.data[i] = data.data[i + 1] = data.data[i + 2] = v;
            data.data[i + 3] = 255;
        }
        ctx.putImageData(data, 0, 0);
        return (noiseCanvas[key] = c);
    }

    /**
     * @param opts {wCm,hCm,fit,crop,mirror,filter,productType,repeatW,repeatH,repeatType,panelLines,panelWidthCm,bleedCm,materialCode,wallW,wallH,align,maxWidthPx}
     */
    function buildPosterCanvas(img, opts) {
        var visW = Math.min(opts.wCm, opts.wallW), visH = Math.min(opts.hCm, opts.wallH);
        var offX = opts.wCm > opts.wallW ? (opts.align === 'left' ? 0 : opts.align === 'right' ? opts.wCm - opts.wallW : (opts.wCm - opts.wallW) / 2) : 0;
        var offY = opts.hCm > opts.wallH ? (opts.hCm - opts.wallH) / 2 : 0;
        var texW = Math.max(32, Math.min(opts.maxWidthPx, 2048));
        var pxPerCm = texW / visW;
        var texH = Math.max(1, Math.round(visH * pxPerCm));
        if (texH > 2048) { pxPerCm = 2048 / visH; texH = 2048; texW = Math.max(1, Math.round(visW * pxPerCm)); }

        var canvas = document.createElement('canvas');
        canvas.width = texW;
        canvas.height = texH;
        var ctx = canvas.getContext('2d');
        ctx.fillStyle = '#fff';
        ctx.fillRect(0, 0, texW, texH);
        var fullW = opts.wCm * pxPerCm, fullH = opts.hCm * pxPerCm;

        ctx.save();
        ctx.translate(-offX * pxPerCm, -offY * pxPerCm);
        if (opts.mirror) { ctx.translate(fullW, 0); ctx.scale(-1, 1); }
        if (supportsCtxFilter) ctx.filter = opts.filter === 'grayscale' ? 'grayscale(1)' : opts.filter === 'sepia' ? 'sepia(1)' : 'none';

        if (opts.productType === 'Pattern' && opts.repeatW > 0 && opts.repeatH > 0) {
            var tw = opts.repeatW * pxPerCm, th = opts.repeatH * pxPerCm;
            var cols = Math.ceil(opts.wCm / opts.repeatW);
            for (var col = 0; col < cols; col++) {
                var o = opts.repeatType === 'HalfDrop' && col % 2 === 1 ? -th / 2 : 0;
                for (var y = o; y < fullH; y += th) ctx.drawImage(img, col * tw, y, tw, th);
            }
        } else if (opts.fit === 'stretch') {
            ctx.drawImage(img, 0, 0, fullW, fullH);
        } else {
            var c = opts.crop || defaultCrop(img.naturalWidth, img.naturalHeight, opts.wCm / opts.hCm);
            ctx.drawImage(img, c[0] * img.naturalWidth, c[1] * img.naturalHeight, c[2] * img.naturalWidth, c[3] * img.naturalHeight, 0, 0, fullW, fullH);
        }
        ctx.restore();

        if (!supportsCtxFilter && opts.filter !== 'none') manualFilter(ctx, texW, texH, opts.filter);

        var noise = { textured: [10, false], textile: [8, false], 'premium-textile': [8, false], 'canvas-adhesive': [8, false], straw: [14, true], metallic: [6, false] }[opts.materialCode];
        if (noise) {
            ctx.save();
            ctx.globalCompositeOperation = 'overlay';
            ctx.globalAlpha = 0.35;
            ctx.fillStyle = ctx.createPattern(materialNoise(noise[0], noise[1]), 'repeat');
            ctx.fillRect(0, 0, texW, texH);
            ctx.restore();
        }

        if (opts.panelLines && opts.panelWidthCm > 0) {
            ctx.save();
            ctx.translate(-offX * pxPerCm, 0);
            var lw = Math.max(1, texW / 700);
            ctx.lineWidth = lw;
            ctx.setLineDash([4 * lw, 3 * lw]);
            for (var xCm = opts.panelWidthCm - opts.bleedCm / 2; xCm < opts.wCm; xCm += opts.panelWidthCm) {
                var px = xCm * pxPerCm;
                ctx.strokeStyle = 'rgba(255,255,255,.9)'; ctx.beginPath(); ctx.moveTo(px, 0); ctx.lineTo(px, texH); ctx.stroke();
                ctx.strokeStyle = 'rgba(0,0,0,.55)'; ctx.beginPath(); ctx.moveTo(px + lw, 0); ctx.lineTo(px + lw, texH); ctx.stroke();
            }
            ctx.restore();
        }
        return canvas;
    }

    function manualFilter(ctx, w, h, filter) {
        var frame = ctx.getImageData(0, 0, w, h), d = frame.data;
        for (var i = 0; i < d.length; i += 4) {
            var r = d[i], g = d[i + 1], b = d[i + 2];
            if (filter === 'grayscale') { d[i] = d[i + 1] = d[i + 2] = 0.2126 * r + 0.7152 * g + 0.0722 * b; }
            else {
                d[i] = Math.min(255, 0.393 * r + 0.769 * g + 0.189 * b);
                d[i + 1] = Math.min(255, 0.349 * r + 0.686 * g + 0.168 * b);
                d[i + 2] = Math.min(255, 0.272 * r + 0.534 * g + 0.131 * b);
            }
        }
        ctx.putImageData(frame, 0, 0);
    }

    // =====================================================================
    // Renderer: WebGL (tercih) / Canvas 2D (yedek)
    // =====================================================================
    var VS = 'attribute vec2 a;void main(){gl_Position=vec4(a,0.,1.);}';
    function fragmentShader(derivatives) {
        return (derivatives ? '#extension GL_OES_standard_derivatives : enable\n' : '') +
            'precision highp float;' +
            'uniform sampler2D uBase,uPoster,uShadow,uMask;uniform mat3 uInv;uniform vec2 uRes;uniform float uHasShadow,uHasMask,uHasPoster;' +
            'void main(){' +
            ' vec2 p=vec2(gl_FragCoord.x,uRes.y-gl_FragCoord.y);vec2 st=p/uRes;' +
            ' vec4 color=texture2D(uBase,st);' +
            ' if(uHasPoster>.5){vec3 q=uInv*vec3(p,1.);vec2 uv=q.xy/q.z;' +
            (derivatives
                ? '  vec2 fw=max(fwidth(uv),vec2(1e-6));float e=min(min(uv.x,1.-uv.x)/fw.x,min(uv.y,1.-uv.y)/fw.y);float cov=clamp(e+.5,0.,1.);'
                : '  float cov=(uv.x>=0.&&uv.y>=0.&&uv.x<=1.&&uv.y<=1.)?1.:0.;') +
            '  if(cov>0.){vec3 pc=texture2D(uPoster,clamp(uv,0.,1.)).rgb;float s=uHasShadow>.5?texture2D(uShadow,st).r:1.;color.rgb=mix(color.rgb,pc*s,cov);}' +
            ' }' +
            ' if(uHasMask>.5){vec4 m=texture2D(uMask,st);color.rgb=mix(color.rgb,m.rgb,m.a);}' +
            ' gl_FragColor=vec4(color.rgb,1.);}';
    }

    function createRenderer(canvas) {
        var gl = null;
        try { gl = canvas.getContext('webgl', { premultipliedAlpha: false, antialias: false }) || canvas.getContext('experimental-webgl'); } catch (e) { gl = null; }
        return gl ? webglRenderer(canvas, gl) : canvas2dRenderer(canvas);
    }

    function webglRenderer(canvas, gl) {
        var deriv = !!gl.getExtension('OES_standard_derivatives');
        function compile(type, src) {
            var s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s);
            if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
            return s;
        }
        var prog = gl.createProgram();
        gl.attachShader(prog, compile(gl.VERTEX_SHADER, VS));
        gl.attachShader(prog, compile(gl.FRAGMENT_SHADER, fragmentShader(deriv)));
        gl.linkProgram(prog);
        gl.useProgram(prog);
        var buf = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
        var loc = gl.getAttribLocation(prog, 'a');
        gl.enableVertexAttribArray(loc);
        gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
        var maxTex = gl.getParameter(gl.MAX_TEXTURE_SIZE);

        var textures = {};
        function texture(name, unit, source) {
            if (!textures[name]) textures[name] = gl.createTexture();
            gl.activeTexture(gl.TEXTURE0 + unit);
            gl.bindTexture(gl.TEXTURE_2D, textures[name]);
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
            gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
            gl.uniform1i(gl.getUniformLocation(prog, name), unit);
        }
        var scene = null;
        return {
            kind: 'webgl',
            maxTextureSize: maxTex,
            setScene: function (layers) {
                scene = layers;
                texture('uBase', 0, layers.base);
                if (layers.shadow) texture('uShadow', 2, layers.shadow);
                if (layers.mask) texture('uMask', 3, layers.mask);
            },
            draw: function (poster, quad) {
                if (!scene) return;
                gl.viewport(0, 0, canvas.width, canvas.height);
                gl.uniform2f(gl.getUniformLocation(prog, 'uRes'), canvas.width, canvas.height);
                gl.uniform1f(gl.getUniformLocation(prog, 'uHasShadow'), scene.shadow ? 1 : 0);
                gl.uniform1f(gl.getUniformLocation(prog, 'uHasMask'), scene.mask ? 1 : 0);
                gl.uniform1f(gl.getUniformLocation(prog, 'uHasPoster'), poster ? 1 : 0);
                if (poster) {
                    texture('uPoster', 1, poster);
                    var inv = invert(homographyFromUnitSquare(quad));
                    // GLSL mat3 sütun öncelikli: satır öncelikli matrisin devriği gönderilir.
                    gl.uniformMatrix3fv(gl.getUniformLocation(prog, 'uInv'), false, [inv[0], inv[3], inv[6], inv[1], inv[4], inv[7], inv[2], inv[5], inv[8]]);
                }
                gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
            }
        };
    }

    function canvas2dRenderer(canvas) {
        var ctx = canvas.getContext('2d');
        var scene = null;
        // Perspektif, dörtgeni N×N hücreye bölüp her üçgeni afin dönüşümle çizerek yaklaşıklanır.
        function drawTriangle(img, s0, s1, s2, d0, d1, d2) {
            ctx.save();
            ctx.beginPath(); ctx.moveTo(d0[0], d0[1]); ctx.lineTo(d1[0], d1[1]); ctx.lineTo(d2[0], d2[1]); ctx.closePath(); ctx.clip();
            var den = s0[0] * (s2[1] - s1[1]) - s1[0] * s2[1] + s2[0] * s1[1] + (s1[0] - s2[0]) * s0[1];
            if (Math.abs(den) < 1e-9) { ctx.restore(); return; }
            var a = -(s0[1] * (d2[0] - d1[0]) - s1[1] * d2[0] + s2[1] * d1[0] + (s1[1] - s2[1]) * d0[0]) / den;
            var b = (s1[1] * d2[1] + s0[1] * (d1[1] - d2[1]) - s2[1] * d1[1] + (s2[1] - s1[1]) * d0[1]) / den;
            var c = (s0[0] * (d2[0] - d1[0]) - s1[0] * d2[0] + s2[0] * d1[0] + (s1[0] - s2[0]) * d0[0]) / den;
            var d = -(s1[0] * d2[1] + s0[0] * (d1[1] - d2[1]) - s2[0] * d1[1] + (s2[0] - s1[0]) * d0[1]) / den;
            var e = (s0[0] * (s2[1] * d1[0] - s1[1] * d2[0]) + s0[1] * (s1[0] * d2[0] - s2[0] * d1[0]) + (s2[0] * s1[1] - s1[0] * s2[1]) * d0[0]) / den;
            var f = (s0[0] * (s2[1] * d1[1] - s1[1] * d2[1]) + s0[1] * (s1[0] * d2[1] - s2[0] * d1[1]) + (s2[0] * s1[1] - s1[0] * s2[1]) * d0[1]) / den;
            ctx.transform(a, b, c, d, e, f);
            ctx.drawImage(img, 0, 0);
            ctx.restore();
        }
        return {
            kind: '2d',
            maxTextureSize: 2048,
            setScene: function (layers) { scene = layers; },
            draw: function (poster, quad) {
                if (!scene) return;
                ctx.setTransform(1, 0, 0, 1, 0, 0);
                ctx.drawImage(scene.base, 0, 0, canvas.width, canvas.height);
                if (poster) {
                    var h = homographyFromUnitSquare(quad), n = 16, pw = poster.width, ph = poster.height;
                    for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) {
                        var u0 = i / n, u1 = (i + 1) / n, v0 = j / n, v1 = (j + 1) / n;
                        var a = mapPoint(h, u0, v0), b = mapPoint(h, u1, v0), c = mapPoint(h, u1, v1), d = mapPoint(h, u0, v1);
                        // Hücreler arası ince boşluk olmasın diye hedef hafifçe genişletilir.
                        drawTriangle(poster, [u0 * pw, v0 * ph], [u1 * pw, v0 * ph], [u1 * pw, v1 * ph], a, b, c);
                        drawTriangle(poster, [u0 * pw, v0 * ph], [u1 * pw, v1 * ph], [u0 * pw, v1 * ph], a, c, d);
                    }
                    if (scene.shadow) {
                        ctx.save();
                        ctx.beginPath(); ctx.moveTo(quad[0], quad[1]); ctx.lineTo(quad[2], quad[3]); ctx.lineTo(quad[4], quad[5]); ctx.lineTo(quad[6], quad[7]); ctx.closePath(); ctx.clip();
                        ctx.globalCompositeOperation = 'multiply';
                        ctx.drawImage(scene.shadow, 0, 0, canvas.width, canvas.height);
                        ctx.restore();
                    }
                }
                if (scene.mask) ctx.drawImage(scene.mask, 0, 0, canvas.width, canvas.height);
            }
        };
    }

    // =====================================================================
    // Yardımcılar
    // =====================================================================
    var imageCache = {};
    function loadImage(url) {
        if (!url) return Promise.resolve(null);
        if (imageCache[url]) return imageCache[url];
        imageCache[url] = new Promise(function (resolve, reject) {
            var img = new Image();
            img.decoding = 'async';
            img.onload = function () { resolve(img); };
            img.onerror = function () { delete imageCache[url]; reject(new Error('Görsel yüklenemedi: ' + url)); };
            img.src = url;
        });
        return imageCache[url];
    }
    function loadScene(s) {
        return Promise.all([loadImage(s.baseImageUrl), loadImage(s.shadowMapUrl).catch(function () { return null; }), loadImage(s.foregroundMaskUrl).catch(function () { return null; })])
            .then(function (r) { return { base: r[0], shadow: r[1], mask: r[2] }; });
    }
    function api(method, url, body) {
        var o = { method: method, credentials: 'same-origin', headers: { Accept: 'application/json' } };
        if (body !== undefined) { o.headers['Content-Type'] = 'application/json'; o.body = JSON.stringify(body); }
        return fetch(url, o).then(function (r) {
            if (r.status === 204 || r.status === 202) return null;
            return r.json().catch(function () { return null; }).then(function (d) {
                if (!r.ok) {
                    var msg = (d && (d.detail || d.title)) || 'İşlem yapılamadı.';
                    if (d && d.errors) msg = Object.keys(d.errors).map(function (k) { return [].concat(d.errors[k]).join(' '); }).join(' ');
                    throw new Error(msg);
                }
                return d;
            });
        });
    }
    function track(type, source, product) {
        try {
            var body = JSON.stringify({ type: type, source: source, product: product });
            if (navigator.sendBeacon) navigator.sendBeacon('/api/v1/events/wall-preview', new Blob([body], { type: 'application/json' }));
            else fetch('/api/v1/events/wall-preview', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: body, keepalive: true });
        } catch (e) { /* ölçüm hatası kullanıcıyı etkilemez */ }
    }
    function toast(message, isError) { if (window.DekorrasWall && window.DekorrasWall.toast) window.DekorrasWall.toast(message, isError); }
    function escapeHtml(v) { return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
    var CM_PER = { cm: 1, m: 100, 'in': 2.54, ft: 30.48 };
    function parseLength(t) { t = String(t || '').trim().replace(',', '.'); return /^\d*\.?\d+$/.test(t) ? parseFloat(t) : NaN; }
    function round1(v) { return Math.round(v * 10) / 10; }
    function fromCm(cm, unit) { var d = unit === 'm' || unit === 'ft' ? 100 : 10; return Math.round(cm / CM_PER[unit] * d) / d; }
    function fmt(v) { return String(v).replace('.', ','); }
    var money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' });
    var num2 = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    window.DekorrasWallVisualizer = { homographyFromUnitSquare: homographyFromUnitSquare, mapPoint: mapPoint, invert: invert, place: place, subQuad: subQuad, buildPosterCanvas: buildPosterCanvas, createRenderer: createRenderer };

    // =====================================================================
    // Görüntüleyici sayfası
    // =====================================================================
    var root = document.querySelector('[data-wall-visualizer]');
    if (root) initVisualizer(root);
    var compareRoot = document.querySelector('[data-wall-compare]');
    if (compareRoot) initCompare(compareRoot);

    function initVisualizer(root) {
        var data = JSON.parse(document.getElementById('wallVizData').textContent);
        var materials = {};
        data.materials.forEach(function (m) { materials[m.code] = m; });
        var scenesById = {};
        data.scenes.forEach(function (s) { scenesById[s.id] = s; });

        var state = {
            product: data.product,
            sceneId: data.sceneId,
            unit: data.initial.unit,
            wCm: data.initial.wCm,
            hCm: data.initial.hCm,
            material: data.initial.material,
            fit: data.initial.fit,
            mirror: !!data.initial.mirror,
            filter: data.initial.filter || 'none',
            crop: data.initial.crop,
            align: data.initial.align || 'center',
            panelLines: false
        };
        var framed = data.mode === 'modal' || data.mode === 'embed';
        var parentOrigin = data.mode === 'embed' ? data.parentOrigin : location.origin;

        var canvas = root.querySelector('[data-viz-canvas]');
        var viewport = root.querySelector('[data-viz-viewport]');
        var zoomLayer = root.querySelector('[data-viz-zoom]');
        var loading = root.querySelector('[data-viz-loading]');
        var clippedBox = root.querySelector('[data-viz-clipped]');
        var errorBox = root.querySelector('[data-viz-error]');
        var wInput = root.querySelector('[data-viz-w]');
        var hInput = root.querySelector('[data-viz-h]');
        var renderer;
        try { renderer = createRenderer(canvas); } catch (e) { renderer = canvas2dRenderer(canvas); }
        root.dataset.renderer = renderer.kind;

        var sceneLayers = null, posterImg = null, renderSeq = 0;

        function scene() { return scenesById[state.sceneId]; }

        function sizeCanvas() {
            var s = scene();
            var cssW = viewport.clientWidth || 800;
            var maxH = Math.max(240, (framed ? window.innerHeight - 160 : window.innerHeight * 0.72));
            var cssH = cssW * s.imageHeightPx / s.imageWidthPx;
            if (cssH > maxH) { cssH = maxH; cssW = cssH * s.imageWidthPx / s.imageHeightPx; }
            var dpr = Math.min(window.devicePixelRatio || 1, 2);
            canvas.style.width = Math.round(cssW) + 'px';
            canvas.style.height = Math.round(cssH) + 'px';
            canvas.width = Math.min(Math.round(cssW * dpr), s.imageWidthPx * 1.5);
            canvas.height = Math.round(canvas.width * s.imageHeightPx / s.imageWidthPx);
        }

        function draw() {
            var s = scene();
            if (!sceneLayers || !posterImg || !s) return;
            var valid = validate();
            var sc = canvas.width / s.imageWidthPx;
            var q = s.wallQuad.map(function (v) { return v * sc; });
            var p = place(s.realWallWidthCm, s.realWallHeightCm, state.wCm, state.hCm, state.align);
            var quad = subQuad(q, p.u0, p.v0, p.u1, p.v1);
            var m = materials[state.material] || data.materials[0];
            var poster = valid ? buildPosterCanvas(posterImg, {
                wCm: state.wCm, hCm: state.hCm, fit: state.fit, crop: state.fit === 'crop' ? state.crop : null, mirror: state.mirror, filter: state.filter,
                productType: state.product.productType, repeatW: state.product.repeatWidthCm, repeatH: state.product.repeatHeightCm, repeatType: state.product.repeatType,
                panelLines: state.panelLines, panelWidthCm: m.panelWidthCm, bleedCm: m.bleedCm, materialCode: state.material,
                wallW: s.realWallWidthCm, wallH: s.realWallHeightCm, align: state.align,
                maxWidthPx: Math.min(renderer.maxTextureSize, Math.ceil(quadWidth(quad) * 1.25))
            }) : null;
            renderer.draw(poster, quad);

            clippedBox.hidden = !(p.clipH || p.clipV);
            if (p.clipH || p.clipV) {
                var fitW = Math.min(state.wCm, s.realWallWidthCm), fitH = Math.min(state.hCm, s.realWallHeightCm);
                clippedBox.innerHTML = escapeHtml('Seçtiğiniz ölçü (' + fmt(state.wCm) + ' × ' + fmt(state.hCm) + ' cm) bu odadaki duvardan (' +
                    fmt(s.realWallWidthCm) + ' × ' + fmt(s.realWallHeightCm) + ' cm) büyük; önizleme duvar ölçüsüne kırpıldı. ') +
                    '<button type="button" class="btn btn-link btn-sm p-0 align-baseline" data-viz-fit-wall data-w="' + fitW + '" data-h="' + fitH + '">' +
                    escapeHtml('Ölçüyü bu duvara eşitle (' + fmt(fitW) + ' × ' + fmt(fitH) + ' cm) ve baskı alanını seç') + '</button>';
            }
            updateCropTools();
        }

        function refresh() {
            var seq = ++renderSeq;
            loading.hidden = false;
            return Promise.all([loadScene(scene()), loadImage(state.product.previewUrl)]).then(function (r) {
                if (seq !== renderSeq) return;
                if (sceneLayers !== r[0]) { sceneLayers = r[0]; sizeCanvas(); renderer.setScene(sceneLayers); }
                posterImg = r[1];
                draw();
            }).catch(function (e) { toast(e.message, true); })
              .finally(function () { if (seq === renderSeq) loading.hidden = true; });
        }

        // ---------- Doğrulama / fiyat ----------
        function validate() {
            var m = materials[state.material];
            var msg = '';
            if (!(state.wCm >= data.rules.minSideCm && state.hCm >= data.rules.minSideCm)) msg = 'Her kenar en az ' + data.rules.minSideCm + ' cm olmalıdır.';
            else if (state.wCm > data.rules.maxWidthCm) msg = 'En en fazla ' + data.rules.maxWidthCm + ' cm olabilir.';
            else if (m && state.hCm > m.maxHeightCm) msg = 'Seçilen malzemede boy en fazla ' + m.maxHeightCm + ' cm olabilir.';
            errorBox.textContent = msg;
            root.querySelector('[data-viz-add-to-cart]').disabled = !!msg;
            return !msg;
        }

        function configPayload() {
            var p = { material: state.material, unit: state.unit, w_cm: String(state.wCm), h_cm: String(state.hCm), fit: state.fit, mirror: state.mirror ? '1' : '0', filter: state.filter };
            if (state.fit === 'crop' && state.crop) p.crop = state.crop.map(function (v) { return String(Math.round(v * 10000) / 10000); }).join(',');
            return p;
        }

        var quoteTimer = null, quoteSeq = 0;
        function quote() {
            clearTimeout(quoteTimer);
            if (!validate() || data.external) return; // harici poster bu sistemde fiyatlanmaz (ana site kendi sepetinde fiyatlar)
            quoteTimer = setTimeout(function () {
                var seq = ++quoteSeq;
                api('POST', '/api/v1/pricing/quote', { product: state.product.slug, configuration: configPayload(), quantity: 1 }).then(function (d) {
                    if (seq !== quoteSeq || !d) return;
                    var set = function (k, v) { var el = root.querySelector('[data-viz-sum="' + k + '"]'); if (el) el.textContent = v; };
                    if (!d.gecerli) { errorBox.textContent = Object.values(d.hatalar || {}).join(' '); return; }
                    set('billed', num2.format(d.faturaM2) + ' m²');
                    set('panels', d.panelSayisi + ' × ' + d.panelGenisligi + ' cm');
                    set('subtotal', money.format(d.araToplam));
                    set('tax', money.format(d.kdv));
                    set('total', money.format(d.toplam));
                    set('note', d.kargo === 0 ? 'Kargo ücretsiz.' : (d.ucretsizKargoIcinKalan > 0 ? 'Ücretsiz kargoya ' + money.format(d.ucretsizKargoIcinKalan) + ' kaldı.' : 'Kesim payı dahil fiyat.'));
                }).catch(function (e) { errorBox.textContent = e.message; });
            }, 250);
        }

        // ---------- URL ----------
        function stateQuery() {
            var q = new URLSearchParams();
            q.set('product', state.product.slug);
            q.set('scene', state.sceneId.replace(/-/g, ''));
            var c = configPayload();
            q.set('material', c.material); q.set('unit', c.unit); q.set('w_cm', state.wCm.toFixed(1)); q.set('h_cm', state.hCm.toFixed(1)); q.set('fit', c.fit);
            if (state.mirror) q.set('mirror', '1');
            if (state.filter !== 'none') q.set('filter', state.filter);
            if (c.crop) q.set('crop', c.crop);
            if (state.align !== 'center') q.set('align', state.align);
            return q;
        }
        function productPageUrl() {
            var p = new URLSearchParams();
            p.set('slug', state.product.slug);
            var c = configPayload();
            Object.keys(c).forEach(function (k) { if (c[k] !== undefined && c[k] !== null && c[k] !== '') p.set(k, c[k]); });
            p.set('view', 'customizer');
            return '/Product/Details?' + p.toString();
        }
        function syncUrl() {
            var q = stateQuery();
            if (data.returnUrl) q.set('return', data.returnUrl);
            if (data.mode === 'modal') q.set('modal', '1');
            if (data.mode !== 'embed') history.replaceState(history.state, '', location.pathname + '?' + q.toString());
            var dl = root.querySelector('[data-viz-download]');
            if (dl) {
                var r = new URLSearchParams(stateQuery());
                r.delete('scene');
                r.set('align', state.align);
                r.set('download', '1');
                dl.href = '/api/v1/scenes/' + state.sceneId + '/render?' + r.toString();
            }
            // Ürün sayfası bağlantıları müşterinin SON seçimlerini (ölçü, birim, malzeme, kırpma, ayna, filtre) taşır;
            // ürün sayfasındaki konfigüratör bunlarla açılır, ölçüler yeniden girilmez.
            var productHref = productPageUrl();
            root.querySelectorAll('[data-viz-product-link]').forEach(function (a) { a.href = productHref; });
        }

        function changed(opts) {
            opts = opts || {};
            // Yeni oran: müşterinin seçtiği baskı alanı korunur (aynı merkez, aynı yakınlık), yalnızca oranı güncellenir.
            if (opts.ratio && posterImg && state.crop) state.crop = refitCrop(state.crop);
            draw();
            quote();
            syncUrl();
        }

        // ---------- Kontroller ----------
        function readDims() {
            var w = parseLength(wInput.value), h = parseLength(hInput.value);
            state.wCm = isNaN(w) ? NaN : round1(w * CM_PER[state.unit]);
            state.hCm = isNaN(h) ? NaN : round1(h * CM_PER[state.unit]);
        }
        var dimTimer = null;
        // Bekleyen (debounce'lu) ölçü okumasını hemen uygular: kullanıcı yazıp hemen "sepete ekle"/"devam et"e
        // basarsa eski ölçünün gönderilmesini önler.
        function flushDims() { if (dimTimer) { clearTimeout(dimTimer); dimTimer = null; readDims(); changed({ ratio: true }); } }
        [wInput, hInput].forEach(function (input) {
            input.addEventListener('input', function () { clearTimeout(dimTimer); dimTimer = setTimeout(function () { dimTimer = null; readDims(); changed({ ratio: true }); }, 120); });
        });
        root.querySelectorAll('[data-viz-unit]').forEach(function (r) {
            r.addEventListener('change', function () {
                state.unit = r.value;
                wInput.value = fmt(fromCm(state.wCm, state.unit));
                hInput.value = fmt(fromCm(state.hCm, state.unit));
                syncUrl(); quote();
            });
        });
        root.querySelector('[data-viz-material]').addEventListener('change', function (e) { state.material = e.target.value; changed(); });
        root.querySelectorAll('[data-viz-fit]').forEach(function (r) { r.addEventListener('change', function () { state.fit = r.value; changed(); }); });
        root.querySelectorAll('[data-viz-align]').forEach(function (r) { r.addEventListener('change', function () { state.align = r.value; changed(); }); });
        root.querySelectorAll('[data-viz-filter]').forEach(function (r) { r.addEventListener('change', function () { state.filter = r.value; changed(); }); });
        root.querySelector('[data-viz-mirror]').addEventListener('change', function (e) { state.mirror = e.target.checked; changed(); });
        root.querySelector('[data-viz-panels]').addEventListener('change', function (e) { state.panelLines = e.target.checked; draw(); });
        root.querySelector('[data-viz-form]').addEventListener('submit', function (e) { e.preventDefault(); });

        root.querySelectorAll('[data-viz-scene]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                if (btn.dataset.vizScene === state.sceneId) return;
                state.sceneId = btn.dataset.vizScene;
                root.querySelectorAll('[data-viz-scene]').forEach(function (b) {
                    var on = b === btn;
                    b.classList.toggle('is-active', on);
                    b.setAttribute('aria-checked', on ? 'true' : 'false');
                });
                track('wall_preview_scene_change', 'goruntuleyici', state.product.slug);
                refresh().then(syncUrl);
            });
        });

        // ---------- Poster listesi ----------
        var listEl = root.querySelector('[data-viz-items]');
        var searchInput = root.querySelector('[data-viz-search]');
        var searchBox = root.querySelector('[data-viz-search-box]');
        var tab = data.openListTab ? 'tryon' : 'all';
        var listItems = [], allPage = 0, allTotalPages = 1, allLoading = false;

        function renderList() {
            if (!listEl) return;
            if (listItems.length === 0) {
                listEl.innerHTML = '<li class="text-muted small p-2">' + (tab === 'tryon' ? '"Duvarımda Dene" listeniz boş.' : tab === 'favorites' ? 'Henüz favoriniz yok.' : 'Sonuç yok.') + '</li>';
                return;
            }
            listEl.innerHTML = listItems.map(function (c) {
                var current = c.slug === state.product.slug;
                return '<li><button type="button" class="wall-viz-item' + (current ? ' is-current' : '') + '" data-viz-pick="' + escapeHtml(c.slug) + '"' + (current ? ' aria-current="true"' : '') + '>' +
                    (c.thumbUrl ? '<img src="' + escapeHtml(c.thumbUrl) + '" alt="" loading="lazy">' : '') + '<span>' + escapeHtml(c.title) + '</span></button></li>';
            }).join('');
        }

        function loadTab() {
            if (!listEl) return;
            root.querySelectorAll('[data-viz-tab]').forEach(function (b) { var on = b.dataset.vizTab === tab; b.classList.toggle('active', on); b.setAttribute('aria-selected', on ? 'true' : 'false'); });
            searchBox.hidden = tab !== 'all';
            listItems = [];
            if (tab === 'tryon') {
                var fromState = function () { listItems = ((window.DekorrasWall && window.DekorrasWall.state.tryOn) || []).map(function (i) { return i.card; }); renderList(); };
                if (window.DekorrasWall && window.DekorrasWall.state.loaded) fromState();
                else api('GET', '/api/v1/try-on-list/items').then(function (d) { listItems = ((d && d.items) || []).map(function (i) { return i.card; }); renderList(); });
            } else if (tab === 'favorites') {
                api('GET', '/api/v1/favorites').then(function (d) {
                    var ids = (d && d.productIds) || [];
                    if (!ids.length) { renderList(); return null; }
                    return api('GET', '/api/v1/products?pageSize=60&ids=' + ids.join(',')).then(function (r) { listItems = r.items; renderList(); });
                }).catch(function () { renderList(); });
            } else {
                allPage = 0; allTotalPages = 1;
                loadMoreAll();
            }
        }
        function loadMoreAll() {
            if (allLoading || allPage >= allTotalPages) return;
            allLoading = true;
            var q = searchInput.value.trim();
            var cat = categorySelect ? categorySelect.value : '';
            api('GET', '/api/v1/products?pageSize=24&page=' + (allPage + 1) + (q ? '&q=' + encodeURIComponent(q) : '') + (cat ? '&category=' + encodeURIComponent(cat) : '')).then(function (r) {
                allPage = r.page; allTotalPages = r.totalPages;
                listItems = listItems.concat(r.items);
                renderList();
            }).finally(function () { allLoading = false; });
        }
        root.querySelectorAll('[data-viz-tab]').forEach(function (b) { b.addEventListener('click', function () { tab = b.dataset.vizTab; loadTab(); }); });

        // Kategori seçimi: Duvar Kağıtları / Posterler ve alt kategorileri. Varsayılan: açılan ürünün kök kategorisi.
        var categorySelect = root.querySelector('[data-viz-category]');
        var categoriesReady = !categorySelect ? Promise.resolve() :
            api('GET', '/api/v1/categories?product=' + encodeURIComponent(state.product.slug)).then(function (d) {
                (d.categories || []).forEach(function (c) {
                    var o = document.createElement('option');
                    o.value = c.slug;
                    o.textContent = (c.depth > 0 ? '  '.repeat(c.depth) + '– ' : '') + c.name + ' (' + c.productCount + ')';
                    categorySelect.appendChild(o);
                });
                if (d.productCategorySlug && categorySelect.querySelector('option[value="' + d.productCategorySlug + '"]')) categorySelect.value = d.productCategorySlug;
            }).catch(function () { /* kategori listesi olmadan da çalışır */ });
        if (categorySelect) categorySelect.addEventListener('change', function () { if (tab === 'all') loadTab(); });
        var searchTimer = null;
        if (listEl) searchInput.addEventListener('input', function () { clearTimeout(searchTimer); searchTimer = setTimeout(function () { if (tab === 'all') loadTab(); }, 300); });
        if (listEl) listEl.addEventListener('scroll', function () { if (tab === 'all' && listEl.scrollTop + listEl.clientHeight > listEl.scrollHeight - 200) loadMoreAll(); });
        if (listEl) listEl.addEventListener('click', function (e) { var b = e.target.closest('[data-viz-pick]'); if (b) swapProduct(b.dataset.vizPick); });
        document.addEventListener('wall:lists-changed', function () { if (tab === 'tryon') loadTab(); });

        function swapProduct(slug) {
            if (slug === state.product.slug) return;
            loading.hidden = false;
            Promise.all([api('GET', '/api/v1/products/' + encodeURIComponent(slug)), api('GET', '/api/v1/materials?product=' + encodeURIComponent(slug))]).then(function (r) {
                state.product = r[0];
                materials = {};
                r[1].forEach(function (m) { materials[m.code] = m; });
                if (!materials[state.material]) state.material = r[1][0].code;
                state.crop = null; // yeni görselin oranına göre varsayılan kırpma
                root.querySelector('[data-viz-title]').textContent = state.product.title;
                track('wall_preview_product_swap', 'goruntuleyici', slug);
                renderList();
                return refresh();
            }).then(function () { quote(); syncUrl(); }).catch(function (e) { loading.hidden = true; toast(e.message, true); });
        }

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
            if (e.target.closest('input, select, textarea, [contenteditable]')) return;
            if (!listItems.length) return;
            var i = listItems.findIndex(function (c) { return c.slug === state.product.slug; });
            var next = e.key === 'ArrowRight' ? (i + 1) % listItems.length : (i - 1 + listItems.length) % listItems.length;
            e.preventDefault();
            swapProduct(listItems[next].slug);
        });

        // ---------- Baskı alanı (kırpma) seçimi ----------
        // Kırpma = orijinal görsel üzerinde normalize [x, y, w, h]; oranı daima sipariş ölçüsünün (en/boy) oranıdır.
        // Müşteri 1) "Baskı alanını seç" penceresinde çerçeveyi sürükleyip büyütüp küçülterek 2) doğrudan duvar
        // önizlemesinde ürünü sürükleyerek görselin HANGİ bölümünün basılacağını seçer; seçim sepete/siparişe gider.
        function cropAllowed() { return state.fit === 'crop' && state.product.productType !== 'Pattern' && !!posterImg && state.wCm > 0 && state.hCm > 0; }
        function ratioNow() { return state.wCm / state.hCm; }
        function clamp01(v, max) { return Math.min(Math.max(0, v), Math.max(0, max)); }
        function currentCrop() { return (state.crop || defaultCrop(posterImg.naturalWidth, posterImg.naturalHeight, ratioNow())).slice(); }
        function normalizeCrop(c) {
            // Sunucu 4 ondalık bekler; x+w ve y+h 1'i aşmasın.
            var r = function (v) { return Math.floor(v * 10000) / 10000; };
            var w = r(Math.min(1, c[2])), h = r(Math.min(1, c[3]));
            return [r(clamp01(c[0], 1 - w)), r(clamp01(c[1], 1 - h)), w, h];
        }
        function refitCrop(c) {
            var iw = posterImg.naturalWidth, ih = posterImg.naturalHeight;
            var prevDef = defaultCrop(iw, ih, (c[2] * iw) / (c[3] * ih));
            var s = Math.min(1, c[2] / prevDef[2]);
            var def = defaultCrop(iw, ih, ratioNow());
            var w = def[2] * s, h = def[3] * s;
            var cx = c[0] + c[2] / 2, cy = c[1] + c[3] / 2;
            return normalizeCrop([cx - w / 2, cy - h / 2, w, h]);
        }
        function effectiveDpi(c) {
            var srcW = state.product.imageWidthPx || posterImg.naturalWidth, srcH = state.product.imageHeightPx || posterImg.naturalHeight;
            return Math.min(c[2] * srcW / (state.wCm / 2.54), c[3] * srcH / (state.hCm / 2.54));
        }

        var cropOpenBtn = root.querySelector('[data-viz-crop-open]');
        function updateCropTools() { if (cropOpenBtn) cropOpenBtn.hidden = !cropAllowed(); }

        var cropDialog = null;
        function openCropEditor() {
            if (!cropAllowed()) return;
            flushDims();
            var iw = posterImg.naturalWidth, ih = posterImg.naturalHeight;
            var def = defaultCrop(iw, ih, ratioNow());
            var c = currentCrop();
            if (!cropDialog) {
                cropDialog = document.createElement('dialog');
                cropDialog.className = 'wall-crop-dialog';
                cropDialog.innerHTML =
                    '<form method="dialog" class="wall-crop-inner">' +
                    ' <div class="d-flex justify-content-between align-items-start gap-2 mb-2">' +
                    '  <div><h2 class="h6 mb-1">Baskı alanını seç</h2><p class="small text-muted mb-0">Çerçeveyi sürükleyerek görselin basılacak bölümünü seçin; köşelerden (veya fare tekerleğiyle) büyütüp küçültün. Çerçeve, girdiğiniz ölçünün (<strong data-crop-size></strong>) oranındadır.</p></div>' +
                    '  <button type="button" class="btn-close" data-crop-cancel aria-label="Kapat"></button>' +
                    ' </div>' +
                    ' <div class="wall-crop-stage" data-crop-stage><img data-crop-img alt="" draggable="false" />' +
                    '  <div class="wall-crop-frame" data-crop-frame tabindex="0" aria-label="Baskı alanı - ok tuşlarıyla taşıyın">' +
                    '   <span class="wall-crop-label" data-crop-label></span>' +
                    '   <span class="wall-crop-grid"></span>' +
                    '   <span class="wall-crop-handle" data-crop-handle="nw"></span><span class="wall-crop-handle" data-crop-handle="ne"></span>' +
                    '   <span class="wall-crop-handle" data-crop-handle="se"></span><span class="wall-crop-handle" data-crop-handle="sw"></span>' +
                    '  </div>' +
                    ' </div>' +
                    ' <div class="d-flex flex-wrap align-items-center gap-2 mt-2">' +
                    '  <label class="small d-flex align-items-center gap-2 flex-grow-1">Yakınlık <input type="range" class="form-range" min="100" max="500" step="1" data-crop-zoom></label>' +
                    '  <button type="button" class="btn btn-sm btn-outline-secondary" data-crop-center>Ortala</button>' +
                    '  <button type="button" class="btn btn-sm btn-outline-secondary" data-crop-fit>Tamamını kullan</button>' +
                    ' </div>' +
                    ' <div class="small mt-1" data-crop-quality></div>' +
                    ' <div class="d-flex justify-content-end gap-2 mt-3">' +
                    '  <button type="button" class="btn btn-outline-secondary" data-crop-cancel>Vazgeç</button>' +
                    '  <button type="button" class="btn btn-primary" data-crop-apply>Bu alanı kullan</button>' +
                    ' </div>' +
                    '</form>';
                root.appendChild(cropDialog);
            }
            var d = cropDialog;
            var stage = d.querySelector('[data-crop-stage]'), frame = d.querySelector('[data-crop-frame]'), zoomInput = d.querySelector('[data-crop-zoom]');
            d.querySelector('[data-crop-img]').src = state.product.previewUrl;
            d.querySelector('[data-crop-size]').textContent = fmt(state.wCm) + ' × ' + fmt(state.hCm) + ' cm';

            function render() {
                frame.style.left = (c[0] * 100) + '%';
                frame.style.top = (c[1] * 100) + '%';
                frame.style.width = (c[2] * 100) + '%';
                frame.style.height = (c[3] * 100) + '%';
                d.querySelector('[data-crop-label]').textContent = fmt(state.wCm) + ' × ' + fmt(state.hCm) + ' cm';
                zoomInput.value = String(Math.round(def[2] / c[2] * 100));
                var dpi = Math.round(effectiveDpi(c));
                var q = d.querySelector('[data-crop-quality]');
                var low = dpi < (data.rules.minPrintDpi || 72);
                q.className = 'small mt-1 ' + (low ? 'text-danger' : 'text-muted');
                q.textContent = (low ? '⚠ Baskı kalitesi düşebilir: ' : 'Baskı çözünürlüğü: ') + '~' + dpi + ' DPI' + (low ? ' (önerilen en az ' + (data.rules.minPrintDpi || 72) + '). Daha geniş bir alan seçin.' : '');
            }
            function setSize(w) {
                w = Math.min(def[2], Math.max(def[2] * 0.2, w));
                var h = w * def[3] / def[2];
                var cx = c[0] + c[2] / 2, cy = c[1] + c[3] / 2;
                c = [clamp01(cx - w / 2, 1 - w), clamp01(cy - h / 2, 1 - h), w, h];
                render();
            }

            var drag = null;
            frame.onpointerdown = function (e) {
                e.preventDefault();
                var rect = stage.getBoundingClientRect();
                var handle = e.target.dataset ? e.target.dataset.cropHandle : null;
                drag = { handle: handle, x: e.clientX, y: e.clientY, c: c.slice(), rw: rect.width, rh: rect.height };
                frame.setPointerCapture(e.pointerId);
            };
            frame.onpointermove = function (e) {
                if (!drag) return;
                var dx = (e.clientX - drag.x) / drag.rw, dy = (e.clientY - drag.y) / drag.rh, s = drag.c;
                if (!drag.handle) {
                    c = [clamp01(s[0] + dx, 1 - s[2]), clamp01(s[1] + dy, 1 - s[3]), s[2], s[3]];
                } else {
                    // Karşı köşe sabit; oran korunarak boyutlandırılır.
                    var east = drag.handle.indexOf('e') >= 0, south = drag.handle.indexOf('s') >= 0;
                    var ax = east ? s[0] : s[0] + s[2], ay = south ? s[1] : s[1] + s[3];
                    var k = def[3] / def[2];
                    var wFromX = east ? s[2] + dx : s[2] - dx, wFromY = (south ? s[3] + dy : s[3] - dy) / k;
                    var w = Math.max(wFromX, wFromY);
                    var maxW = Math.min(east ? 1 - ax : ax, (south ? 1 - ay : ay) / k, def[2]);
                    w = Math.min(maxW, Math.max(def[2] * 0.2, w));
                    var h = w * k;
                    c = [east ? ax : ax - w, south ? ay : ay - h, w, h];
                }
                render();
            };
            frame.onpointerup = frame.onpointercancel = function () { drag = null; };
            frame.onkeydown = function (e) {
                var step = e.shiftKey ? 0.05 : 0.01;
                var delta = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[e.key];
                if (!delta) return;
                e.preventDefault();
                c = [clamp01(c[0] + delta[0], 1 - c[2]), clamp01(c[1] + delta[1], 1 - c[3]), c[2], c[3]];
                render();
            };
            stage.onwheel = function (e) { e.preventDefault(); setSize(c[2] * (e.deltaY < 0 ? 0.92 : 1 / 0.92)); };
            zoomInput.oninput = function () { setSize(def[2] * 100 / +zoomInput.value); };
            d.querySelector('[data-crop-center]').onclick = function () { c = [(1 - c[2]) / 2, (1 - c[3]) / 2, c[2], c[3]]; render(); };
            d.querySelector('[data-crop-fit]').onclick = function () { c = def.slice(); render(); };
            d.querySelectorAll('[data-crop-cancel]').forEach(function (b) { b.onclick = function () { d.close(); }; });
            d.querySelector('[data-crop-apply]').onclick = function () {
                state.crop = normalizeCrop(c);
                d.close();
                changed();
                toast('Baskı alanı seçildi; sepete bu alan eklenecek.');
            };
            render();
            d.showModal();
            // Sahne, görselin oranını koruyarak pencereye sığdırılır (dikey görsel yüksekliği aşmasın).
            var maxW = d.querySelector('.wall-crop-inner').clientWidth - 2, maxH = window.innerHeight * 0.62;
            var sw = Math.min(maxW, maxH * iw / ih);
            stage.style.width = Math.round(sw) + 'px';
            stage.style.height = Math.round(sw * ih / iw) + 'px';
        }
        if (cropOpenBtn) cropOpenBtn.addEventListener('click', openCropEditor);
        // Sipariş ölçüsü duvardan büyükse: ölçüyü duvara eşitle, görselin hangi bölümünün basılacağını seçtir.
        clippedBox.addEventListener('click', function (e) {
            var b = e.target.closest('[data-viz-fit-wall]');
            if (!b) return;
            var prevW = state.wCm, prevH = state.hCm;
            var c = posterImg ? currentCrop() : null;
            state.wCm = round1(+b.dataset.w);
            state.hCm = round1(+b.dataset.h);
            wInput.value = fmt(fromCm(state.wCm, state.unit));
            hInput.value = fmt(fromCm(state.hCm, state.unit));
            if (c) {
                // Başlangıç seçimi = önceki (büyük) ölçüde duvarda GÖRÜNEN bölüm (hizaya göre) - önizleme değişmez.
                var fx = state.wCm / prevW, fy = state.hCm / prevH;
                var u0 = state.align === 'left' ? 0 : state.align === 'right' ? 1 - fx : (1 - fx) / 2;
                state.crop = normalizeCrop([c[0] + u0 * c[2], c[1] + (1 - fy) / 2 * c[3], c[2] * fx, c[3] * fy]);
            }
            changed();
            openCropEditor();
        });

        // Duvar önizlemesinde ürünü doğrudan sürükleyerek baskı alanını kaydırma (yakınlaştırma yokken).
        var pan = null, panFrame = 0;
        function posterUvAt(e) {
            var s = scene();
            var rect = canvas.getBoundingClientRect();
            var sc = canvas.width / s.imageWidthPx;
            var q = s.wallQuad.map(function (v) { return v * sc; });
            var p = place(s.realWallWidthCm, s.realWallHeightCm, state.wCm, state.hCm, state.align);
            var quad = subQuad(q, p.u0, p.v0, p.u1, p.v1);
            var inv = invert(homographyFromUnitSquare(quad));
            return mapPoint(inv, (e.clientX - rect.left) * canvas.width / rect.width, (e.clientY - rect.top) * canvas.height / rect.height);
        }
        function insidePoster(uv) { return uv[0] >= 0 && uv[0] <= 1 && uv[1] >= 0 && uv[1] <= 1; }
        canvas.addEventListener('pointermove', function (e) {
            if (pan || zoom.s !== 1) return;
            canvas.style.cursor = cropAllowed() && insidePoster(posterUvAt(e)) ? 'grab' : '';
        });
        canvas.addEventListener('pointerdown', function (e) {
            if (zoom.s !== 1 || !cropAllowed()) return;
            var uv = posterUvAt(e);
            if (!insidePoster(uv)) return;
            e.stopPropagation();
            var s = scene();
            // Görünen alan (cm) → görseldeki kayma: 1 birim uv = görünen genişlik/yükseklik.
            var visW = Math.min(state.wCm, s.realWallWidthCm), visH = Math.min(state.hCm, s.realWallHeightCm);
            pan = { uv: uv, c: currentCrop(), fx: visW / state.wCm, fy: visH / state.hCm };
            canvas.setPointerCapture(e.pointerId);
            canvas.style.cursor = 'grabbing';
        });
        canvas.addEventListener('pointermove', function (e) {
            if (!pan) return;
            var uv = posterUvAt(e), s0 = pan.c;
            // Ürünü sağa sürüklemek görselin daha solundaki kısmını gösterir (kırpma sola kayar).
            var dx = (uv[0] - pan.uv[0]) * pan.fx * s0[2], dy = (uv[1] - pan.uv[1]) * pan.fy * s0[3];
            state.crop = [clamp01(s0[0] - dx, 1 - s0[2]), clamp01(s0[1] - dy, 1 - s0[3]), s0[2], s0[3]];
            if (!panFrame) panFrame = requestAnimationFrame(function () { panFrame = 0; draw(); });
        });
        function endPan() {
            if (!pan) return;
            pan = null;
            canvas.style.cursor = '';
            state.crop = normalizeCrop(state.crop);
            changed();
        }
        canvas.addEventListener('pointerup', endPan);
        canvas.addEventListener('pointercancel', endPan);

        // ---------- Yakınlaştır / kaydır / tam ekran ----------
        var zoom = { s: 1, x: 0, y: 0 };
        function applyZoom() {
            zoom.s = Math.min(4, Math.max(1, zoom.s));
            if (zoom.s === 1) { zoom.x = 0; zoom.y = 0; }
            zoomLayer.style.transform = 'translate(' + zoom.x + 'px,' + zoom.y + 'px) scale(' + zoom.s + ')';
            viewport.classList.toggle('is-zoomed', zoom.s > 1);
        }
        root.querySelector('[data-viz-zoom-in]').addEventListener('click', function () { zoom.s *= 1.25; applyZoom(); });
        root.querySelector('[data-viz-zoom-out]').addEventListener('click', function () { zoom.s /= 1.25; applyZoom(); });
        root.querySelector('[data-viz-zoom-reset]').addEventListener('click', function () { zoom.s = 1; applyZoom(); });
        viewport.addEventListener('wheel', function (e) { if (!e.ctrlKey && zoom.s === 1 && e.deltaY > 0) return; e.preventDefault(); zoom.s *= e.deltaY < 0 ? 1.1 : 1 / 1.1; applyZoom(); }, { passive: false });
        viewport.addEventListener('keydown', function (e) {
            if (e.key === '+' || e.key === '=') { zoom.s *= 1.25; applyZoom(); }
            else if (e.key === '-') { zoom.s /= 1.25; applyZoom(); }
            else if (e.key === '0') { zoom.s = 1; applyZoom(); }
        });
        var drag = null;
        viewport.addEventListener('pointerdown', function (e) { if (zoom.s === 1) return; drag = { x: e.clientX - zoom.x, y: e.clientY - zoom.y }; viewport.setPointerCapture(e.pointerId); });
        viewport.addEventListener('pointermove', function (e) { if (!drag) return; zoom.x = e.clientX - drag.x; zoom.y = e.clientY - drag.y; applyZoom(); });
        viewport.addEventListener('pointerup', function () { drag = null; });
        root.querySelector('[data-viz-fullscreen]').addEventListener('click', function () {
            if (document.fullscreenElement) document.exitFullscreen();
            else if (root.requestFullscreen) root.requestFullscreen().catch(function () { });
        });
        document.addEventListener('fullscreenchange', function () { setTimeout(function () { sizeCanvas(); draw(); }, 50); });

        // ---------- Ayarları sıfırla ----------
        // Müşteri beğenmediği denemeyi tek tıkla başa alır: ölçü (varsayılan, cm), ilk malzeme, kırp + varsayılan
        // (ortalanmış) baskı alanı, aynasız, filtresiz, ortalı, panel çizgisiz, yakınlaştırmasız. Oda (sahne) ve ürün
        // korunur. Kendi odasındaysa "Duvarın önündeki eşyaları işaretle" ile yapılan işaretleme de (onayla) silinir.
        function checkRadio(attr, value) {
            root.querySelectorAll('[' + attr + ']').forEach(function (r) { r.checked = r.value === value; });
        }
        function clearRoomMask(s) {
            var tokenInput = root.querySelector('input[name="__RequestVerificationToken"]') || document.querySelector('input[name="__RequestVerificationToken"]');
            return fetch('/api/v1/room-previews/' + s.id + '/mask', {
                method: 'DELETE',
                credentials: 'same-origin',
                headers: { Accept: 'application/json', RequestVerificationToken: tokenInput ? tokenInput.value : '' }
            }).then(function (r) {
                if (!r.ok) throw new Error(r.status === 429 ? 'Çok sık işlem yaptınız; lütfen biraz sonra tekrar deneyin.' : 'Eşya işaretlemesi silinemedi.');
                return r.json();
            }).then(function (updated) {
                scenesById[updated.id] = updated;
                data.scenes = data.scenes.map(function (x) { return x.id === updated.id ? updated : x; });
                sceneLayers = null; // yeni (maskesiz) katmanlar yüklensin
                return refresh();
            });
        }
        function resetAll() {
            var current = scene();
            var hasMask = !!(current && current.isUserScene && current.foregroundMaskUrl);
            if (hasMask && !window.confirm('Ayarlar sıfırlanacak ve bu odada "Duvarın önündeki eşyaları işaretle" ile yaptığınız işaretleme de silinecek. Devam edilsin mi?')) return;
            var d = data.defaults || {};
            clearTimeout(dimTimer); dimTimer = null;
            state.unit = 'cm';
            state.wCm = +d.wCm || 300;
            state.hCm = +d.hCm || 250;
            state.material = d.material && materials[d.material] ? d.material : Object.keys(materials)[0];
            state.fit = 'crop';
            state.mirror = false;
            state.filter = 'none';
            state.crop = null;
            state.align = 'center';
            state.panelLines = false;
            checkRadio('data-viz-unit', 'cm');
            checkRadio('data-viz-fit', 'crop');
            checkRadio('data-viz-align', 'center');
            checkRadio('data-viz-filter', 'none');
            wInput.value = fmt(state.wCm);
            hInput.value = fmt(state.hCm);
            var materialSelect = root.querySelector('[data-viz-material]');
            if (materialSelect) materialSelect.value = state.material;
            root.querySelector('[data-viz-mirror]').checked = false;
            root.querySelector('[data-viz-panels]').checked = false;
            zoom.s = 1; applyZoom();
            errorBox.textContent = '';
            changed();
            if (!hasMask) { toast('Ayarlar sıfırlandı; yeniden deneyebilirsiniz.'); return; }
            clearRoomMask(current)
                .then(function () { toast('Ayarlar ve eşya işaretlemesi sıfırlandı; yeniden deneyebilirsiniz.'); })
                .catch(function (e) { toast(e.message, true); });
        }
        var resetBtn = root.querySelector('[data-viz-reset]');
        if (resetBtn) resetBtn.addEventListener('click', resetAll);

        // ---------- Paylaş / sepet / geri aktarım ----------
        if (data.external) root.querySelector('[data-viz-share]').hidden = true;
        root.querySelector('[data-viz-share]').addEventListener('click', function () {
            flushDims();
            var url = location.origin + '/duvarinda-gor?' + stateQuery().toString();
            var done = function () { toast('Bağlantı kopyalandı.'); };
            if (navigator.share && /Mobi|Android/i.test(navigator.userAgent)) navigator.share({ title: state.product.title, url: url }).catch(function () { });
            else if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(url).then(done, function () { window.prompt('Bağlantıyı kopyalayın:', url); });
            else window.prompt('Bağlantıyı kopyalayın:', url);
        });

        function postToParent(message) {
            if (window.parent === window) return;
            window.parent.postMessage(Object.assign({ source: 'dekorras-wall' }, message), parentOrigin || location.origin);
        }

        root.querySelector('[data-viz-add-to-cart]').addEventListener('click', function (e) {
            flushDims();
            if (!validate()) return;
            var btn = e.currentTarget;
            btn.disabled = true;
            track('wall_preview_add_to_cart', framed ? (data.mode === 'embed' ? 'embed' : 'goruntuleyici') : 'goruntuleyici', state.product.slug);
            if (data.mode === 'embed') {
                // Harici sitede ana sitenin KENDİ sepetine eklenmesi için bilgi iletilir (spec 1.6.6-D).
                postToParent({ type: 'wallpreview:add-to-cart', product: state.product.slug, configuration: configPayload() });
                btn.disabled = false;
                toast('Seçiminiz mağazaya iletildi.');
                return;
            }
            api('POST', '/api/v1/cart/items', { product: state.product.slug, configuration: configPayload(), quantity: 1 }).then(function () {
                toast('Sepete eklendi.');
                postToParent({ type: 'wallpreview:add-to-cart', product: state.product.slug, configuration: configPayload() });
            }).catch(function (err) { toast(err.message, true); }).finally(function () { btn.disabled = false; });
        });

        var applyBtn = root.querySelector('[data-viz-apply]');
        if (applyBtn) applyBtn.addEventListener('click', function () {
            flushDims();
            if (!validate()) return;
            var payload = Object.assign({ product: state.product.slug }, configPayload());
            if (framed) { postToParent({ type: 'wallpreview:apply', configuration: payload }); return; }
            if (data.returnUrl) {
                // Farklı sayfa: return URL'sine konfigürasyon query string ile aktarılır (spec 1.6.6-C).
                var target = new URL(data.returnUrl, location.origin);
                if (target.pathname.toLowerCase() === '/product/details') target.searchParams.set('slug', state.product.slug);
                Object.keys(payload).forEach(function (k) { if (k !== 'product') target.searchParams.set(k, payload[k]); });
                target.searchParams.set('view', 'customizer');
                location.href = target.pathname + target.search;
            }
        });
        var closeBtn = root.querySelector('[data-viz-close]');
        if (closeBtn) closeBtn.addEventListener('click', function () { postToParent({ type: 'wallpreview:close' }); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && framed && !document.fullscreenElement) postToParent({ type: 'wallpreview:close' }); });

        // İndirme: kendi oda fotoğrafında kota/giriş kuralı olduğu için önce istenir, hata mesaja çevrilir.
        var downloadLink = root.querySelector('[data-viz-download]');
        if (downloadLink) downloadLink.addEventListener('click', function (e) {
            e.preventDefault();
            flushDims();
            downloadLink.classList.add('disabled');
            fetch(downloadLink.href, { credentials: 'same-origin' }).then(function (r) {
                if (r.ok) return r.blob().then(function (blob) {
                    var a = document.createElement('a');
                    a.href = URL.createObjectURL(blob);
                    a.download = 'duvarinda-gor-' + state.product.slug + '.jpg';
                    document.body.appendChild(a); a.click(); a.remove();
                    setTimeout(function () { URL.revokeObjectURL(a.href); }, 5000);
                });
                return r.json().catch(function () { return {}; }).then(function (d) {
                    if (d.requiresLogin) {
                        toast((d.detail || 'Giriş yapmanız gerekiyor.') + ' Giriş sayfasına yönlendiriliyorsunuz…', true);
                        setTimeout(function () { (framed ? window.top : window).location.href = '/Account/Login?returnUrl=' + encodeURIComponent('/duvarinda-gor?' + stateQuery().toString()); }, 1800);
                    } else toast(d.detail || d.title || 'Görsel indirilemedi.', true);
                });
            }).catch(function () { toast('Görsel indirilemedi.', true); })
              .finally(function () { downloadLink.classList.remove('disabled'); });
        });

        var uploadLink = root.querySelector('[data-viz-upload-room]');
        if (uploadLink) uploadLink.addEventListener('click', function (e) {
            if (window.DekorrasRoomUpload) { e.preventDefault(); window.DekorrasRoomUpload.open({ onCreated: function (scene) { data.scenes.push(scene); scenesById[scene.id] = scene; location.search = stateQuery().toString().replace(/scene=[^&]*/, 'scene=' + scene.id.replace(/-/g, '')); } }); }
            else e.preventDefault();
        });

        // Kendi oda fotoğrafı: duvarın önündeki eşyaları (sonradan da) işaretleme / düzeltme.
        var editMaskBtn = root.querySelector('[data-viz-edit-mask]');
        function updateSceneTools() {
            if (editMaskBtn) editMaskBtn.hidden = !(scene() && scene().isUserScene && window.DekorrasRoomUpload);
        }
        if (editMaskBtn) editMaskBtn.addEventListener('click', function () {
            window.DekorrasRoomUpload.open({
                editScene: scene(),
                onMaskSaved: function (s) {
                    scenesById[s.id] = s;
                    data.scenes = data.scenes.map(function (x) { return x.id === s.id ? s : x; });
                    toast('Eşyalar işaretlendi; ürün artık bunların arkasında görünüyor.');
                    refresh();
                }
            });
        });
        root.querySelectorAll('[data-viz-scene]').forEach(function (btn) { btn.addEventListener('click', function () { setTimeout(updateSceneTools, 0); }); });
        updateSceneTools();

        var resizeTimer = null;
        window.addEventListener('resize', function () { clearTimeout(resizeTimer); resizeTimer = setTimeout(function () { sizeCanvas(); draw(); }, 120); });

        categoriesReady.then(loadTab);
        refresh().then(function () { quote(); syncUrl(); postToParent({ type: 'wallpreview:ready' }); });

        // Gömülü modda iframe yüksekliğini ana sayfaya bildir.
        if (framed && 'ResizeObserver' in window) {
            new ResizeObserver(function () { postToParent({ type: 'wallpreview:resize', height: document.documentElement.scrollHeight }); }).observe(document.body);
        }
    }

    // =====================================================================
    // Karşılaştırma: aynı sahne + aynı ölçü, 2–4 poster
    // =====================================================================
    function initCompare(root) {
        var data = JSON.parse(document.getElementById('wallCompareData').textContent);
        var s = data.scene;
        var material = data.materials.filter(function (m) { return m.code === data.initial.material; })[0] || data.materials[0];
        loadScene(s).then(function (layers) {
            root.querySelectorAll('[data-compare-item]').forEach(function (fig) {
                var product = data.products.filter(function (p) { return p.slug === fig.dataset.compareItem; })[0];
                var canvas = fig.querySelector('[data-compare-canvas]');
                var cssW = fig.clientWidth || 500;
                var dpr = Math.min(window.devicePixelRatio || 1, 2);
                canvas.style.width = '100%';
                canvas.width = Math.min(Math.round(cssW * dpr), s.imageWidthPx);
                canvas.height = Math.round(canvas.width * s.imageHeightPx / s.imageWidthPx);
                var renderer = createRenderer(canvas);
                loadImage(product.previewUrl).then(function (img) {
                    var sc = canvas.width / s.imageWidthPx;
                    var q = s.wallQuad.map(function (v) { return v * sc; });
                    var p = place(s.realWallWidthCm, s.realWallHeightCm, data.initial.wCm, data.initial.hCm, 'center');
                    var quad = subQuad(q, p.u0, p.v0, p.u1, p.v1);
                    var poster = buildPosterCanvas(img, {
                        wCm: data.initial.wCm, hCm: data.initial.hCm, fit: 'crop', crop: null, mirror: false, filter: 'none',
                        productType: product.productType, repeatW: product.repeatWidthCm, repeatH: product.repeatHeightCm, repeatType: product.repeatType,
                        panelLines: false, panelWidthCm: material.panelWidthCm, bleedCm: material.bleedCm, materialCode: material.code,
                        wallW: s.realWallWidthCm, wallH: s.realWallHeightCm, align: 'center', maxWidthPx: Math.min(renderer.maxTextureSize, Math.ceil(quadWidth(quad) * 1.25))
                    });
                    renderer.setScene(layers);
                    renderer.draw(poster, quad);
                });
            });
        });
        var form = root.querySelector('[data-compare-form]');
        form.querySelector('[data-compare-scene]').addEventListener('change', function () { form.submit(); });
    }
})();
