window.pzConsole = {
    scrollToEnd: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    }
};

// Copiar texto: navigator.clipboard só existe em HTTPS/localhost; o painel roda em HTTP na LAN.
window.pzCopy = async function (text) {
    if (navigator.clipboard && window.isSecureContext) {
        try { await navigator.clipboard.writeText(text); return true; } catch (e) { }
    }
    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.appendChild(area);
    area.select();
    let ok = false;
    try { ok = document.execCommand("copy"); } catch (e) { }
    document.body.removeChild(area);
    return ok;
};

// Mantém alterações do SandboxVars nesta aba, inclusive quando o circuito Blazor cai.
window.pzSandboxDraft = (function () {
    let state = null;

    function read(storageKey) {
        try {
            const json = window.sessionStorage.getItem(storageKey);
            return json ? JSON.parse(json) : null;
        } catch (e) {
            return null;
        }
    }

    function write() {
        if (!state) return;
        try {
            if (Object.keys(state.values).length === 0) {
                window.sessionStorage.removeItem(state.storageKey);
                return;
            }
            window.sessionStorage.setItem(state.storageKey, JSON.stringify({
                signature: state.signature,
                values: state.values
            }));
        } catch (e) { }
    }

    function setValue(path, value) {
        if (!state || !path) return;
        if (String(value) === String(state.baseline[path])) delete state.values[path];
        else state.values[path] = String(value);
        write();
    }

    function capture(event) {
        const element = event.target.closest && event.target.closest("[data-sandbox-path]");
        if (!element) return;
        setValue(element.dataset.sandboxPath, element.type === "checkbox" ? String(element.checked) : element.value);
    }

    document.addEventListener("input", capture);
    document.addEventListener("change", capture);

    return {
        initialize: function (storageKey, signature, baseline) {
            const saved = read(storageKey);
            const values = saved && saved.signature === signature && saved.values ? saved.values : {};
            state = { storageKey: storageKey, signature: signature, baseline: baseline || {}, values: values };
            if (saved && saved.signature !== signature) {
                try { window.sessionStorage.removeItem(storageKey); } catch (e) { }
            }
            return values;
        },
        set: function (path, value) {
            setValue(path, value);
        },
        clear: function (storageKey) {
            try { window.sessionStorage.removeItem(storageKey); } catch (e) { }
            state = null;
        }
    };
})();

// Gráfico local e pequeno para o painel offline. Sem CDN e sem acesso à rede do navegador.
window.pzCharts = {
    render: function (id, points, events, fixedMax) {
        const canvas = document.getElementById(id);
        if (!canvas) return;
        const width = Math.max(320, canvas.clientWidth || 800), height = 220, ratio = window.devicePixelRatio || 1;
        canvas.width = width * ratio; canvas.height = height * ratio;
        const ctx = canvas.getContext("2d"); ctx.scale(ratio, ratio); ctx.clearRect(0, 0, width, height);
        const valid = (points || []).filter(p => p.v !== null && Number.isFinite(Number(p.v)));
        if (!valid.length) { ctx.fillStyle = "#98a59a"; ctx.font = "13px Segoe UI"; ctx.fillText("Ainda não há dados para este período.", 18, 35); return; }
        const left = 44, right = 12, top = 15, bottom = 28;
        const minT = Number(valid[0].t), maxT = Math.max(minT + 1, Number(valid[valid.length - 1].t));
        const maxV = Math.max(Number(fixedMax || 0), ...valid.map(p => Number(p.v)), 1);
        const x = t => left + (Number(t) - minT) / (maxT - minT) * (width - left - right);
        const y = v => top + (1 - Number(v) / maxV) * (height - top - bottom);
        ctx.strokeStyle = "#2a352c"; ctx.fillStyle = "#98a59a"; ctx.font = "11px Segoe UI";
        for (let i = 0; i <= 4; i++) { const py = top + i * (height - top - bottom) / 4; ctx.beginPath(); ctx.moveTo(left, py); ctx.lineTo(width-right, py); ctx.stroke(); ctx.fillText((maxV * (4-i) / 4).toFixed(1), 4, py + 4); }
        ctx.strokeStyle = "#8dbf65"; ctx.lineWidth = 2; ctx.beginPath();
        valid.forEach((p, i) => { const px=x(p.t), py=y(p.v); if (i===0) ctx.moveTo(px,py); else ctx.lineTo(px,py); }); ctx.stroke();
        ctx.strokeStyle = "rgba(214,173,92,.75)"; ctx.lineWidth = 1;
        (events || []).forEach(e => { if (e.t < minT || e.t > maxT) return; const px=x(e.t); ctx.beginPath(); ctx.moveTo(px,top); ctx.lineTo(px,height-bottom); ctx.stroke(); });
        ctx.fillStyle="#98a59a"; ctx.fillText(new Date(minT).toLocaleString("pt-BR", {day:"2-digit",month:"2-digit",hour:"2-digit",minute:"2-digit"}), left, height-7);
        const end = new Date(maxT).toLocaleString("pt-BR", {day:"2-digit",month:"2-digit",hour:"2-digit",minute:"2-digit"}); ctx.fillText(end, width-right-ctx.measureText(end).width, height-7);
    }
};
