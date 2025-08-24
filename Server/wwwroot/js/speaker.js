// Server/wwwroot/js/speaker.js - PCM 버전
console.log('[speaker.js] PCM 재생 모듈 로드됨');

let audioContext = null;
let nextStartTime = 0;

export function init() {
    if (audioContext) return;

    audioContext = new (window.AudioContext || window.webkitAudioContext)({
        sampleRate: 16000  // C#에서 사용하는 샘플레이트와 일치
    });

    nextStartTime = audioContext.currentTime;
    console.log('[speaker.js] AudioContext 초기화 완료');
}

export function feed(base64) {
    if (!audioContext) init();

    try {
        // Base64 → Uint8Array
        const binaryString = atob(base64);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }

        // Opus 디코딩이 필요하면 여기서 처리
        // 현재는 PCM 데이터라고 가정

        // Int16 PCM → Float32
        const int16Array = new Int16Array(bytes.buffer);
        const float32Array = new Float32Array(int16Array.length);
        for (let i = 0; i < int16Array.length; i++) {
            float32Array[i] = int16Array[i] / 32768.0;
        }

        // AudioBuffer 생성
        const audioBuffer = audioContext.createBuffer(
            1, // 모노
            float32Array.length,
            16000 // 16kHz
        );
        audioBuffer.getChannelData(0).set(float32Array);

        // 스케줄링하여 끊김 없이 재생
        const source = audioContext.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(audioContext.destination);

        const now = audioContext.currentTime;
        const startTime = Math.max(now, nextStartTime);
        source.start(startTime);

        // 다음 재생 시작 시간 계산
        nextStartTime = startTime + audioBuffer.duration;

    } catch (e) {
        console.error('[speaker.js] 재생 오류:', e);
    }
}