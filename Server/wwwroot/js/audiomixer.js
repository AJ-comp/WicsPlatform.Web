// Server/wwwroot/js/audiomixer.js
console.log('[audiomixer.js] 모듈 로드됨 v5.0 - 마이크 전용 버전');

class AudioMixer {
    constructor() {
        this.audioContext = null;
        this.mediaRecorder = null;
        this.dotNetRef = null;

        // 마이크 전용
        this.micGain = null;
        this.micStream = null;
        this.micSource = null;
        this.destination = null;

        // 상태
        this.isActive = false;
        this.isDisposing = false;

        // Interval 저장
        this.sendInterval = null;
        this.dataRequestInterval = null;

        this.config = {
            sampleRate: 48000,
            channels: 2,
            timeslice: 100,
            bitrate: 128000
        };
    }

    async initialize(dotNetRef, config = {}) {
        console.log('[audiomixer.js] 초기화 시작 (마이크 전용)');

        try {
            this.dotNetRef = dotNetRef;
            this.config = { ...this.config, ...config };
            this.isActive = true;

            // AudioContext
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: this.config.sampleRate
            });

            if (this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }

            // 마이크 Gain 노드만 생성
            this.micGain = this.audioContext.createGain();
            this.micGain.gain.value = config.micVolume || 1.0;

            // Destination
            this.destination = this.audioContext.createMediaStreamDestination();

            // 마이크만 destination에 직접 연결
            this.micGain.connect(this.destination);

            // MediaRecorder
            this.setupMediaRecorder();

            console.log('[audiomixer.js] 초기화 완료 (마이크 전용)');
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 초기화 실패:', error);
            this.isActive = false;
            return false;
        }
    }

    setupMediaRecorder() {
        try {
            this.mediaRecorder = new MediaRecorder(this.destination.stream, {
                mimeType: 'audio/webm',
                audioBitsPerSecond: this.config.bitrate
            });

            let chunks = [];
            const self = this; // this 컨텍스트 보존

            this.mediaRecorder.ondataavailable = async (event) => {
                if (self.isDisposing || !self.isActive) return;

                if (event.data && event.data.size > 0) {
                    chunks.push(event.data);
                    console.log(`[audiomixer.js] 마이크 오디오 청크 수신: ${event.data.size} bytes`);
                }
            };

            // 주기적으로 전송
            this.sendInterval = setInterval(async () => {
                if (chunks.length > 0 && self.dotNetRef && !self.isDisposing) {
                    console.log(`[audiomixer.js] 청크 전송 시작: ${chunks.length}개`);

                    const blob = new Blob(chunks, { type: 'audio/webm' });
                    chunks = []; // 즉시 비우기

                    try {
                        const buffer = await blob.arrayBuffer();
                        const bytes = new Uint8Array(buffer);
                        let base64 = '';

                        // 32KB씩 나누어 인코딩
                        for (let i = 0; i < bytes.length; i += 32768) {
                            base64 += btoa(String.fromCharCode(...bytes.slice(i, i + 32768)));
                        }

                        console.log(`[audiomixer.js] C#으로 전송: ${base64.length} 문자`);
                        await self.dotNetRef.invokeMethodAsync('OnMixedAudioCaptured', base64);
                    } catch (e) {
                        console.error('[audiomixer.js] 데이터 전송 오류:', e);
                    }
                }
            }, this.config.timeslice);

            // MediaRecorder에 주기적으로 데이터 요청
            this.dataRequestInterval = setInterval(() => {
                if (this.mediaRecorder &&
                    this.mediaRecorder.state === 'recording' &&
                    !this.isDisposing) {
                    this.mediaRecorder.requestData();
                }
            }, this.config.timeslice);

        } catch (error) {
            console.error('[audiomixer.js] MediaRecorder 설정 실패:', error);
        }
    }

    startRecording() {
        if (this.mediaRecorder && this.mediaRecorder.state === 'inactive' && !this.isDisposing) {
            this.mediaRecorder.start(this.config.timeslice);
            console.log('[audiomixer.js] 녹음 시작 (timeslice:', this.config.timeslice, 'ms)');
        }
    }

    stopRecording() {
        if (this.mediaRecorder) {
            try {
                if (this.mediaRecorder.state === 'recording') {
                    this.mediaRecorder.requestData();
                    this.mediaRecorder.stop();
                }
            } catch (e) {
                console.warn('[audiomixer.js] MediaRecorder 중지 오류:', e);
            }
        }
    }

    async enableMic() {
        try {
            this.micStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    sampleRate: this.config.sampleRate,
                    channelCount: this.config.channels,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });

            this.micSource = this.audioContext.createMediaStreamSource(this.micStream);
            this.micSource.connect(this.micGain);

            console.log('[audiomixer.js] 마이크 활성화');
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 마이크 실패:', error);
            return false;
        }
    }

    disableMic() {
        if (this.micSource) {
            try { this.micSource.disconnect(); } catch (e) { }
            this.micSource = null;
        }

        if (this.micStream) {
            this.micStream.getTracks().forEach(track => {
                try { track.stop(); } catch (e) { }
            });
            this.micStream = null;
        }
    }

    // 미디어/TTS 관련 메서드는 아무것도 하지 않도록 stub 처리
    async loadMediaPlaylist(urls) {
        console.log('[audiomixer.js] loadMediaPlaylist 호출됨 - 무시됨 (마이크 전용 모드)');
        return true;
    }

    async loadTtsPlaylist(urls) {
        console.log('[audiomixer.js] loadTtsPlaylist 호출됨 - 무시됨 (마이크 전용 모드)');
        return true;
    }

    setVolumes(mic, media, tts) {
        // 마이크 볼륨만 적용
        if (this.micGain) this.micGain.gain.value = mic;
        // media, tts 볼륨은 무시
    }

    async dispose() {
        if (this.isDisposing) return;

        this.isDisposing = true;
        this.isActive = false;
        console.log('[audiomixer.js] dispose 시작');

        try {
            // Interval 정리
            if (this.sendInterval) {
                clearInterval(this.sendInterval);
                this.sendInterval = null;
            }

            if (this.dataRequestInterval) {
                clearInterval(this.dataRequestInterval);
                this.dataRequestInterval = null;
            }

            // 녹음 중지
            this.stopRecording();

            // 마이크 중지
            this.disableMic();

            // Gain 노드 정리
            if (this.micGain) {
                try {
                    this.micGain.disconnect();
                } catch (e) { }
                this.micGain = null;
            }

            // MediaRecorder 정리
            this.mediaRecorder = null;

            // AudioContext 닫기
            if (this.audioContext) {
                try {
                    const closePromise = this.audioContext.close();
                    const timeoutPromise = new Promise((resolve) => {
                        setTimeout(() => {
                            console.warn('[audiomixer.js] AudioContext close 타임아웃');
                            resolve();
                        }, 2000);
                    });

                    await Promise.race([closePromise, timeoutPromise]);
                    console.log('[audiomixer.js] AudioContext 닫기 완료');
                } catch (e) {
                    console.warn('[audiomixer.js] AudioContext 닫기 오류:', e);
                } finally {
                    this.audioContext = null;
                }
            }

            // 참조 제거
            this.dotNetRef = null;
            this.destination = null;

            console.log('[audiomixer.js] dispose 완료');
        } catch (error) {
            console.error('[audiomixer.js] dispose 중 오류:', error);
        }
    }
}

// 전역 인스턴스
let mixerInstance = null;

// 외부 진입점
export async function createMixer(dotNetRef, config) {
    if (mixerInstance) {
        await mixerInstance.dispose();
        mixerInstance = null;
    }

    mixerInstance = new AudioMixer();
    const success = await mixerInstance.initialize(dotNetRef, config);

    if (success) {
        mixerInstance.startRecording();
    }

    return success;
}

export async function enableMic() {
    return mixerInstance ? await mixerInstance.enableMic() : false;
}

// 미디어/TTS 메서드는 유지하되 아무 동작도 하지 않음 (호출 호환성 유지)
export async function loadMediaPlaylist(urls) {
    return mixerInstance ? await mixerInstance.loadMediaPlaylist(urls) : false;
}

export async function loadTtsPlaylist(urls) {
    return mixerInstance ? await mixerInstance.loadTtsPlaylist(urls) : false;
}

export function setVolumes(mic, media, tts) {
    if (mixerInstance) {
        mixerInstance.setVolumes(mic, media, tts);
    }
}

export async function dispose() {
    if (mixerInstance) {
        await mixerInstance.dispose();
        mixerInstance = null;
    }
}

// 디버그
window.mixerDebug = {
    getInstance: () => mixerInstance,
    forceKill: async () => {
        if (mixerInstance) {
            mixerInstance.isActive = false;
            mixerInstance.isDisposing = true;
            await mixerInstance.dispose();
            mixerInstance = null;
        }
        console.log('[audiomixer.js] 강제 종료 완료');
    }
};