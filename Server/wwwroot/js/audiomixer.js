// Server/wwwroot/js/audiomixer.js
console.log('[audiomixer.js] 모듈 로드됨 v6.0 - PCM 직접 전송 버전');

class AudioMixer {
    constructor() {
        this.audioContext = null;
        this.dotNetRef = null;

        // 마이크 관련
        this.micGain = null;
        this.micStream = null;
        this.micSource = null;
        this.scriptProcessor = null;

        // 상태
        this.isActive = false;
        this.isDisposing = false;

        // PCM 버퍼
        this.pcmBuffer = [];
        this.sendInterval = null;

        this.config = {
            sampleRate: 48000,
            channels: 1,  // 모노로 변경 (대역폭 절감)
            bufferSize: 4096,  // ScriptProcessor 버퍼 크기
            sendIntervalMs: 60  // 60ms마다 전송 (Opus 최적)
        };
    }

    async initialize(dotNetRef, config = {}) {
        console.log('[audiomixer.js] PCM 캡처 초기화 시작');

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

            // 마이크 Gain 노드 생성
            this.micGain = this.audioContext.createGain();
            this.micGain.gain.value = config.micVolume || 1.0;

            // ScriptProcessorNode 생성 (PCM 데이터 캡처용)
            this.scriptProcessor = this.audioContext.createScriptProcessor(
                this.config.bufferSize,
                this.config.channels,
                this.config.channels
            );

            // PCM 데이터 처리
            this.scriptProcessor.onaudioprocess = (audioProcessingEvent) => {
                if (!this.isActive || this.isDisposing) return;

                const inputBuffer = audioProcessingEvent.inputBuffer;
                const channelData = inputBuffer.getChannelData(0); // 모노 채널

                // Float32 → Int16 변환
                const pcm16 = new Int16Array(channelData.length);
                for (let i = 0; i < channelData.length; i++) {
                    // Float32 (-1.0 ~ 1.0) → Int16 (-32768 ~ 32767)
                    const sample = Math.max(-1, Math.min(1, channelData[i]));
                    pcm16[i] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
                }

                // 버퍼에 추가
                this.pcmBuffer.push(pcm16);
            };

            // 마이크 → Gain → ScriptProcessor 연결
            // ScriptProcessor는 destination에 연결해야 작동함
            this.micGain.connect(this.scriptProcessor);
            this.scriptProcessor.connect(this.audioContext.destination);

            // 50ms마다 PCM 데이터 전송
            this.setupPCMSender();

            console.log('[audiomixer.js] PCM 캡처 초기화 완료');
            console.log(`[audiomixer.js] 설정: ${this.config.sampleRate}Hz, ${this.config.channels}ch, ${this.config.sendIntervalMs}ms 간격`);

            return true;

        } catch (error) {
            console.error('[audiomixer.js] 초기화 실패:', error);
            this.isActive = false;
            return false;
        }
    }

    setupPCMSender() {
        const samplesPerInterval = (this.config.sampleRate * this.config.sendIntervalMs) / 1000;
        console.log(`[audiomixer.js] ${this.config.sendIntervalMs}ms당 샘플 수: ${samplesPerInterval}`);

        this.sendInterval = setInterval(async () => {
            if (!this.isActive || this.isDisposing || !this.dotNetRef) return;
            if (this.pcmBuffer.length === 0) return;

            try {
                // 버퍼에서 PCM 데이터 수집
                let totalSamples = 0;
                for (const chunk of this.pcmBuffer) {
                    totalSamples += chunk.length;
                }

                // 모든 청크를 하나의 배열로 합침
                const combinedPCM = new Int16Array(totalSamples);
                let offset = 0;
                for (const chunk of this.pcmBuffer) {
                    combinedPCM.set(chunk, offset);
                    offset += chunk.length;
                }

                // 버퍼 비우기
                this.pcmBuffer = [];

                // 50ms 분량만 전송 (나머지는 다음 번에)
                const samplesToSend = Math.min(combinedPCM.length, samplesPerInterval);
                const pcmToSend = combinedPCM.slice(0, samplesToSend);

                // 남은 데이터는 다시 버퍼에 넣기
                if (combinedPCM.length > samplesToSend) {
                    this.pcmBuffer.push(combinedPCM.slice(samplesToSend));
                }

                // Int16Array → Uint8Array 변환 (byte 배열)
                const byteArray = new Uint8Array(pcmToSend.buffer, pcmToSend.byteOffset, pcmToSend.byteLength);

                // Base64 인코딩
                let base64 = '';
                const chunkSize = 32768;
                for (let i = 0; i < byteArray.length; i += chunkSize) {
                    const chunk = byteArray.slice(i, Math.min(i + chunkSize, byteArray.length));
                    base64 += btoa(String.fromCharCode(...chunk));
                }

                console.log(`[audiomixer.js] PCM 전송: ${pcmToSend.length} samples (${byteArray.length} bytes)`);

                // C#으로 전송
                await this.dotNetRef.invokeMethodAsync('OnMixedAudioCaptured', base64);

            } catch (error) {
                console.error('[audiomixer.js] PCM 전송 오류:', error);
            }
        }, this.config.sendIntervalMs);
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

        console.log('[audiomixer.js] 마이크 비활성화');
    }

    // 호환성을 위한 stub 메서드들
    startRecording() {
        console.log('[audiomixer.js] startRecording 호출 - PCM 모드에서는 자동 시작');
    }

    stopRecording() {
        console.log('[audiomixer.js] stopRecording 호출 - PCM 모드에서는 자동 중지');
    }

    async loadMediaPlaylist(urls) {
        console.log('[audiomixer.js] loadMediaPlaylist 호출 - 무시됨');
        return true;
    }

    async loadTtsPlaylist(urls) {
        console.log('[audiomixer.js] loadTtsPlaylist 호출 - 무시됨');
        return true;
    }

    setVolumes(mic, media, tts) {
        if (this.micGain) {
            this.micGain.gain.value = mic;
            console.log(`[audiomixer.js] 마이크 볼륨 설정: ${mic}`);
        }
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

            // 마이크 중지
            this.disableMic();

            // 오디오 노드 정리
            if (this.scriptProcessor) {
                try {
                    this.scriptProcessor.disconnect();
                } catch (e) { }
                this.scriptProcessor = null;
            }

            if (this.micGain) {
                try {
                    this.micGain.disconnect();
                } catch (e) { }
                this.micGain = null;
            }

            // AudioContext 닫기
            if (this.audioContext) {
                try {
                    await this.audioContext.close();
                    console.log('[audiomixer.js] AudioContext 닫기 완료');
                } catch (e) {
                    console.warn('[audiomixer.js] AudioContext 닫기 오류:', e);
                } finally {
                    this.audioContext = null;
                }
            }

            // 참조 제거
            this.dotNetRef = null;
            this.pcmBuffer = [];

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

    // PCM 모드에서는 자동으로 시작됨

    return success;
}

export async function enableMic() {
    return mixerInstance ? await mixerInstance.enableMic() : false;
}

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
    getBufferStatus: () => {
        if (!mixerInstance) return 'No instance';
        return {
            bufferLength: mixerInstance.pcmBuffer.length,
            isActive: mixerInstance.isActive,
            sampleRate: mixerInstance.config.sampleRate
        };
    },
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