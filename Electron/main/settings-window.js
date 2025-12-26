const { BrowserWindow } = require('electron');
const path = require('path');

let settingsWindow = null;
const isDevMode = process.env.NODE_ENV === 'development' || process.argv.includes('--dev');

function createSettingsWindow(parentWindow) {
  if (settingsWindow) {
    settingsWindow.focus();
    return settingsWindow;
  }

  settingsWindow = new BrowserWindow({
    width: 700,
    height: 800,
    parent: parentWindow,
    modal: false,
    frame: true,
    resizable: true,
    backgroundColor: '#1a1a1a',
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      preload: path.join(__dirname, 'preload.js')
    },
    title: 'Settings',
    show: false
  });

  // Load settings HTML
  settingsWindow.loadFile(path.join(__dirname, '../renderer/settings.html'));

  settingsWindow.once('ready-to-show', () => {
    settingsWindow.show();
    
    // Open DevTools in dev mode for debugging
    if (isDevMode) {
      settingsWindow.webContents.openDevTools();
    }
  });

  settingsWindow.on('closed', () => {
    settingsWindow = null;
  });
  
  // Ensure preload script is loaded
  settingsWindow.webContents.on('did-finish-load', () => {
    console.log('Settings window loaded, electronAPI should be available');
  });

  return settingsWindow;
}

function getSettingsWindow() {
  return settingsWindow;
}

function closeSettingsWindow() {
  if (settingsWindow) {
    settingsWindow.close();
  }
}

module.exports = {
  createSettingsWindow,
  getSettingsWindow,
  closeSettingsWindow
};


