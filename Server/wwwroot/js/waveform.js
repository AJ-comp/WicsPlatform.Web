// 웨이브폼 시각화를 위한 JavaScript 모듈
export function drawWaveform(canvasId, audioData) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const width = canvas.width;
    const height = canvas.height;

    // 캔버스 클리어
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, width, height);

    // 웨이브폼 그리기
    ctx.strokeStyle = '#00ff00';
    ctx.lineWidth = 2;
    ctx.beginPath();

    const sliceWidth = width / audioData.length;
    let x = 0;

    for (let i = 0; i < audioData.length; i++) {
        const v = audioData[i] / 128.0; // byte를 -1~1 범위로 정규화
        const y = v * height / 2;

        if (i === 0) {
            ctx.moveTo(x, height / 2 + y);
        } else {
            ctx.lineTo(x, height / 2 + y);
        }

        x += sliceWidth;
    }

    ctx.stroke();

    // 중앙선 그리기
    ctx.strokeStyle = '#444';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(0, height / 2);
    ctx.lineTo(width, height / 2);
    ctx.stroke();
}

// 스펙트럼 분석기 (추후 확장 가능)
export function drawSpectrum(canvasId, frequencyData) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const width = canvas.width;
    const height = canvas.height;

    // 캔버스 클리어
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, width, height);

    // 스펙트럼 바 그리기
    const barWidth = (width / frequencyData.length) * 2.5;
    let barHeight;
    let x = 0;

    for (let i = 0; i < frequencyData.length; i++) {
        barHeight = (frequencyData[i] / 255) * height;

        // 그라데이션 색상
        const r = barHeight + 25 * (i / frequencyData.length);
        const g = 250 * (i / frequencyData.length);
        const b = 50;

        ctx.fillStyle = `rgb(${r},${g},${b})`;
        ctx.fillRect(x, height - barHeight, barWidth, barHeight);

        x += barWidth + 1;
    }
}
