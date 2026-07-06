const ui = {
    bubble: document.getElementById("bubble"),
    bubbleContainer: document.getElementById("bubble-container"),
    thinkingIndicator: document.getElementById("thinking-indicator"),
    chatInput: document.getElementById("chat-input"),
    sendBtn: document.getElementById("send-btn")
};

function formatLogArg(arg) {
    if (arg instanceof Error) return `${arg.name}: ${arg.message}\n${arg.stack || ""}`;
    if (typeof arg === "object") {
        try {
            return JSON.stringify(arg);
        } catch {
            return String(arg);
        }
    }
    return String(arg);
}

function postLog(level, ...args) {
    try {
        window.chrome.webview.postMessage({
            type: "log",
            level,
            text: args.map(formatLogArg).join(" ")
        });
    } catch {
        // ignored
    }
}

{
    const rawWarn = console.warn.bind(console);
    const rawError = console.error.bind(console);
    console.warn = (...args) => {
        postLog("warn", ...args);
        rawWarn(...args);
    };
    console.error = (...args) => {
        postLog("error", ...args);
        rawError(...args);
    };
}

window.addEventListener("error", (e) => {
    const target = e.target;
    if (target && target !== window) {
        postLog("resource-error", target.tagName || "unknown", target.src || target.href || "");
        return;
    }
    postLog("error", e.message, `${e.filename}:${e.lineno}:${e.colno}`, e.error || "");
}, true);

window.addEventListener("unhandledrejection", (e) => {
    postLog("unhandledrejection", e.reason);
});

const app = new PIXI.Application({
    view: document.getElementById("canvas"),
    autoStart: true,
    resizeTo: window,
    transparent: true,
    backgroundAlpha: 0,
});

let model = null;
let isDragging = false;

//输入功能
window.chrome.webview.addEventListener("message", (e) => {
    const msg = e.data;
    switch (msg.type) {
        //加载live2d功能
        case "load":
            loadModel(msg.url);
            break;
        //修改表情
        case "expression":
            if (!model) {
                console.warn("[Pet] Ignore expression because model is not loaded:", msg.id);
                break;
            }
            model.expression(msg.id);
            break;
        //修改动作
        case "motion":
            if (!model) {
                console.warn("[Pet] Ignore motion because model is not loaded:", msg.group, msg.index);
                break;
            }
            model.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE);
            break;
        //修改气泡文字
        case "bubble":
            ui.bubble.innerText = msg.text;
            ui.bubbleContainer.classList.add("show");
            break;
        case "hide-bubble":
            ui.bubbleContainer.classList.remove("show");
            break;
        //修改注视目标
        case "look":
            if (!model) break;
            model.focus(msg.x, msg.y, msg.instant);
            break;
        //修改状态反馈
        case "status":
            if (msg.working) {
                ui.thinkingIndicator.classList.add("show");
            } else {
                ui.thinkingIndicator.classList.remove("show");
            }
            break;
    }

    async function loadModel(url) {
        console.log("[Pet] Loading model:", url);
        if (model) app.stage.removeChild(model);
        try {
            model = await PIXI.live2d.Live2DModel.from(url, {autoInteract: false});
            console.log("[Pet] Model loaded successfully");
        } catch (err) {
            console.error("[Pet] Live2D model load failed:", err);
            postMessage({type: "loaded"});
            return;
        }
        app.stage.addChild(model);

        // 诊断：打印 hitArea 信息
        const hitAreaDefs = model.internalModel?.getHitAreaDefs?.();
        console.log("[Pet] HitArea definitions:", JSON.stringify(hitAreaDefs));
        const drawableIds = model.internalModel?.coreModel?.getDrawableIds?.();
        console.log("[Pet] Drawable IDs:", JSON.stringify(drawableIds));

        // 设置动画曲线
        const ctrl = model.internalModel.focusController;
        if (ctrl) {
            ctrl.acceleration = 0.04;
            ctrl.deceleration = 0.08;
        }

        // 保存原始无缩放的高度，避免因为循环自乘导致闪烁变大
        const baseHeight = model.internalModel.originalHeight || (model.height / model.scale.y);

        const updateLayout = () => {
            const uiScale = window.innerHeight / 540;
            document.documentElement.style.setProperty('--ui-scale', uiScale);
            const scale = (window.innerHeight * 0.9) / baseHeight;
            model.scale.set(scale);
            model.position.set(window.innerWidth / 2, window.innerHeight / 2);
        };

        model.anchor.set(0.5, 0.5);
        updateLayout();
        model.interactive = true;

        window.addEventListener("resize", () => {
            if (model) {
                updateLayout();
            }
        });

        postMessage({type: "loaded"});
    }
});


//输出功能
{
    function postMessage(data) {
        window.chrome.webview.postMessage(data);
    }

    // 缩放按钮拖动逻辑
    const resizeBtn = document.getElementById("resize-btn");
    let startX, startY;
    resizeBtn.addEventListener("pointerdown", (e) => {
        if (e.button !== 0) return;
        resizeBtn.setPointerCapture(e.pointerId);
        startX = e.screenX;
        startY = e.screenY;
    });
    resizeBtn.addEventListener("pointermove", (e) => {
        if (resizeBtn.hasPointerCapture(e.pointerId)) {
            const dx = e.screenX - startX;
            const dy = e.screenY - startY;
            if (dx !== 0 || dy !== 0) {
                postMessage({type: "resize_delta", dx: dx, dy: dy});
                startX = e.screenX;
                startY = e.screenY;
            }
        }
    });
    resizeBtn.addEventListener("pointerup", (e) => {
        resizeBtn.releasePointerCapture(e.pointerId);
    });

    //双击触摸反馈
    window.addEventListener("dblclick", async (e) => {
        if (e.target.tagName !== "CANVAS") return;
        const hitAreas = await model.hitTest(e.clientX, e.clientY);
        console.log("[Pet] hitTest result:", JSON.stringify(hitAreas), "clientX/Y:", e.clientX, e.clientY);
        if (hitAreas.length > 0) postMessage({type: "poke", areas: hitAreas});
    });

    //单击拖动反馈
    window.addEventListener("mousedown", async (e) => {
        if (e.button !== 0 || e.target.tagName !== "CANVAS") return;
        const hitAreas = await model.hitTest(e.clientX, e.clientY);
        if (!hitAreas || hitAreas.length === 0) {
            isDragging = true;
            postMessage({type: "drag_start"});
        }
    });
    window.addEventListener("mouseup", async (e) => {
        if (isDragging === true) {
            isDragging = false;
            postMessage({type: "drag_end"});
        }
    })

    //文本输入反馈
    const onSend = () => {
        const text = ui.chatInput.value.trim();
        if (text) {
            postMessage({type: "input", text});
            ui.chatInput.value = "";
        }
    };
    ui.sendBtn.onclick = onSend;
    ui.chatInput.onkeydown = (e) => {
        if (e.key === "Enter") onSend();
    };

}

postMessage({type: "ready"});
