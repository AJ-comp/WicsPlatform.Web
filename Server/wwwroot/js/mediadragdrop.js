// 미디어 드래그 드롭 관련 이벤트 처리
let dotNetRef = null;
let isDragInProgress = false;

export function initializeMediaDragDrop(dotnetRef) {
    dotNetRef = dotnetRef;

    // 기존 이벤트 리스너 제거
    document.removeEventListener('dragstart', handleDragStart);
    document.removeEventListener('dragend', handleDragEnd);
    document.removeEventListener('dragover', handleDragOver);
    document.removeEventListener('drop', handleDrop);
    document.removeEventListener('dragenter', handleDragEnter);
    document.removeEventListener('dragleave', handleDragLeave);

    // 새 이벤트 리스너 추가
    document.addEventListener('dragstart', handleDragStart);
    document.addEventListener('dragend', handleDragEnd);
    document.addEventListener('dragover', handleDragOver);
    document.addEventListener('drop', handleDrop);
    document.addEventListener('dragenter', handleDragEnter);
    document.addEventListener('dragleave', handleDragLeave);

    console.log('Media drag drop initialized');
}

export function refreshMediaDragDrop(dotnetRef) {
    dotNetRef = dotnetRef;
}

function handleDragStart(e) {
    const mediaCard = e.target.closest('.media-card');
    if (mediaCard && mediaCard.getAttribute('draggable') === 'true') {
        isDragInProgress = true;
        e.dataTransfer.effectAllowed = 'copy';
        e.dataTransfer.setData('text/plain', mediaCard.getAttribute('data-media-id'));

        // 드래그 중인 요소에 시각적 효과 추가
        mediaCard.classList.add('dragging');

        // 모든 플레이리스트 드롭 존을 강조
        document.querySelectorAll('.playlist-drop-zone').forEach(zone => {
            zone.classList.add('drop-ready');
        });
    }
}

function handleDragEnd(e) {
    isDragInProgress = false;

    // 드래그 중인 요소의 시각적 효과 제거
    const draggingElement = document.querySelector('.media-card.dragging');
    if (draggingElement) {
        draggingElement.classList.remove('dragging');
    }

    // 모든 드롭 존의 강조 효과 제거
    document.querySelectorAll('.playlist-drop-zone').forEach(zone => {
        zone.classList.remove('drop-ready', 'drag-over');
    });
}

function handleDragEnter(e) {
    if (!isDragInProgress) return;

    const playlistDropZone = e.target.closest('.playlist-drop-zone');
    if (playlistDropZone) {
        e.preventDefault();
        playlistDropZone.classList.add('drag-over');
    }
}

function handleDragLeave(e) {
    if (!isDragInProgress) return;

    const playlistDropZone = e.target.closest('.playlist-drop-zone');
    if (playlistDropZone) {
        // 드래그가 자식 요소로 이동하는 경우를 체크
        const relatedTarget = e.relatedTarget;
        if (!playlistDropZone.contains(relatedTarget)) {
            playlistDropZone.classList.remove('drag-over');
        }
    }
}

function handleDragOver(e) {
    if (!isDragInProgress) return;

    const playlistDropZone = e.target.closest('.playlist-drop-zone');
    if (playlistDropZone) {
        e.preventDefault(); // 드롭을 허용
        e.dataTransfer.dropEffect = 'copy';
    }
}

function handleDrop(e) {
    if (!isDragInProgress) return;

    const playlistDropZone = e.target.closest('.playlist-drop-zone');
    if (playlistDropZone && dotNetRef) {
        e.preventDefault();
        e.stopPropagation();

        const playlistId = parseInt(playlistDropZone.getAttribute('data-playlist-id'));

        // 드롭 존의 시각적 효과 제거
        playlistDropZone.classList.remove('drag-over');

        // 중복 호출 방지를 위해 플래그 리셋
        isDragInProgress = false;

        // 성공 애니메이션 효과
        playlistDropZone.classList.add('drop-success');
        setTimeout(() => {
            playlistDropZone.classList.remove('drop-success');
        }, 600);

        // C# 메서드 호출
        dotNetRef.invokeMethodAsync('HandleMediaDropFromJS', playlistId)
            .then(() => {
                console.log('Media successfully added to playlist');
            })
            .catch(error => {
                console.error('Error calling HandleMediaDropFromJS:', error);
            });
    }

    // 드래그 종료 처리
    handleDragEnd(e);
}

// 추가 스타일 정의 (동적으로 추가)
const style = document.createElement('style');
style.textContent = `
    .playlist-drop-zone.drop-ready {
        transition: all 0.3s ease;
    }
    
    .playlist-drop-zone.drag-over {
        transform: scale(1.02);
        box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
    }
    
    .playlist-drop-zone.drop-success {
        animation: dropSuccess 0.6s ease;
    }
    
    @keyframes dropSuccess {
        0% {
            transform: scale(1);
        }
        50% {
            transform: scale(1.05);
            box-shadow: 0 8px 32px rgba(76, 175, 80, 0.3);
        }
        100% {
            transform: scale(1);
        }
    }
    
    .media-card.dragging {
        opacity: 0.6;
        transform: scale(0.98);
    }
`;
document.head.appendChild(style);
