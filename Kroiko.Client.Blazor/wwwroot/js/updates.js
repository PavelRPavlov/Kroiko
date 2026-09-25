// New versions of the installed app (ADR-0002 §1, §3–4). index.html starts this module with its service-worker
// registration; the app (Updates/AppUpdates.cs) subscribes through a DotNetObjectReference, applies a waiting version
// and checks for one on demand. Only the published service worker (service-worker.published.js) answers SKIP_WAITING:
// the development one is a no-op, so the update flow exists only in a published build.
// See https://web.dev/articles/service-worker-lifecycle and
// https://developer.mozilla.org/en-US/docs/Web/API/ServiceWorkerRegistration.

// ADR-0002 §4: on top of the browser's own check at each launch.
const checkIntervalMs = 60 * 60 * 1000;

// How long "Презареди" waits for the new version to take over before it reports a failure.
const applyTimeoutMs = 10 * 1000;

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

// Activates the waiting version and reloads the page (see handOver). Settles only when the page has not started to
// reload within applyTimeoutMs, and then fails: a version that never takes over (the development service worker
// ignores SKIP_WAITING) must not leave "Презареди" and "По-късно" disabled for good.
export async function applyUpdate() {
    await handOver();
    await new Promise((_, reject) => setTimeout(() => {
        if (!reloading)
            reject(new Error('The new version did not take over in time.'));
    }, applyTimeoutMs));
}

// The manual check (ADR-0002 §4): 'upToDate', 'downloading' (a newer version is installing or already waits; a
// waiting one is also announced through OnUpdateReady) or 'offline' (offline, or the server could not be asked).
// It reads the registration directly: update() and register() queue behind an install in progress, which can take
// as long as downloading the whole new version.
export async function checkNow() {
    const current = await navigator.serviceWorker?.getRegistration();
    if (!current)
        return 'offline';
    if (current.installing)
        return 'downloading';
    if (!await tryUpdate(current))
        return 'offline';
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

    // Background checks: silent when offline or when the server cannot be reached; the next trigger tries again.
    setInterval(() => tryUpdate(current), checkIntervalMs);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible')
            tryUpdate(current);
    });
    window.addEventListener('online', () => tryUpdate(current));
    return current;
}

// A new version waits only behind the one controlling this page. The first install activates at once (its
// 'installed' is not an update), and a page no worker controls (a hard reload) runs the newest version on its next
// load anyway.
function offerWaiting(current) {
    if (!current.waiting || !navigator.serviceWorker.controller)
        return;
    updateReady = true;
    notify();
}

// Asks the server for a newer version: false, without asking, when offline, and false when the request failed.
async function tryUpdate(current) {
    if (!navigator.onLine)
        return false;
    try {
        await current.update();
        return true;
    } catch {
        return false;
    }
}

// Activates the waiting version and reloads once it controls the page (controllerchange), so the page runs from one
// cache only. A page no worker controls reloads once the version is activated instead. With nothing waiting (another
// window of the app already applied it), a reload is all it takes; a waiting version that a newer one replaced before
// it could activate hands over to that newer one.
async function handOver() {
    const waiting = (await registration)?.waiting;
    if (!waiting) {
        reload();
        return;
    }
    navigator.serviceWorker.addEventListener('controllerchange', reload);
    waiting.addEventListener('statechange', () => {
        if (waiting.state === 'activated')
            reload();
        else if (waiting.state === 'redundant' && !reloading)
            handOver();
    });
    waiting.postMessage('SKIP_WAITING');
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
