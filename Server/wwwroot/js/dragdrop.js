// Drag and Drop functionality for ManageSpeaker page
let dotNetRef = null;
let isDragging = false;

export function initializeDragDrop(ref) {
    dotNetRef = ref;
    setupDragDropHandlers();
}

export function refreshDragDrop(ref) {
    dotNetRef = ref;
    setupDragDropHandlers();
}

function setupDragDropHandlers() {
    // 스피커 카드 드래그 핸들러
    const speakerCards = document.querySelectorAll('[draggable="true"]');
    speakerCards.forEach(card => {
        card.addEventListener('dragstart', handleDragStart);
        card.addEventListener('dragend', handleDragEnd);
    });

    // 그룹 드롭 존 핸들러
    const dropZones = document.querySelectorAll('.group-drop-zone');
    dropZones.forEach(zone => {
        zone.addEventListener('dragover', handleDragOver);
        zone.addEventListener('drop', handleDrop);
        zone.addEventListener('dragenter', handleDragEnter);
        zone.addEventListener('dragleave', handleDragLeave);
    });
}

function handleDragStart(e) {
    isDragging = true;
    e.dataTransfer.effectAllowed = 'copy';
    e.dataTransfer.setData('text/plain', ''); // Firefox 호환성

    // 드래그 중인 요소에 시각적 효과 추가
    this.classList.add('dragging');

    // 모든 그룹 카드에 드래그 가능 힌트 추가
    document.querySelectorAll('.group-card').forEach(card => {
        card.style.transition = 'all 0.3s ease';
    });
}

function handleDragEnd(e) {
    isDragging = false;
    e.preventDefault();

    // 드래그 중인 요소의 시각적 효과 제거
    this.classList.remove('dragging');

    // 모든 드롭 존의 시각적 효과 제거
    document.querySelectorAll('.group-drop-zone').forEach(zone => {
        zone.classList.remove('drag-over');
    });

    // body에서 dragging 클래스 제거
    document.body.classList.remove('dragging');
}

function handleDragOver(e) {
    if (e.preventDefault) {
        e.preventDefault();
    }
    e.dataTransfer.dropEffect = 'copy';
    return false;
}

function handleDragEnter(e) {
    if (isDragging) {
        this.classList.add('drag-over');

        // 드롭 존에 진입 시 시각적 피드백 강화
        const groupCard = this.querySelector('.group-card');
        if (groupCard) {
            groupCard.style.transform = 'scale(1.02)';
            groupCard.style.boxShadow = '0 8px 24px rgba(0, 0, 0, 0.15)';
        }
    }
}

function handleDragLeave(e) {
    // 실제로 드롭 존을 벗어났는지 확인
    if (!this.contains(e.relatedTarget)) {
        this.classList.remove('drag-over');

        // 시각적 피드백 제거
        const groupCard = this.querySelector('.group-card');
        if (groupCard) {
            groupCard.style.transform = '';
            groupCard.style.boxShadow = '';
        }
    }
}

async function handleDrop(e) {
    if (e.stopPropagation) {
        e.stopPropagation();
    }
    e.preventDefault();

    this.classList.remove('drag-over');

    // 시각적 피드백 제거
    const groupCard = this.querySelector('.group-card');
    if (groupCard) {
        groupCard.style.transform = '';
        groupCard.style.boxShadow = '';
    }

    const groupId = parseInt(this.dataset.groupId);

    if (dotNetRef && groupId) {
        try {
            // 처리 중 상태를 즉시 표시하기 위해 body에 클래스 추가
            document.body.classList.add('processing');

            await dotNetRef.invokeMethodAsync('HandleDropFromJS', groupId);
        } catch (error) {
            console.error('Error calling .NET method:', error);
        } finally {
            // 처리 완료 후 클래스 제거
            document.body.classList.remove('processing');
        }
    }

    return false;
}
