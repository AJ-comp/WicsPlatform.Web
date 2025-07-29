// ──────────────────────────────────────────────
// speaker.js – 수신한 Base64 WebM/Opus 청크를 즉시 재생
//   • mic.js → C# → speaker.js 로 데이터 흐름
//   • MediaSource + SourceBuffer 스트리밍 방식
//   • mic.js 수정 없음
// ──────────────────────────────────────────────
console.log('[speaker.js] 모듈 로드됨');

const MIME_TYPE = 'audio/webm;codecs=opus';
let mediaSource = null;
let sourceBuffer = null;
let queue = [];          // 아직 append 못한 청크들

/* 초기화 : C#이 첫 호출 전에 반드시 init() 호출 */
export function init() {
    if (mediaSource) return;      // 이미 초기화됨

    mediaSource = new MediaSource();
    const audio = new Audio();
    audio.src = URL.createObjectURL(mediaSource);
    audio.autoplay = true;        // 자동 재생
    audio.volume = 0.25;        // 하울링 방지 (필요시 조절)
    audio.play().catch(e => console.warn('[speaker.js] 오토플레이 차단:', e));

    mediaSource.addEventListener('sourceopen', () => {
        sourceBuffer = mediaSource.addSourceBuffer(MIME_TYPE);
        sourceBuffer.mode = 'sequence';      // 순차 append
        sourceBuffer.addEventListener('updateend', flushQueue);
        console.log('[speaker.js] SourceBuffer 준비 완료');
    });
}

/* C# → JS : Base64 문자열 전달 */
export function feed(base64) {
    if (!sourceBuffer) {
        queue.push(base64);
        return;
    }
    if (sourceBuffer.updating || queue.length) {
        queue.push(base64);
        return;
    }
    appendBase64(base64);
}

/* 내부 큐 처리 */
function flushQueue() {
    if (queue.length && !sourceBuffer.updating) {
        appendBase64(queue.shift());
    }
}

/* Base64 → Uint8Array → SourceBuffer */
function appendBase64(base64) {
    try {
        const bin = atob(base64);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        sourceBuffer.appendBuffer(bytes);
    } catch (e) {
        console.error('[speaker.js] append 실패:', e);
    }
}
