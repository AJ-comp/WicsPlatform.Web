// Server/wwwroot/js/audiomixer.js
console.log('[audiomixer.js] 모듈 로드됨 v4.2 - WebSocket 데이터 전송 수정');

class AudioMixer {
    constructor() {
        this.audioContext = null;
        this.merger = null;
        this.destination = null;
        this.mediaRecorder = null;
        this.dotNetRef = null;

        // Gain 노드들
        this.micGain = null;
        this.mediaGain = null;
        this.ttsGain = null;

        // 마이크
        this.micStream = null;
        this.micSource = null;

        // 단일 Audio 객체 (재사용)
        this.sharedAudioElement = null;
        this.sharedSourceNode = null;
        this.currentPlayingType = null; // 'media' or 'tts'

        // 플레이리스트
        this.mediaPlaylist = [];
        this.ttsPlaylist = [];
        this.currentMediaIndex = 0;
        this.currentTtsIndex = 0;

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
        console.log('[audiomixer.js] 초기화 시작');

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

            // 노드 생성
            this.merger = this.audioContext.createChannelMerger(this.config.channels);
            this.destination = this.audioContext.createMediaStreamDestination();

            // Gain 노드
            this.micGain = this.audioContext.createGain();
            this.mediaGain = this.audioContext.createGain();
            this.ttsGain = this.audioContext.createGain();

            this.micGain.gain.value = config.micVolume || 1.0;
            this.mediaGain.gain.value = config.mediaVolume || 1.0;
            this.ttsGain.gain.value = config.ttsVolume || 1.0;

            // 연결
            this.micGain.connect(this.merger);
            this.mediaGain.connect(this.merger);
            this.ttsGain.connect(this.merger);
            this.merger.connect(this.destination);

            // 단일 Audio 엘리먼트 생성
            this.createSharedAudioElement();

            // MediaRecorder
            this.setupMediaRecorder();

            console.log('[audiomixer.js] 초기화 완료');
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 초기화 실패:', error);
            this.isActive = false;
            return false;
        }
    }

    createSharedAudioElement() {
        if (this.sharedAudioElement) return;

        this.sharedAudioElement = new Audio();
        this.sharedAudioElement.crossOrigin = 'anonymous';
        this.sharedAudioElement.preload = 'auto';

        // 소스 노드 생성 (한번만!)
        try {
            this.sharedSourceNode = this.audioContext.createMediaElementSource(this.sharedAudioElement);
        } catch (error) {
            console.error('[audiomixer.js] 소스 노드 생성 실패:', error);
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
                    console.log(`[audiomixer.js] 오디오 청크 수신: ${event.data.size} bytes`);
                }
            };

            // 주기적으로 전송 - setInterval을 인스턴스에 저장
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
            // timeslice 옵션 추가
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

    async loadMediaPlaylist(urls) {
        if (!this.isActive || this.isDisposing) return false;

        if (!Array.isArray(urls)) {
            urls = typeof urls === 'object' ? Object.values(urls) : [];
        }

        if (urls.length === 0) return false;

        this.stopCurrentPlayback();

        this.mediaPlaylist = urls;
        this.currentMediaIndex = 0;
        this.currentPlayingType = 'media';

        if (this.sharedSourceNode) {
            try {
                this.sharedSourceNode.disconnect();
                this.sharedSourceNode.connect(this.mediaGain);
            } catch (e) { }
        }

        console.log(`[audiomixer.js] ${urls.length}개 미디어 로드`);
        return await this.playNext();
    }

    async loadTtsPlaylist(urls) {
        if (!this.isActive || this.isDisposing) return false;

        if (!Array.isArray(urls) || urls.length === 0) return false;

        this.stopCurrentPlayback();

        this.ttsPlaylist = urls;
        this.currentTtsIndex = 0;
        this.currentPlayingType = 'tts';

        if (this.sharedSourceNode) {
            try {
                this.sharedSourceNode.disconnect();
                this.sharedSourceNode.connect(this.ttsGain);
            } catch (e) { }
        }

        return await this.playNext();
    }

    async playNext() {
        if (!this.isActive || this.isDisposing) return false;

        const playlist = this.currentPlayingType === 'media' ? this.mediaPlaylist : this.ttsPlaylist;
        const currentIndex = this.currentPlayingType === 'media' ? this.currentMediaIndex : this.currentTtsIndex;

        if (currentIndex >= playlist.length) {
            // 루프
            if (this.currentPlayingType === 'media') {
                this.currentMediaIndex = 0;
            } else {
                this.currentTtsIndex = 0;
            }

            if (playlist.length > 0 && this.isActive) {
                return await this.playNext();
            }
            return false;
        }

        const url = playlist[currentIndex];
        console.log(`[audiomixer.js] 재생: ${currentIndex + 1}/${playlist.length}`);

        try {
            const audio = this.sharedAudioElement;

            // 이전 이벤트 제거
            audio.onended = null;
            audio.onerror = null;

            // 새 URL 설정
            audio.src = url;

            // 이벤트 설정
            audio.onended = () => {
                if (!this.isActive || this.isDisposing) return;

                if (this.currentPlayingType === 'media') {
                    this.currentMediaIndex++;
                } else {
                    this.currentTtsIndex++;
                }
                this.playNext();
            };

            audio.onerror = () => {
                console.error('[audiomixer.js] 재생 오류:', url);
                if (!this.isActive || this.isDisposing) return;

                if (this.currentPlayingType === 'media') {
                    this.currentMediaIndex++;
                } else {
                    this.currentTtsIndex++;
                }
                this.playNext();
            };

            // 재생
            await audio.play();
            return true;

        } catch (error) {
            console.error('[audiomixer.js] 재생 실패:', error);

            if (this.currentPlayingType === 'media') {
                this.currentMediaIndex++;
            } else {
                this.currentTtsIndex++;
            }

            if (this.isActive && !this.isDisposing) {
                return await this.playNext();
            }
            return false;
        }
    }

    stopCurrentPlayback() {
        if (this.sharedAudioElement) {
            try {
                // 이벤트 리스너 먼저 제거
                this.sharedAudioElement.onended = null;
                this.sharedAudioElement.onerror = null;

                // 재생 중지
                if (!this.sharedAudioElement.paused) {
                    this.sharedAudioElement.pause();
                }

                // 위치 초기화
                this.sharedAudioElement.currentTime = 0;

                // 소스 제거
                this.sharedAudioElement.src = '';

                // 메모리 정리
                this.sharedAudioElement.load();
            } catch (e) {
                console.warn('[audiomixer.js] stopCurrentPlayback 오류:', e);
            }
        }
    }

    setVolumes(mic, media, tts) {
        if (this.micGain) this.micGain.gain.value = mic;
        if (this.mediaGain) this.mediaGain.gain.value = media;
        if (this.ttsGain) this.ttsGain.gain.value = tts;
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

            // 1. 재생 중지
            this.stopCurrentPlayback();

            // 2. 녹음 중지
            this.stopRecording();

            // 3. 마이크 중지
            this.disableMic();

            // 4. 모든 노드 연결 해제
            if (this.sharedSourceNode) {
                try {
                    this.sharedSourceNode.disconnect();
                    this.sharedSourceNode = null;
                } catch (e) {
                    console.warn('[audiomixer.js] 소스 노드 연결 해제 오류:', e);
                }
            }

            // 5. Audio 엘리먼트 완전 정리
            if (this.sharedAudioElement) {
                try {
                    this.sharedAudioElement.onended = null;
                    this.sharedAudioElement.onerror = null;
                    this.sharedAudioElement.pause();
                    this.sharedAudioElement.currentTime = 0;
                    this.sharedAudioElement.src = '';
                    this.sharedAudioElement.load();
                    this.sharedAudioElement = null;
                } catch (e) {
                    console.warn('[audiomixer.js] Audio 엘리먼트 정리 오류:', e);
                }
            }

            // 6. Gain 노드 정리
            try {
                if (this.micGain) {
                    this.micGain.disconnect();
                    this.micGain = null;
                }
                if (this.mediaGain) {
                    this.mediaGain.disconnect();
                    this.mediaGain = null;
                }
                if (this.ttsGain) {
                    this.ttsGain.disconnect();
                    this.ttsGain = null;
                }
                if (this.merger) {
                    this.merger.disconnect();
                    this.merger = null;
                }
            } catch (e) {
                console.warn('[audiomixer.js] 노드 연결 해제 오류:', e);
            }

            // 7. MediaRecorder 정리
            this.mediaRecorder = null;

            // 8. AudioContext 닫기 (타임아웃 추가)
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

            // 9. 참조 제거
            this.dotNetRef = null;
            this.destination = null;

            // 10. 플레이리스트 초기화
            this.mediaPlaylist = [];
            this.ttsPlaylist = [];
            this.currentMediaIndex = 0;
            this.currentTtsIndex = 0;

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