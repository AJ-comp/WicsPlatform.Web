// Server/wwwroot/js/audiomixer.js
console.log('[audiomixer.js] 모듈 로드됨 v9.0 - 60ms 실시간 최적화');

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
        this.isSending = false;  // 전송 중 플래그

        // PCM 버퍼 (고정 크기 링 버퍼)
        this.pcmRingBuffer = [];
        this.bufferWriteIndex = 0;
        this.bufferReadIndex = 0;
        this.maxBufferChunks = 3;  // 최대 3개 청크 (180ms 분량)

        // 타이머
        this.sendTimer = null;
        this.lastSendTime = 0;

        this.config = {
            sampleRate: 16000,
            channels: 1,  // 모노
            bufferSize: 2048,  // ScriptProcessor 버퍼 (약 42ms @ 48kHz)
            sendIntervalMs: 60,  // 60ms 전송 주기
            samplesPerSend: 2880  // 60ms @ 48kHz = 2880 샘플
        };

        // 통계
        this.stats = {
            capturedChunks: 0,
            sentPackets: 0,
            droppedChunks: 0,
            avgLatency: 0,
            maxLatency: 0
        };
    }

    async initialize(dotNetRef, config = {}) {
        console.log('[audiomixer.js] 60ms 실시간 초기화 시작');

        try {
            this.dotNetRef = dotNetRef;
            this.config = { ...this.config, ...config };

            // 60ms에 해당하는 샘플 수 재계산
            this.config.samplesPerSend = Math.floor(
                (this.config.sampleRate * this.config.sendIntervalMs) / 1000
            );

            this.isActive = true;
            this.isSending = false;

            // AudioContext (최소 레이턴시)
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: this.config.sampleRate,
                latencyHint: 'interactive'  // 최소 레이턴시
            });

            if (this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }

            // 마이크 Gain
            this.micGain = this.audioContext.createGain();
            this.micGain.gain.value = config.micVolume || 1.0;

            // ScriptProcessorNode (실시간 PCM 캡처)
            // 버퍼 크기를 60ms에 가깝게 조정 (2880 샘플에 가장 가까운 2의 제곱수)
            this.scriptProcessor = this.audioContext.createScriptProcessor(
                4096,  // 약 85ms @ 48kHz (2048은 42ms, 4096은 85ms)
                this.config.channels,
                this.config.channels
            );

            // 링 버퍼 초기화
            this.initializeRingBuffer();

            // 실시간 오디오 처리
            this.scriptProcessor.onaudioprocess = (e) => {
                if (!this.isActive || this.isDisposing) return;

                const inputBuffer = e.inputBuffer;
                const channelData = inputBuffer.getChannelData(0);

                // Float32 → Int16 변환
                const pcm16 = new Int16Array(channelData.length);
                for (let i = 0; i < channelData.length; i++) {
                    const sample = Math.max(-1, Math.min(1, channelData[i]));
                    pcm16[i] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
                }

                // 링 버퍼에 쓰기
                this.writeToRingBuffer(pcm16);
            };

            // 연결
            this.micGain.connect(this.scriptProcessor);
            this.scriptProcessor.connect(this.audioContext.destination);

            // 60ms 정확한 타이머 시작
            this.startPrecisionTimer();

            console.log(`[audiomixer.js] 초기화 완료 - ${this.config.sampleRate}Hz, 60ms 주기`);
            console.log(`[audiomixer.js] 60ms = ${this.config.samplesPerSend} 샘플`);

            return true;

        } catch (error) {
            console.error('[audiomixer.js] 초기화 실패:', error);
            this.isActive = false;
            return false;
        }
    }

    initializeRingBuffer() {
        // 링 버퍼를 고정 크기로 초기화
        this.pcmRingBuffer = new Array(this.maxBufferChunks);
        for (let i = 0; i < this.maxBufferChunks; i++) {
            this.pcmRingBuffer[i] = null;
        }
        this.bufferWriteIndex = 0;
        this.bufferReadIndex = 0;
    }

    writeToRingBuffer(pcmData) {
        const nextWriteIndex = (this.bufferWriteIndex + 1) % this.maxBufferChunks;

        // 버퍼가 가득 찬 경우 (쓰기가 읽기를 따라잡음)
        if (nextWriteIndex === this.bufferReadIndex) {
            // 가장 오래된 데이터 덮어쓰기
            this.bufferReadIndex = (this.bufferReadIndex + 1) % this.maxBufferChunks;
            this.stats.droppedChunks++;

            if (this.stats.droppedChunks % 10 === 0) {
                console.warn(`[audiomixer.js] 버퍼 오버런 - ${this.stats.droppedChunks}개 청크 드롭`);
            }
        }

        // 데이터 쓰기
        this.pcmRingBuffer[this.bufferWriteIndex] = pcmData;
        this.bufferWriteIndex = nextWriteIndex;
        this.stats.capturedChunks++;
    }

    readFromRingBuffer() {
        const chunks = [];
        let totalSamples = 0;

        // 사용 가능한 모든 청크 읽기
        while (this.bufferReadIndex !== this.bufferWriteIndex) {
            const chunk = this.pcmRingBuffer[this.bufferReadIndex];
            if (chunk) {
                chunks.push(chunk);
                totalSamples += chunk.length;
            }

            this.pcmRingBuffer[this.bufferReadIndex] = null;  // 메모리 해제
            this.bufferReadIndex = (this.bufferReadIndex + 1) % this.maxBufferChunks;

            // 60ms 분량만 읽기
            if (totalSamples >= this.config.samplesPerSend) {
                break;
            }
        }

        if (chunks.length === 0) return null;

        // 청크들을 하나로 결합
        const combined = new Int16Array(totalSamples);
        let offset = 0;
        for (const chunk of chunks) {
            combined.set(chunk, offset);
            offset += chunk.length;
        }

        // 정확히 60ms 분량만 반환
        if (combined.length > this.config.samplesPerSend) {
            // 초과분은 다시 버퍼에 넣기
            const excess = combined.slice(this.config.samplesPerSend);
            this.writeToRingBuffer(excess);
            return combined.slice(0, this.config.samplesPerSend);
        }

        return combined;
    }

    startPrecisionTimer() {
        let nextSendTime = performance.now() + this.config.sendIntervalMs;

        const sendLoop = () => {
            if (!this.isActive || this.isDisposing) return;

            const now = performance.now();
            const drift = now - nextSendTime;

            // 드리프트 보정
            if (Math.abs(drift) > 5) {
                console.log(`[audiomixer.js] 타이밍 드리프트: ${drift.toFixed(2)}ms`);
                nextSendTime = now + this.config.sendIntervalMs;
            } else {
                nextSendTime += this.config.sendIntervalMs;
            }

            // 데이터 전송 (비동기, 블로킹 없음)
            this.sendPCMData();

            // 다음 전송 예약 (드리프트 보정)
            const delay = Math.max(1, nextSendTime - performance.now());
            this.sendTimer = setTimeout(sendLoop, delay);
        };

        // 시작
        sendLoop();
    }

    async sendPCMData() {
        // 이미 전송 중이면 스킵
        if (this.isSending) {
            console.log('[audiomixer.js] 이전 전송 진행 중, 스킵');
            return;
        }

        const pcmData = this.readFromRingBuffer();
        if (!pcmData || pcmData.length === 0) {
            return;  // 전송할 데이터 없음
        }

        this.isSending = true;
        const sendStartTime = performance.now();

        try {
            // Int16Array → Uint8Array
            const byteArray = new Uint8Array(
                pcmData.buffer,
                pcmData.byteOffset,
                pcmData.byteLength
            );

            // Base64 인코딩 (빠른 방식)
            const base64 = this.fastBase64Encode(byteArray);

            // C# 전송 (await 없이 비동기)
            this.dotNetRef.invokeMethodAsync('OnMixedAudioCaptured', base64)
                .then(() => {
                    const latency = performance.now() - sendStartTime;
                    this.updateLatencyStats(latency);
                })
                .catch(err => {
                    console.error('[audiomixer.js] 전송 오류:', err);
                });

            this.stats.sentPackets++;

            // 디버그 (100번마다)
            if (this.stats.sentPackets % 100 === 0) {
                console.log(`[audiomixer.js] 상태: 전송=${this.stats.sentPackets}, ` +
                    `드롭=${this.stats.droppedChunks}, ` +
                    `평균지연=${this.stats.avgLatency.toFixed(2)}ms`);
            }

        } catch (error) {
            console.error('[audiomixer.js] PCM 처리 오류:', error);
        } finally {
            this.isSending = false;
        }
    }

    // 빠른 Base64 인코딩
    fastBase64Encode(uint8Array) {
        const CHUNK_SIZE = 65536;  // 64KB
        let result = '';

        for (let i = 0; i < uint8Array.length; i += CHUNK_SIZE) {
            const chunk = uint8Array.subarray(i, Math.min(i + CHUNK_SIZE, uint8Array.length));
            result += btoa(String.fromCharCode.apply(null, chunk));
        }

        return result;
    }

    updateLatencyStats(latency) {
        // 이동 평균
        this.stats.avgLatency = this.stats.avgLatency * 0.9 + latency * 0.1;
        this.stats.maxLatency = Math.max(this.stats.maxLatency, latency);

        // 레이턴시가 너무 높으면 경고
        if (latency > this.config.sendIntervalMs * 0.8) {
            console.warn(`[audiomixer.js] 높은 레이턴시: ${latency.toFixed(2)}ms`);
        }
    }

    async enableMic() {
        try {
            // 최적화된 마이크 설정
            this.micStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    sampleRate: { exact: this.config.sampleRate },
                    channelCount: { exact: this.config.channels },
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true,
                    latency: 0,
                    sampleSize: 16
                }
            });

            const track = this.micStream.getAudioTracks()[0];
            const settings = track.getSettings();
            console.log('[audiomixer.js] 마이크 설정:', settings);

            this.micSource = this.audioContext.createMediaStreamSource(this.micStream);
            this.micSource.connect(this.micGain);

            console.log('[audiomixer.js] 마이크 활성화');
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 마이크 오류:', error);
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

    // 호환성 메서드
    startRecording() {
        console.log('[audiomixer.js] PCM 모드 - 자동 시작');
    }

    stopRecording() {
        console.log('[audiomixer.js] PCM 모드 - 자동 중지');
    }

    setVolumes(mic, media, tts) {
        if (this.micGain) {
            this.micGain.gain.value = mic;
        }
    }

    async loadMediaPlaylist(urls) { return true; }
    async loadTtsPlaylist(urls) { return true; }

    async dispose() {
        if (this.isDisposing) return;

        this.isDisposing = true;
        this.isActive = false;
        console.log('[audiomixer.js] Dispose 시작');

        try {
            // 타이머 정리
            if (this.sendTimer) {
                clearTimeout(this.sendTimer);
                this.sendTimer = null;
            }

            // 마이크 중지
            this.disableMic();

            // 오디오 노드 정리
            if (this.scriptProcessor) {
                try {
                    this.scriptProcessor.disconnect();
                    this.scriptProcessor.onaudioprocess = null;
                } catch (e) { }
                this.scriptProcessor = null;
            }

            if (this.micGain) {
                try { this.micGain.disconnect(); } catch (e) { }
                this.micGain = null;
            }

            // AudioContext 닫기
            if (this.audioContext && this.audioContext.state !== 'closed') {
                try {
                    await this.audioContext.close();
                } catch (e) { }
                this.audioContext = null;
            }

            // 버퍼 정리
            this.pcmRingBuffer = [];
            this.dotNetRef = null;

            console.log('[audiomixer.js] Dispose 완료, 최종 통계:', this.stats);

        } catch (error) {
            console.error('[audiomixer.js] Dispose 오류:', error);
        }
    }

    getStats() {
        const bufferUsage = ((this.bufferWriteIndex - this.bufferReadIndex +
            this.maxBufferChunks) % this.maxBufferChunks);
        return {
            bufferUsage: `${bufferUsage}/${this.maxBufferChunks}`,
            capturedChunks: this.stats.capturedChunks,
            sentPackets: this.stats.sentPackets,
            droppedChunks: this.stats.droppedChunks,
            dropRate: this.stats.capturedChunks > 0
                ? (this.stats.droppedChunks / this.stats.capturedChunks * 100).toFixed(2) + '%'
                : '0%',
            avgLatency: this.stats.avgLatency.toFixed(2) + 'ms',
            maxLatency: this.stats.maxLatency.toFixed(2) + 'ms'
        };
    }
}

// 전역 인스턴스
let mixerInstance = null;

// 외부 진입점
export async function createMixer(dotNetRef, config) {
    console.log('[audiomixer.js] createMixer 호출 (60ms 모드)');

    if (mixerInstance) {
        await mixerInstance.dispose();
        mixerInstance = null;
    }

    mixerInstance = new AudioMixer();
    return await mixerInstance.initialize(dotNetRef, config);
}

export async function enableMic() {
    return mixerInstance ? await mixerInstance.enableMic() : false;
}

export function setVolumes(mic, media, tts) {
    if (mixerInstance) {
        mixerInstance.setVolumes(mic, media, tts);
    }
}

export async function loadMediaPlaylist(urls) {
    return mixerInstance ? await mixerInstance.loadMediaPlaylist(urls) : false;
}

export async function loadTtsPlaylist(urls) {
    return mixerInstance ? await mixerInstance.loadTtsPlaylist(urls) : false;
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
    getStats: () => mixerInstance ? mixerInstance.getStats() : null,
    setBufferSize: (size) => {
        if (mixerInstance) {
            mixerInstance.maxBufferChunks = size;
            console.log(`[audiomixer.js] 버퍼 크기: ${size} 청크`);
        }
    },
    forceKill: async () => {
        if (mixerInstance) {
            mixerInstance.isActive = false;
            mixerInstance.isDisposing = true;
            await mixerInstance.dispose();
            mixerInstance = null;
        }
        console.log('[audiomixer.js] 강제 종료');
    }
};