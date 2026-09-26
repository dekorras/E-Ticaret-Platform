// Duvar kağıdı modülünün tüm sayfalarda ortak istemci durumu: favoriler (♡) ve "Duvarımda Dene"
// listesi + ekranın altındaki sabit tepsi (spec 1.6.2). Sunucu tek doğruluk kaynağıdır; bu modül
// yalnızca /api/v1 uçlarını çağırır ve [data-wall-card] kartlarını işaretler. Diğer modüller
// window.DekorrasWall üzerinden kullanır ve 'wall:lists-changed' olayını dinleyebilir.
(function () {
    'use strict';

    var state = { favorites: new Set(), tryOn: [], maxItems: 12, loaded: false };
    var selectedForCompare = new Set();

    function api(method, url, body) {
        var options = { method: method, headers: { 'Accept': 'application/json' }, credentials: 'same-origin' };
        if (body !== undefined) {
            options.headers['Content-Type'] = 'application/json';
            options.body = JSON.stringify(body);
        }
        return fetch(url, options).then(function (response) {
            if (response.status === 204) return null;
            return response.json().catch(function () { return null; }).then(function (data) {
                if (!response.ok) {
                    var message = (data && (data.detail || data.title)) || 'İşlem yapılamadı.';
                    if (data && data.errors) message = Object.values(data.errors).flat().join(' ');
                    throw new Error(message);
                }
                return data;
            });
        });
    }

    function toast(message, isError) {
        var region = document.getElementById('wallToast');
        if (!region) {
            region = document.createElement('div');
            region.id = 'wallToast';
            region.className = 'wall-toast';
            region.setAttribute('role', 'status');
            region.setAttribute('aria-live', 'polite');
            document.body.appendChild(region);
        }
        region.textContent = message;
        region.classList.toggle('is-error', !!isError);
        region.classList.add('is-visible');
        clearTimeout(region._timer);
        region._timer = setTimeout(function () { region.classList.remove('is-visible'); }, 3200);
    }

    function emit() {
        markCards(document);
        renderTray();
        document.dispatchEvent(new CustomEvent('wall:lists-changed', { detail: { favorites: Array.from(state.favorites), tryOn: state.tryOn.slice() } }));
    }

    function applyTryOn(list) {
        state.tryOn = (list && list.items) || [];
        state.maxItems = (list && list.maxItems) || 12;
        selectedForCompare.forEach(function (id) {
            if (!state.tryOn.some(function (i) { return i.productId === id; })) selectedForCompare.delete(id);
        });
    }

    function load() {
        return Promise.all([
            api('GET', '/api/v1/favorites').then(function (d) { state.favorites = new Set((d && d.productIds) || []); }),
            api('GET', '/api/v1/try-on-list/items').then(applyTryOn)
        ]).then(function () { state.loaded = true; emit(); }).catch(function () { /* liste yüklenemezse sayfa yine çalışır */ });
    }

    function isInTryOn(productId) {
        return state.tryOn.some(function (i) { return i.productId === productId; });
    }

    function markCards(root) {
        root.querySelectorAll('[data-wall-card]').forEach(function (card) {
            var id = card.dataset.productId;
            var fav = card.querySelector('[data-fav-toggle]');
            if (fav) {
                var on = state.favorites.has(id);
                fav.setAttribute('aria-pressed', on ? 'true' : 'false');
                fav.setAttribute('aria-label', on ? 'Favorilerden çıkar' : 'Favorilere ekle');
                fav.title = fav.getAttribute('aria-label');
                var icon = fav.querySelector('i');
                if (icon) icon.className = on ? 'bi bi-heart-fill' : 'bi bi-heart';
            }
            var tryBtn = card.querySelector('[data-tryon-toggle]');
            if (tryBtn) {
                var inList = isInTryOn(id);
                tryBtn.setAttribute('aria-pressed', inList ? 'true' : 'false');
                tryBtn.classList.toggle('btn-secondary', inList);
                tryBtn.classList.toggle('btn-outline-secondary', !inList);
                var label = tryBtn.querySelector('span');
                if (label) label.textContent = inList ? 'Listede' : 'Duvarımda Dene';
            }
        });
    }

    function toggleFavorite(productId) {
        var on = state.favorites.has(productId);
        var request = on ? api('DELETE', '/api/v1/favorites/' + productId) : api('POST', '/api/v1/favorites', { productId: productId });
        return request.then(function () {
            if (on) state.favorites.delete(productId); else state.favorites.add(productId);
            toast(on ? 'Favorilerden çıkarıldı.' : 'Favorilere eklendi.');
            emit();
        }).catch(function (e) { toast(e.message, true); });
    }

    function addTryOn(productId, configuration) {
        var body = { productId: productId };
        if (configuration) body.configuration = configuration;
        return api('POST', '/api/v1/try-on-list/items', body).then(function (list) {
            applyTryOn(list);
            toast('"Duvarımda Dene" listesine eklendi.');
            emit();
        }).catch(function (e) { toast(e.message, true); throw e; });
    }

    function removeTryOn(productId) {
        return api('DELETE', '/api/v1/try-on-list/items/' + productId).then(function (list) {
            applyTryOn(list);
            emit();
        }).catch(function (e) { toast(e.message, true); });
    }

    function reorder(productIds) {
        var byId = {};
        state.tryOn.forEach(function (i) { byId[i.productId] = i; });
        state.tryOn = productIds.map(function (id) { return byId[id]; }).filter(Boolean);
        emit();
        return api('PATCH', '/api/v1/try-on-list/order', { productIds: productIds }).catch(function (e) { toast(e.message, true); });
    }

    function share() {
        return api('POST', '/api/v1/try-on-list/share').then(function (d) {
            var done = function () { toast('Paylaşım bağlantısı kopyalandı.'); };
            if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(d.url).then(done, function () { window.prompt('Bağlantıyı kopyalayın:', d.url); });
            else window.prompt('Bağlantıyı kopyalayın:', d.url);
            return d.url;
        }).catch(function (e) { toast(e.message, true); });
    }

    // ---- Tepsi ----
    var trayCollapsed = false;
    try { trayCollapsed = localStorage.getItem('wallTrayCollapsed') === '1'; } catch (e) { /* depolama kapalı */ }

    function renderTray() {
        if (document.body.classList.contains('wall-bare')) return; // görüntüleyici modal/embed içinde tepsi gösterilmez
        var tray = document.getElementById('wallTray');
        if (state.tryOn.length === 0) {
            if (tray) tray.remove();
            document.body.classList.remove('has-wall-tray');
            return;
        }
        if (!tray) {
            tray = document.createElement('aside');
            tray.id = 'wallTray';
            tray.className = 'wall-tray';
            tray.setAttribute('aria-label', 'Duvarımda Dene listesi');
            document.body.appendChild(tray);
        }
        document.body.classList.add('has-wall-tray');
        tray.classList.toggle('is-collapsed', trayCollapsed);

        var compareIds = Array.from(selectedForCompare);
        var compareSlugs = state.tryOn.filter(function (i) { return selectedForCompare.has(i.productId); }).map(function (i) { return i.card.slug; });
        var first = state.tryOn[0];

        tray.innerHTML =
            '<div class="wall-tray-head">' +
            '  <button type="button" class="wall-tray-toggle" data-tray-toggle aria-expanded="' + (!trayCollapsed) + '">' +
            '    <i class="bi bi-layers"></i> Duvarımda Dene <span class="badge bg-dark">' + state.tryOn.length + '/' + state.maxItems + '</span>' +
            '    <i class="bi ' + (trayCollapsed ? 'bi-chevron-up' : 'bi-chevron-down') + '"></i></button>' +
            '  <div class="wall-tray-tools">' +
            '    <a class="btn btn-sm btn-primary" href="/duvarinda-gor?product=' + encodeURIComponent(first.card.slug) + '&list=1&src=tepsi" data-wall-preview="' + first.card.slug + '" data-wall-preview-source="tepsi"><i class="bi bi-house-door"></i> Duvarında Gör</a>' +
            '    <a class="btn btn-sm btn-outline-dark' + (compareIds.length >= 2 ? '' : ' disabled') + '" aria-disabled="' + (compareIds.length < 2) + '" href="' +
                    (compareIds.length >= 2 ? '/duvarinda-gor/karsilastir?products=' + compareSlugs.map(encodeURIComponent).join(',') : '#') + '">' +
            '      <i class="bi bi-layout-split"></i> Karşılaştır (' + compareIds.length + '/4)</a>' +
            '    <button type="button" class="btn btn-sm btn-outline-secondary" data-tray-share><i class="bi bi-share"></i> Paylaş</button>' +
            '  </div>' +
            '</div>' +
            '<ol class="wall-tray-items" data-tray-list>' +
            state.tryOn.map(function (item) {
                var c = item.card;
                var checked = selectedForCompare.has(item.productId);
                return '<li class="wall-tray-item" draggable="true" data-id="' + item.productId + '">' +
                    '<a href="/duvarinda-gor?product=' + encodeURIComponent(c.slug) + '&src=tepsi" data-wall-preview="' + c.slug + '" data-wall-preview-source="tepsi" title="' + escapeHtml(c.title) + '">' +
                    (c.thumbUrl ? '<img src="' + c.thumbUrl + '" alt="' + escapeHtml(c.title) + '" loading="lazy">' : '<span>' + escapeHtml(c.title) + '</span>') + '</a>' +
                    '<label class="wall-tray-compare" title="Karşılaştırmaya ekle"><input type="checkbox" data-tray-compare value="' + item.productId + '"' + (checked ? ' checked' : '') +
                    (!checked && compareIds.length >= 4 ? ' disabled' : '') + '> <span class="visually-hidden">Karşılaştır</span></label>' +
                    '<button type="button" class="wall-tray-remove" data-tray-remove="' + item.productId + '" aria-label="' + escapeHtml(c.title) + ' listeden çıkar">&times;</button>' +
                    '</li>';
            }).join('') +
            '</ol>';

        bindDrag(tray.querySelector('[data-tray-list]'));
    }

    function bindDrag(list) {
        var dragged = null;
        list.addEventListener('dragstart', function (e) {
            dragged = e.target.closest('.wall-tray-item');
            if (!dragged) return;
            dragged.classList.add('is-dragging');
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', dragged.dataset.id);
        });
        list.addEventListener('dragover', function (e) {
            if (!dragged) return;
            e.preventDefault();
            var over = e.target.closest('.wall-tray-item');
            if (!over || over === dragged) return;
            var rect = over.getBoundingClientRect();
            var after = (e.clientX - rect.left) > rect.width / 2;
            list.insertBefore(dragged, after ? over.nextSibling : over);
        });
        list.addEventListener('dragend', function () {
            if (!dragged) return;
            dragged.classList.remove('is-dragging');
            dragged = null;
            var ids = Array.from(list.querySelectorAll('.wall-tray-item')).map(function (li) { return li.dataset.id; });
            reorder(ids);
        });
        // Klavye ile sıralama: öğe odaktayken Alt+← / Alt+→
        list.addEventListener('keydown', function (e) {
            if (!e.altKey || (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight')) return;
            var li = e.target.closest('.wall-tray-item');
            if (!li) return;
            e.preventDefault();
            var ids = state.tryOn.map(function (i) { return i.productId; });
            var index = ids.indexOf(li.dataset.id);
            var target = e.key === 'ArrowLeft' ? index - 1 : index + 1;
            if (target < 0 || target >= ids.length) return;
            ids.splice(target, 0, ids.splice(index, 1)[0]);
            reorder(ids).then(function () {
                var moved = document.querySelector('.wall-tray-item[data-id="' + li.dataset.id + '"] a');
                if (moved) moved.focus();
            });
        });
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value).replace(/[&<>"']/g, function (ch) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch];
        });
    }

    // ---- Olay delegasyonu (sonradan eklenen kartlar da çalışır) ----
    document.addEventListener('click', function (e) {
        var fav = e.target.closest('[data-fav-toggle]');
        if (fav) {
            e.preventDefault();
            var card = fav.closest('[data-wall-card]');
            var id = fav.dataset.productId || (card && card.dataset.productId);
            if (id) toggleFavorite(id);
            return;
        }
        var tryBtn = e.target.closest('[data-tryon-toggle]');
        if (tryBtn) {
            e.preventDefault();
            var tryCard = tryBtn.closest('[data-wall-card]');
            var productId = tryBtn.dataset.productId || (tryCard && tryCard.dataset.productId);
            if (!productId) return;
            if (isInTryOn(productId)) removeTryOn(productId);
            else addTryOn(productId, window.DekorrasWall.currentConfiguration ? window.DekorrasWall.currentConfiguration(productId) : null).catch(function () { });
            return;
        }
        var remove = e.target.closest('[data-tray-remove]');
        if (remove) { removeTryOn(remove.dataset.trayRemove); return; }
        if (e.target.closest('[data-tray-share]')) { share(); return; }
        if (e.target.closest('[data-tray-toggle]')) {
            trayCollapsed = !trayCollapsed;
            try { localStorage.setItem('wallTrayCollapsed', trayCollapsed ? '1' : '0'); } catch (err) { /* yok say */ }
            renderTray();
            return;
        }
        var addAll = e.target.closest('[data-tryon-add-all]');
        if (addAll) {
            var ids = Array.from(document.querySelectorAll('[data-wall-card]')).map(function (c) { return c.dataset.productId; }).filter(function (id) { return !isInTryOn(id); });
            ids.reduce(function (p, id) { return p.then(function () { return addTryOn(id); }); }, Promise.resolve()).catch(function () { });
        }
    });

    document.addEventListener('change', function (e) {
        var cb = e.target.closest('[data-tray-compare]');
        if (!cb) return;
        if (cb.checked) selectedForCompare.add(cb.value); else selectedForCompare.delete(cb.value);
        renderTray();
    });

    window.DekorrasWall = {
        api: api,
        toast: toast,
        state: state,
        load: load,
        markCards: markCards,
        toggleFavorite: toggleFavorite,
        addTryOn: addTryOn,
        removeTryOn: removeTryOn,
        reorder: reorder,
        share: share,
        isInTryOn: isInTryOn,
        currentConfiguration: null
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', load);
    else load();
})();
