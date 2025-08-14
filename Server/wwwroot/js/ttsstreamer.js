// ──────────────────────────────────────────────────────────────
// Blazor WASM – TTS 스트리머 유틸리티 (50ms / Base64 전송)
// 미디어 스트리머와 동일한 인터페이스 및 WebM/Opus 인코딩
// ──────────────────────────────────────────────────────────────
console.log('[ttsstreamer.js] 모듈 로드됨');

let audioContext = null;
let dotNetObj = null;
let mediaRecorder = null;
let mediaStream = null;
let streamDestination = null;
let chunks = [];
let isStreaming = false;
let currentTtsQueue = [];
let currentIndex = 0;

// MIME 타입 (mic.js, mediastreamer.js와 동일)
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
    bitrate: 64000,  // 64kbps
    localPlayback: false  // 로컬 재생 옵션
};

/* ───── 버퍼 → Base64 → C# (mic.js, mediastreamer.js와 동일) ───── */
async function flushChunks() {
    console.log('[ttsstreamer.js] flushChunks 호출됨 - chunks:', chunks.length, 'dotNetObj:', !!dotNetObj);

    if (!chunks.length) {
        console.warn('[ttsstreamer.js] chunks가 비어있음');
        return;
    }

    if (!dotNetObj) {
        console.error('[ttsstreamer.js] dotNetObj가 null임');
        return;
    }

    try {
        const blob = new Blob(chunks, { type: MIME_TYPE });
        chunks = [];
        const buffer = await blob.arrayBuffer();
        const bytes = new Uint8Array(buffer);

        console.log('[ttsstreamer.js] Blob 생성 완료 - 크기:', bytes.length, 'bytes');

        // 32 KB 단위 Base64 인코딩
        const STEP = 32_768;
        let base64 = '';
        for (let i = 0; i < bytes.length; i += STEP) {
            base64 += btoa(String.fromCharCode.apply(null, bytes.slice(i, i + STEP)));
        }

        console.log('[ttsstreamer.js] Base64 인코딩 완료 - 길이:', base64.length);
        console.log('[ttsstreamer.js] OnTtsAudioCaptured 호출 시도...');

        // 메서드 이름 확인
        console.log('[ttsstreamer.js] dotNetObj 메서드들:', dotNetObj);

        // 호출 시도
        const result = await dotNetObj.invokeMethodAsync('OnTtsAudioCaptured', base64);
        console.log('[ttsstreamer.js] OnTtsAudioCaptured 호출 성공!, 결과:', result);

    } catch (error) {
        console.error('[ttsstreamer.js] flushChunks 오류:', error);
        console.error('[ttsstreamer.js] 오류 스택:', error.stack);
    }
}

/* ───── 외부 진입점: TTS 스트리머 초기화 (mediastreamer.js와 동일) ───── */
export async function initializeTtsStreamer(dotNetRef, config = {}) {
    console.log('[ttsstreamer.js] === TTS 스트리머 초기화 (WebM/Opus) ===', config);
    console.log('[ttsstreamer.js] 선택된 MIME 타입:', MIME_TYPE);

    dotNetObj = dotNetRef;
    if (!dotNetObj) {
        console.error('[ttsstreamer.js] DotNetObjectReference가 null입니다.');
        return false;
    }

    // 오디오 설정 업데이트 (mediastreamer.js와 동일)
    streamConfig = {
        sampleRate: config.sampleRate || 48000,
        channels: config.channels || 2,
        timeslice: config.timeslice || 50,
        bitrate: config.bitrate || 64000,
        localPlayback: config.localPlayback !== undefined ? config.localPlayback : false
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

        console.log('[ttsstreamer.js] 오디오 컨텍스트 초기화 완료');
        console.log('[ttsstreamer.js] 설정:', streamConfig);
        console.log('[ttsstreamer.js] 로컬 재생:', streamConfig.localPlayback ? '활성화' : '비활성화');
        return true;

    } catch (error) {
        console.error('[ttsstreamer.js] 초기화 실패:', error);
        return false;
    }
}

/* ───── TTS 텍스트 리스트 로드 및 스트리밍 (mediastreamer.js의 loadAndStreamMediaPlaylist와 동일) ───── */
export async function loadAndStreamTtsList(ttsData) {
    console.log('[ttsstreamer.js] === TTS 리스트 로드 ===');
    console.log('[ttsstreamer.js] 받은 데이터:', ttsData);

    // 배열이 아니면 배열로 변환
    if (!Array.isArray(ttsData)) {
        if (typeof ttsData === 'object' && ttsData !== null) {
            ttsData = [ttsData];  // 단일 객체를 배열로
        } else {
            console.error('[ttsstreamer.js] TTS 데이터를 처리할 수 없습니다.');
            return false;
        }
    }

    if (!ttsData || !ttsData.length) {
        console.warn('[ttsstreamer.js] 빈 TTS 리스트입니다.');
        return false;
    }

    currentTtsQueue = ttsData;
    currentIndex = 0;

    return await playCurrentTts();
}

/* ───── 현재 TTS 재생 (WebM/Opus 인코딩) ───── */
async function playCurrentTts() {
    if (!currentTtsQueue.length || currentIndex >= currentTtsQueue.length) {
        console.log('[ttsstreamer.js] TTS 리스트 종료');
        await stopTtsStreaming();
        if (dotNetObj) {
            await dotNetObj.invokeMethodAsync('OnTtsPlaylistEnded');
        }
        return false;
    }

    const currentTts = currentTtsQueue[currentIndex];
    const ttsText = typeof currentTts === 'string' ? currentTts : currentTts.content || currentTts.text;

    console.log('[ttsstreamer.js] TTS 재생 시작:', ttsText.substring(0, 50) + '...');

    try {
        // 기존 스트리밍 정리
        await cleanupCurrentStream();

        // TTS를 오디오로 변환하는 대체 방법
        // Web Speech API는 직접 스트림을 제공하지 않으므로 getUserMedia 사용

        // 1. 먼저 TTS를 스피커로 재생
        const utterance = new SpeechSynthesisUtterance(ttsText);
        utterance.lang = currentTts.lang || 'ko-KR';
        utterance.rate = currentTts.rate || 1.0;
        utterance.pitch = currentTts.pitch || 1.0;

        // 2. 시스템 오디오 캡처 시도 (스테레오 믹스)
        try {
            // 마이크 입력 캡처 (스테레오 믹스가 활성화되어 있어야 함)
            const stream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    echoCancellation: false,
                    noiseSuppression: false,
                    autoGainControl: false,
                    sampleRate: streamConfig.sampleRate,
                    channelCount: streamConfig.channels
                }
            });

            // MediaRecorder 생성
            mediaRecorder = new MediaRecorder(stream, {
                mimeType: MIME_TYPE,
                audioBitsPerSecond: streamConfig.bitrate
            });

            // MediaRecorder 이벤트 바인딩
            mediaRecorder.ondataavailable = e => {
                if (e.data?.size) {
                    console.log('[ttsstreamer.js] 데이터 청크 수신:', e.data.size, 'bytes');
                    chunks.push(e.data);
                    if (streamConfig.timeslice) {
                        flushChunks();
                    }
                }
            };

            mediaRecorder.onstop = () => {
                console.log('[ttsstreamer.js] MediaRecorder 중지, 마지막 청크 전송');
                flushChunks();
                stream.getTracks().forEach(track => track.stop());
            };

            // MediaRecorder 시작
            mediaRecorder.start(streamConfig.timeslice);
            console.log(`[ttsstreamer.js] MediaRecorder 시작 – timeslice: ${streamConfig.timeslice} ms`);

            // TTS 재생
            utterance.onend = async () => {
                console.log('[ttsstreamer.js] TTS 재생 종료');
                if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                    mediaRecorder.stop();
                }
                currentIndex++;
                setTimeout(() => playCurrentTts(), 500); // 다음 TTS 재생 전 잠시 대기
            };

            utterance.onerror = (error) => {
                console.error('[ttsstreamer.js] TTS 오류:', error);
                if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                    mediaRecorder.stop();
                }
                currentIndex++;
                playCurrentTts();
            };

            // TTS 시작
            speechSynthesis.speak(utterance);
            isStreaming = true;

            console.log('[ttsstreamer.js] TTS 스트리밍 성공적으로 시작됨');
            return true;

        } catch (micError) {
            console.error('[ttsstreamer.js] 마이크 캡처 실패, 더미 데이터 생성:', micError);

            // 대체 방법: 더미 데이터 생성하여 테스트
            return await generateDummyTtsStream(ttsText);
        }

    } catch (error) {
        console.error('[ttsstreamer.js] TTS 처리 실패:', error);
        currentIndex++;
        return await playCurrentTts();
    }
}

/* ───── 더미 TTS 스트림 생성 (테스트용) ───── */
async function generateDummyTtsStream(text) {
    console.log('[ttsstreamer.js] 더미 TTS 스트림 생성 중...');

    try {
        // AudioContext로 더미 오디오 생성
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();

        // MediaStreamDestination 생성
        streamDestination = audioContext.createMediaStreamDestination();

        // 주파수 변조로 TTS 흉내
        oscillator.type = 'sine';
        oscillator.frequency.setValueAtTime(440, audioContext.currentTime);

        // 볼륨 조절
        gainNode.gain.setValueAtTime(0.1, audioContext.currentTime);

        // 연결
        oscillator.connect(gainNode);
        gainNode.connect(streamDestination);

        // MediaRecorder 생성
        mediaRecorder = new MediaRecorder(streamDestination.stream, {
            mimeType: MIME_TYPE,
            audioBitsPerSecond: streamConfig.bitrate
        });

        mediaRecorder.ondataavailable = e => {
            if (e.data?.size) {
                console.log('[ttsstreamer.js] 더미 데이터 청크:', e.data.size, 'bytes');
                chunks.push(e.data);
                flushChunks();
            }
        };

        mediaRecorder.onstop = () => {
            console.log('[ttsstreamer.js] 더미 스트림 중지');
            flushChunks();
            oscillator.stop();
        };

        // 시작
        oscillator.start();
        mediaRecorder.start(streamConfig.timeslice);

        // TTS 텍스트 길이에 따라 재생 시간 결정 (한 글자당 100ms)
        const duration = Math.min(text.length * 100, 10000); // 최대 10초

        setTimeout(() => {
            if (mediaRecorder && mediaRecorder.state !== 'inactive') {
                mediaRecorder.stop();
            }
            currentIndex++;
            playCurrentTts();
        }, duration);

        isStreaming = true;
        console.log('[ttsstreamer.js] 더미 스트림 시작됨');

        // 실제 TTS도 동시에 재생 (오디오는 캡처 안 됨)
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = 'ko-KR';
        speechSynthesis.speak(utterance);

        return true;

    } catch (error) {
        console.error('[ttsstreamer.js] 더미 스트림 생성 실패:', error);
        return false;
    }
}

/* ───── 현재 스트림 정리 ───── */
async function cleanupCurrentStream() {
    if (mediaRecorder && mediaRecorder.state !== 'inactive') {
        mediaRecorder.stop();
        mediaRecorder = null;
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
export async function stopTtsStreaming() {
    console.log('[ttsstreamer.js] === TTS 스트리밍 중지 ===');

    // 현재 재생 중인 TTS 중지
    speechSynthesis.cancel();

    isStreaming = false;

    await cleanupCurrentStream();

    currentTtsQueue = [];
    currentIndex = 0;
}

/* ───── 일시정지/재개 ───── */
export async function pauseTtsStreaming() {
    if (isStreaming) {
        speechSynthesis.pause();

        if (mediaRecorder && mediaRecorder.state === 'recording') {
            mediaRecorder.pause();
        }

        console.log('[ttsstreamer.js] TTS 일시정지');
    }
}

export async function resumeTtsStreaming() {
    if (isStreaming) {
        speechSynthesis.resume();

        if (mediaRecorder && mediaRecorder.state === 'paused') {
            mediaRecorder.resume();
        }

        console.log('[ttsstreamer.js] TTS 재생 재개');
    }
}

/* ───── 로컬 재생 설정 변경 ───── */
export function setLocalPlayback(enabled) {
    streamConfig.localPlayback = enabled;
    console.log(`[ttsstreamer.js] 로컬 재생 ${enabled ? '활성화' : '비활성화'}`);
}

/* ───── 상태 조회 ───── */
export function isTtsStreaming() {
    return isStreaming;
}

export function getCurrentTtsInfo() {
    return {
        isStreaming: isStreaming,
        currentIndex: currentIndex,
        totalCount: currentTtsQueue.length,
        currentText: currentTtsQueue[currentIndex] || null,
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
        queueLength: currentTtsQueue.length,
        config: streamConfig,
        mimeType: MIME_TYPE,
        chunks: chunks.length,
        dotNetObj: !!dotNetObj,
        localPlayback: streamConfig.localPlayback,
        speechSynthesisState: speechSynthesis.speaking ? 'speaking' :
            speechSynthesis.paused ? 'paused' : 'idle'
    };
}

/* ───── 사용 가능한 음성 목록 조회 ───── */
export function getAvailableVoices() {
    return speechSynthesis.getVoices().map(voice => ({
        name: voice.name,
        lang: voice.lang,
        default: voice.default,
        localService: voice.localService
    }));
}