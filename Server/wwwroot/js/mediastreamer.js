// Server/wwwroot/js/mediastreamer.js
// 파일별로 다른 콜백 메서드를 지정할 수 있는 버전

console.log('[mediastreamer.js] 모듈 로드됨 (콜백 라우팅 지원)');

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

// ★ 콜백 메서드 이름 저장
let currentCallbackMethod = 'OnMediaAudioCaptured';  // 기본값
let playlistCallbackMethods = [];  // 각 파일별 콜백 메서드 배열

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
    timeslice: 50,
    bitrate: 64000,
    localPlayback: false
};

/* ───── 버퍼 → Base64 → C# (수정된 버전) ───── */
async function flushChunks() {
    if (!chunks.length || !dotNetObj) return;

    const blob = new Blob(chunks, { type: MIME_TYPE });
    chunks = [];
    const buffer = await blob.arrayBuffer();
    const bytes = new Uint8Array(buffer);

    const STEP = 32_768;
    let base64 = '';
    for (let i = 0; i < bytes.length; i += STEP) {
        base64 += btoa(String.fromCharCode.apply(null, bytes.slice(i, i + STEP)));
    }

    // ★ 현재 재생 중인 파일에 해당하는 콜백 메서드 사용
    const methodName = playlistCallbackMethods[currentIndex] || currentCallbackMethod;

    console.log(`[mediastreamer.js] Calling ${methodName} for file ${currentIndex + 1}/${currentPlaylist.length}`);
    await dotNetObj.invokeMethodAsync(methodName, base64);
}

/* ───── 초기화 ───── */
export async function initializeMediaStreamer(dotNetRef, config = {}) {
    console.log('[mediastreamer.js] === 미디어 스트리머 초기화 ===', config);

    dotNetObj = dotNetRef;
    if (!dotNetObj) {
        console.error('[mediastreamer.js] DotNetObjectReference가 null입니다.');
        return false;
    }

    streamConfig = {
        sampleRate: config.sampleRate || 48000,
        channels: config.channels || 2,
        timeslice: config.timeslice || 50,
        bitrate: config.bitrate || 64000,
        localPlayback: config.localPlayback !== undefined ? config.localPlayback : false,
        // ★ 기본 콜백 메서드 설정 가능
        defaultCallback: config.defaultCallback || 'OnMediaAudioCaptured'
    };

    currentCallbackMethod = streamConfig.defaultCallback;

    try {
        if (!audioContext) {
            audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: streamConfig.sampleRate
            });
        }

        if (audioContext.state === 'suspended') {
            await audioContext.resume();
        }

        console.log('[mediastreamer.js] 초기화 완료');
        return true;

    } catch (error) {
        console.error('[mediastreamer.js] 초기화 실패:', error);
        return false;
    }
}

/* ───── 플레이리스트 로드 (기본 버전 - 모든 파일 동일 콜백) ───── */
export async function loadAndStreamMediaPlaylist(mediaUrls) {
    console.log('[mediastreamer.js] === 플레이리스트 로드 (기본 콜백) ===');

    if (!Array.isArray(mediaUrls)) {
        if (typeof mediaUrls === 'object' && mediaUrls !== null) {
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

    currentPlaylist = mediaUrls;
    currentIndex = 0;

    // ★ 모든 파일에 기본 콜백 메서드 설정
    playlistCallbackMethods = new Array(mediaUrls.length).fill(currentCallbackMethod);

    return await playCurrentMedia();
}

/* ───── ★ 플레이리스트 로드 (고급 버전 - 파일별 콜백 지정) ───── */
export async function loadAndStreamMediaPlaylistWithCallbacks(playlistData) {
    console.log('[mediastreamer.js] === 플레이리스트 로드 (파일별 콜백) ===');

    /*
    playlistData 형식:
    [
        { url: '/Uploads/media1.mp3', callback: 'OnMediaAudioCaptured' },
        { url: '/Uploads/tts1.wav', callback: 'OnTtsAudioCaptured' },
        { url: '/Uploads/media2.mp3', callback: 'OnMediaAudioCaptured' }
    ]
    또는
    {
        urls: ['/Uploads/media1.mp3', '/Uploads/tts1.wav'],
        callbacks: ['OnMediaAudioCaptured', 'OnTtsAudioCaptured']
    }
    */

    let urls = [];
    let callbacks = [];

    // 입력 형식 파싱
    if (Array.isArray(playlistData)) {
        // 배열 형식
        if (playlistData.length > 0) {
            if (typeof playlistData[0] === 'string') {
                // 단순 URL 배열
                urls = playlistData;
                callbacks = new Array(urls.length).fill(currentCallbackMethod);
            } else if (typeof playlistData[0] === 'object') {
                // 객체 배열
                urls = playlistData.map(item => item.url || item.path || item);
                callbacks = playlistData.map(item => item.callback || item.method || currentCallbackMethod);
            }
        }
    } else if (typeof playlistData === 'object' && playlistData !== null) {
        // 객체 형식
        urls = playlistData.urls || playlistData.paths || [];
        callbacks = playlistData.callbacks || playlistData.methods || [];

        // callbacks 배열이 짧으면 마지막 값으로 채움
        while (callbacks.length < urls.length) {
            callbacks.push(callbacks[callbacks.length - 1] || currentCallbackMethod);
        }
    }

    if (!urls.length) {
        console.error('[mediastreamer.js] 유효한 URL이 없습니다.');
        return false;
    }

    console.log('[mediastreamer.js] 플레이리스트 구성:');
    urls.forEach((url, index) => {
        console.log(`  ${index + 1}. ${url} → ${callbacks[index]}`);
    });

    currentPlaylist = urls;
    playlistCallbackMethods = callbacks;
    currentIndex = 0;

    return await playCurrentMedia();
}

/* ───── ★ 단일 미디어 스트리밍 (콜백 지정) ───── */
export async function streamSingleMedia(url, callbackMethod) {
    console.log(`[mediastreamer.js] 단일 미디어 스트리밍: ${url} → ${callbackMethod || currentCallbackMethod}`);

    currentPlaylist = [url];
    playlistCallbackMethods = [callbackMethod || currentCallbackMethod];
    currentIndex = 0;

    return await playCurrentMedia();
}

/* ───── ★ TTS 전용 스트리밍 헬퍼 ───── */
export async function loadAndStreamTtsPlaylist(ttsUrls) {
    console.log('[mediastreamer.js] === TTS 플레이리스트 로드 ===');

    if (!Array.isArray(ttsUrls)) {
        ttsUrls = [ttsUrls];
    }

    // 모든 TTS 파일에 OnTtsAudioCaptured 콜백 설정
    const playlistData = ttsUrls.map(url => ({
        url: url,
        callback: 'OnTtsAudioCaptured'
    }));

    return await loadAndStreamMediaPlaylistWithCallbacks(playlistData);
}

/* ───── ★ 혼합 플레이리스트 (미디어 + TTS) ───── */
export async function loadAndStreamMixedPlaylist(mixedData) {
    console.log('[mediastreamer.js] === 혼합 플레이리스트 로드 ===');

    /*
    mixedData 예시:
    [
        { url: '/Uploads/intro.mp3', type: 'media' },
        { url: '/Uploads/tts_announcement.wav', type: 'tts' },
        { url: '/Uploads/bgm.mp3', type: 'media' },
        { url: '/Uploads/tts_closing.wav', type: 'tts' }
    ]
    */

    const playlistData = mixedData.map(item => ({
        url: item.url,
        callback: item.type === 'tts' ? 'OnTtsAudioCaptured' : 'OnMediaAudioCaptured'
    }));

    return await loadAndStreamMediaPlaylistWithCallbacks(playlistData);
}

/* ───── 현재 미디어 재생 (기존 코드와 동일) ───── */
async function playCurrentMedia() {
    if (!currentPlaylist.length || currentIndex >= currentPlaylist.length) {
        console.log('[mediastreamer.js] 플레이리스트 종료');
        await stopMediaStreaming();

        // ★ 현재 콜백에 따라 다른 종료 메서드 호출
        if (dotNetObj) {
            const currentCallback = playlistCallbackMethods[currentIndex - 1] || currentCallbackMethod;
            if (currentCallback === 'OnTtsAudioCaptured') {
                await dotNetObj.invokeMethodAsync('OnTtsPlaylistEnded');
            } else {
                await dotNetObj.invokeMethodAsync('OnMediaPlaylistEnded');
            }
        }
        return false;
    }

    const mediaUrl = currentPlaylist[currentIndex];
    const callbackMethod = playlistCallbackMethods[currentIndex] || currentCallbackMethod;

    console.log(`[mediastreamer.js] 재생 ${currentIndex + 1}/${currentPlaylist.length}: ${mediaUrl}`);
    console.log(`[mediastreamer.js] 콜백 메서드: ${callbackMethod}`);

    try {
        await cleanupCurrentStream();

        audioElement = new Audio();
        audioElement.crossOrigin = "anonymous";
        audioElement.preload = "auto";
        audioElement.src = mediaUrl;

        streamDestination = audioContext.createMediaStreamDestination();
        const channelMerger = audioContext.createChannelMerger(streamConfig.channels);

        audioSource = audioContext.createMediaElementSource(audioElement);

        if (streamConfig.channels === 1) {
            audioSource.connect(channelMerger, 0, 0);
        } else {
            audioSource.connect(channelMerger);
        }

        channelMerger.connect(streamDestination);

        if (streamConfig.localPlayback) {
            channelMerger.connect(audioContext.destination);
        }

        mediaStream = streamDestination.stream;

        mediaRecorder = new MediaRecorder(mediaStream, {
            mimeType: MIME_TYPE,
            audioBitsPerSecond: streamConfig.bitrate
        });

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

        audioElement.oncanplaythrough = () => {
            console.log('[mediastreamer.js] 미디어 재생 준비 완료');
            mediaRecorder.start(streamConfig.timeslice);
            audioElement.play().catch(error => {
                console.error('[mediastreamer.js] 재생 시작 오류:', error);
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
            currentIndex++;
            if (currentIndex < currentPlaylist.length) {
                playCurrentMedia();
            } else {
                console.log('[mediastreamer.js] 모든 미디어 재생 실패');
                if (dotNetObj) {
                    const callback = playlistCallbackMethods[currentIndex - 1] || currentCallbackMethod;
                    if (callback === 'OnTtsAudioCaptured') {
                        dotNetObj.invokeMethodAsync('OnTtsPlaylistEnded');
                    } else {
                        dotNetObj.invokeMethodAsync('OnMediaPlaylistEnded');
                    }
                }
            }
        };

        audioElement.load();
        isStreaming = true;
        return true;

    } catch (error) {
        console.error('[mediastreamer.js] 미디어 처리 실패:', error);
        currentIndex++;
        return await playCurrentMedia();
    }
}

/* ───── 나머지 함수들은 기존과 동일 ───── */
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

export async function stopMediaStreaming() {
    console.log('[mediastreamer.js] === 미디어 스트리밍 중지 ===');
    isStreaming = false;
    await cleanupCurrentStream();
    currentPlaylist = [];
    playlistCallbackMethods = [];
    currentIndex = 0;
}

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

export function getDebugInfo() {
    return {
        audioContext: audioContext ? audioContext.state : 'null',
        isStreaming: isStreaming,
        mediaRecorder: mediaRecorder ? mediaRecorder.state : 'null',
        currentIndex: currentIndex,
        playlistLength: currentPlaylist.length,
        currentCallbacks: playlistCallbackMethods,
        config: streamConfig,
        mimeType: MIME_TYPE,
        chunks: chunks.length,
        dotNetObj: !!dotNetObj
    };
}