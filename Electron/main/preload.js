const { contextBridge, ipcRenderer } = require('electron');

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  // Config
  getConfig: () => ipcRenderer.invoke('get-config'),
  saveConfig: (config) => ipcRenderer.invoke('save-config', config),

  // Captions
  startCaptions: (config) => ipcRenderer.invoke('start-captions', config),
  stopCaptions: () => ipcRenderer.invoke('stop-captions'),
  setCaptionMode: (useWindowTextReader) => ipcRenderer.invoke('set-caption-mode', useWindowTextReader),
  checkAccessibilityPermissions: () => ipcRenderer.invoke('check-accessibility-permissions'),
  requestAccessibilityPermissions: () => ipcRenderer.invoke('request-accessibility-permissions'),
  
  // Clipboard
  writeClipboard: (text) => ipcRenderer.invoke('write-clipboard', text),
  getTranscript: () => ipcRenderer.invoke('get-transcript'),
  getDelta: () => ipcRenderer.invoke('get-delta'),
  markDeltaSent: () => ipcRenderer.invoke('mark-delta-sent'),
  clearTranscript: () => ipcRenderer.invoke('clear-transcript'),
  sendTranscript: (transcript) => ipcRenderer.invoke('send-transcript', transcript),
  onTranscriptUpdated: (callback) => {
    ipcRenderer.on('transcript-updated', (event, transcript) => callback(transcript));
  },
  onSpeechError: (error) => ipcRenderer.invoke('speech-error', error),

  // Calendar
  connectCalendar: () => ipcRenderer.invoke('connect-calendar'),
  refreshCalendar: () => ipcRenderer.invoke('refresh-calendar'),
  getUpcomingEvents: () => ipcRenderer.invoke('get-upcoming-events'),

  // Window
  moveWindow: (dx, dy) => ipcRenderer.invoke('move-window', dx, dy),
  resizeWindow: (dw, dh) => ipcRenderer.invoke('resize-window', dw, dh),
  setOpacity: (opacity) => ipcRenderer.invoke('set-opacity', opacity),
  adjustOpacity: (delta) => ipcRenderer.invoke('adjust-opacity', delta),
  toggleVisibility: () => ipcRenderer.invoke('toggle-visibility'),
  setExcludedFromCapture: (enabled) => ipcRenderer.invoke('set-excluded-from-capture', enabled),

  // Browser
  openURL: (url) => ipcRenderer.invoke('open-url', url),

  // Screenshot
  takeScreenshot: () => ipcRenderer.invoke('take-screenshot'),

  // App
  exitApp: () => ipcRenderer.invoke('exit-app'),
  toggleDevTools: () => ipcRenderer.invoke('toggle-devtools'),
  
  // Hotkey handlers (exposed for renderer to listen)
  onHotkey: (channel, callback) => {
    ipcRenderer.on(channel, (event, ...args) => callback(...args));
  },
  
  // Remove hotkey listener
  removeHotkeyListener: (channel, callback) => {
    ipcRenderer.removeListener(channel, callback);
  },
  
  // Hotkey configuration
  getHotkeys: () => ipcRenderer.invoke('get-hotkeys'),
  updateHotkeys: (hotkeys) => ipcRenderer.invoke('update-hotkeys', hotkeys),
  onHotkeysUpdated: (callback) => {
    ipcRenderer.on('hotkeys-updated', (event, hotkeys) => callback(hotkeys));
  },
  
  // Prompts loaded notification
  onPromptsLoaded: (callback) => {
    ipcRenderer.on('prompts-loaded', (event, data) => callback(data));
  },
  
  // Settings
  openSettings: () => ipcRenderer.invoke('open-settings'),
  
  // Server config
  getServerConfig: () => ipcRenderer.invoke('get-server-config'),
  saveServerConfig: (config) => ipcRenderer.invoke('save-server-config', config),
  loginToServer: (serverUrl, username, password) => ipcRenderer.invoke('login-to-server', serverUrl, username, password),
  reloadPrompts: () => ipcRenderer.invoke('reload-prompts'),
  logoutFromServer: () => ipcRenderer.invoke('logout-from-server'),
  
  // Debug
  testHotkeys: () => ipcRenderer.invoke('test-hotkeys'),
  
  // Debug helpers
  log: (...args) => console.log('[Renderer]', ...args),
  error: (...args) => console.error('[Renderer]', ...args),
  warn: (...args) => console.warn('[Renderer]', ...args)
});

