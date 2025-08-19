// Server/wwwroot/js/audiomixer.js
// Web Audio API를 사용한 실시간 오디오 믹싱 모듈
console.log('[audiomixer.js] 모듈 로드됨');

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

        // 활성 소스 추적
        this.micStream = null;
        this.micSource = null;
        this.activeAudioElements = new Set();
        this.activeSources = new Map();

        // 플레이리스트
        this.mediaPlaylist = [];
        this.ttsPlaylist = [];
        this.currentMediaIndex = 0;
        this.currentTtsIndex = 0;

        // 상태
        this.isRecording = false;
        this.config = {
            sampleRate: 48000,
            channels: 2,
            timeslice: 100,  // 50ms → 100ms로 변경
            bitrate: 128000
        };

        // 디버그용
        this.debugMode = true;
        this.dataPacketCount = 0;
    }

    // 초기화
    async initialize(dotNetRef, config = {}) {
        console.log('[audiomixer.js] 믹서 초기화', config);

        this.dotNetRef = dotNetRef;
        this.config = { ...this.config, ...config };

        try {
            // AudioContext 생성
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)({
                sampleRate: this.config.sampleRate,
                latencyHint: 'interactive'
            });

            if (this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }

            // 노드 생성
            this.merger = this.audioContext.createChannelMerger(this.config.channels);
            this.destination = this.audioContext.createMediaStreamDestination();

            // Gain 노드 생성 및 연결
            this.micGain = this.audioContext.createGain();
            this.mediaGain = this.audioContext.createGain();
            this.ttsGain = this.audioContext.createGain();

            // 초기 볼륨 설정
            this.micGain.gain.value = config.micVolume || 1.0;
            this.mediaGain.gain.value = config.mediaVolume || 1.0;
            this.ttsGain.gain.value = config.ttsVolume || 1.0;

            // Merger에 연결
            this.micGain.connect(this.merger);
            this.mediaGain.connect(this.merger);
            this.ttsGain.connect(this.merger);

            // Merger를 destination에 연결
            this.merger.connect(this.destination);

            // MediaRecorder 설정
            this.setupMediaRecorder();

            console.log('[audiomixer.js] ✅ 초기화 완료');
            console.log('[audiomixer.js] AudioContext 상태:', this.audioContext.state);
            console.log('[audiomixer.js] 샘플레이트:', this.audioContext.sampleRate, 'Hz');
            console.log('[audiomixer.js] 볼륨 - Mic:', this.micGain.gain.value, 'Media:', this.mediaGain.gain.value, 'TTS:', this.ttsGain.gain.value);

            return true;

        } catch (error) {
            console.error('[audiomixer.js] ❌ 초기화 실패:', error);
            return false;
        }
    }

    // MediaRecorder 설정
    setupMediaRecorder() {
        const mimeType = this.getSupportedMimeType();

        this.mediaRecorder = new MediaRecorder(this.destination.stream, {
            mimeType: mimeType,
            audioBitsPerSecond: this.config.bitrate
        });

        this.mediaRecorder.ondataavailable = async (event) => {
            if (event.data && event.data.size > 0 && this.dotNetRef) {
                try {
                    this.dataPacketCount++;

                    // 100개마다 한 번만 로그
                    if (this.dataPacketCount % 100 === 0) {
                        console.log(`[audiomixer.js] 데이터 패킷 ${this.dataPacketCount}개 전송됨`);
                    }

                    const base64 = await this.blobToBase64(event.data);
                    await this.dotNetRef.invokeMethodAsync('OnMixedAudioCaptured', base64);
                } catch (error) {
                    console.error('[audiomixer.js] ❌ 데이터 전송 오류:', error);
                }
            }
        };

        this.mediaRecorder.onerror = (error) => {
            console.error('[audiomixer.js] ❌ MediaRecorder 오류:', error);
        };
    }

    // 지원되는 MIME 타입 찾기
    getSupportedMimeType() {
        const types = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
            'audio/ogg'
        ];

        for (const type of types) {
            if (MediaRecorder.isTypeSupported(type)) {
                console.log('[audiomixer.js] MIME 타입:', type);
                return type;
            }
        }

        return 'audio/webm';
    }

    // Blob을 Base64로 변환
    async blobToBase64(blob) {
        const buffer = await blob.arrayBuffer();
        const bytes = new Uint8Array(buffer);

        const CHUNK_SIZE = 32768;
        let base64 = '';

        for (let i = 0; i < bytes.length; i += CHUNK_SIZE) {
            const chunk = bytes.slice(i, i + CHUNK_SIZE);
            base64 += btoa(String.fromCharCode.apply(null, chunk));
        }

        return base64;
    }

    // 녹음 시작
    startRecording() {
        if (this.mediaRecorder && this.mediaRecorder.state === 'inactive') {
            this.mediaRecorder.start(this.config.timeslice);
            this.isRecording = true;
            console.log('[audiomixer.js] ✅ 녹음 시작 - timeslice:', this.config.timeslice, 'ms');
        }
    }

    // 녹음 중지
    stopRecording() {
        if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
            this.mediaRecorder.stop();
            this.isRecording = false;
            console.log('[audiomixer.js] 녹음 중지');
        }
    }

    // 마이크 활성화
    async enableMic() {
        try {
            // 기존 스트림 정리
            if (this.micStream) {
                this.disableMic();
            }

            // 마이크 권한 요청
            this.micStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    sampleRate: this.config.sampleRate,
                    channelCount: this.config.channels,
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true
                }
            });

            // 소스 생성 및 연결
            this.micSource = this.audioContext.createMediaStreamSource(this.micStream);
            this.micSource.connect(this.micGain);

            console.log('[audiomixer.js] ✅ 마이크 활성화 완료');
            console.log('[audiomixer.js] 마이크 → micGain → merger 연결됨');

            return true;

        } catch (error) {
            console.error('[audiomixer.js] ❌ 마이크 활성화 실패:', error);

            if (error.name === 'NotAllowedError' && this.dotNetRef) {
                await this.dotNetRef.invokeMethodAsync('ShowMicHelp');
            }

            return false;
        }
    }

    // 마이크 비활성화
    disableMic() {
        if (this.micSource) {
            this.micSource.disconnect();
            this.micSource = null;
        }

        if (this.micStream) {
            this.micStream.getTracks().forEach(track => {
                track.stop();
            });
            this.micStream = null;
        }

        console.log('[audiomixer.js] 마이크 비활성화됨');
    }

    // 미디어 플레이리스트 로드
    // loadMediaPlaylist 함수 수정
    async loadMediaPlaylist(urls) {
        console.log('[audiomixer.js] loadMediaPlaylist 호출됨');
        console.log('[audiomixer.js] 받은 파라미터 타입:', typeof urls);
        console.log('[audiomixer.js] 받은 파라미터:', urls);

        // 타입 확인 및 변환
        if (!urls) {
            console.warn('[audiomixer.js] ⚠️ urls가 null 또는 undefined');
            return false;
        }

        // object를 배열로 변환 (Blazor에서 올 때 가끔 이런 경우 있음)
        if (!Array.isArray(urls)) {
            console.log('[audiomixer.js] 배열이 아님, 변환 시도...');
            if (typeof urls === 'object') {
                urls = Object.values(urls);
                console.log('[audiomixer.js] Object.values로 변환:', urls);
            } else {
                console.error('[audiomixer.js] 배열로 변환 불가');
                return false;
            }
        }

        if (urls.length === 0) {
            console.warn('[audiomixer.js] ⚠️ 빈 미디어 플레이리스트');
            return false;
        }

        this.mediaPlaylist = urls;
        this.currentMediaIndex = 0;

        console.log('[audiomixer.js] 📁 미디어 플레이리스트 로드:', urls.length, '개 파일');
        console.log('[audiomixer.js] URLs:', urls);

        // 첫 번째 미디어 재생
        return await this.playNextMedia();
    }

    // 다음 미디어 재생
    async playNextMedia() {
        if (this.currentMediaIndex >= this.mediaPlaylist.length) {
            console.log('[audiomixer.js] 미디어 플레이리스트 종료 - 루프 재시작');
            this.currentMediaIndex = 0;

            // 루프 재생
            if (this.mediaPlaylist.length > 0) {
                return await this.playNextMedia();
            }

            if (this.dotNetRef) {
                await this.dotNetRef.invokeMethodAsync('OnMediaPlaylistEnded');
            }
            return false;
        }

        const url = this.mediaPlaylist[this.currentMediaIndex];
        console.log(`[audiomixer.js] 🎵 미디어 재생 시도 ${this.currentMediaIndex + 1}/${this.mediaPlaylist.length}: ${url}`);

        try {
            const audio = new Audio();
            audio.crossOrigin = 'anonymous';
            audio.src = url;

            // 디버그: 로드 상태 확인
            audio.onloadstart = () => {
                console.log('[audiomixer.js] 미디어 로드 시작:', url);
            };

            audio.onloadeddata = () => {
                console.log('[audiomixer.js] ✅ 미디어 데이터 로드 완료');
                console.log('[audiomixer.js] Duration:', audio.duration, 'seconds');
            };

            audio.oncanplay = () => {
                console.log('[audiomixer.js] 미디어 재생 가능 상태');
            };

            // Set에 추가 (메모리 관리)
            this.activeAudioElements.add(audio);

            // 소스 생성 및 연결
            const source = this.audioContext.createMediaElementSource(audio);
            source.connect(this.mediaGain);

            // 디버그: 연결 확인
            console.log('[audiomixer.js] 🔗 미디어 연결 상태:');
            console.log('  - mediaGain 볼륨:', this.mediaGain.gain.value);
            console.log('  - merger 연결 확인:', this.mediaGain.numberOfOutputs > 0 ? '✅' : '❌');
            console.log('  - AudioContext 상태:', this.audioContext.state);

            // 이벤트 핸들러
            audio.onplay = () => {
                console.log('[audiomixer.js] ▶️ 미디어 재생 시작됨!');
            };

            audio.onended = async () => {
                console.log('[audiomixer.js] ⏹️ 미디어 재생 종료:', url);

                // 정리
                source.disconnect();
                audio.src = '';
                audio.load();
                this.activeAudioElements.delete(audio);

                // 다음 미디어 재생
                this.currentMediaIndex++;
                await this.playNextMedia();
            };

            audio.onerror = (error) => {
                console.error('[audiomixer.js] ❌ 미디어 재생 오류:', url);
                console.error('[audiomixer.js] 오류 상세:', error);
                console.error('[audiomixer.js] Audio error code:', audio.error?.code);
                console.error('[audiomixer.js] Audio error message:', audio.error?.message);

                // 정리
                source.disconnect();
                audio.src = '';
                audio.load();
                this.activeAudioElements.delete(audio);

                // 다음 미디어로 넘어감
                this.currentMediaIndex++;
                this.playNextMedia();
            };

            // 재생 시작
            await audio.play();
            return true;

        } catch (error) {
            console.error('[audiomixer.js] ❌ 미디어 로드 실패:', error);
            console.error('[audiomixer.js] URL:', url);
            this.currentMediaIndex++;
            return await this.playNextMedia();
        }
    }

    // TTS 플레이리스트 로드
    async loadTtsPlaylist(urls) {
        if (!urls || !Array.isArray(urls) || urls.length === 0) {
            console.warn('[audiomixer.js] ⚠️ 빈 TTS 플레이리스트');
            return false;
        }

        this.ttsPlaylist = urls;
        this.currentTtsIndex = 0;

        console.log('[audiomixer.js] 🗣️ TTS 플레이리스트 로드:', urls.length, '개 파일');
        console.log('[audiomixer.js] URLs:', urls);

        // 첫 번째 TTS 재생
        return await this.playNextTts();
    }

    // 다음 TTS 재생
    async playNextTts() {
        if (this.currentTtsIndex >= this.ttsPlaylist.length) {
            console.log('[audiomixer.js] TTS 플레이리스트 종료 - 루프 재시작');
            this.currentTtsIndex = 0;

            // 루프 재생
            if (this.ttsPlaylist.length > 0) {
                return await this.playNextTts();
            }

            if (this.dotNetRef) {
                await this.dotNetRef.invokeMethodAsync('OnTtsPlaylistEnded');
            }
            return false;
        }

        const url = this.ttsPlaylist[this.currentTtsIndex];
        console.log(`[audiomixer.js] 🗣️ TTS 재생 시도 ${this.currentTtsIndex + 1}/${this.ttsPlaylist.length}: ${url}`);

        try {
            const audio = new Audio();
            audio.crossOrigin = 'anonymous';
            audio.src = url;

            // Set에 추가 (메모리 관리)
            this.activeAudioElements.add(audio);

            // 소스 생성 및 연결
            const source = this.audioContext.createMediaElementSource(audio);
            source.connect(this.ttsGain);

            console.log('[audiomixer.js] 🔗 TTS 연결 상태:');
            console.log('  - ttsGain 볼륨:', this.ttsGain.gain.value);
            console.log('  - merger 연결 확인:', this.ttsGain.numberOfOutputs > 0 ? '✅' : '❌');

            // 이벤트 핸들러
            audio.onplay = () => {
                console.log('[audiomixer.js] ▶️ TTS 재생 시작됨!');
            };

            audio.onended = async () => {
                console.log('[audiomixer.js] ⏹️ TTS 재생 종료:', url);

                // 정리
                source.disconnect();
                audio.src = '';
                audio.load();
                this.activeAudioElements.delete(audio);

                // 다음 TTS 재생
                this.currentTtsIndex++;
                await this.playNextTts();
            };

            audio.onerror = (error) => {
                console.error('[audiomixer.js] ❌ TTS 재생 오류:', url, error);

                // 정리
                source.disconnect();
                audio.src = '';
                audio.load();
                this.activeAudioElements.delete(audio);

                // 다음 TTS로 넘어감
                this.currentTtsIndex++;
                this.playNextTts();
            };

            // 재생 시작
            await audio.play();
            return true;

        } catch (error) {
            console.error('[audiomixer.js] ❌ TTS 로드 실패:', error);
            this.currentTtsIndex++;
            return await this.playNextTts();
        }
    }

    // 볼륨 설정
    setVolumes(mic, media, tts) {
        if (this.micGain) this.micGain.gain.value = mic;
        if (this.mediaGain) this.mediaGain.gain.value = media;
        if (this.ttsGain) this.ttsGain.gain.value = tts;

        console.log(`[audiomixer.js] 볼륨 설정 - 마이크: ${mic}, 미디어: ${media}, TTS: ${tts}`);
    }

    // 일시정지
    pause() {
        if (!this.isRecording) return false;

        console.log('[audiomixer.js] 일시정지');

        // 녹음 일시정지
        if (this.mediaRecorder && this.mediaRecorder.state === 'recording') {
            this.mediaRecorder.pause();
        }

        // 모든 오디오 엘리먼트 일시정지
        this.activeAudioElements.forEach(audio => {
            if (!audio.paused) {
                audio.pause();
            }
        });

        return true;
    }

    // 재개
    resume() {
        if (!this.isRecording) return false;

        console.log('[audiomixer.js] 재개');

        // 녹음 재개
        if (this.mediaRecorder && this.mediaRecorder.state === 'paused') {
            this.mediaRecorder.resume();
        }

        // 모든 오디오 엘리먼트 재개
        this.activeAudioElements.forEach(audio => {
            if (audio.paused) {
                audio.play().catch(err => {
                    console.error('[audiomixer.js] 재생 재개 실패:', err);
                });
            }
        });

        return true;
    }

    // 정지 (dispose 호출)
    stop() {
        console.log('[audiomixer.js] 정지');
        return this.dispose();
    }

    // 전체 정리
    async dispose() {
        console.log('[audiomixer.js] 믹서 정리 시작');

        // 1. 녹음 중지
        this.stopRecording();

        // 2. 마이크 정리
        this.disableMic();

        // 3. 모든 오디오 엘리먼트 정리
        this.activeAudioElements.forEach(audio => {
            audio.pause();
            audio.src = '';
            audio.load();
        });
        this.activeAudioElements.clear();

        // 4. 노드 연결 해제
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

        // 5. AudioContext 닫기
        if (this.audioContext && this.audioContext.state !== 'closed') {
            await this.audioContext.close();
            this.audioContext = null;
        }

        // 6. 참조 제거
        this.dotNetRef = null;
        this.mediaRecorder = null;
        this.destination = null;

        console.log('[audiomixer.js] 믹서 정리 완료');
    }

    // 상태 조회 (디버그용)
    getStatus() {
        const status = {
            isRecording: this.isRecording,
            audioContextState: this.audioContext ? this.audioContext.state : 'closed',
            micEnabled: this.micStream !== null,
            activeAudioCount: this.activeAudioElements.size,
            mediaPlaylistLength: this.mediaPlaylist.length,
            ttsPlaylistLength: this.ttsPlaylist.length,
            currentMediaIndex: this.currentMediaIndex,
            currentTtsIndex: this.currentTtsIndex,
            volumes: {
                mic: this.micGain ? this.micGain.gain.value : 0,
                media: this.mediaGain ? this.mediaGain.gain.value : 0,
                tts: this.ttsGain ? this.ttsGain.gain.value : 0
            },
            dataPackets: this.dataPacketCount
        };

        console.log('[audiomixer.js] 현재 상태:', status);
        return status;
    }
}

// 전역 인스턴스
let mixerInstance = null;

// 외부 진입점
export async function createMixer(dotNetRef, config) {
    // 기존 믹서 정리
    if (mixerInstance) {
        await mixerInstance.dispose();
    }

    // 새 믹서 생성
    mixerInstance = new AudioMixer();
    const success = await mixerInstance.initialize(dotNetRef, config);

    if (success) {
        mixerInstance.startRecording();
    }

    return success;
}

export async function enableMic() {
    if (!mixerInstance) {
        console.error('[audiomixer.js] ❌ 믹서가 초기화되지 않았습니다');
        return false;
    }
    return await mixerInstance.enableMic();
}

export async function disableMic() {
    if (mixerInstance) {
        mixerInstance.disableMic();
    }
}

export async function loadMediaPlaylist(urls) {
    if (!mixerInstance) {
        console.error('[audiomixer.js] ❌ 믹서가 초기화되지 않았습니다');
        return false;
    }
    return await mixerInstance.loadMediaPlaylist(urls);
}

export async function loadTtsPlaylist(urls) {
    if (!mixerInstance) {
        console.error('[audiomixer.js] ❌ 믹서가 초기화되지 않았습니다');
        return false;
    }
    return await mixerInstance.loadTtsPlaylist(urls);
}

export function setVolumes(mic, media, tts) {
    if (mixerInstance) {
        mixerInstance.setVolumes(mic, media, tts);
    }
}

export function pause() {
    if (mixerInstance) {
        return mixerInstance.pause();
    }
    return false;
}

export function resume() {
    if (mixerInstance) {
        return mixerInstance.resume();
    }
    return false;
}

export function stop() {
    if (mixerInstance) {
        return mixerInstance.stop();
    }
    return false;
}

export async function dispose() {
    if (mixerInstance) {
        await mixerInstance.dispose();
        mixerInstance = null;
    }
}

export function getStatus() {
    if (mixerInstance) {
        return mixerInstance.getStatus();
    }
    return null;
}

// 디버그 헬퍼 - 브라우저 콘솔에서 직접 호출 가능
window.mixerDebug = {
    getStatus: () => mixerInstance?.getStatus(),
    checkVolumes: () => {
        if (mixerInstance) {
            console.log('마이크 볼륨:', mixerInstance.micGain?.gain.value);
            console.log('미디어 볼륨:', mixerInstance.mediaGain?.gain.value);
            console.log('TTS 볼륨:', mixerInstance.ttsGain?.gain.value);
        }
    },
    checkConnections: () => {
        if (mixerInstance) {
            console.log('Merger 입력 수:', mixerInstance.merger?.numberOfInputs);
            console.log('Merger 출력 수:', mixerInstance.merger?.numberOfOutputs);
            console.log('Destination 채널 수:', mixerInstance.destination?.stream.getAudioTracks()[0].getSettings());
        }
    },
    getInstance: () => mixerInstance
};