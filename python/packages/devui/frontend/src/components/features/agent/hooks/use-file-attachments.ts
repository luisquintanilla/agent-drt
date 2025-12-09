/**
 * useFileAttachments - Hook for handling file attachments, drag & drop, and paste
 */

import { useState, useRef, useCallback } from "react";
import { useDevUIStore } from "@/stores";
import type { AttachmentItem } from "@/components/ui/attachment-gallery";

/**
 * Detect file extension from text content
 */
function detectFileExtension(text: string): string {
  const trimmed = text.trim();
  const lines = trimmed.split("\n");

  // JSON detection
  if (/^{[\s\S]*}$|^\[[\s\S]*\]$/.test(trimmed)) return ".json";

  // XML/HTML detection
  if (/^<\?xml|^<html|^<!DOCTYPE/i.test(trimmed)) return ".html";

  // Markdown detection (code blocks)
  if (/^```/.test(trimmed)) return ".md";

  // TSV detection (tabs with multiple lines)
  if (/\t/.test(text) && lines.length > 1) return ".tsv";

  // CSV detection (more strict) - need multiple lines with consistent comma patterns
  if (lines.length > 2) {
    const commaLines = lines.filter((line) => line.includes(","));
    const semicolonLines = lines.filter((line) => line.includes(";"));

    // If >50% of lines have commas and it looks tabular
    if (commaLines.length > lines.length * 0.5) {
      const avgCommas =
        commaLines.reduce(
          (sum, line) => sum + (line.match(/,/g) || []).length,
          0
        ) / commaLines.length;
      if (avgCommas >= 2) return ".csv";
    }

    // If >50% of lines have semicolons and it looks tabular
    if (semicolonLines.length > lines.length * 0.5) {
      const avgSemicolons =
        semicolonLines.reduce(
          (sum, line) => sum + (line.match(/;/g) || []).length,
          0
        ) / semicolonLines.length;
      if (avgSemicolons >= 2) return ".csv";
    }
  }

  return ".txt";
}

/**
 * Get file type from MIME type
 */
function getFileType(file: File): AttachmentItem["type"] {
  if (file.type.startsWith("image/")) return "image";
  if (file.type === "application/pdf") return "pdf";
  if (file.type.startsWith("audio/")) return "audio";
  return "other";
}

/**
 * Read file as data URL
 */
function readFileAsDataURL(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = reject;
    reader.readAsDataURL(file);
  });
}

export interface UseFileAttachmentsOptions {
  isSubmitting: boolean;
  isStreaming: boolean;
  textareaRef: React.RefObject<HTMLTextAreaElement | null>;
}

export interface UseFileAttachmentsReturn {
  isDragOver: boolean;
  pasteNotification: string | null;
  handleFilesSelected: (files: File[]) => Promise<void>;
  handleRemoveAttachment: (id: string) => void;
  handleDragEnter: (e: React.DragEvent) => void;
  handleDragLeave: (e: React.DragEvent) => void;
  handleDragOver: (e: React.DragEvent) => void;
  handleDrop: (e: React.DragEvent) => Promise<void>;
  handlePaste: (e: React.ClipboardEvent) => Promise<void>;
  readFileAsDataURL: typeof readFileAsDataURL;
}

export function useFileAttachments({
  isSubmitting,
  isStreaming,
  textareaRef,
}: UseFileAttachmentsOptions): UseFileAttachmentsReturn {
  const setAttachments = useDevUIStore((state) => state.setAttachments);
  const setInputValue = useDevUIStore((state) => state.setInputValue);

  const [isDragOver, setIsDragOver] = useState(false);
  const [pasteNotification, setPasteNotification] = useState<string | null>(null);
  const dragCounterRef = useRef(0);

  const handleFilesSelected = useCallback(async (files: File[]) => {
    const newAttachments: AttachmentItem[] = [];

    for (const file of files) {
      const id = `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
      const type = getFileType(file);

      let preview: string | undefined;
      if (type === "image") {
        preview = await readFileAsDataURL(file);
      }

      newAttachments.push({
        id,
        file,
        preview,
        type,
      });
    }

    setAttachments([...useDevUIStore.getState().attachments, ...newAttachments]);
  }, [setAttachments]);

  const handleRemoveAttachment = useCallback((id: string) => {
    setAttachments(useDevUIStore.getState().attachments.filter((att) => att.id !== id));
  }, [setAttachments]);

  const handleDragEnter = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current += 1;
    if (e.dataTransfer.items && e.dataTransfer.items.length > 0) {
      setIsDragOver(true);
    }
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounterRef.current -= 1;
    if (dragCounterRef.current === 0) {
      setIsDragOver(false);
    }
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
  }, []);

  const handleDrop = useCallback(async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragOver(false);
    dragCounterRef.current = 0;

    if (isSubmitting || isStreaming) return;

    const files = Array.from(e.dataTransfer.files);
    if (files.length > 0) {
      await handleFilesSelected(files);
    }
  }, [isSubmitting, isStreaming, handleFilesSelected]);

  const handlePaste = useCallback(async (e: React.ClipboardEvent) => {
    const items = Array.from(e.clipboardData.items);
    const files: File[] = [];
    let hasProcessedText = false;
    const TEXT_THRESHOLD = 8000; // Convert to file if text is larger than this

    for (const item of items) {
      // Handle pasted images (screenshots)
      if (item.type.startsWith("image/")) {
        e.preventDefault();
        const blob = item.getAsFile();
        if (blob) {
          const timestamp = Date.now();
          files.push(
            new File([blob], `screenshot-${timestamp}.png`, { type: blob.type })
          );
        }
      }
      // Handle text - only process first text item (browsers often duplicate)
      else if (item.type === "text/plain" && !hasProcessedText) {
        hasProcessedText = true;

        // We need to check the text synchronously to decide whether to prevent default
        // Unfortunately, getAsString is async, so we'll prevent default for all text
        // and then decide whether to actually create a file or manually insert the text
        e.preventDefault();

        await new Promise<void>((resolve) => {
          item.getAsString((text) => {
            // Check if text should be converted to file
            const lineCount = (text.match(/\n/g) || []).length;
            const shouldConvert =
              text.length > TEXT_THRESHOLD ||
              lineCount > 50 || // Many lines suggests logs/data
              /^\s*[{[][\s\S]*[}\]]\s*$/.test(text) || // JSON-like
              /^<\?xml|^<html|^<!DOCTYPE/i.test(text); // XML/HTML

            if (shouldConvert) {
              // Create file for large/complex text
              const extension = detectFileExtension(text);
              const timestamp = Date.now();
              const blob = new Blob([text], { type: "text/plain" });
              files.push(
                new File([blob], `pasted-text-${timestamp}${extension}`, {
                  type: "text/plain",
                })
              );
            } else {
              // For small text, manually insert into textarea since we prevented default
              const textarea = textareaRef.current;
              if (textarea) {
                const start = textarea.selectionStart;
                const end = textarea.selectionEnd;
                const currentValue = textarea.value;
                const newValue =
                  currentValue.slice(0, start) + text + currentValue.slice(end);
                setInputValue(newValue);

                // Restore cursor position after the inserted text
                setTimeout(() => {
                  textarea.selectionStart = textarea.selectionEnd =
                    start + text.length;
                  textarea.focus();
                }, 0);
              }
            }
            resolve();
          });
        });
      }
    }

    // Process collected files
    if (files.length > 0) {
      await handleFilesSelected(files);

      // Show notification with appropriate icon
      const message =
        files.length === 1
          ? files[0].name.includes("screenshot")
            ? "Screenshot added as attachment"
            : "Large text converted to file"
          : `${files.length} files added`;

      setPasteNotification(message);
      setTimeout(() => setPasteNotification(null), 3000);
    }
  }, [handleFilesSelected, setInputValue, textareaRef]);

  return {
    isDragOver,
    pasteNotification,
    handleFilesSelected,
    handleRemoveAttachment,
    handleDragEnter,
    handleDragLeave,
    handleDragOver,
    handleDrop,
    handlePaste,
    readFileAsDataURL,
  };
}
