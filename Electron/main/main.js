const { app, BrowserWindow, ipcMain, globalShortcut, shell, session } = require('electron');
const path = require('path');
const fs = require('fs');

// File watcher for hot reload (dev mode only)
let fileWatcher = null;
const isDevMode = process.env.NODE_ENV === 'development' || process.argv.includes('--dev');

// Services
let CaptionService = null;
let CalendarService = null;
let HotkeyService = null;

// Main window reference
let mainWindow = null;
let helpWindow = null;
const settingsWindowManager = require('./settings-window');

// Configuration
let config = null;
const configPath = path.join(app.getPath('userData'), 'config.json');

// Load configuration
function loadConfig() {
  const defaultConfig = require('../shared/config');
  try {
    if (fs.existsSync(configPath)) {
      const data = fs.readFileSync(configPath, 'utf8');
      const loadedConfig = JSON.parse(data);
      // Merge with defaults to ensure all keys exist
      config = {
        ...defaultConfig.defaultConfig,
        ...loadedConfig,
        hotkeys: {
          ...defaultConfig.hotkeys,
          ...(loadedConfig.hotkeys || {})
        }
      };
    } else {
      config = {
        ...defaultConfig.defaultConfig,
        hotkeys: defaultConfig.hotkeys
      };
      saveConfig();
    }
  } catch (error) {
    console.error('Error loading config:', error);
    config = {
      ...defaultConfig.defaultConfig,
      hotkeys: defaultConfig.hotkeys
    };
  }
}

// Save configuration
function saveConfig() {
  try {
    fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
  } catch (error) {
    console.error('Error saving config:', error);
  }
}

// Setup hot reload file watcher
function setupHotReload() {
  if (!isDevMode || !mainWindow) return;
  
  const rendererPath = path.join(__dirname, '../renderer');
  
  // Try to use chokidar (better performance), fallback to fs.watch
  try {
    const chokidar = require('chokidar');
    
    // Watch for changes in renderer files
    fileWatcher = chokidar.watch(rendererPath, {
      ignored: /(^|[\/\\])\../, // ignore dotfiles
      persistent: true,
      ignoreInitial: true,
      awaitWriteFinish: {
        stabilityThreshold: 100,
        pollInterval: 50
      }
    });
    
    fileWatcher.on('change', (filePath) => {
      const ext = path.extname(filePath).toLowerCase();
      if (['.html', '.css', '.js'].includes(ext)) {
        console.log(`[Hot Reload] File changed: ${path.basename(filePath)}`);
        
        // Small delay to ensure file is fully written
        setTimeout(() => {
          if (mainWindow && !mainWindow.isDestroyed()) {
            mainWindow.webContents.reload();
          }
        }, 150);
      }
    });
    
    console.log('[Hot Reload] Watching for file changes in:', rendererPath);
  } catch (error) {
    // Fallback to fs.watch if chokidar is not available
    console.warn('[Hot Reload] chokidar not available, using fallback:', error.message);
    const hotReloadFallback = require('./hot-reload');
    hotReloadFallback.setupHotReloadFallback(mainWindow, rendererPath);
  }
}

// Create main window
function createWindow() {
  loadConfig();

  mainWindow = new BrowserWindow({
    width: config.windowBounds.width || 680,
    height: config.windowBounds.height || 650,
    x: config.windowBounds.x,
    y: config.windowBounds.y,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: true,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js'),
      webviewTag: true
      // Note: enableBlinkFeatures is deprecated and causes security warnings
      // Web Speech API should work without it in modern Electron
    },
    show: false,
    skipTaskbar: true
  });

  // Load the renderer
  mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'));

  // Show window when ready
  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
    
    // Set opacity
    if (config.opacity !== undefined) {
      mainWindow.setOpacity(config.opacity);
    }

    // Set excluded from capture if needed
    if (config.excludedFromCapture) {
      setExcludedFromCapture(true);
    }
    
    // Register hotkeys after window is ready
    // This is the proper timing for global shortcuts in Electron
    registerHotkeys();
  });

  // Save window bounds on move/resize
  mainWindow.on('moved', () => {
    if (config) {
      const bounds = mainWindow.getBounds();
      config.windowBounds.x = bounds.x;
      config.windowBounds.y = bounds.y;
      saveConfig();
    }
  });

  mainWindow.on('resized', () => {
    if (config) {
      const bounds = mainWindow.getBounds();
      config.windowBounds.width = bounds.width;
      config.windowBounds.height = bounds.height;
      saveConfig();
    }
  });

  // Handle window close
  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  // Initialize services after window is ready
  mainWindow.webContents.once('did-finish-load', () => {
    initializeServices();
    setupHotkeyHandlers();
    setupCaptionServiceEvents(); // Set up event forwarding after window is ready
    
    // Auto-login and load prompts if token exists
    autoLoginAndLoadPrompts();
    
    // Open DevTools in development mode
    if (isDevMode) {
      mainWindow.webContents.openDevTools();
    }
    
    // Setup hot reload in dev mode
    if (isDevMode) {
      setupHotReload();
    }
  });
  
  // Add keyboard shortcut to toggle DevTools (Cmd+Option+I on Mac, Ctrl+Shift+I on Windows/Linux)
  mainWindow.webContents.on('before-input-event', (event, input) => {
    if (input.control && input.shift && input.key.toLowerCase() === 'i') {
      mainWindow.webContents.toggleDevTools();
    }
  });
}

// Register global hotkeys
function registerHotkeys() {
  if (!HotkeyService || !mainWindow) {
    console.warn('Cannot register hotkeys: HotkeyService or mainWindow not available');
    return;
  }
  
  // Ensure app is ready
  if (!app.isReady()) {
    console.warn('App not ready, delaying hotkey registration');
    app.whenReady().then(() => {
      setTimeout(() => registerHotkeys(), 100);
    });
    return;
  }
  
  try {
    HotkeyService.initialize(mainWindow);
    console.log('Global hotkeys registered successfully');
  } catch (error) {
    console.error('Error registering hotkeys:', error);
  }
}

// Setup hotkey event handlers from renderer
function setupHotkeyHandlers() {
  if (!mainWindow) return;
  
  // HotkeyService sends messages via webContents.send
  // The renderer listens for these messages via electronAPI.onHotkey
  // No additional setup needed here - the communication is direct
}

// Window management functions
function moveWindow(dx, dy) {
  if (mainWindow) {
    const bounds = mainWindow.getBounds();
    mainWindow.setBounds({
      x: bounds.x + dx,
      y: bounds.y + dy,
      width: bounds.width,
      height: bounds.height
    });
  }
}

function resizeWindow(dw, dh) {
  if (mainWindow) {
    const bounds = mainWindow.getBounds();
    mainWindow.setBounds({
      x: bounds.x,
      y: bounds.y,
      width: Math.max(200, bounds.width + dw),
      height: Math.max(200, bounds.height + dh)
    });
  }
}

function adjustOpacity(delta) {
  if (mainWindow) {
    const current = mainWindow.getOpacity();
    const newOpacity = Math.max(0.1, Math.min(1.0, current + delta));
    mainWindow.setOpacity(newOpacity);
    if (config) {
      config.opacity = newOpacity;
      saveConfig();
    }
  }
}

function toggleVisibility() {
  if (mainWindow) {
    if (mainWindow.isVisible()) {
      mainWindow.hide();
    } else {
      mainWindow.show();
    }
  }
}

async function takeScreenshot() {
  if (!mainWindow) return;
  
  try {
    // Use Electron's built-in screenshot capability
    const { screen } = require('electron');
    const primaryDisplay = screen.getPrimaryDisplay();
    const { width, height } = primaryDisplay.size;
    
    // For now, just log - full implementation would capture screen
    console.log('Screenshot requested');
    // TODO: Implement full screenshot functionality
  } catch (error) {
    console.error('Error taking screenshot:', error);
  }
}

function handleSendToWebView() {
  if (mainWindow) {
    mainWindow.webContents.send('hotkey-send-to-webview');
  }
}

// Initialize services
function initializeServices() {
  // Initialize CaptionService (will be implemented with native module)
  try {
    CaptionService = require('../services/CaptionService');
    if (CaptionService) {
      CaptionService.initialize();
    }
  } catch (error) {
    console.error('Error initializing CaptionService:', error);
  }

  // Initialize CalendarService
  try {
    CalendarService = require('../services/CalendarService');
    if (CalendarService) {
      CalendarService.initialize();
    }
  } catch (error) {
    console.error('Error initializing CalendarService:', error);
  }

  // HotkeyService is loaded in app.whenReady()
  // Hotkey registration is done in registerHotkeys() after window is ready
}

// Set excluded from capture (macOS)
function setExcludedFromCapture(enabled) {
  if (mainWindow && process.platform === 'darwin') {
    // Use Electron's setContentProtection (macOS only)
    // This prevents the window from being captured in screenshots/recordings
    mainWindow.setContentProtection(enabled);
    
    // Also try to set window sharing type via native module if available
    try {
      const { nativeImage } = require('electron');
      // Additional protection can be set via native module
      // For now, setContentProtection should work for most cases
    } catch (error) {
      console.error('Error setting content protection:', error);
    }
  }
}

// IPC Handlers
ipcMain.handle('get-config', () => {
  return config;
});

ipcMain.handle('save-config', (event, newConfig) => {
  config = { ...config, ...newConfig };
  saveConfig();
  return config;
});

ipcMain.handle('start-captions', async (event, config) => {
  if (CaptionService) {
    return await CaptionService.start(config || {});
  }
  return { success: false, error: 'CaptionService not initialized' };
});

ipcMain.handle('set-caption-mode', async (event, useWindowTextReader) => {
  if (CaptionService) {
    CaptionService.setMode(useWindowTextReader);
    
    // If switching to window mode, check Accessibility permissions
    if (useWindowTextReader) {
      const hasPermission = await checkAccessibilityPermissions();
      if (!hasPermission) {
        return { 
          success: false, 
          error: 'Accessibility permissions required. Please grant permission in System Settings > Privacy & Security > Accessibility',
          needsPermission: true
        };
      }
    }
    
    return { success: true };
  }
  return { success: false, error: 'CaptionService not initialized' };
});

ipcMain.handle('check-accessibility-permissions', async () => {
  return await checkAccessibilityPermissions();
});

ipcMain.handle('request-accessibility-permissions', async () => {
  requestAccessibilityPermissions();
  return { success: true };
});

ipcMain.handle('write-clipboard', async (event, text) => {
  const { clipboard } = require('electron');
  clipboard.writeText(text);
  return { success: true };
});

// Helper function to check Accessibility permissions
async function checkAccessibilityPermissions() {
  try {
    // Try to use Accessibility API - if it fails, permissions are not granted
    const { systemPreferences } = require('electron');
    if (systemPreferences && systemPreferences.isTrustedAccessibilityClient) {
      return systemPreferences.isTrustedAccessibilityClient(false);
    }
    // Fallback: try to create an accessibility element
    // This is a simple check - if we can't access, permissions aren't granted
    return false;
  } catch (error) {
    console.error('Error checking Accessibility permissions:', error);
    return false;
  }
}

// Helper function to request Accessibility permissions
function requestAccessibilityPermissions() {
  try {
    const { shell } = require('electron');
    // Open System Settings to Accessibility pane
    shell.openExternal('x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility');
  } catch (error) {
    console.error('Error opening Accessibility settings:', error);
  }
}

ipcMain.handle('stop-captions', () => {
  if (CaptionService) {
    CaptionService.stop();
    return { success: true };
  }
  return { success: false, error: 'CaptionService not initialized' };
});

ipcMain.handle('get-transcript', () => {
  if (CaptionService) {
    return CaptionService.getTranscript();
  }
  return '';
});

ipcMain.handle('get-delta', () => {
  if (CaptionService) {
    return CaptionService.getDelta();
  }
  return '';
});

ipcMain.handle('mark-delta-sent', () => {
  if (CaptionService) {
    CaptionService.markDeltaSent();
    return { success: true };
  }
  return { success: false };
});

ipcMain.handle('clear-transcript', () => {
  if (CaptionService) {
    CaptionService.clear();
    return { success: true };
  }
  return { success: false };
});

ipcMain.handle('send-transcript', (event, transcript) => {
  if (CaptionService) {
    CaptionService.onTranscriptReceived(transcript);
    return { success: true };
  }
  return { success: false };
});

// Throttle speech error logging to avoid spam
let lastSpeechErrorTime = 0;
let speechErrorCount = 0;

ipcMain.handle('speech-error', (event, error) => {
  const now = Date.now();
  speechErrorCount++;
  
  // Only log once per 5 seconds, or every 10th error
  if (now - lastSpeechErrorTime > 5000 || speechErrorCount % 10 === 0) {
    console.error(`Speech recognition error from renderer: ${error} (count: ${speechErrorCount})`);
    lastSpeechErrorTime = now;
    
    // Reset counter periodically
    if (speechErrorCount > 100) {
      speechErrorCount = 0;
    }
  }
  return { success: true };
});

ipcMain.handle('connect-calendar', async () => {
  if (CalendarService) {
    return await CalendarService.connect();
  }
  return { success: false, error: 'CalendarService not initialized' };
});

ipcMain.handle('refresh-calendar', async () => {
  if (CalendarService) {
    return await CalendarService.refresh();
  }
  return { success: false, error: 'CalendarService not initialized' };
});

ipcMain.handle('get-upcoming-events', async () => {
  if (CalendarService) {
    return await CalendarService.getUpcomingEvents();
  }
  return [];
});

ipcMain.handle('move-window', (event, dx, dy) => {
  moveWindow(dx, dy);
});

ipcMain.handle('resize-window', (event, dw, dh) => {
  resizeWindow(dw, dh);
});

ipcMain.handle('set-opacity', (event, opacity) => {
  if (mainWindow) {
    const clamped = Math.max(0.1, Math.min(1.0, opacity));
    mainWindow.setOpacity(clamped);
    if (config) {
      config.opacity = clamped;
      saveConfig();
    }
  }
});

ipcMain.handle('adjust-opacity', (event, delta) => {
  adjustOpacity(delta);
});

ipcMain.handle('toggle-visibility', () => {
  if (mainWindow) {
    if (mainWindow.isVisible()) {
      mainWindow.hide();
    } else {
      mainWindow.show();
    }
  }
});

ipcMain.handle('set-excluded-from-capture', (event, enabled) => {
  setExcludedFromCapture(enabled);
  if (config) {
    config.excludedFromCapture = enabled;
    saveConfig();
  }
  return { success: true };
});

ipcMain.handle('open-url', (event, url) => {
  shell.openExternal(url);
});

ipcMain.handle('take-screenshot', async () => {
  return await takeScreenshot();
});

ipcMain.handle('exit-app', () => {
  app.quit();
});

ipcMain.handle('toggle-devtools', () => {
  if (mainWindow) {
    mainWindow.webContents.toggleDevTools();
  }
});

ipcMain.handle('get-hotkeys', () => {
  if (HotkeyService) {
    return HotkeyService.getHotkeys();
  }
  return require('../shared/config').hotkeys;
});

ipcMain.handle('update-hotkeys', (event, newHotkeys) => {
  if (HotkeyService) {
    const success = HotkeyService.updateHotkeys(newHotkeys);
    if (success && config) {
      config.hotkeys = HotkeyService.getHotkeys();
      saveConfig();
      // Notify renderer that hotkeys have changed
      if (mainWindow) {
        mainWindow.webContents.send('hotkeys-updated', config.hotkeys);
      }
    }
    return { success };
  }
  return { success: false, error: 'HotkeyService not initialized' };
});

ipcMain.handle('open-settings', () => {
  if (mainWindow) {
    settingsWindowManager.createSettingsWindow(mainWindow);
    return { success: true };
  }
  return { success: false, error: 'Main window not available' };
});

ipcMain.handle('test-hotkeys', () => {
  if (!HotkeyService) {
    return { success: false, error: 'HotkeyService not loaded' };
  }
  
  const hotkeys = HotkeyService.getHotkeys();
  const testResults = {};
  
  for (const [name, accelerator] of Object.entries(hotkeys)) {
    const isRegistered = globalShortcut.isRegistered(accelerator);
    testResults[name] = {
      accelerator,
      registered: isRegistered
    };
  }
  
  return { success: true, results: testResults };
});

// Server config IPC handlers
ipcMain.handle('get-server-config', () => {
  if (config) {
    return {
      serverUrl: config.serverUrl || '',
      username: config.username || '',
      authToken: config.authToken || ''
    };
  }
  return { serverUrl: '', username: '', authToken: '' };
});

ipcMain.handle('save-server-config', (event, serverConfig) => {
  if (config) {
    config.serverUrl = serverConfig.serverUrl || '';
    config.username = serverConfig.username || '';
    config.authToken = serverConfig.authToken || '';
    saveConfig();
    return { success: true };
  }
  return { success: false, error: 'Config not loaded' };
});

ipcMain.handle('login-to-server', async (event, serverUrl, username, password) => {
  try {
    const ApiService = require('../services/ApiService');
    ApiService.setConfig(serverUrl, null);
    
    const result = await ApiService.login(username, password);
    
    if (result && result.token) {
      // Save config
      if (config) {
        config.serverUrl = serverUrl;
        config.username = username;
        config.authToken = result.token;
        saveConfig();
      }
      
      return { success: true, token: result.token, user: result.user };
    }
    
    return { success: false, error: 'Login failed' };
  } catch (error) {
    console.error('Login error:', error);
    return { success: false, error: error.message || 'Login failed' };
  }
});

ipcMain.handle('reload-prompts', async () => {
  try {
    if (!config || !config.serverUrl || !config.authToken) {
      return { success: false, error: 'Not logged in' };
    }

    const ApiService = require('../services/ApiService');
    ApiService.setConfig(config.serverUrl, config.authToken);
    
    const prompts = await ApiService.fetchPrompts();
    
    if (config) {
      config.prompts = prompts;
      saveConfig();
    }
    
    return { success: true, count: prompts.length, prompts };
  } catch (error) {
    console.error('Reload prompts error:', error);
    
    // If unauthorized, clear token
    if (error.message === 'UNAUTHORIZED' && config) {
      config.authToken = '';
      saveConfig();
    }
    
    return { success: false, error: error.message || 'Failed to reload prompts' };
  }
});

// Auto-login and load prompts on startup if token exists
async function autoLoginAndLoadPrompts() {
  try {
    if (!config || !config.serverUrl || !config.authToken) {
      console.log('No saved credentials for auto-login');
      return;
    }

    console.log('Attempting auto-login with saved token...');
    const ApiService = require('../services/ApiService');
    ApiService.setConfig(config.serverUrl, config.authToken);
    
    // Verify token is still valid by trying to fetch user info
    try {
      await ApiService.verifyToken();
      console.log('Token is valid, loading prompts...');
      
      // Token is valid, load prompts
      const prompts = await ApiService.fetchPrompts();
      
      if (config) {
        config.prompts = prompts;
        saveConfig();
      }
      
      console.log(`Auto-login successful. Loaded ${prompts.length} prompts.`);
      
      // Notify renderer that prompts are ready
      if (mainWindow && !mainWindow.isDestroyed()) {
        mainWindow.webContents.send('prompts-loaded', { count: prompts.length, prompts });
      }
    } catch (error) {
      console.log('Token verification failed:', error.message);
      // Token is invalid, clear it
      if (config) {
        config.authToken = '';
        saveConfig();
      }
      console.log('Cleared invalid token');
    }
  } catch (error) {
    console.error('Auto-login error:', error);
  }
}

ipcMain.handle('logout-from-server', () => {
  if (config) {
    config.authToken = '';
    config.username = '';
    saveConfig();
  }
  return { success: true };
});

// CaptionService event forwarding
// Set up after window is created
function setupCaptionServiceEvents() {
  if (CaptionService) {
    // Remove existing listeners to avoid duplicates
    CaptionService.removeAllListeners('transcript-updated');
    CaptionService.removeAllListeners('error');
    
    CaptionService.on('transcript-updated', (transcript) => {
      if (mainWindow && mainWindow.webContents && !mainWindow.webContents.isDestroyed()) {
        try {
          mainWindow.webContents.send('transcript-updated', transcript);
        } catch (error) {
          console.error('Error sending transcript-updated:', error);
        }
      }
    });
    
    // Log errors but don't spam
    let lastErrorTime = 0;
    CaptionService.on('error', (error) => {
      const now = Date.now();
      // Only log errors once per second to avoid spam
      if (now - lastErrorTime > 1000) {
        console.error('CaptionService error:', error);
        lastErrorTime = now;
      }
    });
    
  }
}

// Setup microphone permissions
function setupPermissions() {
  session.defaultSession.setPermissionRequestHandler((webContents, permission, callback) => {
    // Allow microphone permission
    if (permission === 'media') {
      callback(true);
    } else {
      callback(false);
    }
  });
  
  // Handle permission check
  session.defaultSession.setPermissionCheckHandler((webContents, permission, requestingOrigin) => {
    if (permission === 'media') {
      return true;
    }
    return false;
  });
}

// App event handlers
app.whenReady().then(() => {
  // Setup permissions first
  setupPermissions();
  
  // Load HotkeyService module
  try {
    HotkeyService = require('../services/HotkeyService');
  } catch (error) {
    console.error('Error loading HotkeyService:', error);
  }
  
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('will-quit', () => {
  // Unregister all shortcuts
  globalShortcut.unregisterAll();
  
  // Close file watcher
  if (fileWatcher) {
    try {
      fileWatcher.close();
    } catch (error) {
      // Ignore errors
    }
    fileWatcher = null;
  }
  
  // Cleanup fallback watcher
  try {
    const hotReloadFallback = require('./hot-reload');
    hotReloadFallback.cleanup();
  } catch (error) {
    // Ignore if module doesn't exist
  }
  
  // Cleanup services
  if (CaptionService) {
    CaptionService.cleanup();
  }
  if (CalendarService) {
    CalendarService.cleanup();
  }
  if (HotkeyService) {
    HotkeyService.cleanup();
  }
});

