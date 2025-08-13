// ──────────────────────────────────────────────────────────────
// Blazor WASM – 미디어 파일 스트리머 유틸리티 (50ms / Base64 전송)
// 마이크와 동일한 WebM/Opus 형식으로 인코딩하여 전송
// 2025-01-09 수정: 로컬 재생 옵션 추가
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
    bitrate: 64000,  // 64kbps (mic.js와 동일)
    localPlayback: false  // 로컬 재생 옵션 (기본값: false)
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
        bitrate: config.bitrate || 64000,
        localPlayback: config.localPlayback !== undefined ? config.localPlayback : false  // 로컬 재생 옵션
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
        console.log('[mediastreamer.js] 로컬 재생:', streamConfig.localPlayback ? '활성화' : '비활성화');
        return true;

    } catch (error) {
        console.error('[mediastreamer.js] 초기화 실패:', error);
        return false;
    }
}

/* ───── 플레이리스트 로드 및 스트리밍 ───── */
export async function loadAndStreamMediaPlaylist(mediaUrls) {
    console.log('[mediastreamer.js] === 플레이리스트 로드 ===');
    console.log('[mediastreamer.js] 받은 데이터:', mediaUrls);
    console.log('[mediastreamer.js] 데이터 타입:', typeof mediaUrls);
    console.log('[mediastreamer.js] 배열 여부:', Array.isArray(mediaUrls));

    // 배열이 아니면 배열로 변환
    if (!Array.isArray(mediaUrls)) {
        console.warn('[mediastreamer.js] mediaUrls가 배열이 아닙니다. 변환 시도...');
        if (typeof mediaUrls === 'object' && mediaUrls !== null) {
            // 객체를 배열로 변환
            mediaUrls = Object.values(mediaUrls);
        } else {
            console.error('[mediastreamer.js] mediaUrls를 배열로 변환할 수 없습니다.');
            return false;
        }
    }

    if (!mediaUrls || !mediaUrls.length) {
        console.warn('[mediastreamer.js] 빈 플레이리스트입니다.');
        return false;
    }

    // URL 목록 출력
    mediaUrls.forEach((url, index) => {
        console.log(`[mediastreamer.js] URL ${index + 1}: ${url}`);
    });

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

        // 새 Audio 엘리먼트 생성 (mediaplayer.js와 동일한 방식)
        audioElement = new Audio();
        audioElement.crossOrigin = "anonymous";
        audioElement.preload = "auto";

        // 미디어 로드 시작 (바로 src 설정)
        audioElement.src = mediaUrl;

        // MediaStreamDestination 생성 (MediaRecorder용)
        streamDestination = audioContext.createMediaStreamDestination();

        // 채널 수 조정을 위한 ChannelMerger 생성
        const channelMerger = audioContext.createChannelMerger(streamConfig.channels);

        // 오디오 소스 생성
        audioSource = audioContext.createMediaElementSource(audioElement);

        // 오디오 체인 연결
        if (streamConfig.channels === 1) {
            audioSource.connect(channelMerger, 0, 0);
        } else {
            audioSource.connect(channelMerger);
        }

        // 스트리밍 대상에 연결
        channelMerger.connect(streamDestination);

        // 로컬 재생 옵션이 true일 때만 스피커로 출력
        if (streamConfig.localPlayback) {
            channelMerger.connect(audioContext.destination);
            console.log('[mediastreamer.js] 로컬 재생 활성화됨');
        } else {
            console.log('[mediastreamer.js] 로컬 재생 비활성화됨');
        }

        // MediaStream 가져오기
        mediaStream = streamDestination.stream;

        // MediaRecorder 생성
        mediaRecorder = new MediaRecorder(mediaStream, {
            mimeType: MIME_TYPE,
            audioBitsPerSecond: streamConfig.bitrate
        });

        // MediaRecorder 이벤트 바인딩
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

        // Audio 엘리먼트 이벤트 리스너
        audioElement.oncanplaythrough = () => {
            console.log('[mediastreamer.js] 미디어 재생 준비 완료');

            // MediaRecorder 시작
            mediaRecorder.start(streamConfig.timeslice);
            console.log(`[mediastreamer.js] MediaRecorder 시작 – timeslice: ${streamConfig.timeslice} ms`);

            // 오디오 재생 시작
            audioElement.play().catch(error => {
                console.error('[mediastreamer.js] 재생 시작 오류:', error);
                // 재생 오류 시 다음 파일로
                currentIndex++;
                playCurrentMedia();
            });
        };

        audioElement.onended = async () => {
            console.log('[mediastreamer.js] 현재 미디어 종료');
            if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                mediaRecorder.stop();
            }
            currentIndex++;
            await playCurrentMedia();
        };

        audioElement.onerror = (error) => {
            console.error('[mediastreamer.js] 미디어 로드 오류:', mediaUrl, error);
            // 오류 시 다음 파일로
            currentIndex++;
            if (currentIndex < currentPlaylist.length) {
                playCurrentMedia();
            } else {
                console.log('[mediastreamer.js] 모든 미디어 재생 실패');
                if (dotNetObj) {
                    dotNetObj.invokeMethodAsync('OnMediaPlaylistEnded');
                }
            }
        };

        // load() 호출로 미디어 로드 시작
        audioElement.load();

        isStreaming = true;
        return true;

    } catch (error) {
        console.error('[mediastreamer.js] 미디어 처리 실패:', error);
        currentIndex++;
        return await playCurrentMedia();
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

/* ───── 로컬 재생 설정 변경 ───── */
export function setLocalPlayback(enabled) {
    streamConfig.localPlayback = enabled;
    console.log(`[mediastreamer.js] 로컬 재생 ${enabled ? '활성화' : '비활성화'}`);

    // 현재 재생 중이면 설정 즉시 적용
    if (isStreaming && audioSource) {
        try {
            if (enabled) {
                // 스피커 연결
                audioSource.connect(audioContext.destination);
            } else {
                // 스피커 연결 해제
                audioSource.disconnect(audioContext.destination);
            }
        } catch (error) {
            console.error('[mediastreamer.js] 로컬 재생 설정 변경 오류:', error);
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
        currentTime: audioElement ? audioElement.currentTime : 0,
        localPlayback: streamConfig.localPlayback
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
        dotNetObj: !!dotNetObj,
        localPlayback: streamConfig.localPlayback
    };
}