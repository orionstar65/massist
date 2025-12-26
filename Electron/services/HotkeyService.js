const { globalShortcut, BrowserWindow } = require('electron');
const defaultConfig = require('../shared/config');
const fs = require('fs');
const path = require('path');
const { app } = require('electron');

class HotkeyService {
  constructor() {
    this.window = null;
    this.registeredHotkeys = new Map();
    this.isInitialized = false;
    this.configPath = null;
  }
  
  getHotkeys() {
    // Load hotkeys from user config file, fallback to defaults
    const defaultHotkeys = defaultConfig.hotkeys;
    
    if (!this.configPath) {
      this.configPath = path.join(app.getPath('userData'), 'config.json');
    }
    
    try {
      if (fs.existsSync(this.configPath)) {
        const data = fs.readFileSync(this.configPath, 'utf8');
        const userConfig = JSON.parse(data);
        if (userConfig.hotkeys) {
          return { ...defaultHotkeys, ...userConfig.hotkeys };
        }
      }
    } catch (error) {
      console.error('Error loading hotkey config:', error);
    }
    
    return defaultHotkeys;
  }
  
  updateHotkeys(newHotkeys) {
    // Update config file with new hotkeys
    if (!this.configPath) {
      this.configPath = path.join(app.getPath('userData'), 'config.json');
    }
    
    try {
      let userConfig = {};
      if (fs.existsSync(this.configPath)) {
        const data = fs.readFileSync(this.configPath, 'utf8');
        userConfig = JSON.parse(data);
      }
      
      userConfig.hotkeys = { ...this.getHotkeys(), ...newHotkeys };
      
      fs.writeFileSync(this.configPath, JSON.stringify(userConfig, null, 2));
      
      // Re-register all hotkeys with new values
      this.unregisterAll();
      this.registerAllHotkeys();
      
      return true;
    } catch (error) {
      console.error('Error updating hotkeys:', error);
      return false;
    }
  }
  
  unregisterAll() {
    this.registeredHotkeys.forEach((callback, accelerator) => {
      globalShortcut.unregister(accelerator);
    });
    this.registeredHotkeys.clear();
  }

  initialize(window) {
    this.window = window;
    
    if (!this.window) {
      console.error('HotkeyService: Window not provided');
      return;
    }

    // Check if app is ready (required for global shortcuts)
    const { app } = require('electron');
    if (!app.isReady()) {
      console.error('HotkeyService: App not ready! Global shortcuts require app.whenReady()');
      return;
    }

    console.log('HotkeyService: Initializing global shortcuts...');
    
    // Register all hotkeys
    const results = this.registerAllHotkeys();
    
    // Report registration status
    const successCount = results.filter(r => r.success).length;
    const failCount = results.filter(r => !r.success).length;
    
    if (failCount > 0) {
      console.warn(`\n⚠️  Hotkey Registration Status:`);
      console.warn(`   ✓ Registered: ${successCount}`);
      console.warn(`   ✗ Failed: ${failCount}`);
      console.warn(`\n   Failed hotkeys:`);
      results.filter(r => !r.success).forEach(r => {
        const hotkeys = this.getHotkeys();
        const accelerator = Object.entries(hotkeys).find(([key]) => key === r.name)?.[1];
        console.warn(`     - ${r.name}: ${accelerator || 'unknown'}`);
      });
      
      if (process.platform === 'darwin') {
        console.warn(`\n   📋 macOS Fix:`);
        console.warn(`   1. Open System Preferences > Security & Privacy > Privacy > Accessibility`);
        console.warn(`   2. Add this app to the list and enable it`);
        console.warn(`   3. Restart the app`);
        console.warn(`   4. Check System Preferences > Keyboard > Shortcuts for conflicts\n`);
      }
    } else {
      console.log(`✓ HotkeyService: All ${successCount} hotkeys registered successfully\n`);
    }
    
    this.isInitialized = true;
  }

  registerAllHotkeys() {
    const hotkeys = this.getHotkeys();
    const results = [];
    
    // Send to WebView
    results.push({
      name: 'sendToWebView',
      success: this.registerHotkey(hotkeys.sendToWebView, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-send-to-webview');
        }
      })
    });

    // Window movement
    results.push({
      name: 'moveLeft',
      success: this.registerHotkey(hotkeys.moveLeft, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-move', { dx: -8, dy: 0 });
        }
      })
    });

    results.push({
      name: 'moveRight',
      success: this.registerHotkey(hotkeys.moveRight, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-move', { dx: 8, dy: 0 });
        }
      })
    });

    results.push({
      name: 'moveUp',
      success: this.registerHotkey(hotkeys.moveUp, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-move', { dx: 0, dy: -8 });
        }
      })
    });

    results.push({
      name: 'moveDown',
      success: this.registerHotkey(hotkeys.moveDown, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-move', { dx: 0, dy: 8 });
        }
      })
    });

    // Window resizing
    results.push({
      name: 'widthDecrease',
      success: this.registerHotkey(hotkeys.widthDecrease, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-resize', { dw: -10, dh: 0 });
        }
      })
    });

    results.push({
      name: 'widthIncrease',
      success: this.registerHotkey(hotkeys.widthIncrease, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-resize', { dw: 10, dh: 0 });
        }
      })
    });

    results.push({
      name: 'heightDecrease',
      success: this.registerHotkey(hotkeys.heightDecrease, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-resize', { dw: 0, dh: -10 });
        }
      })
    });

    results.push({
      name: 'heightIncrease',
      success: this.registerHotkey(hotkeys.heightIncrease, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-resize', { dw: 0, dh: 10 });
        }
      })
    });

    // Opacity
    results.push({
      name: 'opacityIncrease',
      success: this.registerHotkey(hotkeys.opacityIncrease, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-opacity', 0.05);
        }
      })
    });

    results.push({
      name: 'opacityDecrease',
      success: this.registerHotkey(hotkeys.opacityDecrease, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-opacity', -0.05);
        }
      })
    });

    // Visibility
    results.push({
      name: 'toggleVisibility',
      success: this.registerHotkey(hotkeys.toggleVisibility, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-toggle-visibility');
        }
      })
    });

    // Screenshot
    results.push({
      name: 'screenshot',
      success: this.registerHotkey(hotkeys.screenshot, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-screenshot');
        }
      })
    });

    // Exit
    results.push({
      name: 'exit',
      success: this.registerHotkey(hotkeys.exit, () => {
        if (this.window) {
          this.window.webContents.send('hotkey-exit');
        }
      })
    });
    
    return results;
  }

  registerHotkey(accelerator, callback) {
    if (!accelerator || !callback) {
      console.error(`Invalid hotkey registration: accelerator=${accelerator}, callback=${!!callback}`);
      return false;
    }
    
    // Unregister if already registered
    if (globalShortcut.isRegistered(accelerator)) {
      console.log(`Unregistering existing hotkey: ${accelerator}`);
      globalShortcut.unregister(accelerator);
      this.registeredHotkeys.delete(accelerator);
    }
    
    // Register the hotkey
    const success = globalShortcut.register(accelerator, () => {
      try {
        callback();
      } catch (error) {
        console.error(`Error in hotkey callback for ${accelerator}:`, error);
      }
    });
    
    if (success) {
      // Double-check registration
      const isRegistered = globalShortcut.isRegistered(accelerator);
      if (isRegistered) {
        this.registeredHotkeys.set(accelerator, callback);
        console.log(`✓ Hotkey registered: ${accelerator}`);
        return true;
      } else {
        console.error(`✗ Hotkey registration verification failed: ${accelerator}`);
        return false;
      }
    } else {
      console.error(`✗ Failed to register hotkey: ${accelerator}`);
      console.error(`  Possible reasons:`);
      console.error(`  - Conflict with system shortcuts (check System Preferences > Keyboard > Shortcuts)`);
      console.error(`  - Missing Accessibility permissions (macOS: System Preferences > Security & Privacy > Privacy > Accessibility)`);
      console.error(`  - Invalid accelerator format`);
      console.error(`  - Another app is using this shortcut`);
      
      // On macOS, provide helpful guidance
      if (process.platform === 'darwin') {
        console.error(`  macOS: Make sure the app is in the Accessibility list and enabled`);
      }
      
      return false;
    }
  }

  unregisterHotkey(accelerator) {
    if (this.registeredHotkeys.has(accelerator)) {
      globalShortcut.unregister(accelerator);
      this.registeredHotkeys.delete(accelerator);
      console.log(`Hotkey unregistered: ${accelerator}`);
      return true;
    }
    return false;
  }

  cleanup() {
    // Unregister all hotkeys
    this.registeredHotkeys.forEach((callback, accelerator) => {
      globalShortcut.unregister(accelerator);
    });
    
    this.registeredHotkeys.clear();
    this.isInitialized = false;
    console.log('HotkeyService cleaned up');
  }
}

// Export singleton instance
const hotkeyService = new HotkeyService();
module.exports = hotkeyService;

