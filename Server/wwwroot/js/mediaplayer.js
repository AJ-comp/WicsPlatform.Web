// Audio playback utility used by ManageMedia page
let audioElement;

export function playMedia(url) {
    if (!audioElement) {
        audioElement = document.createElement('audio');
        document.body.appendChild(audioElement);
    }

    audioElement.src = url;
    audioElement.play();
}
