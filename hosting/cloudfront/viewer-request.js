// The CloudFront Function on the viewer request of both distributions (runtime cloudfront-js-2.0), the only logic
// at the edge (ADR-0010, docs/implementation/07-hosting-and-go-live.md step 07a.1). Deploys publish this file
// unchanged to each environment's function.
//
// A release folder holds two trees of the same files: br/ (the published .br files, stored with
// Content-Encoding: br) and raw/ (the plain files, which CloudFront compresses itself where it can).
// - A path whose last segment has no file extension is an app route: it gets /index.html. A missing file with an
//   extension goes to S3 unchanged and is a 404.
// - A request that accepts br goes to br/, every other one to raw/.

function handler(event) {
    const request = event.request;
    const lastSegment = request.uri.substring(request.uri.lastIndexOf('/') + 1);
    const path = lastSegment.indexOf('.') === -1 ? '/index.html' : request.uri;
    const acceptEncoding = request.headers['accept-encoding'];

    request.uri = (acceptsBrotli(acceptEncoding ? acceptEncoding.value : '') ? '/br' : '/raw') + path;
    return request;
}

// "gzip, deflate, br" accepts br; "br;q=0" refuses it. A wildcard is not taken as br: raw/ is always safe.
function acceptsBrotli(header) {
    const codings = header.split(',');
    for (let i = 0; i < codings.length; i++) {
        const parameters = codings[i].split(';');
        if (parameters[0].trim().toLowerCase() !== 'br')
            continue;
        for (let j = 1; j < parameters.length; j++) {
            const parameter = parameters[j].trim().toLowerCase();
            if (parameter.indexOf('q=') === 0 && parseFloat(parameter.substring(2)) === 0)
                return false;
        }
        return true;
    }
    return false;
}
