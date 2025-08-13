// ──────────────────────────────────────────────────────────────
// Blazor WASM – 미디어 파일 스트리머 유틸리티 (50ms / Base64 전송)
// 마이크와 동일한 WebM/Opus 형식으로 인코딩하여 전송
// 2025-01-09 수정: WebM/Opus 인코딩 통일
// ──────────────────────────────────────────────────────────────
console.log('[mediastreamer.js] 모듈 로드됨');

let audioContext = null;
let audioElement = null;
let dotNetObj = null;
let isStreaming = false;
let currentPlaylist = [];
let currentIndex = 0;
let mediaRecorder = null;
let mediaStream = null;
let audioSource = null;
let streamDestination = null;
let chunks = [];

// MIME 타입 (mic.js와 동일)
const SUPPORTED_TYPES = [
    'audio/webm;codecs=opus',
    'audio/webm',
    'audio/ogg;codecs=opus',
    'audio/ogg'
];
const MIME_TYPE = SUPPORTED_TYPES.find(t => MediaRecorder.isTypeSupported(t)) || 'audio/webm';

let streamConfig = {
    sampleRate: 48000,
    channels: 2,
    timeslice: 50,  // 50ms 간격
    bitrate: 64000  // 64kbps (mic.js와 동일)
};

/* ───── 버퍼 → Base64 → C# (mic.js와 동일) ───── */
async function flushChunks() {
    if (!chunks.length || !dotNetObj) return;

    const blob = new Blob(chunks, { type: MIME_TYPE });
    chunks = [];
    const buffer = await blob.arrayBuffer();
    const bytes = new Uint8Array(buffer);

    // 32 KB 단위 Base64 인코딩
    const STEP = 32_768;
    let base64 = '';
    for (let i = 0; i < bytes.length; i += STEP) {
        base64 += btoa(String.fromCharCode.apply(null, bytes.slice(i, i + STEP)));
    }

    await dotNetObj.invokeMethodAsync('OnMediaAudioCaptured', base64);
}

/* ───── 외부 진입점: 미디어 스트리머 초기화 ───── */
export async function initializeMediaStreamer(dotNetRef, config = {}) {
    console.log('[mediastreamer.js] === 미디어 스트리머 초기화 (WebM/Opus) ===', config);
    console.log('[mediastreamer.js] 선택된 MIME 타입:', MIME_TYPE);

    dotNetObj = dotNetRef;
    if (!dotNetObj) {
        console.error('[mediastreamer.js] DotNetObjectReference가 null입니다.');
        return false;
    }

    // 오디오 설정 업데이트
    streamConfig = {
        sampleRate: config.sampleRate || 48000,
        channels: config.channels || 2,
        timeslice: config.timeslice || 50,
        bitrate: config.bitrate || 64000
    };

    try {
        // AudioContext 초기화
        if (!audioContext) {
            audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: streamConfig.sampleRate
            });
        }

        if (audioContext.state === 'suspended') {
            await audioContext.resume();
        }

        console.log('[mediastreamer.js] 오디오 컨텍스트 초기화 완료');
        console.log('[mediastreamer.js] 설정:', streamConfig);
        return true;

    } catch (error) {
        console.error('[mediastreamer.js] 초기화 실패:', error);
        return false;
    }
}

/* ───── 플레이리스트 로드 및 스트리밍 ───── */
export async function loadAndStreamMediaPlaylist(mediaUrls) {
    console.log('[mediastreamer.js] === 플레이리스트 로드 ===', mediaUrls);

    if (!mediaUrls || !mediaUrls.length) {
        console.warn('[mediastreamer.js] 빈 플레이리스트입니다.');
        return false;
    }

    currentPlaylist = mediaUrls;
    currentIndex = 0;

    return await playCurrentMedia();
}

/* ───── 현재 미디어 재생 (WebM/Opus 인코딩) ───── */
export async function playCurrentMedia() {
    if (!currentPlaylist.length || currentIndex >= currentPlaylist.length) {
        console.log('[mediastreamer.js] 플레이리스트 종료');
        await stopMediaStreaming();
        if (dotNetObj) {
            await dotNetObj.invokeMethodAsync('OnMediaPlaylistEnded');
        }
        return false;
    }

    const mediaUrl = currentPlaylist[currentIndex];
    console.log('[mediastreamer.js] 미디어 재생:', mediaUrl);

    try {
        // 기존 스트리밍 정리
        await cleanupCurrentStream();

        // 새 Audio 엘리먼트 생성
        audioElement = new Audio();
        audioElement.crossOrigin = "anonymous";
        audioElement.preload = "auto";

        // MediaStreamDestination 생성 (MediaRecorder용)
        streamDestination = audioContext.createMediaStreamDestination();

        // 채널 수 조정을 위한 ChannelMerger 생성
        const channelMerger = audioContext.createChannelMerger(streamConfig.channels);

        // 오디오 소스 생성
        audioSource = audioContext.createMediaElementSource(audioElement);

        // 오디오 체인 연결
        // audioSource → channelMerger → streamDestination
        if (streamConfig.channels === 1) {
            // 모노: 좌우 채널을 모노로 병합
            audioSource.connect(channelMerger, 0, 0);
        } else {
            // 스테레오: 그대로 연결
            audioSource.connect(channelMerger);
        }

        channelMerger.connect(streamDestination);

        // 스피커로도 출력 (선택사항)
        channelMerger.connect(audioContext.destination);

        // MediaStream 가져오기
        mediaStream = streamDestination.stream;

        // MediaRecorder 생성 (mic.js와 동일한 설정)
        try {
            mediaRecorder = new MediaRecorder(mediaStream, {
                mimeType: MIME_TYPE,
                audioBitsPerSecond: streamConfig.bitrate
            });
        } catch (err) {
            console.error('[mediastreamer.js] MediaRecorder 생성 실패:', err);
            return false;
        }

        // MediaRecorder 이벤트 바인딩 (mic.js와 동일)
        mediaRecorder.ondataavailable = e => {
            if (e.data?.size) {
                chunks.push(e.data);
                if (streamConfig.timeslice) {
                    flushChunks();
                }
            }
        };

        mediaRecorder.onstop = () => {
            flushChunks();
        };

        mediaRecorder.onerror = e => {
            console.error('[mediastreamer.js] MediaRecorder 오류:', e.error);
        };

        // Audio 엘리먼트 이벤트 리스너
        audioElement.onended = async () => {
            console.log('[mediastreamer.js] 현재 미디어 종료');

            // MediaRecorder 정지
            if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                mediaRecorder.stop();
            }

            currentIndex++;
            await playCurrentMedia();
        };

        audioElement.onerror = (error) => {
            console.error('[mediastreamer.js] 미디어 로드 오류:', error);
            currentIndex++;
            playCurrentMedia();
        };

        audioElement.oncanplaythrough = () => {
            console.log('[mediastreamer.js] 미디어 재생 준비 완료');

            // MediaRecorder 시작
            try {
                mediaRecorder.start(streamConfig.timeslice);
                console.log(`[mediastreamer.js] MediaRecorder 시작 – timeslice: ${streamConfig.timeslice} ms`);
            } catch (err) {
                console.error('[mediastreamer.js] MediaRecorder 시작 실패:', err);
            }

            // 오디오 재생 시작
            audioElement.play().catch(error => {
                console.error('[mediastreamer.js] 재생 시작 오류:', error);
            });
        };

        // 미디어 로드 시작
        audioElement.src = mediaUrl;
        audioElement.load();

        isStreaming = true;
        return true;

    } catch (error) {
        console.error('[mediastreamer.js] 미디어 로드 실패:', error);
        return false;
    }
}

/* ───── 현재 스트림 정리 ───── */
async function cleanupCurrentStream() {
    if (mediaRecorder && mediaRecorder.state !== 'inactive') {
        mediaRecorder.stop();
        mediaRecorder = null;
    }

    if (audioElement) {
        audioElement.pause();
        audioElement.src = '';
        audioElement = null;
    }

    if (audioSource) {
        audioSource.disconnect();
        audioSource = null;
    }

    if (streamDestination) {
        streamDestination.disconnect();
        streamDestination = null;
    }

    if (mediaStream) {
        mediaStream.getTracks().forEach(track => track.stop());
        mediaStream = null;
    }

    chunks = [];
}

/* ───── 스트리밍 중지 ───── */
export async function stopMediaStreaming() {
    console.log('[mediastreamer.js] === 미디어 스트리밍 중지 ===');

    isStreaming = false;

    await cleanupCurrentStream();

    currentPlaylist = [];
    currentIndex = 0;
}

/* ───── 일시정지/재개 ───── */
export async function pauseMediaStreaming() {
    if (audioElement && isStreaming) {
        audioElement.pause();

        if (mediaRecorder && mediaRecorder.state === 'recording') {
            mediaRecorder.pause();
        }

        console.log('[mediastreamer.js] 미디어 일시정지');
    }
}

export async function resumeMediaStreaming() {
    if (audioElement && isStreaming) {
        try {
            await audioElement.play();

            if (mediaRecorder && mediaRecorder.state === 'paused') {
                mediaRecorder.resume();
            }

            console.log('[mediastreamer.js] 미디어 재생 재개');
        } catch (error) {
            console.error('[mediastreamer.js] 재생 재개 오류:', error);
        }
    }
}

/* ───── 상태 조회 ───── */
export function isMediaStreaming() {
    return isStreaming;
}

export function getCurrentMediaInfo() {
    return {
        isStreaming: isStreaming,
        currentIndex: currentIndex,
        totalCount: currentPlaylist.length,
        currentUrl: currentPlaylist[currentIndex] || null,
        duration: audioElement ? audioElement.duration : 0,
        currentTime: audioElement ? audioElement.currentTime : 0
    };
}

/* ───── 디버깅 정보 ───── */
export function getDebugInfo() {
    return {
        audioContext: audioContext ? audioContext.state : 'null',
        isStreaming: isStreaming,
        mediaRecorder: mediaRecorder ? mediaRecorder.state : 'null',
        currentIndex: currentIndex,
        playlistLength: currentPlaylist.length,
        config: streamConfig,
        mimeType: MIME_TYPE,
        chunks: chunks.length,
        dotNetObj: !!dotNetObj
    };
}