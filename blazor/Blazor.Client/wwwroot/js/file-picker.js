// A drop zone and a hidden file input for one image, for the shared picker (ImagePicker). Loaded as a same-origin
// module through the import map. A picked or dropped file is offered to .NET with its size, type and a blob: preview; only
// an accepted file replaces the selection, so a rejected file, or a file dialog closed without a choice, keeps the earlier
// selection readable. The selection is read back as a stream (IJSStreamReference), which .NET opens with a size bound. One
// preview URL is live at a time: an accepted file, reset and dispose revoke the previous one, a rejected file's own at once.
// Dragging over the zone toggles a class; nothing here writes a style attribute.

const ON_FILE_CHOSEN = "OnFileChosen";
const draggingClass = "image-picker-dragging";

export function attach(dropZone, input, dotNet) {
  let selectedFile = null;
  let previewUrl = null;

  const revokePreview = () => {
    if (previewUrl !== null) URL.revokeObjectURL(previewUrl);
    previewUrl = null;
  };

  const offer = async (file) => {
    if (!file || input.disabled) return;
    const candidatePreviewUrl = URL.createObjectURL(file);
    let accepted = false;
    try {
      accepted = await dotNet.invokeMethodAsync(ON_FILE_CHOSEN, { size: file.size, contentType: file.type, previewUrl: candidatePreviewUrl });
    } catch {
      // The component is gone or the runtime is leaving the document
    }
    if (!accepted) {
      URL.revokeObjectURL(candidatePreviewUrl);
      return;
    }
    revokePreview();
    selectedFile = file;
    previewUrl = candidatePreviewUrl;
  };

  const onChange = () => {
    const file = input.files?.[0];
    input.value = "";
    offer(file);
  };

  const hasFiles = (event) => Array.from(event.dataTransfer?.types ?? []).includes("Files");

  const onDragOver = (event) => {
    if (!hasFiles(event) || input.disabled) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    dropZone.classList.add(draggingClass);
  };

  const onDragLeave = (event) => {
    if (dropZone.contains(event.relatedTarget)) return;
    dropZone.classList.remove(draggingClass);
  };

  const onDrop = (event) => {
    dropZone.classList.remove(draggingClass);
    if (!hasFiles(event)) return;
    event.preventDefault();
    offer(event.dataTransfer.files[0]);
  };

  input.addEventListener("change", onChange);
  dropZone.addEventListener("dragover", onDragOver);
  dropZone.addEventListener("dragleave", onDragLeave);
  dropZone.addEventListener("drop", onDrop);

  return {
    open: () => input.click(),
    // The selected file as a Blob for a .NET stream reference, or null when nothing is selected
    readSelected: () => selectedFile,
    reset: () => {
      revokePreview();
      selectedFile = null;
      input.value = "";
    },
    dispose: () => {
      revokePreview();
      selectedFile = null;
      input.removeEventListener("change", onChange);
      dropZone.removeEventListener("dragover", onDragOver);
      dropZone.removeEventListener("dragleave", onDragLeave);
      dropZone.removeEventListener("drop", onDrop);
      dropZone.classList.remove(draggingClass);
    }
  };
}
