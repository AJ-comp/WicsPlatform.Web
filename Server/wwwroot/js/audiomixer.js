// Server/wwwroot/js/audiomixer.js

// 🔧 디버그 플래그 - false로 설정하면 모든 로그 출력 안 됨
const DEBUG_MODE = false;

// 조건부 로그 함수
const debugLog = (...args) => {
    if (DEBUG_MODE) console.log(...args);
};
const debugWarn = (...args) => {
    if (DEBUG_MODE) console.warn(...args);
};
const debugError = (...args) => {
    console.error(...args); // 에러는 항상 출력
};

debugLog('[audiomixer.js] AudioWorklet 버전 v3.0 - 타이머 제거, 즉시 전송');

class AudioMixer {
    constructor() {
        this.audioContext = null;
        this.workletNode = null;
        this.micStream = null;
        this.micSource = null;
        this.dotNetRef = null;

        // PCM 버퍼 관리
        this.pcmBuffer = [];
        this.totalSamples = 0;

        // 상태
        this.isActive = false;
        this.isDisposing = false;
        this.isSending = false; // 전송 중 플래그

        // 설정 (기본값)
        this.config = {
            sampleRate: 48000,
            channels: 1,
            sendIntervalMs: 60,  // 참고용 (실제로는 사용 안함)
            samplesPerSend: 2880, // 60ms @ 48kHz
            micVolume: 1.0
        };

        // 통계
        this.stats = {
            packetsReceived: 0,
            packetsSent: 0,
            samplesReceived: 0,
            samplesSent: 0,
            bufferSize: 0,
            maxBufferSize: 0,
            minBufferSize: Infinity,
            lastResetTime: Date.now(),
            bufferHistory: [],
            // 타이밍 통계
            lastReceiveTime: 0,
            lastSendTime: 0,
            receiveIntervals: [],
            sendIntervals: []
        };
    }

    getTimestamp() {
        return new Date().toISOString().substr(11, 12); // HH:MM:SS.mmm
    }

    async initialize(dotNetRef, config = {}) {
        const timestamp = this.getTimestamp();
        debugLog(`[${timestamp}] 🚀 AudioWorklet 초기화 시작 (타이머 없는 버전)`, config);

        try {
            this.dotNetRef = dotNetRef;

            // 설정 병합 및 동적 계산
            this.config = {
                sampleRate: config.sampleRate || 48000,
                channels: config.channels || 1,
                sendIntervalMs: config.timeslice || 60,
                micVolume: config.micVolume || 1.0
            };

            // 샘플레이트에 따른 전송 샘플 수 자동 계산
            this.config.samplesPerSend = Math.floor(
                (this.config.sampleRate * this.config.sendIntervalMs) / 1000
            );

            debugLog(`[${timestamp}] 📊 설정 완료:`, {
                sampleRate: `${this.config.sampleRate}Hz`,
                samplesPerSend: `${this.config.samplesPerSend} 샘플`,
                targetLatency: `${this.config.sendIntervalMs}ms`
            });

            this.isActive = true;

            // AudioContext 생성
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: this.config.sampleRate,
                latencyHint: 'interactive'
            });

            if (this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }

            // 실제 샘플레이트 확인
            const actualSampleRate = this.audioContext.sampleRate;
            if (actualSampleRate !== this.config.sampleRate) {
                debugWarn(`[${timestamp}] ⚠️ 샘플레이트 불일치! 요청: ${this.config.sampleRate}Hz, 실제: ${actualSampleRate}Hz`);

                this.config.sampleRate = actualSampleRate;
                this.config.samplesPerSend = Math.floor(
                    (actualSampleRate * this.config.sendIntervalMs) / 1000
                );

                if (this.dotNetRef) {
                    await this.dotNetRef.invokeMethodAsync('OnAudioConfigurationDetected', {
                        sampleRate: actualSampleRate,
                        channelCount: this.config.channels,
                        samplesPerSend: this.config.samplesPerSend
                    });
                }
            }

            debugLog(`[${timestamp}] 🎯 실제 AudioContext 샘플레이트: ${this.audioContext.sampleRate}Hz`);

            // AudioWorklet 모듈 로드
            await this.audioContext.audioWorklet.addModule('./js/pcm-processor.js');

            // AudioWorkletNode 생성
            this.workletNode = new AudioWorkletNode(this.audioContext, 'pcm-capture', {
                numberOfInputs: 1,
                numberOfOutputs: 0,
                processorOptions: {
                    sampleRate: this.config.sampleRate
                }
            });

            // 메시지 수신 핸들러
            this.workletNode.port.onmessage = (event) => {
                if (event.data.type === 'audio') {
                    this.handleAudioData(event.data.pcm);
                } else if (event.data.type === 'debug') {
                    const timestamp = this.getTimestamp();
                    debugLog(`[${timestamp}] [AudioWorklet]`, event.data.message);
                }
            };

            // 버퍼 모니터링 시작 (타이머는 모니터링용으로만)
            this.startBufferMonitoring();

            debugLog(`[${timestamp}] ✅ AudioWorklet 초기화 완료 - 즉시 전송 모드`);
            return true;

        } catch (error) {
            const timestamp = this.getTimestamp();
            debugError(`[${timestamp}] ❌ 초기화 실패:`, error);
            this.isActive = false;
            return false;
        }
    }

    handleAudioData(arrayBuffer) {
        if (!this.isActive || this.isDisposing) return;

        const now = Date.now();
        const timestamp = this.getTimestamp();

        // 수신 간격 계산
        if (this.stats.lastReceiveTime) {
            const interval = now - this.stats.lastReceiveTime;
            this.stats.receiveIntervals.push(interval);
            if (this.stats.receiveIntervals.length > 100) {
                this.stats.receiveIntervals.shift();
            }
        }
        this.stats.lastReceiveTime = now;

        // ArrayBuffer → Int16Array
        const pcm16 = new Int16Array(arrayBuffer);
        const samplesReceived = pcm16.length;
        const beforeSamples = this.totalSamples;

        // 버퍼에 추가
        this.pcmBuffer.push(pcm16);
        this.totalSamples += samplesReceived;
        this.stats.samplesReceived += samplesReceived;
        this.stats.packetsReceived++;

        // 수신 로깅 (매 10번째마다)
        if (this.stats.packetsReceived % 10 === 0) {
            const avgInterval = this.stats.receiveIntervals.length > 0
                ? (this.stats.receiveIntervals.reduce((a, b) => a + b, 0) / this.stats.receiveIntervals.length).toFixed(1)
                : 'N/A';

            debugLog(`📥 [${timestamp}] 수신: +${samplesReceived} | 버퍼: ${beforeSamples} → ${this.totalSamples} (${(this.totalSamples / this.config.sampleRate * 1000).toFixed(1)}ms) | 평균간격: ${avgInterval}ms`);
        }

        // 통계 업데이트
        this.stats.bufferSize = this.totalSamples;
        if (this.totalSamples > this.stats.maxBufferSize) {
            this.stats.maxBufferSize = this.totalSamples;
        }
        if (this.totalSamples < this.stats.minBufferSize) {
            this.stats.minBufferSize = this.totalSamples;
        }

        // 🔥 즉시 전송 로직 - 960샘플 이상이면 바로 전송
        let sendCount = 0;
        while (this.totalSamples >= this.config.samplesPerSend && !this.isSending) {
            const beforeSend = this.totalSamples;
            this.sendPCMData();
            sendCount++;

            debugLog(`🚀 [${timestamp}] 즉시 전송 #${sendCount} | ${beforeSend} → ${this.totalSamples} 샘플`);

            // 무한 루프 방지
            if (sendCount > 5) {
                debugWarn(`[${timestamp}] ⚠️ 한 번에 너무 많은 전송 (${sendCount}회)`);
                break;
            }
        }

        // 버퍼 상태 체크
        const bufferMs = this.totalSamples / this.config.sampleRate * 1000;
        if (bufferMs > 200) {
            debugWarn(`[${timestamp}] ⚠️ 버퍼 과다! ${this.totalSamples} 샘플 (${bufferMs.toFixed(1)}ms)`);
        }
    }

    sendPCMData() {
        if (this.isSending) {
            debugWarn(`[${this.getTimestamp()}] ⏭️ 이미 전송 중 - 스킵`);
            return;
        }

        try {
            this.isSending = true;
            const now = Date.now();
            const timestamp = this.getTimestamp();

            // 전송 간격 계산
            if (this.stats.lastSendTime) {
                const interval = now - this.stats.lastSendTime;
                this.stats.sendIntervals.push(interval);
                if (this.stats.sendIntervals.length > 100) {
                    this.stats.sendIntervals.shift();
                }
            }
            this.stats.lastSendTime = now;

            const samplesToSend = this.config.samplesPerSend;
            const beforeSamples = this.totalSamples;

            // 버퍼에서 정확한 샘플 수 추출
            const combinedPCM = new Int16Array(samplesToSend);
            let offset = 0;
            let remaining = samplesToSend;
            let chunksUsed = 0;

            while (remaining > 0 && this.pcmBuffer.length > 0) {
                const chunk = this.pcmBuffer[0];
                chunksUsed++;

                if (chunk.length <= remaining) {
                    combinedPCM.set(chunk, offset);
                    offset += chunk.length;
                    remaining -= chunk.length;
                    this.pcmBuffer.shift();
                } else {
                    combinedPCM.set(chunk.slice(0, remaining), offset);
                    this.pcmBuffer[0] = chunk.slice(remaining);
                    remaining = 0;
                }
            }

            // 총 샘플 수 업데이트
            this.totalSamples -= samplesToSend;
            this.stats.samplesSent += samplesToSend;
            this.stats.packetsSent++;

            // 평균 전송 간격 계산
            const avgSendInterval = this.stats.sendIntervals.length > 0
                ? (this.stats.sendIntervals.reduce((a, b) => a + b, 0) / this.stats.sendIntervals.length).toFixed(1)
                : 'N/A';

            debugLog(`📤 [${timestamp}] 전송 완료 #${this.stats.packetsSent} | 버퍼: ${beforeSamples} → ${this.totalSamples} 샘플 (${(this.totalSamples / this.config.sampleRate * 1000).toFixed(1)}ms) | 평균간격: ${avgSendInterval}ms`);

            // Int16Array → Base64
            const byteArray = new Uint8Array(combinedPCM.buffer);
            let base64 = '';
            const chunkSize = 32768;

            for (let i = 0; i < byteArray.length; i += chunkSize) {
                const chunk = byteArray.slice(i, Math.min(i + chunkSize, byteArray.length));
                base64 += btoa(String.fromCharCode(...chunk));
            }

            // C#으로 전송 (비동기지만 await 안함 - 빠른 처리를 위해)
            this.dotNetRef.invokeMethodAsync('OnMixedAudioCaptured', base64);

            // 통계 출력 (10번마다)
            if (this.stats.packetsSent % 10 === 0) {
                this.printStatistics();
            }

        } catch (error) {
            const timestamp = this.getTimestamp();
            debugError(`[${timestamp}] ❌ PCM 전송 오류:`, error);
        } finally {
            this.isSending = false;
        }
    }

    printStatistics() {
        const timestamp = this.getTimestamp();
        const bufferMs = (this.totalSamples / this.config.sampleRate * 1000).toFixed(1);
        const avgReceiveInterval = this.stats.receiveIntervals.length > 0
            ? (this.stats.receiveIntervals.reduce((a, b) => a + b, 0) / this.stats.receiveIntervals.length).toFixed(1)
            : 'N/A';
        const avgSendInterval = this.stats.sendIntervals.length > 0
            ? (this.stats.sendIntervals.reduce((a, b) => a + b, 0) / this.stats.sendIntervals.length).toFixed(1)
            : 'N/A';

        debugLog(`📊 [${timestamp}] 통계`, {
            현재버퍼: `${this.totalSamples} 샘플 (${bufferMs}ms)`,
            수신: {
                packets: this.stats.packetsReceived,
                samples: this.stats.samplesReceived,
                avgInterval: `${avgReceiveInterval}ms`
            },
            전송: {
                packets: this.stats.packetsSent,
                samples: this.stats.samplesSent,
                avgInterval: `${avgSendInterval}ms`
            },
            비율: `수신/전송 = ${(this.stats.samplesReceived / this.stats.samplesSent).toFixed(3)}`
        });
    }

    startBufferMonitoring() {
        // 1초마다 상태 요약 (모니터링용으로만)
        this.monitorInterval = setInterval(() => {
            const timestamp = this.getTimestamp();
            const bufferMs = (this.totalSamples / this.config.sampleRate * 1000).toFixed(1);
            const maxBufferMs = (this.stats.maxBufferSize / this.config.sampleRate * 1000).toFixed(1);
            const minBufferMs = this.stats.minBufferSize === Infinity ? '0' :
                (this.stats.minBufferSize / this.config.sampleRate * 1000).toFixed(1);

            const elapsedSeconds = (Date.now() - this.stats.lastResetTime) / 1000;
            const receiveRate = this.stats.samplesReceived / elapsedSeconds;
            const sendRate = this.stats.samplesSent / elapsedSeconds;

            debugLog(`⏱️ [${timestamp}] 1초 모니터링`, {
                현재버퍼: `${this.totalSamples} 샘플 (${bufferMs}ms)`,
                최소최대: `${minBufferMs}ms ~ ${maxBufferMs}ms`,
                수신율: `${receiveRate.toFixed(0)} 샘플/초`,
                전송율: `${sendRate.toFixed(0)} 샘플/초`,
                패킷: `수신:${this.stats.packetsReceived}, 전송:${this.stats.packetsSent}`,
                효율: `${((sendRate / receiveRate) * 100).toFixed(1)}%`
            });

            // 30초마다 통계 리셋
            if (elapsedSeconds > 30) {
                this.stats.lastResetTime = Date.now();
                this.stats.samplesReceived = 0;
                this.stats.samplesSent = 0;
                this.stats.minBufferSize = Infinity;
                this.stats.maxBufferSize = 0;
                debugLog(`[${timestamp}] 📊 통계 리셋`);
            }

        }, 1000);
    }

    async enableMic() {
        try {
            const timestamp = this.getTimestamp();

            // 이미 마이크가 활성화되어 있는지 확인
            if (this.micStream && this.micSource) {
                const tracks = this.micStream.getAudioTracks();
                if (tracks.length > 0 && tracks[0].enabled && tracks[0].readyState === 'live') {
                    debugLog(`[${timestamp}] 🎤 마이크가 이미 활성화되어 있습니다. 스킵합니다.`);
                    return true;
                }
            }

            const constraints = {
                audio: {
                    sampleRate: { ideal: this.config.sampleRate },
                    channelCount: { ideal: this.config.channels },
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            };

            debugLog(`[${timestamp}] 🎤 마이크 요청:`, constraints);

            this.micStream = await navigator.mediaDevices.getUserMedia(constraints);

            const audioTrack = this.micStream.getAudioTracks()[0];
            const settings = audioTrack.getSettings();
            debugLog(`[${timestamp}] 🎤 마이크 실제 설정:`, settings);

            // 마이크 → AudioWorklet 연결
            this.micSource = this.audioContext.createMediaStreamSource(this.micStream);

            if (this.config.micVolume !== 1.0) {
                const gainNode = this.audioContext.createGain();
                gainNode.gain.value = this.config.micVolume;
                this.micSource.connect(gainNode);
                gainNode.connect(this.workletNode);
                debugLog(`[${timestamp}] 🎤 마이크 볼륨: ${(this.config.micVolume * 100).toFixed(0)}%`);
            } else {
                this.micSource.connect(this.workletNode);
            }

            debugLog(`[${timestamp}] ✅ 마이크 활성화 완료`);
            return true;

        } catch (error) {
            const timestamp = this.getTimestamp();
            debugError(`[${timestamp}] ❌ 마이크 활성화 실패:`, error);

            if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
                if (this.dotNetRef) {
                    await this.dotNetRef.invokeMethodAsync('ShowMicHelp');
                }
            }

            return false;
        }
    }

    disableMic() {
        const timestamp = this.getTimestamp();

        if (this.micSource) {
            try {
                this.micSource.disconnect();
            } catch (e) { }
            this.micSource = null;
        }

        if (this.micStream) {
            this.micStream.getTracks().forEach(track => {
                try {
                    track.stop();
                } catch (e) { }
            });
            this.micStream = null;
        }

        debugLog(`[${timestamp}] 🎤 마이크 비활성화`);
    }

    setVolumes(mic, media, tts) {
        const timestamp = this.getTimestamp();
        this.config.micVolume = mic;
        debugLog(`[${timestamp}] 🔊 볼륨 설정:`, { mic, media, tts });
    }

    getBufferStatus() {
        const bufferMs = (this.totalSamples / this.config.sampleRate * 1000).toFixed(1);
        const maxBufferMs = (this.stats.maxBufferSize / this.config.sampleRate * 1000).toFixed(1);
        const minBufferMs = this.stats.minBufferSize === Infinity ? '0' :
            (this.stats.minBufferSize / this.config.sampleRate * 1000).toFixed(1);

        const avgReceiveInterval = this.stats.receiveIntervals.length > 0
            ? (this.stats.receiveIntervals.reduce((a, b) => a + b, 0) / this.stats.receiveIntervals.length).toFixed(1)
            : 'N/A';
        const avgSendInterval = this.stats.sendIntervals.length > 0
            ? (this.stats.sendIntervals.reduce((a, b) => a + b, 0) / this.stats.sendIntervals.length).toFixed(1)
            : 'N/A';

        return {
            samples: this.totalSamples,
            milliseconds: bufferMs,
            packetsReceived: this.stats.packetsReceived,
            packetsSent: this.stats.packetsSent,
            samplesReceived: this.stats.samplesReceived,
            samplesSent: this.stats.samplesSent,
            maxBufferMs: maxBufferMs,
            minBufferMs: minBufferMs,
            avgReceiveInterval: avgReceiveInterval,
            avgSendInterval: avgSendInterval,
            sampleRate: this.config.sampleRate,
            samplesPerSend: this.config.samplesPerSend
        };
    }

    async dispose() {
        if (this.isDisposing) return;

        this.isDisposing = true;
        this.isActive = false;

        const timestamp = this.getTimestamp();
        debugLog(`[${timestamp}] 🔚 Dispose 시작`);
        debugLog(`[${timestamp}] 📊 최종 통계:`, this.getBufferStatus());

        try {
            // 모니터링 타이머만 정리
            if (this.monitorInterval) {
                clearInterval(this.monitorInterval);
                this.monitorInterval = null;
            }

            // 마이크 정리
            this.disableMic();

            // AudioWorklet 정리
            if (this.workletNode) {
                this.workletNode.disconnect();
                this.workletNode.port.onmessage = null;
                this.workletNode = null;
            }

            // AudioContext 정리
            if (this.audioContext) {
                await this.audioContext.close();
                this.audioContext = null;
            }

            // 버퍼 정리
            this.pcmBuffer = [];
            this.totalSamples = 0;

            // 참조 정리
            this.dotNetRef = null;

            debugLog(`[${timestamp}] ✅ Dispose 완료`);

        } catch (error) {
            debugError(`[${timestamp}] ❌ Dispose 오류:`, error);
        }
    }
}

// 전역 인스턴스
let mixerInstance = null;

// ==== 외부 진입점 (C#에서 호출) ====

// 🔥 마이크 활성화 상태 확인 함수 추가
export function isMicrophoneEnabled() {
    const timestamp = new Date().toISOString().substr(11, 12);

    if (!mixerInstance) {
        debugLog(`[${timestamp}] 🎤 마이크 상태: 믹서 없음 (false)`);
        return false;
    }

    if (!mixerInstance.micStream) {
        debugLog(`[${timestamp}] 🎤 마이크 상태: 스트림 없음 (false)`);
        return false;
    }

    const tracks = mixerInstance.micStream.getAudioTracks();
    const isEnabled = tracks.length > 0 &&
        tracks.some(track => track.enabled && track.readyState === 'live');

    debugLog(`[${timestamp}] 🎤 마이크 상태 확인: ${isEnabled ? 'ACTIVE' : 'INACTIVE'}`);
    return isEnabled;
}

export async function createMixer(dotNetRef, config) {
    const timestamp = new Date().toISOString().substr(11, 12);
    debugLog(`[${timestamp}] 🚀 createMixer 호출됨 (타이머 없는 버전)`, config);

    // 이미 믹서가 있고 마이크가 활성화되어 있으면 스킵
    if (mixerInstance && isMicrophoneEnabled()) {
        debugLog(`[${timestamp}] 🎤 믹서와 마이크가 이미 활성화되어 있습니다. 기존 인스턴스 유지`);

        // 볼륨 설정만 업데이트
        if (config.micVolume !== undefined) {
            mixerInstance.setVolumes(config.micVolume, config.mediaVolume || 1.0, config.ttsVolume || 1.0);
        }

        return true;
    }

    if (mixerInstance) {
        await mixerInstance.dispose();
        mixerInstance = null;
    }

    mixerInstance = new AudioMixer();
    return await mixerInstance.initialize(dotNetRef, config);
}

export async function enableMic() {
    if (!mixerInstance) {
        debugLog(`[${new Date().toISOString().substr(11, 12)}] ❌ enableMic: 믹서 인스턴스가 없습니다`);
        return false;
    }

    return await mixerInstance.enableMic();
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

// ==== 호환성 스텁 ====

export async function loadMediaPlaylist(urls) {
    const timestamp = new Date().toISOString().substr(11, 12);
    debugLog(`[${timestamp}] 미디어 재생은 AudioWorklet 버전에서 미구현`);
    return false;
}

export async function loadTtsPlaylist(urls) {
    const timestamp = new Date().toISOString().substr(11, 12);
    debugLog(`[${timestamp}] TTS는 AudioWorklet 버전에서 미구현`);
    return false;
}

export function startRecording() {
    const timestamp = new Date().toISOString().substr(11, 12);
    debugLog(`[${timestamp}] startRecording - AudioWorklet에서는 자동`);
}

export function stopRecording() {
    const timestamp = new Date().toISOString().substr(11, 12);
    debugLog(`[${timestamp}] stopRecording - AudioWorklet에서는 자동`);
}

// ==== 디버깅 헬퍼 ====

window.mixerDebug = {
    getInstance: () => mixerInstance,
    getStatus: () => mixerInstance ? mixerInstance.getBufferStatus() : null,

    // 마이크 상태 확인 추가
    isMicEnabled: () => isMicrophoneEnabled(),

    // 통계 보기
    getStats: () => {
        if (!mixerInstance) return null;
        return {
            buffer: mixerInstance.totalSamples,
            bufferMs: (mixerInstance.totalSamples / mixerInstance.config.sampleRate * 1000).toFixed(1),
            received: mixerInstance.stats.packetsReceived,
            sent: mixerInstance.stats.packetsSent,
            ratio: (mixerInstance.stats.samplesReceived / mixerInstance.stats.samplesSent).toFixed(3)
        };
    },

    // 강제 통계 출력
    printStats: () => {
        if (mixerInstance) {
            mixerInstance.printStatistics();
        }
    },

    // 강제 종료
    forceDispose: async () => {
        if (mixerInstance) {
            mixerInstance.isActive = false;
            mixerInstance.isDisposing = true;
            await mixerInstance.dispose();
            mixerInstance = null;
        }
        const timestamp = new Date().toISOString().substr(11, 12);
        debugLog(`[${timestamp}] 강제 종료 완료`);
    }
};

debugLog('[audiomixer.js] 📊 모듈 로드 완료 - 타이머 없는 즉시 전송 버전');
debugLog('  mixerDebug.getStatus() - 현재 상태');
debugLog('  mixerDebug.getStats() - 간단 통계');
debugLog('  mixerDebug.printStats() - 상세 통계');
debugLog('  mixerDebug.isMicEnabled() - 마이크 활성화 상태');