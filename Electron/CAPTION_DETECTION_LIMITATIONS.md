# Caption Detection Limitations and Alternatives

## Current Approach: Web Speech API

**What it does:**
- Captures audio from the **microphone**
- Sends audio to Google's speech recognition service
- Returns transcribed text

**What it CANNOT do:**
- ❌ Capture system audio (audio from other apps)
- ❌ Capture captions from other browsers/applications
- ❌ Read text from other windows
- ❌ Detect macOS system captions

## Windows Version Approach

The Windows version uses **UI Automation (UIA)** to:
1. Find the "Live captions" window by title
2. Use `TextPattern` to read text content from that window
3. Extract captions as they appear in the window

This is fundamentally different from speech recognition - it's **reading text from a window**.

## macOS Alternatives for Caption Detection

To capture captions from other browsers/applications on macOS, you would need:

### Option 1: macOS Accessibility APIs (AXUIElement)
- **What**: Read text from other application windows
- **How**: Use `AXUIElement` to find windows and extract text
- **Limitations**: Requires Accessibility permissions, may not work with all apps
- **Similar to**: Windows UI Automation approach

### Option 2: Screen Recording + OCR
- **What**: Capture screen area and use OCR to extract text
- **How**: Use Screen Recording API to capture pixels, then OCR
- **Limitations**: Requires Screen Recording permissions, computationally expensive
- **Accuracy**: Depends on OCR quality

### Option 3: System Audio Capture + Transcription
- **What**: Capture system audio and transcribe it
- **How**: Use Core Audio to capture system audio, then send to speech recognition
- **Limitations**: Requires Screen Recording permissions, needs internet for transcription
- **Note**: macOS doesn't easily allow capturing system audio

### Option 4: Browser Extension/API
- **What**: Use browser APIs to access captions if available
- **How**: Some browsers expose caption APIs (e.g., Chrome's `chrome.captions`)
- **Limitations**: Only works within the browser, not system-wide

## Recommendation

If you need to capture captions from other browsers/applications (like the Windows version does), you should:

1. **Use macOS Accessibility APIs** - Similar to Windows UI Automation
   - Create a native Node.js module using `AXUIElement`
   - Find windows by title/process name
   - Extract text content from windows
   - Poll for changes (similar to Windows version's polling)

2. **Or use a hybrid approach**:
   - Keep Web Speech API for microphone input
   - Add Accessibility API support for reading captions from other apps
   - Let user choose which source to use

## Implementation Notes

The Windows version:
- Polls every 160ms (`POLL_INTERVAL_MS`)
- Uses `TextPattern` to read text
- Normalizes and tracks changes
- Maintains a transcript history

A macOS equivalent would:
- Use `AXUIElement` to find windows
- Use `AXValue` to read text content
- Poll at similar intervals
- Handle text normalization

## Current Status

**Current implementation**: Web Speech API (microphone only)
- ✅ Works for microphone input
- ❌ Cannot detect system captions
- ❌ Cannot read from other browsers

**To add system caption detection**: Would need native macOS Accessibility API implementation


