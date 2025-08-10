// ──────────────────────────────────────────────────────────────
// Blazor WASM – 마이크 캡처 유틸리티 (50 ms / Base64 전송)
// 2025‑08‑08 수정 - 사용자 정의 샘플레이트 지원
//   • DEFAULT_TIMESLICE = 50 ms
//   • audioBitsPerSecond = 64 kbps (음성 전용)
//   • 사용자 정의 샘플레이트 및 오디오 제약 조건 지원
// ──────────────────────────────────────────────────────────────
console.log('[mic.js] 모듈 로드됨');

let mediaStream = null;   // MediaStream
let mediaRecorder = null;   // MediaRecorder
let chunks = [];     // Blob 파편
let dotNetObj = null;   // DotNetObjectReference

/* ───── MIME 타입 선택 ───── */
const SUPPORTED_TYPES = [
    'audio/webm;codecs=opus',
    'audio/webm',
    'audio/ogg;codecs=opus',
    'audio/ogg'
];
const MIME_TYPE = SUPPORTED_TYPES.find(t => MediaRecorder.isTypeSupported(t)) || 'audio/webm';
console.log('[mic.js] 선택된 MIME 타입:', MIME_TYPE);

/* ───── 환경 확인 ───── */
if (!navigator.mediaDevices?.getUserMedia) {
    console.error('[mic.js] getUserMedia API를 지원하지 않습니다.');
}
if (typeof MediaRecorder === 'undefined') {
    console.error('[mic.js] MediaRecorder API를 지원하지 않습니다.');
}

/* ───── 권한 상태 확인 ───── */
async function getMicPermissionState() {
    if (!navigator.permissions) return 'unsupported';
    try {
        const st = await navigator.permissions.query({ name: 'microphone' });
        return st.state;
    } catch {
        return 'unsupported';
    }
}

async function handleDenied() {
    const goHelp = confirm(
        '마이크 접근이 차단되었습니다.\n' +
        '권한을 다시 허용하는 방법을 보시겠습니까?'
    );
    if (goHelp && dotNetObj) {
        await dotNetObj.invokeMethodAsync('ShowMicHelp');
    }
    return false;
}

/* ───── 버퍼 → Base64 → C# ───── */
async function flushChunks() {
    if (!chunks.length || !dotNetObj) return;

    const blob = new Blob(chunks, { type: MIME_TYPE });
    chunks = [];                         // 버퍼 초기화
    const buffer = await blob.arrayBuffer();
    const bytes = new Uint8Array(buffer);

    // 32 KB 단위 Base64 인코딩
    const STEP = 32_768;
    let base64 = '';
    for (let i = 0; i < bytes.length; i += STEP) {
        base64 += btoa(String.fromCharCode.apply(null, bytes.slice(i, i + STEP)));
    }

    await dotNetObj.invokeMethodAsync('OnAudioCaptured', base64);
}

/* ───── 외부 진입점 : 녹음 시작 ───── */
export async function start(dotNetRef, cfg = {}) {
    console.log('[mic.js] === 녹음 시작 ===', cfg);
    dotNetObj = dotNetRef;
    if (!dotNetObj) {
        console.error('[mic.js] DotNetObjectReference가 null입니다.');
        return false;
    }

    /* 0) 권한 사전 체크 */
    if (await getMicPermissionState() === 'denied') return handleDenied();

    /* 1) 오디오 제약 조건 설정 */
    const audioConstraints = {
        audio: {
            // ✅ 사용자 정의 샘플레이트 (기본값: 44100)
            sampleRate: cfg.sampleRate || 44100,
            
            // ✅ 채널 수 (기본값: 2 - 스테레오)
            channelCount: cfg.channels || 2,
            
            // ✅ 오디오 품질 개선 옵션
            echoCancellation: cfg.echoCancellation !== false, // 기본값: true
            noiseSuppression: cfg.noiseSuppression !== false, // 기본값: true
            autoGainControl: cfg.autoGainControl !== false,   // 기본값: true
            
            // ✅ 추가 옵션 (브라우저 지원 시)
            ...(cfg.latency && { latency: cfg.latency }), // 지연시간 (초)
            ...(cfg.sampleSize && { sampleSize: cfg.sampleSize }) // 비트 뎁스
        }
    };

    console.log('[mic.js] 오디오 제약 조건:', audioConstraints);

    /* 2) getUserMedia */
    try {
        mediaStream = await navigator.mediaDevices.getUserMedia(audioConstraints);
        
        // ✅ 실제 적용된 오디오 설정 확인
        const audioTrack = mediaStream.getAudioTracks()[0];
        const settings = audioTrack.getSettings();
        const capabilities = audioTrack.getCapabilities();
        
        console.log('[mic.js] 실제 오디오 설정:', settings);
        console.log('[mic.js] 오디오 능력:', capabilities);
        
        // ✅ C#으로 실제 오디오 설정 전달
        await dotNetObj.invokeMethodAsync('OnAudioConfigurationDetected', {
            sampleRate: settings.sampleRate || cfg.sampleRate || 44100,
            channelCount: settings.channelCount || cfg.channels || 2,
            echoCancellation: settings.echoCancellation,
            noiseSuppression: settings.noiseSuppression,
            autoGainControl: settings.autoGainControl,
            deviceId: settings.deviceId,
            groupId: settings.groupId
        });
        
    } catch (err) {
        if (err.name === 'NotAllowedError' || err.name === 'SecurityError') {
            return handleDenied();
        }
        if (err.name === 'OverconstrainedError') {
            console.warn('[mic.js] 요청한 제약조건을 만족할 수 없습니다:', err.constraint);
            // 기본 설정으로 재시도
            try {
                mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
                const audioTrack = mediaStream.getAudioTracks()[0];
                const settings = audioTrack.getSettings();
                
                await dotNetObj.invokeMethodAsync('OnAudioConfigurationDetected', {
                    sampleRate: settings.sampleRate || 44100,
                    channelCount: settings.channelCount || 2,
                    echoCancellation: settings.echoCancellation,
                    noiseSuppression: settings.noiseSuppression,
                    autoGainControl: settings.autoGainControl
                });
            } catch (fallbackErr) {
                console.error('[mic.js] 기본 설정으로도 실패:', fallbackErr);
                return false;
            }
        } else {
            console.error('[mic.js] getUserMedia 오류:', err);
            return false;
        }
    }

    /* 3) MediaRecorder 생성 */
    try {
        mediaRecorder = new MediaRecorder(mediaStream, {
            mimeType: MIME_TYPE,
            audioBitsPerSecond: cfg.bitrate || 64_000   // ===== 기본 64 kbps =====
        });
    } catch (err) {
        console.error('[mic.js] MediaRecorder 생성 실패:', err);
        return false;
    }

    /* 4) 이벤트 바인딩 */
    mediaRecorder.ondataavailable = e => {
        if (e.data?.size) {
            chunks.push(e.data);
            if (cfg.timeslice) flushChunks();
        }
    };
    mediaRecorder.onstop = () => flushChunks();
    mediaRecorder.onerror = e => console.error('[mic.js] MediaRecorder 오류:', e.error);

    /* 5) 녹음 시작 */
    const DEFAULT_TIMESLICE = 50;          // ===== 50 ms =====
    if (!cfg.timeslice) cfg.timeslice = DEFAULT_TIMESLICE;

    try {
        mediaRecorder.start(cfg.timeslice);
        console.log(`[mic.js] MediaRecorder 시작 – timeslice: ${cfg.timeslice} ms`);
        return true;
    } catch (err) {
        console.error('[mic.js] 녹음 시작 실패:', err);
        return false;
    }
}

/* ───── 유틸 ───── */
export function stop() {
    if (mediaRecorder?.state !== 'inactive') mediaRecorder.stop();
    mediaStream?.getTracks().forEach(t => t.stop());

    mediaStream = mediaRecorder = null;
    chunks = [];
}

export function isRecording() {
    return mediaRecorder?.state === 'recording';
}

export function getDebugInfo() {
    return {
        mediaStream: mediaStream ? 'Active' : 'Null',
        mediaRecorder: mediaRecorder ? mediaRecorder.state : 'Null',
        chunks: chunks.length,
        mimeType: MIME_TYPE,
        dotNetObj: !!dotNetObj
    };
}

/* ───── 오디오 디바이스 목록 조회 ───── */
export async function getAudioDevices() {
    try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        return devices
            .filter(device => device.kind === 'audioinput')
            .map(device => ({
                deviceId: device.deviceId,
                label: device.label || `마이크 ${device.deviceId.substring(0, 8)}`,
                groupId: device.groupId
            }));
    } catch (err) {
        console.error('[mic.js] 오디오 디바이스 조회 실패:', err);
        return [];
    }
}
