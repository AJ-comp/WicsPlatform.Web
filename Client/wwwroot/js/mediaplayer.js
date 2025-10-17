// Audio playback utility
let audioElement;
let currentUrl = null;

export function playMedia(url) {
    if (!audioElement) {
        audioElement = document.createElement('audio');
        document.body.appendChild(audioElement);
    }

    audioElement.src = url;
    audioElement.play();
    currentUrl = url;
}

export function stopMedia() {
    if (audioElement) {
        audioElement.pause();
        audioElement.currentTime = 0;
        currentUrl = null;
    }
}

export function isPlaying(url) {
    if (!audioElement) return false;
    return currentUrl === url && !audioElement.paused;
}

export function getCurrentPlayingUrl() {
    return currentUrl;
}
