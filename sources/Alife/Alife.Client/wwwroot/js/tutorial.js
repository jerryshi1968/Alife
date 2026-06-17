window.getStepRect = (selector) => {
    const element = document.querySelector(selector);
    if (!element) return null;
    const rect = element.getBoundingClientRect();
    return {
        top: rect.top + window.scrollY,
        left: rect.left + window.scrollX,
        width: rect.width,
        height: rect.height
    };
};

window.getPluginMarketDimensions = () => {
    const container = document.getElementById('pluginMarketContainer');
    if (!container) return { width: 0, height: 0 };
    return { width: container.clientWidth, height: container.clientHeight };
};

window._pluginMarketResizeObserver = null;
window.watchPluginMarketResize = (dotnetRef) => {
    window.unwatchPluginMarketResize();
    const container = document.getElementById('pluginMarketContainer');
    if (!container) return;
    let debounceTimer = null;
    window._pluginMarketResizeObserver = new ResizeObserver((entries) => {
        if (debounceTimer) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => {
            const entry = entries[0];
            if (entry) {
                const { width, height } = entry.contentRect;
                dotnetRef.invokeMethodAsync('OnContainerResized', Math.round(width), Math.round(height));
            }
        }, 100);
    });
    window._pluginMarketResizeObserver.observe(container);
};

window.unwatchPluginMarketResize = () => {
    if (window._pluginMarketResizeObserver) {
        window._pluginMarketResizeObserver.disconnect();
        window._pluginMarketResizeObserver = null;
    }
};
