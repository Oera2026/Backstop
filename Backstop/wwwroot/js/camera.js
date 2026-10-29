// Camera and picture previews for Show me, Check and Explain. Pictures stay on this device until the user sends them.
window.backstopCamera = {
  // Starts the back camera in a <video>. Returns false when there is no camera or permission is refused.
  async start(video) {
    if (!video || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return false;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
      video.srcObject = stream;
      await video.play();
      return true;
    } catch (e) {
      return false;
    }
  },

  // Takes a still from the video, shows it in <img>, stops the camera and hands the JPEG bytes to .NET as a stream.
  async capture(video, img) {
    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth || 1280;
    canvas.height = video.videoHeight || 720;
    canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
    if (img) img.src = URL.createObjectURL(blob);
    this.stop(video);
    return new Uint8Array(await blob.arrayBuffer());
  },

  stop(video) {
    const stream = video && video.srcObject;
    if (stream) stream.getTracks().forEach(t => t.stop());
    if (video) video.srcObject = null;
  },

  // Shows the chosen file in <img> without sending it anywhere.
  preview(input, img) {
    const file = input && input.files && input.files[0];
    if (!img) return;
    if (file && file.type.startsWith('image/')) { img.src = URL.createObjectURL(file); img.hidden = false; }
    else { img.removeAttribute('src'); img.hidden = true; }
  }
};
