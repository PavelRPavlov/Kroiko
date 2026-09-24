// New versions of the installed app (ADR-0002 §1, §3–4). index.html starts this module with its service-worker
// registration; the app (Updates/AppUpdates.cs) subscribes through a DotNetObjectReference, applies a waiting version
// and checks for one on demand. Only the published service worker (service-worker.published.js) answers SKIP_WAITING:
// the development one is a no-op, so the update flow exists only in a published build.
// See https://web.dev/articles/service-worker-lifecycle and
// https://developer.mozilla.org/en-US/docs/Web/API/ServiceWorkerRegistration.

// ADR-0002 §4: on top of the browser's own check at each launch.
const checkIntervalMs = 60 * 60 * 1000;

// The registration start() watches: null until it is started, and when registering failed.
let registration = Promise.resolve(null);
let started = false;
let updateReady = false;
let subscriber = null;
let reloading = false;

// Called by index.html with the promise navigator.serviceWorker.register returned.
export function start(registering) {
    if (started)
        return;
    started = true;
    registration = Promise.resolve(registering).then(watch, error => {
        console.error('The service worker could not be registered.', error);
        return null;
    });
}

// The app's DotNetObjectReference; its OnUpdateReady is called once a new version waits, even if it waited before.
export function subscribe(dotNetReference) {
    subscriber = dotNetReference;
    if (updateReady)
        notify();
}

export function unsubscribe() {
    subscriber = null;
}

// Activates the waiting version and reloads once it controls the page, so the page runs from one cache only.
// With nothing waiting (another window of the app already applied it), a reload is all it takes.
export async function applyUpdate() {
    const waiting = (await registration)?.waiting;
    if (!waiting) {
        reload();
        return;
    }
    navigator.serviceWorker.addEventListener('controllerchange', reload);
    waiting.postMessage('SKIP_WAITING');
}

// The manual check (ADR-0002 §4): 'upToDate', 'downloading' (a newer version is installing or already waits; a
// waiting one is also announced through OnUpdateReady) or 'offline' (the server could not be asked).
export async function checkNow() {
    const current = await registration;
    if (!current || !navigator.onLine)
        return 'offline';
    try {
        await current.update();
    } catch {
        return 'offline';
    }
    return current.installing || current.waiting ? 'downloading' : 'upToDate';
}

function watch(current) {
    offerWaiting(current);
    current.addEventListener('updatefound', () => {
        const worker = current.installing;
        worker?.addEventListener('statechange', () => {
            if (worker.state === 'installed')
                offerWaiting(current);
        });
    });

    setInterval(() => check(current), checkIntervalMs);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible')
            check(current);
    });
    window.addEventListener('online', () => check(current));
    return current;
}

// A new version waits only behind the one controlling this page. The first install activates at once (its
// 'installed' is not an update), and a page no worker controls (a hard reload) would never see the
// controllerchange that reloads it; its next load runs the newest version anyway.
function offerWaiting(current) {
    if (!current.waiting || !navigator.serviceWorker.controller)
        return;
    updateReady = true;
    notify();
}

// A background check: silent when offline or the server cannot be reached; the next trigger tries again.
async function check(current) {
    if (!navigator.onLine)
        return;
    try {
        await current.update();
    } catch {
        // Offline in all but name: nothing to report.
    }
}

function notify() {
    subscriber?.invokeMethodAsync('OnUpdateReady')
        .catch(error => console.warn('The app could not be told about the new version.', error));
}

function reload() {
    if (reloading)
        return;
    reloading = true;
    location.reload();
}
