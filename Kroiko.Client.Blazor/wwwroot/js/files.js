// Saving the generated order files (ADR-0003): downloads (phase 04) and "Запази в папка…" (phase 05).

// How long a download's blob URL stays valid: the browser may read it after the click has returned.
const blobUrlLifetimeMs = 60_000;

// Downloads one file: the bytes arrive as a DotNetStreamReference and are saved through a Blob and an
// <a download> (ADR-0003 §5). The name is used as given; the app has already sanitised it (ADR-0003 §7).
export async function downloadFile(fileName, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([arrayBuffer]));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), blobUrlLifetimeMs);
}

// "Запази в папка…" (ADR-0003 §2–4, §6) through the File System Access API, which only Chromium has:
// https://wicg.github.io/file-system-access/, https://developer.mozilla.org/en-US/docs/Web/API/Window/showDirectoryPicker
// The last picked folder's handle is kept per device in IndexedDB (handles cannot go into localStorage) and the
// picker opens there. No permission is kept or checked between saves: each pick grants it (ADR-0003 §3).
const folderDatabase = 'kroiko';
const folderStore = 'folders';
const lastFolderKey = 'last';

// The last picked folder's handle, or null: a promise that never rejects, loaded ahead of the click.
let lastFolder = null;

// The browser can pick a folder to write into. Also starts loading the last folder, so the click need not wait for it.
export function canSaveToFolder() {
    const available = typeof window.showDirectoryPicker === 'function';
    if (available) {
        lastFolder ??= loadLastFolder();
    }

    return available;
}

// Opens the folder picker, for writing, at the last picked folder. Call it straight from the click: the picker needs
// the click's user activation, and the last folder is normally loaded already. Returns { outcome: 'picked', name,
// folder } (the FileSystemDirectoryHandle as a .NET IJSObjectReference), { outcome: 'cancelled' } when the operator
// closed the picker or did not allow writing (AbortError), or { outcome: 'blocked' } when the browser refused to open
// it (SecurityError, NotAllowedError; e.g. an Edge policy). Anything else is thrown.
export async function pickFolder() {
    let folder;
    try {
        const startIn = await (lastFolder ??= loadLastFolder());
        folder = await window.showDirectoryPicker(startIn ? { mode: 'readwrite', startIn } : { mode: 'readwrite' });
    } catch (error) {
        switch (error?.name) {
            case 'AbortError':
                return { outcome: 'cancelled' };
            case 'SecurityError':
            case 'NotAllowedError':
                return { outcome: 'blocked' };
            default:
                throw error;
        }
    }

    lastFolder = Promise.resolve(folder);
    rememberLastFolder(folder);
    return { outcome: 'picked', name: folder.name, folder: DotNet.createJSObjectReference(folder) };
}

// The name of every entry in the folder, files and subfolders alike: a file cannot take a subfolder's name either.
export async function listNames(folder) {
    const names = [];
    for await (const name of folder.keys()) {
        names.push(name);
    }

    return names;
}

// Writes one file into the folder: the bytes arrive as a DotNetStreamReference. The name is used as given; the app
// has already sanitised it (ADR-0003 §7) and made sure the folder does not have it (ADR-0003 §4).
export async function writeFile(folder, fileName, contentStreamReference) {
    const content = await contentStreamReference.arrayBuffer();
    const file = await folder.getFileHandle(fileName, { create: true });
    const writable = await file.createWritable();
    try {
        await writable.write(content);
        await writable.close();
    } catch (error) {
        await writable.abort().catch(() => { });
        throw error;
    }
}

function openFolderDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(folderDatabase, 1);
        request.onupgradeneeded = () => request.result.createObjectStore(folderStore);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

// Runs one request on the folder store and closes the database.
async function withFolderStore(mode, makeRequest) {
    const database = await openFolderDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const transaction = database.transaction(folderStore, mode);
            const request = makeRequest(transaction.objectStore(folderStore));
            transaction.oncomplete = () => resolve(request.result);
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error);
        });
    } finally {
        database.close();
    }
}

// The last picked folder, or null when there is none or storage fails: the picker then opens where the browser wants.
async function loadLastFolder() {
    try {
        return (await withFolderStore('readonly', store => store.get(lastFolderKey))) ?? null;
    } catch (error) {
        console.warn('The last folder could not be loaded.', error);
        return null;
    }
}

// Keeps the folder for the next pick; a failure only means the next picker opens elsewhere.
function rememberLastFolder(folder) {
    withFolderStore('readwrite', store => store.put(folder, lastFolderKey))
        .catch(error => console.warn('The folder could not be remembered.', error));
}
