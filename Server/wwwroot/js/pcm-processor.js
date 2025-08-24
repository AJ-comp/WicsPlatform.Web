// AudioWorklet Processor - 별도 파일로 저장
class PCMCaptureProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.sampleCount = 0;
    }

    process(inputs, outputs, parameters) {
        const input = inputs[0];

        // 입력이 있으면 Main Thread로 전송
        if (input && input.length > 0) {
            const channelData = input[0]; // 모노 채널

            // Float32 → Int16 변환
            const pcm16 = new Int16Array(channelData.length);
            for (let i = 0; i < channelData.length; i++) {
                const sample = Math.max(-1, Math.min(1, channelData[i]));
                pcm16[i] = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
            }

            // Main Thread로 전송 (128 샘플)
            this.port.postMessage({
                type: 'audio',
                pcm: pcm16.buffer
            }, [pcm16.buffer]); // Transferable로 전송 (효율적)

            this.sampleCount += channelData.length;

            // 디버깅용 (1초마다)
            if (this.sampleCount >= 48000) {
                this.port.postMessage({
                    type: 'debug',
                    message: `1초 처리 완료: ${this.sampleCount} 샘플`
                });
                this.sampleCount = 0;
            }
        }

        return true; // 계속 처리
    }
}

registerProcessor('pcm-capture', PCMCaptureProcessor);