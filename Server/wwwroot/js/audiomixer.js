// Server/wwwroot/js/audiomixer.js
console.log('[audiomixer.js] AudioWorklet 버전 v2.0 - 동적 샘플레이트 지원');

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

        // 설정 (기본값)
        this.config = {
            sampleRate: 48000,
            channels: 1,
            sendIntervalMs: 60,  // Opus 60ms
            samplesPerSend: 2880, // 60ms @ 48kHz
            micVolume: 1.0
        };

        // 통계
        this.stats = {
            packetsReceived: 0,
            packetsSent: 0,
            bufferSize: 0,
            maxBufferSize: 0,
            lastResetTime: Date.now()
        };
    }

    async initialize(dotNetRef, config = {}) {
        console.log('[audiomixer.js] AudioWorklet 초기화 시작', config);

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

            console.log(`[audiomixer.js] 설정 완료:`, {
                sampleRate: `${this.config.sampleRate}Hz`,
                sendInterval: `${this.config.sendIntervalMs}ms`,
                samplesPerSend: `${this.config.samplesPerSend} 샘플`,
                processInterval: `${(128 / this.config.sampleRate * 1000).toFixed(2)}ms`
            });

            this.isActive = true;

            // AudioContext 생성 (지정된 샘플레이트로)
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: this.config.sampleRate,
                latencyHint: 'interactive'
            });

            if (this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }

            // 실제 샘플레이트 확인 (브라우저가 다른 값을 사용할 수 있음)
            const actualSampleRate = this.audioContext.sampleRate;
            if (actualSampleRate !== this.config.sampleRate) {
                console.warn(`[audiomixer.js] 샘플레이트 불일치! 요청: ${this.config.sampleRate}Hz, 실제: ${actualSampleRate}Hz`);

                // 실제 값으로 재계산
                this.config.sampleRate = actualSampleRate;
                this.config.samplesPerSend = Math.floor(
                    (actualSampleRate * this.config.sendIntervalMs) / 1000
                );

                // C#에 실제 설정 알림
                if (this.dotNetRef) {
                    await this.dotNetRef.invokeMethodAsync('OnAudioConfigurationDetected', {
                        sampleRate: actualSampleRate,
                        channelCount: this.config.channels,
                        samplesPerSend: this.config.samplesPerSend
                    });
                }
            }

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
                    console.log('[AudioWorklet]', event.data.message);
                }
            };

            // 전송 타이머 설정
            this.setupSender();

            console.log('[audiomixer.js] AudioWorklet 초기화 완료');
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 초기화 실패:', error);
            this.isActive = false;
            return false;
        }
    }

    handleAudioData(arrayBuffer) {
        if (!this.isActive || this.isDisposing) return;

        // ArrayBuffer → Int16Array
        const pcm16 = new Int16Array(arrayBuffer);

        // 버퍼에 추가
        this.pcmBuffer.push(pcm16);
        this.totalSamples += pcm16.length;

        this.stats.packetsReceived++;
        this.stats.bufferSize = this.totalSamples;

        // 최대 버퍼 크기 기록
        if (this.totalSamples > this.stats.maxBufferSize) {
            this.stats.maxBufferSize = this.totalSamples;
        }

        // 디버깅 (샘플레이트에 따라 주기 조정)
        const debugInterval = Math.floor(this.config.sampleRate / 128); // 약 1초마다
        if (this.stats.packetsReceived % debugInterval === 0) {
            const bufferMs = (this.totalSamples / this.config.sampleRate * 1000).toFixed(1);
            console.log(`[audiomixer.js] 버퍼 상태: ${this.totalSamples} 샘플 (${bufferMs}ms)`);
        }
    }

    setupSender() {
        this.sendInterval = setInterval(async () => {
            if (!this.isActive || this.isDisposing || !this.dotNetRef) return;

            // 충분한 샘플이 있으면 전송
            if (this.totalSamples >= this.config.samplesPerSend) {
                await this.sendPCMData();
            } else if (this.totalSamples > 0) {
                // 버퍼 부족 상태 로깅 (디버깅용)
                const shortage = this.config.samplesPerSend - this.totalSamples;
                const shortageMs = (shortage / this.config.sampleRate * 1000).toFixed(1);

                // 부족이 128 샘플 이상이면 경고
                if (shortage > 128) {
                    console.log(`[audiomixer.js] 버퍼 부족: ${shortage} 샘플 (${shortageMs}ms) 더 필요`);
                }
            }

        }, this.config.sendIntervalMs);
    }

    async sendPCMData() {
        try {
            const samplesToSend = this.config.samplesPerSend;

            // 버퍼에서 정확한 샘플 수 추출
            const combinedPCM = new Int16Array(samplesToSend);
            let offset = 0;
            let remaining = samplesToSend;

            while (remaining > 0 && this.pcmBuffer.length > 0) {
                const chunk = this.pcmBuffer[0];

                if (chunk.length <= remaining) {
                    // 청크 전체 사용
                    combinedPCM.set(chunk, offset);
                    offset += chunk.length;
                    remaining -= chunk.length;
                    this.pcmBuffer.shift();
                } else {
                    // 청크 일부만 사용
                    combinedPCM.set(chunk.slice(0, remaining), offset);
                    this.pcmBuffer[0] = chunk.slice(remaining);
                    remaining = 0;
                }
            }

            // 총 샘플 수 업데이트
            this.totalSamples -= samplesToSend;

            // Int16Array → Base64
            const byteArray = new Uint8Array(combinedPCM.buffer);
            let base64 = '';
            const chunkSize = 32768;

            for (let i = 0; i < byteArray.length; i += chunkSize) {
                const chunk = byteArray.slice(i, Math.min(i + chunkSize, byteArray.length));
                base64 += btoa(String.fromCharCode(...chunk));
            }

            // C#으로 전송
            await this.dotNetRef.invokeMethodAsync('OnMixedAudioCaptured', base64);

            this.stats.packetsSent++;

            // 통계 출력 (10번마다)
            if (this.stats.packetsSent % 10 === 0) {
                const bufferMs = (this.totalSamples / this.config.sampleRate * 1000).toFixed(1);
                const maxBufferMs = (this.stats.maxBufferSize / this.config.sampleRate * 1000).toFixed(1);

                console.log(`[audiomixer.js] 전송 통계:`, {
                    sent: this.stats.packetsSent,
                    bufferRemaining: `${this.totalSamples} 샘플`,
                    bufferMs: `${bufferMs}ms`,
                    maxBufferMs: `${maxBufferMs}ms`,
                    sampleRate: `${this.config.sampleRate}Hz`
                });

                // 1분마다 최대 버퍼 리셋
                if (Date.now() - this.stats.lastResetTime > 60000) {
                    this.stats.maxBufferSize = this.totalSamples;
                    this.stats.lastResetTime = Date.now();
                }
            }

        } catch (error) {
            console.error('[audiomixer.js] PCM 전송 오류:', error);
        }
    }

    async enableMic() {
        try {
            // 마이크 권한 요청 (샘플레이트 명시)
            const constraints = {
                audio: {
                    sampleRate: { ideal: this.config.sampleRate },
                    channelCount: { ideal: this.config.channels },
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            };

            console.log('[audiomixer.js] 마이크 요청:', constraints);

            this.micStream = await navigator.mediaDevices.getUserMedia(constraints);

            // 실제 획득된 트랙 설정 확인
            const audioTrack = this.micStream.getAudioTracks()[0];
            const settings = audioTrack.getSettings();
            console.log('[audiomixer.js] 마이크 실제 설정:', settings);

            // 마이크 → AudioWorklet 연결
            this.micSource = this.audioContext.createMediaStreamSource(this.micStream);

            // 볼륨 조절용 GainNode (옵션)
            if (this.config.micVolume !== 1.0) {
                const gainNode = this.audioContext.createGain();
                gainNode.gain.value = this.config.micVolume;
                this.micSource.connect(gainNode);
                gainNode.connect(this.workletNode);
                console.log(`[audiomixer.js] 마이크 볼륨: ${(this.config.micVolume * 100).toFixed(0)}%`);
            } else {
                this.micSource.connect(this.workletNode);
            }

            console.log('[audiomixer.js] 마이크 활성화 완료');
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 마이크 활성화 실패:', error);

            // 권한 거부 처리
            if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
                if (this.dotNetRef) {
                    await this.dotNetRef.invokeMethodAsync('ShowMicHelp');
                }
            }

            return false;
        }
    }

    disableMic() {
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

        console.log('[audiomixer.js] 마이크 비활성화');
    }

    setVolumes(mic, media, tts) {
        // 실시간 볼륨 조절 (향후 구현)
        this.config.micVolume = mic;
        console.log('[audiomixer.js] 볼륨 설정:', { mic, media, tts });
    }

    getBufferStatus() {
        const bufferMs = (this.totalSamples / this.config.sampleRate * 1000).toFixed(1);
        const maxBufferMs = (this.stats.maxBufferSize / this.config.sampleRate * 1000).toFixed(1);

        return {
            samples: this.totalSamples,
            milliseconds: bufferMs,
            packetsReceived: this.stats.packetsReceived,
            packetsSent: this.stats.packetsSent,
            maxBufferMs: maxBufferMs,
            sampleRate: this.config.sampleRate,
            samplesPerSend: this.config.samplesPerSend,
            expectedLatency: `${this.config.sendIntervalMs + parseFloat(bufferMs)}ms`
        };
    }

    async dispose() {
        if (this.isDisposing) return;

        this.isDisposing = true;
        this.isActive = false;

        console.log('[audiomixer.js] Dispose 시작');
        console.log('[audiomixer.js] 최종 통계:', this.getBufferStatus());

        try {
            // 타이머 정리
            if (this.sendInterval) {
                clearInterval(this.sendInterval);
                this.sendInterval = null;
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

            console.log('[audiomixer.js] Dispose 완료');

        } catch (error) {
            console.error('[audiomixer.js] Dispose 오류:', error);
        }
    }
}

// 전역 인스턴스
let mixerInstance = null;

// ==== 외부 진입점 (C#에서 호출) ====

export async function createMixer(dotNetRef, config) {
    console.log('[audiomixer.js] createMixer 호출됨', config);

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

export async function dispose() {
    if (mixerInstance) {
        await mixerInstance.dispose();
        mixerInstance = null;
    }
}

// ==== 호환성 스텁 (기존 코드 호환) ====

export async function loadMediaPlaylist(urls) {
    console.log('[audiomixer.js] 미디어 재생은 AudioWorklet 버전에서 미구현');
    return false;
}

export async function loadTtsPlaylist(urls) {
    console.log('[audiomixer.js] TTS는 AudioWorklet 버전에서 미구현');
    return false;
}

export function startRecording() {
    console.log('[audiomixer.js] startRecording - AudioWorklet에서는 자동');
}

export function stopRecording() {
    console.log('[audiomixer.js] stopRecording - AudioWorklet에서는 자동');
}

// ==== 디버깅 헬퍼 ====

window.mixerDebug = {
    getInstance: () => mixerInstance,
    getStatus: () => mixerInstance ? mixerInstance.getBufferStatus() : null,

    // 샘플레이트 테스트
    testSampleRate: async (rate) => {
        if (!mixerInstance) {
            console.error('Mixer not initialized');
            return;
        }

        console.log(`Testing ${rate}Hz...`);
        const ctx = new AudioContext({ sampleRate: rate });
        console.log(`Requested: ${rate}Hz, Got: ${ctx.sampleRate}Hz`);
        ctx.close();
    },

    // 강제 종료
    forceDispose: async () => {
        if (mixerInstance) {
            mixerInstance.isActive = false;
            mixerInstance.isDisposing = true;
            await mixerInstance.dispose();
            mixerInstance = null;
        }
        console.log('[audiomixer.js] 강제 종료 완료');
    }
};

console.log('[audiomixer.js] 모듈 로드 완료 - mixerDebug.getStatus()로 상태 확인 가능');