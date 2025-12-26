// Hot reload fallback using fs.watch (no external dependencies)
// This is a simpler alternative if chokidar is not available

const fs = require('fs');
const path = require('path');

let watchers = [];

function setupHotReloadFallback(window, rendererPath) {
  if (!window || !fs.existsSync(rendererPath)) {
    return;
  }

  console.log('[Hot Reload] Setting up file watcher (fallback mode)');

  // Watch renderer directory
  try {
    const watcher = fs.watch(rendererPath, { recursive: true }, (eventType, filename) => {
      if (filename && (filename.endsWith('.html') || filename.endsWith('.css') || filename.endsWith('.js'))) {
        console.log(`[Hot Reload] File changed: ${filename}`);
        
        // Small delay to ensure file is written
        setTimeout(() => {
          if (window && !window.isDestroyed()) {
            window.webContents.reload();
          }
        }, 100);
      }
    });

    watchers.push(watcher);
    console.log('[Hot Reload] Watching:', rendererPath);
  } catch (error) {
    console.warn('[Hot Reload] Failed to setup file watcher:', error.message);
  }
}

function cleanup() {
  watchers.forEach(watcher => {
    try {
      watcher.close();
    } catch (error) {
      // Ignore errors on cleanup
    }
  });
  watchers = [];
}

module.exports = {
  setupHotReloadFallback,
  cleanup
};

