# Debugging Guide

## Quick Debug Methods

### 1. Open DevTools (Renderer Process)

**Method 1: Keyboard Shortcut**
- Press `Ctrl+Shift+I` (or `Cmd+Option+I` on Mac) while the app is focused
- This toggles the Chrome DevTools for the renderer process

**Method 2: Run in Dev Mode**
```bash
npm run dev
```
This automatically opens DevTools on startup.

**Method 3: From Renderer Code**
Add this to your renderer JavaScript:
```javascript
if (window.electronAPI) {
  window.electronAPI.toggleDevTools();
}
```

### 2. Debug Main Process

**Method 1: Run with Inspector**
```bash
npm run debug
```
Then connect Chrome DevTools to `chrome://inspect` or use VS Code debugger.

**Method 2: Console Logging**
The main process logs to the terminal where you ran `npm start`. Check there for:
- `console.log()` statements
- Error messages
- Service initialization logs

### 3. Console Logging

**Renderer Process (Browser Console)**
```javascript
// In renderer/main.js or browser console
console.log('Debug message');
console.error('Error message');
console.warn('Warning message');

// Or use the exposed API
if (window.electronAPI) {
  window.electronAPI.log('Custom log message');
}
```

**Main Process (Terminal)**
```javascript
// In main/main.js or services
console.log('Main process log');
console.error('Main process error');
```

### 4. Debug Native Module

**Check Native Module Loading**
```javascript
// In services/CaptionService.js
console.log('Native module loaded:', this.speechRecognition);
```

**Check Build Output**
```bash
ls -la main/native/speech-recognition/build/Release/
# Should see speech-recognition.node
```

### 5. Debug IPC Communication

**In Renderer:**
```javascript
// Listen to all IPC messages
window.electronAPI.onHotkey('*', (data) => {
  console.log('IPC message:', data);
});
```

**In Main Process:**
```javascript
// Log all IPC calls
ipcMain.handle('*', (event, ...args) => {
  console.log('IPC call:', event, args);
});
```

### 6. Debug Hotkeys

**Check Registered Hotkeys**
The HotkeyService logs registration:
```bash
# Check terminal output for:
# "Hotkey registered: CommandOrControl+Shift+/"
# "Failed to register hotkey: ..."
```

### 7. Debug Services

**CaptionService:**
```javascript
// Check if native module loaded
console.log('CaptionService initialized:', CaptionService);

// Check transcript updates
CaptionService.on('transcript-updated', (text) => {
  console.log('Transcript updated:', text);
});
```

**CalendarService:**
```javascript
// Check authentication
console.log('Calendar connected:', CalendarService.isAuthenticated);
```

### 8. VS Code Debugging Setup

Create `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug Main Process",
      "type": "node",
      "request": "launch",
      "cwd": "${workspaceFolder}/Mac",
      "runtimeExecutable": "${workspaceFolder}/Mac/node_modules/.bin/electron",
      "windows": {
        "runtimeExecutable": "${workspaceFolder}/Mac/node_modules/.bin/electron.cmd"
      },
      "args": ["."],
      "outputCapture": "std"
    },
    {
      "name": "Debug Renderer",
      "type": "chrome",
      "request": "attach",
      "port": 9222,
      "webRoot": "${workspaceFolder}/Mac/renderer"
    }
  ]
}
```

### 9. Common Debug Scenarios

**App won't start:**
- Check terminal for errors
- Verify `node_modules` installed: `ls node_modules/electron`
- Check main.js syntax: `node -c main/main.js`

**DevTools won't open:**
- Try `Ctrl+Shift+I` keyboard shortcut
- Run with `npm run dev`
- Check if window is focused

**Hotkeys not working:**
- Check terminal for registration errors
- Verify hotkeys aren't conflicting with system shortcuts
- Check if window has focus

**Speech recognition not working:**
- Check microphone permissions in System Preferences
- Verify native module built: `ls main/native/speech-recognition/build/Release/`
- Check console for errors

**IPC not working:**
- Verify preload.js is loaded
- Check contextIsolation settings
- Use console.log in both processes

### 10. Performance Debugging

**Monitor Memory:**
```javascript
// In renderer
setInterval(() => {
  console.log('Memory:', performance.memory);
}, 5000);
```

**Profile Rendering:**
- Open DevTools → Performance tab
- Record a session
- Analyze frame times and bottlenecks

### 11. Network Debugging

**WebView Network:**
- Right-click in WebView → Inspect Element
- Use Network tab in DevTools

**API Calls:**
- Check terminal for service logs
- Use Network tab if using fetch/XMLHttpRequest

## Tips

1. **Always check the terminal** - Main process errors appear there
2. **Use DevTools Console** - Renderer errors appear there
3. **Enable verbose logging** - Set `NODE_ENV=development`
4. **Check permissions** - macOS may block microphone/camera access
5. **Reload window** - `Ctrl+R` or `Cmd+R` to reload renderer

## Quick Reference

| Action | Command/Shortcut |
|--------|------------------|
| Open DevTools | `Ctrl+Shift+I` or `Cmd+Option+I` |
| Run in Dev Mode | `npm run dev` |
| Debug Main Process | `npm run debug` |
| Reload Window | `Ctrl+R` or `Cmd+R` |
| Check Logs | Terminal (main) or DevTools Console (renderer) |

