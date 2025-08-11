// ──────────────────────────────────────────────────────────────
// Blazor WASM – 미디어 파일 스트리머 유틸리티 (50ms / Base64 전송)
// 마이크와 동일한 방식으로 미디어 파일을 실시간 스트리밍
// ──────────────────────────────────────────────────────────────
console.log('[mediastreamer.js] 모듈 로드됨');

let audioContext = null;
let audioSource = null;
let scriptProcessor = null;
let audioElement = null;
let dotNetObj = null;
let isStreaming = false;
let currentPlaylist = [];
let currentIndex = 0;
let streamConfig = {
    sampleRate: 48000,
    channels: 2,
    timeslice: 50  // 50ms 간격
};

/* ───── 오디오 형식 변환 유틸리티 ───── */
function convertToPCM16(audioBuffer) {
    const channels = audioBuffer.numberOfChannels;
    const sampleRate = audioBuffer.sampleRate;
    const length = audioBuffer.length;
    
    // 16-bit PCM 데이터 생성
    const pcmData = new Int16Array(length * channels);
    
    for (let channel = 0; channel < channels; channel++) {
        const channelData = audioBuffer.getChannelData(channel);
        for (let i = 0; i < length; i++) {
            const sample = Math.max(-1, Math.min(1, channelData[i]));
            pcmData[i * channels + channel] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
        }
    }
    
    return new Uint8Array(pcmData.buffer);
}

function resampleAudioBuffer(sourceBuffer, targetSampleRate) {
    if (sourceBuffer.sampleRate === targetSampleRate) {
        return sourceBuffer;
    }
    
    const ratio = sourceBuffer.sampleRate / targetSampleRate;
    const newLength = Math.round(sourceBuffer.length / ratio);
    const newBuffer = audioContext.createBuffer(
        sourceBuffer.numberOfChannels, 
        newLength, 
        targetSampleRate
    );
    
    for (let channel = 0; channel < sourceBuffer.numberOfChannels; channel++) {
        const oldData = sourceBuffer.getChannelData(channel);
        const newData = newBuffer.getChannelData(channel);
        
        for (let i = 0; i < newLength; i++) {
            const oldIndex = i * ratio;
            const oldIndexFloor = Math.floor(oldIndex);
            const oldIndexCeil = Math.min(Math.ceil(oldIndex), oldData.length - 1);
            const fraction = oldIndex - oldIndexFloor;
            
            newData[i] = oldData[oldIndexFloor] * (1 - fraction) + 
                        oldData[oldIndexCeil] * fraction;
        }
    }
    
    return newBuffer;
}

function convertChannels(sourceBuffer, targetChannels) {
    if (sourceBuffer.numberOfChannels === targetChannels) {
        return sourceBuffer;
    }
    
    const newBuffer = audioContext.createBuffer(
        targetChannels,
        sourceBuffer.length,
        sourceBuffer.sampleRate
    );
    
    if (sourceBuffer.numberOfChannels === 2 && targetChannels === 1) {
        // 스테레오 → 모노: 좌우 채널 평균
        const leftChannel = sourceBuffer.getChannelData(0);
        const rightChannel = sourceBuffer.getChannelData(1);
        const monoChannel = newBuffer.getChannelData(0);
        
        for (let i = 0; i < sourceBuffer.length; i++) {
            monoChannel[i] = (leftChannel[i] + rightChannel[i]) * 0.5;
        }
    } else if (sourceBuffer.numberOfChannels === 1 && targetChannels === 2) {
        // 모노 → 스테레오: 모노 채널 복제
        const monoChannel = sourceBuffer.getChannelData(0);
        const leftChannel = newBuffer.getChannelData(0);
        const rightChannel = newBuffer.getChannelData(1);
        
        for (let i = 0; i < sourceBuffer.length; i++) {
            leftChannel[i] = rightChannel[i] = monoChannel[i];
        }
    }
    
    return newBuffer;
}

/* ───── 오디오 처리 및 전송 ───── */
function processAudioData(event) {
    if (!isStreaming || !dotNetObj) return;
    
    try {
        const inputBuffer = event.inputBuffer;
        
        // 1. 리샘플링
        const resampledBuffer = resampleAudioBuffer(inputBuffer, streamConfig.sampleRate);
        
        // 2. 채널 변환
        const channelConvertedBuffer = convertChannels(resampledBuffer, streamConfig.channels);
        
        // 3. 16-bit PCM으로 변환
        const pcmData = convertToPCM16(channelConvertedBuffer);
        
        // 4. Base64 인코딩
        const base64Data = btoa(String.fromCharCode.apply(null, pcmData));
        
        // 5. C#으로 전송
        dotNetObj.invokeMethodAsync('OnMediaAudioCaptured', base64Data);
        
    } catch (error) {
        console.error('[mediastreamer.js] 오디오 처리 오류:', error);
    }
}

/* ───── 외부 진입점: 미디어 스트리밍 시작 ───── */
export async function initializeMediaStreamer(dotNetRef, config = {}) {
    console.log('[mediastreamer.js] === 미디어 스트리머 초기화 ===', config);
    
    dotNetObj = dotNetRef;
    if (!dotNetObj) {
        console.error('[mediastreamer.js] DotNetObjectReference가 null입니다.');
        return false;
    }
    
    // 오디오 설정 업데이트
    streamConfig = {
        sampleRate: config.sampleRate || 48000,
        channels: config.channels || 2,
        timeslice: config.timeslice || 50
    };
    
    try {
        // AudioContext 초기화
        if (!audioContext) {
            audioContext = new (window.AudioContext || window.webkitAudioContext)();
        }
        
        if (audioContext.state === 'suspended') {
            await audioContext.resume();
        }
        
        console.log('[mediastreamer.js] 오디오 컨텍스트 초기화 완료');
        return true;
        
    } catch (error) {
        console.error('[mediastreamer.js] 초기화 실패:', error);
        return false;
    }
}

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
        await stopMediaStreaming();
        
        // 새 Audio 엘리먼트 생성
        audioElement = new Audio();
        audioElement.crossOrigin = "anonymous";
        audioElement.preload = "auto";
        
        // 오디오 소스 생성
        audioSource = audioContext.createMediaElementSource(audioElement);
        
        // ScriptProcessor 생성 (실시간 처리용)
        const bufferSize = Math.floor(streamConfig.sampleRate * streamConfig.timeslice / 1000);
        scriptProcessor = audioContext.createScriptProcessor(bufferSize, streamConfig.channels, streamConfig.channels);
        scriptProcessor.onaudioprocess = processAudioData;
        
        // 오디오 체인 연결
        audioSource.connect(scriptProcessor);
        scriptProcessor.connect(audioContext.destination);
        
        // 이벤트 리스너 설정
        audioElement.onended = async () => {
            console.log('[mediastreamer.js] 현재 미디어 종료');
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

export async function stopMediaStreaming() {
    console.log('[mediastreamer.js] === 미디어 스트리밍 중지 ===');
    
    isStreaming = false;
    
    if (audioElement) {
        audioElement.pause();
        audioElement.src = '';
        audioElement = null;
    }
    
    if (scriptProcessor) {
        scriptProcessor.disconnect();
        scriptProcessor = null;
    }
    
    if (audioSource) {
        audioSource.disconnect();
        audioSource = null;
    }
    
    currentPlaylist = [];
    currentIndex = 0;
}

export async function pauseMediaStreaming() {
    if (audioElement && isStreaming) {
        audioElement.pause();
        console.log('[mediastreamer.js] 미디어 일시정지');
    }
}

export async function resumeMediaStreaming() {
    if (audioElement && isStreaming) {
        try {
            await audioElement.play();
            console.log('[mediastreamer.js] 미디어 재생 재개');
        } catch (error) {
            console.error('[mediastreamer.js] 재생 재개 오류:', error);
        }
    }
}

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
        currentIndex: currentIndex,
        playlistLength: currentPlaylist.length,
        config: streamConfig,
        dotNetObj: !!dotNetObj
    };
}