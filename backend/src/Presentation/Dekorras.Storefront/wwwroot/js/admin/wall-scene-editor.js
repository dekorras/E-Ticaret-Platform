// WallScenes.razor için duvar köşesi düzenleyici: taban görselin üstüne görselin piksel koordinatlarında
// (viewBox = görsel boyutu) bir SVG çizer; 4 tutamaç (sol-üst, sağ-üst, sağ-alt, sol-alt) sürüklenir.
// SVG tamamen JS'e aittir (Blazor diff'i ile çakışmaması için Blazor yalnızca boş kapsayıcıyı render eder);
// bırakınca köşeler .NET'e `CornersChanged(double[8])` ile bildirilir.
(function () {
    var NS = 'http://www.w3.org/2000/svg';
    var LABELS = ['SÜ', 'SĞÜ', 'SĞA', 'SA'];

    window.dekorrasWallSceneEditor = {
        init: function (containerId, imageUrl, width, height, corners, dotNetHelper) {
            var host = document.getElementById(containerId);
            if (!host) return;
            host.innerHTML = '';
            host.style.position = 'relative';

            var img = document.createElement('img');
            img.src = imageUrl;
            img.alt = '';
            img.style.width = '100%';
            img.style.display = 'block';
            img.draggable = false;
            host.appendChild(img);

            var svg = document.createElementNS(NS, 'svg');
            svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
            svg.setAttribute('preserveAspectRatio', 'none');
            svg.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;touch-action:none;';
            host.appendChild(svg);

            var poly = document.createElementNS(NS, 'polygon');
            poly.setAttribute('fill', 'rgba(64,81,137,.25)');
            poly.setAttribute('stroke', '#405189');
            poly.setAttribute('stroke-width', Math.max(2, width / 400));
            svg.appendChild(poly);

            var pts = [];
            for (var i = 0; i < 4; i++) pts.push({ x: corners[i * 2], y: corners[i * 2 + 1] });
            var r = Math.max(8, width / 80);
            var handles = pts.map(function (p, i) {
                var g = document.createElementNS(NS, 'g');
                g.style.cursor = 'move';
                g.setAttribute('data-corner', i);
                var c = document.createElementNS(NS, 'circle');
                c.setAttribute('r', r);
                c.setAttribute('fill', '#fff');
                c.setAttribute('stroke', '#f06548');
                c.setAttribute('stroke-width', Math.max(2, width / 500));
                var t = document.createElementNS(NS, 'text');
                t.textContent = LABELS[i];
                t.setAttribute('font-size', r * 1.1);
                t.setAttribute('fill', '#f06548');
                t.setAttribute('font-family', 'sans-serif');
                g.appendChild(c);
                g.appendChild(t);
                svg.appendChild(g);
                return { g: g, c: c, t: t };
            });

            function draw() {
                poly.setAttribute('points', pts.map(function (p) { return p.x + ',' + p.y; }).join(' '));
                handles.forEach(function (h, i) {
                    h.c.setAttribute('cx', pts[i].x);
                    h.c.setAttribute('cy', pts[i].y);
                    h.t.setAttribute('x', pts[i].x + r * 1.3);
                    h.t.setAttribute('y', pts[i].y - r * 1.1);
                });
            }

            function toImage(evt) {
                var rect = svg.getBoundingClientRect();
                return {
                    x: Math.round(Math.min(width, Math.max(0, (evt.clientX - rect.left) / rect.width * width)) * 10) / 10,
                    y: Math.round(Math.min(height, Math.max(0, (evt.clientY - rect.top) / rect.height * height)) * 10) / 10
                };
            }

            var active = -1;
            svg.addEventListener('pointerdown', function (evt) {
                var g = evt.target.closest('[data-corner]');
                if (!g) return;
                active = parseInt(g.getAttribute('data-corner'), 10);
                svg.setPointerCapture(evt.pointerId);
                evt.preventDefault();
            });
            svg.addEventListener('pointermove', function (evt) {
                if (active < 0) return;
                pts[active] = toImage(evt);
                draw();
            });
            function end() {
                if (active < 0) return;
                active = -1;
                var flat = [];
                pts.forEach(function (p) { flat.push(p.x, p.y); });
                dotNetHelper.invokeMethodAsync('CornersChanged', flat);
            }
            svg.addEventListener('pointerup', end);
            svg.addEventListener('pointercancel', end);
            draw();
        }
    };
})();
