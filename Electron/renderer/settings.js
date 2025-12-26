// Settings window JavaScript
// Use window.electronAPI directly to avoid declaration conflicts
// electronAPI is exposed by preload.js via contextBridge

// Hotkey definitions with labels
const hotkeyDefinitions = {
  sendToWebView: { label: 'Send to WebView', description: 'Send text to the browser view' },
  moveLeft: { label: 'Move Left', description: 'Move window left' },
  moveRight: { label: 'Move Right', description: 'Move window right' },
  moveUp: { label: 'Move Up', description: 'Move window up' },
  moveDown: { label: 'Move Down', description: 'Move window down' },
  widthDecrease: { label: 'Width Decrease', description: 'Decrease window width' },
  widthIncrease: { label: 'Width Increase', description: 'Increase window width' },
  heightDecrease: { label: 'Height Decrease', description: 'Decrease window height' },
  heightIncrease: { label: 'Height Increase', description: 'Increase window height' },
  opacityIncrease: { label: 'Opacity Increase', description: 'Increase window opacity' },
  opacityDecrease: { label: 'Opacity Decrease', description: 'Decrease window opacity' },
  toggleVisibility: { label: 'Toggle Visibility', description: 'Show/hide window' },
  screenshot: { label: 'Screenshot', description: 'Take screenshot' },
  exit: { label: 'Exit', description: 'Exit application' }
};

let currentHotkeys = {};
let originalHotkeys = {};
let hasChanges = false;

// Server settings state
let serverConfig = {
  serverUrl: '',
  username: '',
  authToken: ''
};

// Timeout for debouncing server config saves
let saveConfigTimeout = null;

// Initialize
async function init() {
  console.log('Settings: Initializing...');
  console.log('Document ready state:', document.readyState);
  
  // Get electronAPI from window (it's exposed by preload script)
  const api = window.electronAPI;
  console.log('electronAPI available:', !!api);
  console.log('electronAPI methods:', api ? Object.keys(api) : 'none');
  
  if (!api) {
    console.error('Settings: electronAPI not available!');
    // Show error to user
    const statusEl = document.getElementById('statusMessage');
    if (statusEl) {
      statusEl.textContent = 'Error: electronAPI not available. Please restart the app.';
      statusEl.className = 'settings-status show error';
    }
    return;
  }
  
  try {
    await loadHotkeys();
    renderHotkeys();
    await loadServerConfig();
    
    // Setup event listeners - ensure DOM is ready
    const closeBtn = document.getElementById('closeButton');
    if (closeBtn) {
      setupEventListeners();
      updateServerStatus();
      console.log('Settings: Initialization complete');
    } else {
      console.warn('Settings: DOM elements not found, retrying...');
      setTimeout(() => {
        setupEventListeners();
        updateServerStatus();
      }, 100);
    }
  } catch (error) {
    console.error('Settings: Initialization error:', error);
    const statusEl = document.getElementById('statusMessage');
    if (statusEl) {
      statusEl.textContent = 'Initialization error: ' + error.message;
      statusEl.className = 'settings-status show error';
    }
  }
}

// Load hotkeys from main process
async function loadHotkeys() {
  try {
    const api = window.electronAPI;
    if (api && api.getHotkeys) {
      currentHotkeys = await api.getHotkeys();
      originalHotkeys = { ...currentHotkeys };
    } else {
      showStatus('Error: Cannot access hotkey API', 'error');
    }
  } catch (error) {
    console.error('Error loading hotkeys:', error);
    showStatus('Error loading hotkeys', 'error');
  }
}

// Render hotkeys list
function renderHotkeys() {
  const list = document.getElementById('hotkeysList');
  list.innerHTML = '';
  
  for (const [key, def] of Object.entries(hotkeyDefinitions)) {
    const item = document.createElement('div');
    item.className = 'hotkey-item';
    item.dataset.key = key;
    
    const value = currentHotkeys[key] || '';
    
    item.innerHTML = `
      <div class="hotkey-label">
        ${def.label}
        <span class="description">${def.description}</span>
      </div>
      <input 
        type="text" 
        class="hotkey-input" 
        value="${escapeHtml(value)}" 
        placeholder="e.g., CommandOrControl+Shift+/"
        data-key="${key}"
      />
      <div class="hotkey-status" id="status-${key}"></div>
    `;
    
    list.appendChild(item);
    
    // Setup input handler
    const input = item.querySelector('.hotkey-input');
    input.addEventListener('input', (e) => {
      handleHotkeyInput(e.target, key);
    });
    
    input.addEventListener('blur', (e) => {
      validateHotkey(e.target, key);
    });
  }
}

// Handle hotkey input
function handleHotkeyInput(input, key) {
  hasChanges = true;
  const value = input.value.trim();
  currentHotkeys[key] = value;
  
  // Clear previous status
  const statusEl = document.getElementById(`status-${key}`);
  if (statusEl) {
    statusEl.textContent = '';
    statusEl.className = 'hotkey-status';
  }
  
  // Remove error styling
  input.classList.remove('error');
  input.parentElement.classList.remove('error');
}

// Validate hotkey format
function validateHotkey(input, key) {
  const value = input.value.trim();
  const statusEl = document.getElementById(`status-${key}`);
  
  if (!value) {
    statusEl.textContent = 'Required';
    statusEl.className = 'hotkey-status error';
    input.classList.add('error');
    input.parentElement.classList.add('error');
    return false;
  }
  
  // Basic validation - should contain CommandOrControl or Command or Control
  if (!value.match(/^(Command|Control|CommandOrControl)/i)) {
    statusEl.textContent = 'Invalid format';
    statusEl.className = 'hotkey-status error';
    input.classList.add('error');
    input.parentElement.classList.add('error');
    return false;
  }
  
  // Clear error if valid
  statusEl.textContent = '';
  statusEl.className = 'hotkey-status';
  input.classList.remove('error');
  input.parentElement.classList.remove('error');
  return true;
}

// Apply changes
async function applyChanges() {
  // Validate all hotkeys
  let allValid = true;
  for (const key of Object.keys(hotkeyDefinitions)) {
    const input = document.querySelector(`input[data-key="${key}"]`);
    if (input && !validateHotkey(input, key)) {
      allValid = false;
    }
  }
  
  if (!allValid) {
    showStatus('Please fix all errors before applying', 'error');
    return;
  }
  
  try {
    const api = window.electronAPI;
    if (api && api.updateHotkeys) {
      const result = await api.updateHotkeys(currentHotkeys);
      if (result.success) {
        originalHotkeys = { ...currentHotkeys };
        hasChanges = false;
        showStatus('Hotkeys updated successfully!', 'success');
        setTimeout(() => {
          hideStatus();
        }, 2000);
      } else {
        showStatus(`Error: ${result.error || 'Failed to update hotkeys'}`, 'error');
      }
    } else {
      showStatus('Error: Cannot access hotkey API', 'error');
    }
  } catch (error) {
    console.error('Error applying hotkeys:', error);
    showStatus('Error applying hotkeys', 'error');
  }
}

// Reset to defaults
async function resetToDefaults() {
  console.log('resetToDefaults called');
  if (confirm('Reset all hotkeys to default values?')) {
    try {
      const api = window.electronAPI;
      if (api && api.getHotkeys) {
        // Get defaults from shared config
        const defaultConfig = await api.getConfig();
        if (defaultConfig && defaultConfig.hotkeys) {
          currentHotkeys = { ...defaultConfig.hotkeys };
        } else {
          // Fallback to hardcoded defaults
          currentHotkeys = {
            sendToWebView: 'CommandOrControl+Shift+/',
            moveLeft: 'CommandOrControl+Shift+J',
            moveRight: 'CommandOrControl+Shift+L',
            moveUp: 'CommandOrControl+Shift+I',
            moveDown: 'CommandOrControl+Shift+K',
            widthDecrease: 'CommandOrControl+Shift+R',
            widthIncrease: 'CommandOrControl+Shift+T',
            heightDecrease: 'CommandOrControl+Shift+Q',
            heightIncrease: 'CommandOrControl+Shift+A',
            toggleVisibility: 'CommandOrControl+Shift+H',
            exit: 'CommandOrControl+Shift+P',
            opacityIncrease: 'CommandOrControl+Shift+Up',
            opacityDecrease: 'CommandOrControl+Shift+Down',
            screenshot: 'CommandOrControl+Shift+.'
          };
        }
        originalHotkeys = { ...currentHotkeys };
        hasChanges = false;
        renderHotkeys();
        showStatus('Reset to defaults', 'info');
        setTimeout(() => hideStatus(), 2000);
      } else {
        console.error('electronAPI.getHotkeys not available');
      }
    } catch (error) {
      console.error('Error resetting hotkeys:', error);
      showStatus('Error resetting hotkeys', 'error');
    }
  }
}


// Load server config
async function loadServerConfig() {
  try {
    const api = window.electronAPI;
    if (api && api.getServerConfig) {
      const config = await api.getServerConfig();
      serverConfig = {
        serverUrl: config.serverUrl || '',
        username: config.username || '',
        authToken: config.authToken || ''
      };

      document.getElementById('serverUrl').value = serverConfig.serverUrl;
      document.getElementById('username').value = serverConfig.username;
    }
  } catch (error) {
    console.error('Error loading server config:', error);
  }
}

// Update server status display
function updateServerStatus() {
  const statusIndicator = document.getElementById('statusIndicator');
  const statusText = document.getElementById('statusText');
  const reloadButton = document.getElementById('reloadPromptsButton');
  const logoutButton = document.getElementById('logoutButton');

  if (serverConfig.authToken && serverConfig.serverUrl) {
    statusIndicator.className = 'status-indicator connected';
    statusText.textContent = `Connected as ${serverConfig.username}`;
    reloadButton.style.display = 'inline-block';
    logoutButton.style.display = 'inline-block';
  } else {
    statusIndicator.className = 'status-indicator';
    statusText.textContent = 'Not connected';
    reloadButton.style.display = 'none';
    logoutButton.style.display = 'none';
  }
}

// Test connection
async function testConnection() {
  console.log('testConnection called');
  const serverUrl = document.getElementById('serverUrl').value.trim();
  if (!serverUrl) {
    showStatus('Please enter a server URL', 'error');
    return;
  }

  const statusIndicator = document.getElementById('statusIndicator');
  const statusText = document.getElementById('statusText');

  if (statusIndicator) statusIndicator.className = 'status-indicator connecting';
  if (statusText) statusText.textContent = 'Testing connection...';

  try {
    // Use fetch with proper error handling
    const testUrl = `${serverUrl}/api/auth/me`;
    console.log('Testing connection to:', testUrl);
    const response = await fetch(testUrl, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${serverConfig.authToken || 'test'}`
      }
    });

    console.log('Response status:', response.status);
    if (response.ok || response.status === 401) {
      // Server is reachable (401 means server is up but auth failed)
      if (statusIndicator) statusIndicator.className = 'status-indicator connected';
      if (statusText) statusText.textContent = 'Server is reachable';
      showStatus('Connection test successful', 'success');
      setTimeout(() => hideStatus(), 2000);
    } else {
      throw new Error(`Server returned status ${response.status}`);
    }
  } catch (error) {
    console.error('Connection test error:', error);
    if (statusIndicator) statusIndicator.className = 'status-indicator error';
    if (statusText) statusText.textContent = 'Connection failed';
    showStatus(`Failed to connect to server: ${error.message}. Check the URL and ensure the server is running.`, 'error');
  }
}

// Login to server
async function loginToServer() {
  console.log('loginToServer called');
  const serverUrl = document.getElementById('serverUrl').value.trim();
  const username = document.getElementById('username').value.trim();
  const password = document.getElementById('password').value;

  if (!serverUrl || !username || !password) {
    showStatus('Please fill in all fields', 'error');
    return;
  }

  const statusIndicator = document.getElementById('statusIndicator');
  const statusText = document.getElementById('statusText');

  if (statusIndicator) statusIndicator.className = 'status-indicator connecting';
  if (statusText) statusText.textContent = 'Logging in...';

  try {
    const api = window.electronAPI;
    if (api && api.loginToServer) {
      console.log('Calling electronAPI.loginToServer');
      const result = await api.loginToServer(serverUrl, username, password);
      console.log('Login result:', result);
      if (result && result.success) {
        serverConfig = {
          serverUrl: serverUrl,
          username: username,
          authToken: result.token
        };

        // Save config (including serverUrl and username, but not password)
        const api = window.electronAPI;
        if (api && api.saveServerConfig) {
          await api.saveServerConfig(serverConfig);
        }

        // Clear password field (never save password)
        const passwordField = document.getElementById('password');
        if (passwordField) passwordField.value = '';

        if (statusIndicator) statusIndicator.className = 'status-indicator connected';
        if (statusText) statusText.textContent = `Connected as ${username}`;
        updateServerStatus();
        showStatus('Login successful!', 'success');
        setTimeout(() => hideStatus(), 2000);
      } else {
        throw new Error(result?.error || 'Login failed');
      }
    } else {
      throw new Error('Login API not available');
    }
  } catch (error) {
    console.error('Login error:', error);
    if (statusIndicator) statusIndicator.className = 'status-indicator error';
    if (statusText) statusText.textContent = 'Login failed';
    showStatus(`Login failed: ${error.message}`, 'error');
  }
}

// Save server URL and username when they change (but not on every keystroke)
// saveConfigTimeout is declared at top level
function saveServerUrlAndUsername() {
  const serverUrl = document.getElementById('serverUrl').value.trim();
  const username = document.getElementById('username').value.trim();

  // Clear existing timeout
  if (saveConfigTimeout) {
    clearTimeout(saveConfigTimeout);
  }

  // Save after user stops typing (500ms delay)
  saveConfigTimeout = setTimeout(async () => {
    if (serverUrl || username) {
      const currentConfig = {
        serverUrl: serverUrl,
        username: username,
        authToken: serverConfig.authToken || '' // Keep existing token if any
      };

      const api = window.electronAPI;
      if (api && api.saveServerConfig) {
        try {
          await api.saveServerConfig(currentConfig);
          serverConfig.serverUrl = serverUrl;
          serverConfig.username = username;
        } catch (error) {
          console.error('Error saving server config:', error);
        }
      }
    }
  }, 500);
}

// Reload prompts
async function reloadPrompts() {
  if (!serverConfig.authToken || !serverConfig.serverUrl) {
    showStatus('Not logged in', 'error');
    return;
  }

  try {
    const api = window.electronAPI;
    if (api && api.reloadPrompts) {
      const result = await api.reloadPrompts();
      if (result.success) {
        showStatus(`Loaded ${result.count || 0} prompts`, 'success');
        setTimeout(() => hideStatus(), 2000);
      } else {
        throw new Error(result.error || 'Failed to reload prompts');
      }
    } else {
      throw new Error('Reload prompts API not available');
    }
  } catch (error) {
    showStatus(`Failed to reload prompts: ${error.message}`, 'error');
  }
}

// Logout from server
async function logoutFromServer() {
  const api = window.electronAPI;
  if (api && api.logoutFromServer) {
    await api.logoutFromServer();
  }

  serverConfig = {
    serverUrl: serverConfig.serverUrl, // Keep URL
    username: serverConfig.username, // Keep username
    authToken: ''
  };

  // Save config (keep URL and username, clear token)
  if (api && api.saveServerConfig) {
    await api.saveServerConfig(serverConfig);
  }

  document.getElementById('password').value = '';

  updateServerStatus();
  showStatus('Logged out', 'info');
  setTimeout(() => hideStatus(), 2000);
}


// Tab switching
function setupTabs() {
  const tabButtons = document.querySelectorAll('.tab-button');
  const tabPanes = document.querySelectorAll('.tab-pane');

  tabButtons.forEach(button => {
    button.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      
      const targetTab = button.getAttribute('data-tab');
      console.log('Switching to tab:', targetTab);
      
      // Remove active class from all buttons and panes
      tabButtons.forEach(btn => btn.classList.remove('active'));
      tabPanes.forEach(pane => pane.classList.remove('active'));
      
      // Add active class to clicked button and corresponding pane
      button.classList.add('active');
      const targetPane = document.getElementById(targetTab + 'Tab');
      if (targetPane) {
        targetPane.classList.add('active');
      }
    };
  });
}

// Setup event listeners
function setupEventListeners() {
  console.log('Settings: Setting up event listeners...');
  const api = window.electronAPI;
  console.log('electronAPI in setupEventListeners:', !!api);
  
  // Setup tabs
  setupTabs();
  
  const closeBtn = document.getElementById('closeButton');
  if (closeBtn) {
    closeBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Close button clicked');
      if (hasChanges) {
        if (confirm('You have unsaved changes. Close anyway?')) {
          if (window.electronAPI && window.electronAPI.exitApp) {
            // Use IPC to close window properly
            window.close();
          } else {
            window.close();
          }
        }
      } else {
        window.close();
      }
    };
    console.log('Close button listener added');
  } else {
    console.error('Close button not found');
  }
  
  const applyBtn = document.getElementById('applyButton');
  if (applyBtn) {
    applyBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Apply button clicked');
      applyChanges();
    };
    console.log('Apply button listener added');
  } else {
    console.error('Apply button not found');
  }
  
  const resetBtn = document.getElementById('resetButton');
  if (resetBtn) {
    resetBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Reset button clicked');
      resetToDefaults();
    };
    console.log('Reset button listener added');
  } else {
    console.error('Reset button not found');
  }

  // Server settings event listeners
  const testConnBtn = document.getElementById('testConnectionButton');
  if (testConnBtn) {
    testConnBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Test connection button clicked');
      testConnection();
    };
    console.log('Test connection button listener added');
  } else {
    console.error('testConnectionButton not found');
  }
  
  const loginBtn = document.getElementById('loginButton');
  if (loginBtn) {
    loginBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Login button clicked');
      loginToServer();
    };
    console.log('Login button listener added');
  } else {
    console.error('loginButton not found');
  }
  
  const reloadBtn = document.getElementById('reloadPromptsButton');
  if (reloadBtn) {
    reloadBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Reload prompts button clicked');
      reloadPrompts();
    };
    console.log('Reload prompts button listener added');
  } else {
    console.error('reloadPromptsButton not found');
  }
  
  const logoutBtn = document.getElementById('logoutButton');
  if (logoutBtn) {
    logoutBtn.onclick = (e) => {
      e.preventDefault();
      e.stopPropagation();
      console.log('Logout button clicked');
      logoutFromServer();
    };
    console.log('Logout button listener added');
  } else {
    console.error('logoutButton not found');
  }

  // Auto-save server URL and username when they change
  const serverUrlInput = document.getElementById('serverUrl');
  if (serverUrlInput) {
    serverUrlInput.addEventListener('input', saveServerUrlAndUsername);
    serverUrlInput.addEventListener('blur', async () => {
      // Save immediately on blur
      if (saveConfigTimeout) {
        clearTimeout(saveConfigTimeout);
        saveConfigTimeout = null;
      }
      const serverUrl = serverUrlInput.value.trim();
      const usernameField = document.getElementById('username');
      const username = usernameField ? usernameField.value.trim() : '';
      
      const currentConfig = {
        serverUrl: serverUrl,
        username: username,
        authToken: serverConfig.authToken || ''
      };

      serverConfig.serverUrl = serverUrl;
      serverConfig.username = username;

      const api = window.electronAPI;
      if (api && api.saveServerConfig) {
        try {
          await api.saveServerConfig(currentConfig);
        } catch (error) {
          console.error('Error saving server config:', error);
        }
      }
    });
  }

  const usernameInput = document.getElementById('username');
  if (usernameInput) {
    usernameInput.addEventListener('input', saveServerUrlAndUsername);
    usernameInput.addEventListener('blur', async () => {
      // Save immediately on blur
      if (saveConfigTimeout) {
        clearTimeout(saveConfigTimeout);
        saveConfigTimeout = null;
      }
      const username = usernameInput.value.trim();
      const serverUrlField = document.getElementById('serverUrl');
      const serverUrl = serverUrlField ? serverUrlField.value.trim() : '';
      
      const currentConfig = {
        serverUrl: serverUrl,
        username: username,
        authToken: serverConfig.authToken || ''
      };

      serverConfig.serverUrl = serverUrl;
      serverConfig.username = username;

      const api = window.electronAPI;
      if (api && api.saveServerConfig) {
        try {
          await api.saveServerConfig(currentConfig);
        } catch (error) {
          console.error('Error saving server config:', error);
        }
      }
    });
  }
}

// Show status message
function showStatus(message, type = 'info') {
  const statusEl = document.getElementById('statusMessage');
  statusEl.textContent = message;
  statusEl.className = `settings-status show ${type}`;
}

// Hide status message
function hideStatus() {
  const statusEl = document.getElementById('statusMessage');
  statusEl.className = 'settings-status';
}

// Escape HTML
function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// Initialize on load
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  // DOM already loaded
  init();
}


