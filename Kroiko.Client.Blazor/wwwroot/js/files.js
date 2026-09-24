// Saving the generated order files (ADR-0003). Phase 04: downloads; phase 05 adds the folder picker here.

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
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), blobUrlLifetimeMs);
}
