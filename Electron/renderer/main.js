// Main renderer process JavaScript
// Use IIFE to prevent duplicate declarations on hot reload
(function() {
  'use strict';
  
  // Check if already initialized (for hot reload)
  if (window.__APP_INITIALIZED__) {
    return;
  }
  window.__APP_INITIALIZED__ = true;

  // Safely get electronAPI - handle cases where it might already be declared
  let electronAPI;
  try {
    if (window.electronAPI) {
      electronAPI = window.electronAPI;
    }
  } catch (error) {
    console.error('Error accessing electronAPI:', error);
  }

// State
let isCapturing = false;
let transcript = '';
let lastSentPos = 0;
let config = null;
let isInterviewMode = true;
let isLoggingEnabled = false;
let currentCursorSystem = 'normal';
let speechRecognition = null;
let networkRetryCount = 0;
let networkRetryTimer = null;
let isRetrying = false; // Flag to prevent onend from restarting during retry
let captionSource = 'window'; // 'microphone' or 'window' - default to window for system audio
const MAX_NETWORK_RETRIES = 3;
let prompts = []; // Loaded prompts from server
let defaultPrompts = [ // Fallback default prompts
  { name: 'Professional', prompt: 'Professional', value: 'professional' },
  { name: 'Funny', prompt: 'Interesting - light Funny', value: 'funny' },
  { name: 'STAR', prompt: 'STAR', value: 'star' },
  { name: 'Sharp', prompt: 'Sharp', value: 'sharp' },
  { name: 'Creative', prompt: 'Creative', value: 'creative' },
  { name: 'Specific', prompt: 'Mentioning specific thing(s)', value: 'specific' },
  { name: 'Story mode', prompt: 'Story mode', value: 'story' },
  { name: 'Impactful', prompt: 'Impactful', value: 'impactful' },
  { name: 'Tech stack versions', prompt: 'mentioning Tech stack versions at the next of each tech stacks inside the answer if the answer explains technical problem', value: 'techstack' },
  { name: 'Detailed', prompt: 'Detailed', value: 'detailed' },
  { name: 'Step-by-Step', prompt: 'Step-by-Step', value: 'stepbystep' }
];

// DOM Elements - will be populated when DOM is ready
let elements = {};

// Initialize DOM elements
function initializeElements() {
  elements = {
    startStopButton: document.getElementById('startStopButton'),
    copyDeltaButton: document.getElementById('copyDeltaButton'),
    clearButton: document.getElementById('clearButton'),
  fontSizeSlider: document.getElementById('fontSizeSlider'),
  fontSizeLabel: document.getElementById('fontSizeLabel'),
  captionSourceRadios: document.querySelectorAll('input[name="captionSource"]'),
  statusLight: document.getElementById('statusLight'),
    captionText: document.getElementById('captionText'),
    captionScroll: document.getElementById('captionScroll'),
    calendarSection: document.getElementById('calendarSection'),
    connectCalendarButton: document.getElementById('connectCalendarButton'),
    refreshCalendarButton: document.getElementById('refreshCalendarButton'),
    toggleCalendarButton: document.getElementById('toggleCalendarButton'),
    calendarStatus: document.getElementById('calendarStatus'),
    calendarEvents: document.getElementById('calendarEvents'),
    stealthBrowser: document.getElementById('stealthBrowser'),
    chatGPTButton: document.getElementById('chatGPTButton'),
    deepSeekButton: document.getElementById('deepSeekButton'),
    perplexityButton: document.getElementById('perplexityButton'),
    grokButton: document.getElementById('grokButton'),
    toggleButtons: document.querySelectorAll('.toggle-button'),
    radioButtons: document.querySelectorAll('.radio-button'),
    followUpButton: document.getElementById('followUpButton'),
    clarifyButton: document.getElementById('clarifyButton'),
    answerButton: document.getElementById('answerButton'),
    arrowCursorButton: document.getElementById('arrowCursorButton'),
    caretCursorButton: document.getElementById('caretCursorButton'),
    normalCursorButton: document.getElementById('normalCursorButton'),
    loggingButton: document.getElementById('loggingButton'),
    modeToggleButton: document.getElementById('modeToggleButton'),
    settingsButton: document.getElementById('settingsButton'),
    helpButton: document.getElementById('helpButton'),
    exitButton: document.getElementById('exitButton'),
    helpWindow: document.getElementById('helpWindow'),
    startupLabel: document.getElementById('startupLabel'),
    mainBorder: document.getElementById('mainBorder'),
    contentGrid: document.getElementById('contentGrid'),
    buttonGroupsRow: document.getElementById('buttonGroupsRow'),
    controlPanel: document.getElementById('controlPanel')
  };
  
  // Log missing elements for debugging
  const missingElements = [];
  for (const [key, value] of Object.entries(elements)) {
    if (!value && key !== 'toggleButtons' && key !== 'radioButtons') {
      missingElements.push(key);
    }
  }
  
  if (missingElements.length > 0) {
    console.warn('Missing DOM elements:', missingElements);
  } else {
  }
}

// Initialize
async function init() {
  
  // Initialize DOM elements first
  initializeElements();
  
  // Check if electronAPI is available
  if (!electronAPI) {
    console.error('electronAPI not available - cannot initialize app');
    // Show error message to user
    if (elements.captionText) {
      elements.captionText.textContent = 'Error: electronAPI not available. Please restart the app.';
    }
    return;
  }
  
  
  try {
    // Load config
    config = await electronAPI.getConfig();
  } catch (error) {
    console.error('Error loading config:', error);
    return;
  }
  
  // Set initial state
  if (config) {
    isInterviewMode = config.isInterviewMode !== undefined ? config.isInterviewMode : true;
    isLoggingEnabled = config.isLoggingEnabled !== undefined ? config.isLoggingEnabled : false;
  }

  // Load prompts from server or use defaults
  await loadPrompts();

  // Setup event listeners
  setupEventListeners();

  // Check Web Speech API availability on init
  checkWebSpeechAPIAvailability();
  
  // Setup transcript listener
  if (electronAPI && electronAPI.onTranscriptUpdated) {
    electronAPI.onTranscriptUpdated((transcript) => {
      updateTranscript(transcript);
    });
  } else {
    console.error('electronAPI.onTranscriptUpdated not available');
  }
  
  // Setup hotkey listeners
  setupHotkeyHandlers();
  
  // Listen for hotkey updates
  if (electronAPI.onHotkeysUpdated) {
    electronAPI.onHotkeysUpdated((hotkeys) => {
      if (config) {
        config.hotkeys = hotkeys;
      }
      setHotkeyBadges();
    });
  }

  // Listen for prompts loaded from auto-login
  if (electronAPI.onPromptsLoaded) {
    electronAPI.onPromptsLoaded((data) => {
      console.log('Prompts loaded from auto-login:', data.count);
      if (data.prompts && data.prompts.length > 0) {
        prompts = data.prompts.map(p => ({
          id: p.id,
          name: p.name,
          prompt: p.prompt,
          description: p.description,
          value: p.name.toLowerCase().replace(/\s+/g, '')
        }));
        renderPromptButtons();
      }
    });
  }

  // Start startup animation
  startStartupAnimation();

  // Initialize UI state
  updateUI();
  
  // Set hotkey badges on buttons
  setHotkeyBadges();
}

// Load prompts from server or use defaults
async function loadPrompts() {
  try {
    if (config && config.serverUrl && config.authToken) {
      // Try to reload prompts from server
      if (electronAPI && electronAPI.reloadPrompts) {
        const result = await electronAPI.reloadPrompts();
        if (result.success && result.prompts && result.prompts.length > 0) {
          prompts = result.prompts.map(p => ({
            id: p.id,
            name: p.name,
            prompt: p.prompt,
            description: p.description,
            value: p.name.toLowerCase().replace(/\s+/g, '')
          }));
          renderPromptButtons();
          return;
        }
      }
    }

    // Use cached prompts if available
    if (config && config.prompts && config.prompts.length > 0) {
      prompts = config.prompts.map(p => ({
        id: p.id,
        name: p.name,
        prompt: p.prompt,
        description: p.description,
        value: p.name.toLowerCase().replace(/\s+/g, '')
      }));
      renderPromptButtons();
      return;
    }

    // Fallback to default prompts
    prompts = defaultPrompts;
    renderPromptButtons();
  } catch (error) {
    console.error('Error loading prompts:', error);
    // Use defaults on error
    prompts = defaultPrompts;
    renderPromptButtons();
  }
}

// Render prompt buttons dynamically
function renderPromptButtons() {
  const container = document.getElementById('toggleButtons');
  if (!container) return;

  container.innerHTML = '';

  prompts.forEach((prompt, index) => {
    const button = document.createElement('button');
    button.className = 'toggle-button';
    button.dataset.value = prompt.value;
    button.dataset.promptId = prompt.id || '';
    button.textContent = prompt.name;
    
    // Set some defaults as active (first and one in middle)
    if (index === 0 || (prompt.value === 'impactful')) {
      button.classList.add('active');
    }

    button.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      button.classList.toggle('active');
    });

    container.appendChild(button);
  });

  // Update elements reference
  elements.toggleButtons = document.querySelectorAll('.toggle-button');
}

// Set hotkey badges on buttons
async function setHotkeyBadges() {
  let hotkeys = {};
  try {
    if (electronAPI.getHotkeys) {
      hotkeys = await electronAPI.getHotkeys();
    } else if (config && config.hotkeys) {
      hotkeys = config.hotkeys;
    }
  } catch (error) {
    console.error('Error loading hotkeys for badges:', error);
  }
  
  // Helper to extract key from accelerator string
  function getKeyFromAccelerator(accelerator) {
    if (!accelerator) return '';
    // Extract the last key (after the last +)
    const parts = accelerator.split('+');
    const key = parts[parts.length - 1];
    // Map special keys to shorter display
    const keyMap = {
      '/': '/',
      '.': '.',
      'Up': '↑',
      'Down': '↓',
      'I': 'I',
      'P': 'P'
    };
    return keyMap[key] || key.charAt(0).toUpperCase();
  }
  
  // Answer button - sends to webview
  if (elements.answerButton && hotkeys.sendToWebView) {
    const badge = elements.answerButton.querySelector('.hotkey-badge');
    if (badge) badge.textContent = getKeyFromAccelerator(hotkeys.sendToWebView);
  }
  
  // Exit button
  if (elements.exitButton && hotkeys.exit) {
    const badge = elements.exitButton.querySelector('.hotkey-badge');
    if (badge) badge.textContent = getKeyFromAccelerator(hotkeys.exit);
  }
  
  // Help button - DevTools (Ctrl+Shift+I) - this is hardcoded in the app
  if (elements.helpButton) {
    const badge = elements.helpButton.querySelector('.hotkey-badge');
    if (badge) badge.textContent = 'I';
  }
  
  // Note: Other buttons don't have direct hotkeys, so badges remain empty (hidden)
  // The badges will automatically hide when empty due to CSS
}

// Setup event listeners
function setupEventListeners() {
  console.log('Setting up event listeners for buttons...');
  
  // Caption controls
  if (elements.startStopButton) {
    elements.startStopButton.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      toggleCaptions();
    });
  } else {
    console.error('✗ startStopButton not found');
  }
  
  if (elements.copyDeltaButton) {
    elements.copyDeltaButton.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      copyDelta();
    });
  } else {
    console.error('✗ copyDeltaButton not found');
  }
  
  if (elements.clearButton) {
    elements.clearButton.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      clearTranscript();
    });
  } else {
    console.error('✗ clearButton not found');
  }
  if (elements.fontSizeSlider) {
    elements.fontSizeSlider.addEventListener('input', (e) => {
      const size = parseInt(e.target.value);
      if (elements.fontSizeLabel) {
        elements.fontSizeLabel.textContent = size;
      }
      if (elements.captionText) {
        elements.captionText.style.fontSize = `${size}px`;
      }
    });
  }
  
  // Caption source toggle
  if (elements.captionSourceRadios && elements.captionSourceRadios.length > 0) {
    elements.captionSourceRadios.forEach(radio => {
      radio.addEventListener('change', (e) => {
        if (e.target.checked) {
          captionSource = e.target.value;
          
          // Update mode in main process
          if (electronAPI && electronAPI.setCaptionMode) {
            electronAPI.setCaptionMode(captionSource === 'window');
          }
          
          // If currently capturing, restart with new mode
          if (isCapturing) {
            toggleCaptions(); // Stop
            setTimeout(() => {
              toggleCaptions(); // Start with new mode
            }, 500);
          }
        }
      });
    });
  }

  // Calendar controls
  if (elements.connectCalendarButton) {
    elements.connectCalendarButton.addEventListener('click', (e) => {
      console.log('Connect Calendar button clicked');
      e.preventDefault();
      e.stopPropagation();
      connectCalendar();
    });
  }
  if (elements.refreshCalendarButton) {
    elements.refreshCalendarButton.addEventListener('click', (e) => {
      console.log('Refresh Calendar button clicked');
      e.preventDefault();
      e.stopPropagation();
      refreshCalendar();
    });
  }
  if (elements.toggleCalendarButton) {
    elements.toggleCalendarButton.addEventListener('click', (e) => {
      console.log('Toggle Calendar button clicked');
      e.preventDefault();
      e.stopPropagation();
      toggleCalendar();
    });
  }

  // Browser controls
  if (elements.chatGPTButton) {
    elements.chatGPTButton.addEventListener('click', (e) => {
      console.log('ChatGPT button clicked');
      e.preventDefault();
      e.stopPropagation();
      openBrowser('https://chatgpt.com');
    });
  }
  if (elements.deepSeekButton) {
    elements.deepSeekButton.addEventListener('click', (e) => {
      console.log('DeepSeek button clicked');
      e.preventDefault();
      e.stopPropagation();
      openBrowser('https://chat.deepseek.com');
    });
  }
  if (elements.perplexityButton) {
    elements.perplexityButton.addEventListener('click', (e) => {
      console.log('Perplexity button clicked');
      e.preventDefault();
      e.stopPropagation();
      openBrowser('https://www.perplexity.ai');
    });
  }
  if (elements.grokButton) {
    elements.grokButton.addEventListener('click', (e) => {
      console.log('Grok button clicked');
      e.preventDefault();
      e.stopPropagation();
      openBrowser('https://x.com/i/grok');
    });
  }

  // Toggle buttons - event listeners are set up in renderPromptButtons()
  // This is handled there since buttons are dynamically generated

  // Radio buttons
  if (elements.radioButtons && elements.radioButtons.length > 0) {
    elements.radioButtons.forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        elements.radioButtons.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        const radio = btn.querySelector('input[type="radio"]');
        if (radio) {
          radio.checked = true;
        }
      });
    });
  }

  // Action buttons
  if (elements.followUpButton) {
    elements.followUpButton.addEventListener('click', (e) => {
      console.log('FollowUp button clicked');
      e.preventDefault();
      e.stopPropagation();
      sendToBrowser('followup');
    });
  }
  if (elements.clarifyButton) {
    elements.clarifyButton.addEventListener('click', (e) => {
      console.log('Clarify button clicked');
      e.preventDefault();
      e.stopPropagation();
      sendToBrowser('clarify');
    });
  }
  if (elements.answerButton) {
    elements.answerButton.addEventListener('click', (e) => {
      console.log('Answer button clicked');
      e.preventDefault();
      e.stopPropagation();
      sendToBrowser('answer');
    });
  }

  // Control panel
  if (elements.arrowCursorButton) {
    elements.arrowCursorButton.addEventListener('click', (e) => {
      console.log('Arrow cursor button clicked');
      e.preventDefault();
      e.stopPropagation();
      setCursorSystem('arrow');
    });
  }
  if (elements.caretCursorButton) {
    elements.caretCursorButton.addEventListener('click', (e) => {
      console.log('Caret cursor button clicked');
      e.preventDefault();
      e.stopPropagation();
      setCursorSystem('caret');
    });
  }
  if (elements.normalCursorButton) {
    elements.normalCursorButton.addEventListener('click', (e) => {
      console.log('Normal cursor button clicked');
      e.preventDefault();
      e.stopPropagation();
      setCursorSystem('normal');
    });
  }
  if (elements.loggingButton) {
    elements.loggingButton.addEventListener('click', (e) => {
      console.log('Logging button clicked');
      e.preventDefault();
      e.stopPropagation();
      toggleLogging();
    });
  }
  if (elements.modeToggleButton) {
    elements.modeToggleButton.addEventListener('click', (e) => {
      console.log('Mode toggle button clicked');
      e.preventDefault();
      e.stopPropagation();
      toggleInterviewMode();
    });
  }
  if (elements.settingsButton) {
    elements.settingsButton.addEventListener('click', (e) => {
      console.log('Settings button clicked');
      e.preventDefault();
      e.stopPropagation();
      if (electronAPI && electronAPI.openSettings) {
        electronAPI.openSettings();
      }
    });
  }
  if (elements.helpButton) {
    elements.helpButton.addEventListener('click', (e) => {
      console.log('Help button clicked');
      e.preventDefault();
      e.stopPropagation();
      toggleHelp();
    });
  }
  if (elements.exitButton) {
    elements.exitButton.addEventListener('click', (e) => {
      console.log('Exit button clicked');
      e.preventDefault();
      e.stopPropagation();
      if (electronAPI && electronAPI.exitApp) {
        electronAPI.exitApp();
      }
    });
  }
}

// Check Web Speech API availability
function checkWebSpeechAPIAvailability() {
  // Function kept for potential future use, but no logging needed
}

// Initialize Web Speech API
function initializeSpeechRecognition() {
  // Check if Web Speech API is available
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  
  if (!SpeechRecognition) {
    console.error('Web Speech API not available');
    updateStatusLight('red', '✗ Web Speech API not available');
    return null;
  }
  
  const recognition = new SpeechRecognition();
  recognition.continuous = true;
  recognition.interimResults = true;
  recognition.lang = 'en-US';
  
  let accumulatedTranscript = '';
  
  recognition.onstart = () => {
    accumulatedTranscript = '';
    transcript = '';
    networkRetryCount = 0; // Reset retry count on successful start
    isRetrying = false; // Clear retry flag on successful start
    updateStatusLight('green', '✓ Listening...');
  };
  
  recognition.onresult = (event) => {
    let interimTranscript = '';
    let newFinalText = '';
    
    // Process all results since last event
    for (let i = event.resultIndex; i < event.results.length; i++) {
      const result = event.results[i][0].transcript;
      if (event.results[i].isFinal) {
        newFinalText += result + ' ';
      } else {
        interimTranscript += result;
      }
    }
    
    // Add new final text to accumulated transcript
    if (newFinalText) {
      accumulatedTranscript += newFinalText;
    }
    
    // Combine accumulated final text with interim results
    const fullTranscript = accumulatedTranscript + interimTranscript;
    
    if (fullTranscript.trim()) {
      // Update local transcript
      transcript = fullTranscript.trim();
      
      // Update UI immediately
      updateTranscript(transcript);
      
      // Send via IPC to main process
      if (electronAPI.sendTranscript) {
        electronAPI.sendTranscript(transcript);
      }
    }
  };
  
  recognition.onerror = (event) => {
    console.error('Speech recognition error:', event.error, event);
    
    let errorMessage = 'Speech recognition error';
    let shouldStop = false;
    let shouldRetry = false;
    
    if (event.error === 'no-speech') {
      // This is normal, just continue
      return;
    } else if (event.error === 'audio-capture') {
      errorMessage = 'No microphone found';
      shouldStop = true;
      networkRetryCount = 0; // Reset retry count for non-network errors
    } else if (event.error === 'not-allowed') {
      errorMessage = 'Microphone permission denied. Please grant permission in System Settings.';
      shouldStop = true;
      networkRetryCount = 0;
    } else if (event.error === 'network') {
      errorMessage = 'Network error - Speech recognition requires internet connection';
      // Only retry if we haven't exceeded max retries
      if (networkRetryCount < MAX_NETWORK_RETRIES && isCapturing) {
        shouldRetry = true;
        shouldStop = false;
      } else {
        shouldStop = true;
        errorMessage = `Network error - Failed after ${MAX_NETWORK_RETRIES} retries. Please check your internet connection.`;
      }
    } else if (event.error === 'aborted') {
      // User stopped, this is normal
      networkRetryCount = 0;
      return;
    } else if (event.error === 'service-not-allowed') {
      errorMessage = 'Speech recognition service not allowed';
      shouldStop = true;
      networkRetryCount = 0;
    } else {
      errorMessage = `Error: ${event.error}`;
      shouldStop = true;
      networkRetryCount = 0;
    }
    
    if (shouldRetry && isCapturing) {
      // Clear any existing retry timer
      if (networkRetryTimer) {
        clearTimeout(networkRetryTimer);
        networkRetryTimer = null;
      }
      
      networkRetryCount++;
      isRetrying = true; // Set flag to prevent onend from restarting
      updateStatusLight('orange', `⚠ Network error - Retrying (${networkRetryCount}/${MAX_NETWORK_RETRIES})...`);
      
      // Retry after a delay
      networkRetryTimer = setTimeout(() => {
        if (!isCapturing) {
          // User stopped, cancel retry
          networkRetryTimer = null;
          isRetrying = false;
          return;
        }
        
        networkRetryTimer = null;
        isRetrying = false; // Clear flag before retrying
        
        try {
          // Stop current recognition if it exists
          if (speechRecognition) {
            try {
              speechRecognition.stop();
            } catch (e) {
              // Ignore stop errors
            }
            // Don't set to null immediately - let onend handle cleanup
            setTimeout(() => {
              if (isCapturing && !isRetrying) {
                speechRecognition = null;
                startSpeechRecognition();
              }
            }, 1000); // Longer delay to ensure cleanup
          } else {
            // No existing recognition, start fresh
            if (isCapturing) {
              startSpeechRecognition();
            }
          }
        } catch (error) {
          console.error('Error retrying recognition:', error);
          isRetrying = false;
          if (networkRetryCount >= MAX_NETWORK_RETRIES) {
            shouldStop = true;
            networkRetryCount = MAX_NETWORK_RETRIES;
          }
        }
      }, 3000);
    } else if (shouldStop) {
      // Clear retry timer if stopping
      if (networkRetryTimer) {
        clearTimeout(networkRetryTimer);
        networkRetryTimer = null;
      }
      
      isRetrying = false; // Clear retry flag
      console.error('Stopping speech recognition due to error:', errorMessage);
      updateStatusLight('red', `✗ ${errorMessage}`);
      stopSpeechRecognition();
      isCapturing = false;
      networkRetryCount = 0; // Reset retry count
      if (elements.startStopButton) {
        elements.startStopButton.querySelector('.icon').textContent = '▶';
        elements.startStopButton.title = 'Start Live Captions';
      }
    }
    
    // Notify main process
    if (electronAPI && electronAPI.onSpeechError) {
      electronAPI.onSpeechError(event.error);
    }
  };
  
  recognition.onend = () => {
    
    // Don't auto-restart if we're in a retry state (retry logic will handle it)
    if (isRetrying) {
      return;
    }
    
    // Don't auto-restart if there's a pending retry timer
    if (networkRetryTimer) {
      return;
    }
    
    // Don't auto-restart if we've exceeded retry limit
    if (networkRetryCount >= MAX_NETWORK_RETRIES) {
      return;
    }
    
    // If we're still supposed to be capturing, restart
    if (isCapturing && !isRetrying && !networkRetryTimer) {
      try {
        // Small delay before restarting to avoid immediate re-trigger
        setTimeout(() => {
          // Double-check conditions before restarting
          if (isCapturing && speechRecognition && !isRetrying && !networkRetryTimer && networkRetryCount < MAX_NETWORK_RETRIES) {
            try {
              speechRecognition.start();
            } catch (error) {
              // Handle "already started" error
              if (error.message && error.message.includes('already started')) {
              } else {
                throw error;
              }
            }
          } else {
            console.log('Conditions not met for auto-restart:', {
              isCapturing,
              hasRecognition: !!speechRecognition,
              isRetrying,
              hasTimer: !!networkRetryTimer,
              retryCount: networkRetryCount
            });
          }
        }, 500); // Increased delay to give retry logic time
      } catch (error) {
        console.error('Error restarting recognition:', error);
        stopSpeechRecognition();
        isCapturing = false;
        updateStatusLight('red', '✗ Failed to restart recognition');
      }
    }
  };
  
  return recognition;
}

// Start speech recognition
function startSpeechRecognition() {
  
  // Check if already running
  if (speechRecognition) {
    // Check the actual state of the recognition
    try {
      // If recognition exists, it might already be running
      // We'll check by trying to access its state
      // Note: Web Speech API doesn't expose a direct "isRunning" property
      // So we'll rely on our isCapturing flag and handle errors gracefully
    } catch (error) {
      console.log('Error checking recognition state:', error);
    }
  }
  
  if (!speechRecognition) {
    speechRecognition = initializeSpeechRecognition();
  }
  
  if (!speechRecognition) {
    console.error('Failed to initialize speech recognition');
    updateStatusLight('red', '✗ Web Speech API not available');
    return false;
  }
  
  try {
    console.log('Calling speechRecognition.start()...');
    speechRecognition.start();
    console.log('speechRecognition.start() called successfully');
    return true;
  } catch (error) {
    // Handle "already started" error gracefully
    if (error.message && error.message.includes('already started')) {
      return true; // Return true since it's already running
    }
    console.error('Error starting speech recognition:', error);
    console.error('Error details:', error.message, error.stack);
    updateStatusLight('red', `✗ Failed to start: ${error.message || 'Unknown error'}`);
    return false;
  }
}

// Stop speech recognition
function stopSpeechRecognition() {
  // Clear any pending retry timers
  if (networkRetryTimer) {
    clearTimeout(networkRetryTimer);
    networkRetryTimer = null;
  }
  
  // Reset retry state
  networkRetryCount = 0;
  isRetrying = false;
  
  if (speechRecognition) {
    try {
      speechRecognition.stop();
    } catch (error) {
      console.error('Error stopping speech recognition:', error);
    }
    speechRecognition = null;
  }
}

// Caption functions
let isToggling = false; // Prevent double-clicks

async function toggleCaptions() {
  if (!electronAPI) {
    console.error('electronAPI not available');
    return;
  }
  
  // Prevent double-clicks
  if (isToggling) {
    console.log('Toggle already in progress, ignoring click...');
    return;
  }
  
  isToggling = true;
  
  try {
    if (isCapturing) {
      // Stop
      console.log('Stopping captions...');
      stopSpeechRecognition();
      if (electronAPI.stopCaptions) {
        await electronAPI.stopCaptions();
      }
      isCapturing = false;
      if (elements.startStopButton) {
        elements.startStopButton.querySelector('.icon').textContent = '▶';
        elements.startStopButton.title = 'Start Live Captions';
      }
      updateStatusLight('gray', 'Stopped - Click Start to begin capturing');
    } else {
      // Start
      if (elements.startStopButton) {
        elements.startStopButton.querySelector('.icon').textContent = '⏸';
        elements.startStopButton.title = 'Connecting...';
      }
      updateStatusLight('orange', 'Requesting microphone access...');
      
      // Check internet connectivity first
      if (!navigator.onLine) {
        throw new Error('No internet connection. Web Speech API requires internet.');
      }
      
      // Request permission and start
      
      // Check if getUserMedia is available
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        throw new Error('getUserMedia not available. Check microphone permissions in System Settings.');
      }
      
      // Request microphone permission
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      
      // Stop the stream immediately - we just needed permission
      stream.getTracks().forEach(track => {
        track.stop();
      });
      
      // Determine which mode to use
      const useWindowMode = captionSource === 'window';
      
      // Set mode in main process
      if (electronAPI.setCaptionMode) {
        const modeResult = await electronAPI.setCaptionMode(useWindowMode);
        if (!modeResult.success) {
          if (modeResult.needsPermission) {
            // Show permission request
            const grantPermission = confirm(
              'Accessibility permissions are required to read captions from other windows.\n\n' +
              'Click OK to open System Settings, then:\n' +
              '1. Enable Accessibility for this app\n' +
              '2. Return and try again'
            );
            
            if (grantPermission && electronAPI.requestAccessibilityPermissions) {
              await electronAPI.requestAccessibilityPermissions();
            }
            
            isCapturing = false;
            if (elements.startStopButton) {
              elements.startStopButton.querySelector('.icon').textContent = '▶';
              elements.startStopButton.title = 'Start Live Captions';
            }
            updateStatusLight('orange', '⚠ Accessibility permission required');
            return;
          }
          throw new Error(modeResult.error || 'Failed to set caption mode');
        }
      }
      
      // Start captions in main process
      let startConfig = {};
      if (useWindowMode) {
        // Configuration for window text reader
        startConfig = {
          windowTitle: 'Live captions', // Default - can be configured
          processName: '' // Can specify browser process name if needed
        };
      }
      
      if (electronAPI.startCaptions) {
        const result = await electronAPI.startCaptions(startConfig);
        if (!result.success) {
          throw new Error(result.error || 'Failed to start captions');
        }
      }
      
      if (useWindowMode) {
        // Window text reader mode - no need for Web Speech API
        isCapturing = true;
        if (elements.startStopButton) {
          elements.startStopButton.querySelector('.icon').textContent = '⏹';
          elements.startStopButton.title = 'Stop Live Captions';
        }
        updateStatusLight('green', '✓ Reading window text...');
      } else {
        // Web Speech API mode
        // Small delay to ensure permission is fully processed
        await new Promise(resolve => setTimeout(resolve, 200));
        
        // Start Web Speech API
        if (startSpeechRecognition()) {
          isCapturing = true;
          if (elements.startStopButton) {
            elements.startStopButton.querySelector('.icon').textContent = '⏹';
            elements.startStopButton.title = 'Stop Live Captions';
          }
          updateStatusLight('green', '✓ Connected - Capturing...');
        } else {
          isCapturing = false;
          if (elements.startStopButton) {
            elements.startStopButton.querySelector('.icon').textContent = '▶';
            elements.startStopButton.title = 'Start Live Captions';
          }
          updateStatusLight('red', '✗ Failed to start recognition');
        }
      }
    }
  } catch (error) {
    console.error('Error in toggleCaptions:', error);
    console.error('Error name:', error.name);
    console.error('Error message:', error.message);
    isCapturing = false;
    if (elements.startStopButton) {
      elements.startStopButton.querySelector('.icon').textContent = '▶';
      elements.startStopButton.title = 'Start Live Captions';
    }
    
    let errorMsg = '✗ Microphone permission denied';
    if (error.name === 'NotAllowedError') {
      errorMsg = '✗ Permission denied. Grant microphone access in System Settings > Privacy & Security > Microphone';
    } else if (error.name === 'NotFoundError') {
      errorMsg = '✗ No microphone found';
    } else if (error.message && error.message.includes('internet')) {
      errorMsg = '✗ No internet connection. Web Speech API requires internet.';
    } else {
      errorMsg = `✗ Error: ${error.message || error.name}`;
    }
    updateStatusLight('red', errorMsg);
  } finally {
    // Reset toggle lock after a short delay
    setTimeout(() => {
      isToggling = false;
    }, 500);
  }
}

async function copyDelta() {
  if (!electronAPI || !electronAPI.getDelta) {
    console.error('electronAPI not available');
    return;
  }
  const delta = await electronAPI.getDelta();
  if (delta) {
    try {
      await navigator.clipboard.writeText(delta);
    } catch (clipboardError) {
      console.error('Clipboard write failed:', clipboardError);
      // Try using Electron's clipboard API as fallback
      if (electronAPI && electronAPI.writeClipboard) {
        try {
          await electronAPI.writeClipboard(delta);
          console.log('Text copied to clipboard via Electron API');
        } catch (e) {
          console.error('Electron clipboard also failed:', e);
        }
      }
    }
    if (electronAPI.markDeltaSent) {
      await electronAPI.markDeltaSent();
    }
  }
}

async function clearTranscript() {
  if (!electronAPI || !electronAPI.clearTranscript) {
    console.error('electronAPI not available');
    return;
  }
  await electronAPI.clearTranscript();
  transcript = '';
  lastSentPos = 0;
  if (elements.captionText) {
    elements.captionText.textContent = '';
  }
}

function updateTranscript(newTranscript) {
  transcript = newTranscript || '';
  
  if (elements.captionText && elements.captionScroll) {
    // Check if user is near the bottom before updating (within 50px)
    const isNearBottom = elements.captionScroll.scrollHeight - elements.captionScroll.scrollTop <= elements.captionScroll.clientHeight + 50;
    
    elements.captionText.textContent = transcript;
    
    // Auto-scroll to bottom if user was already at/near bottom (or always scroll for real-time updates)
    // Always scroll to show latest captions in real-time
    scrollToBottom();
  } else {
    console.error('✗ elements.captionText or captionScroll not found, cannot update UI');
  }
}

function scrollToBottom() {
  if (elements.captionScroll) {
    // Use requestAnimationFrame to ensure DOM is updated, then scroll
    requestAnimationFrame(() => {
      // Scroll to bottom with a small delay to ensure content is rendered
      setTimeout(() => {
        elements.captionScroll.scrollTop = elements.captionScroll.scrollHeight;
      }, 0);
    });
  }
}

function updateStatusLight(color, message) {
  elements.statusLight.className = 'status-light';
  if (color !== 'gray') {
    elements.statusLight.classList.add(color);
  }
  elements.statusLight.title = message;
}

// Calendar functions
async function connectCalendar() {
  if (!electronAPI || !electronAPI.connectCalendar) {
    console.error('electronAPI not available');
    return;
  }
  elements.calendarStatus.textContent = 'Connecting to Google Calendar...';
  const result = await electronAPI.connectCalendar();
  if (result.success) {
    elements.calendarStatus.textContent = '✓ Connected to Google Calendar';
    elements.refreshCalendarButton.disabled = false;
    await refreshCalendar();
  } else {
    elements.calendarStatus.textContent = `✗ ${result.error || 'Failed to connect'}`;
  }
}

async function refreshCalendar() {
  if (!electronAPI || !electronAPI.getUpcomingEvents) {
    console.error('electronAPI not available');
    return;
  }
  if (elements.calendarStatus) {
    elements.calendarStatus.textContent = 'Refreshing calendar...';
  }
  const events = await electronAPI.getUpcomingEvents();
  
  elements.calendarEvents.innerHTML = '';
  if (events.length > 0) {
    events.forEach(event => {
      const eventDiv = document.createElement('div');
      eventDiv.className = 'calendar-event';
      eventDiv.innerHTML = `
        <div class="calendar-event-title">${event.summary}</div>
        <div class="calendar-event-time">${event.timeRange}</div>
        ${event.calendarName ? `<div class="calendar-event-calendar">📅 ${event.calendarName}</div>` : ''}
      `;
      elements.calendarEvents.appendChild(eventDiv);
    });
    elements.calendarStatus.textContent = `✓ Found ${events.length} upcoming interview(s)`;
  } else {
    elements.calendarStatus.textContent = '✓ No upcoming interviews found';
  }
}

function toggleCalendar() {
  const isVisible = elements.calendarSection.style.display !== 'none';
  elements.calendarSection.style.display = isVisible ? 'none' : 'block';
}

// Browser functions
function openBrowser(url) {
  if (elements.stealthBrowser) {
    elements.stealthBrowser.src = url;
  }
}

// Handle hotkey events from main process
if (electronAPI) {
  // Listen for hotkey events
  window.addEventListener('message', (event) => {
    // Handle messages from main process if needed
  });
}

async function sendToBrowser(action) {
  if (!electronAPI || !electronAPI.getDelta) {
    console.error('electronAPI not available');
    return;
  }
  const delta = await electronAPI.getDelta();
  if (!delta || delta.trim() === '') {
    console.log('No new text to send');
    return;
  }

  const activeToggles = Array.from(elements.toggleButtons)
    .filter(btn => btn.classList.contains('active'))
    .map(btn => {
      const value = btn.dataset.value;
      // Find prompt by value
      const prompt = prompts.find(p => p.value === value);
      if (prompt) {
        return prompt.prompt; // Use the actual prompt text
      }
      // Fallback to old mapping for backwards compatibility
      const mapping = {
        'professional': 'Professional',
        'funny': 'Interesting - light Funny',
        'star': 'STAR',
        'sharp': 'Sharp',
        'creative': 'Creative',
        'specific': 'Mentioning specific thing(s)',
        'story': 'Story mode',
        'impactful': 'Impactful',
        'techstack': 'mentioning Tech stack versions at the next of each tech stacks inside the answer if the answer explains technical problem',
        'detailed': 'Detailed',
        'stepbystep': 'Step-by-Step'
      };
      return mapping[value] || value;
    });

  const duration = document.querySelector('input[name="duration"]:checked')?.value || '1min';

  let prompt = '';
  if (action === 'followup') {
    prompt = `Based on the following conversation, ask a thoughtful follow-up question:\n\n${delta}`;
  } else if (action === 'clarify') {
    prompt = `Clarify the following point:\n\n${delta}`;
  } else {
    // Answer
    const modifiers = activeToggles.length > 0 ? `\n\nPlease make the answer: ${activeToggles.join(', ')}` : '';
    const durationText = duration !== '1min' ? ` ${duration}` : '';
    prompt = `Please provide a${durationText} answer to the following question${modifiers}:\n\n${delta}`;
  }

  // Inject into webview
  const webview = elements.stealthBrowser;
  if (webview && webview.getWebContents) {
    try {
      // Try multiple strategies to inject text
      await webview.executeJavaScript(`
        (function() {
          // Strategy 1: Find textarea or contenteditable
          let textarea = document.querySelector('textarea');
          if (!textarea) {
            textarea = document.querySelector('input[type="text"]');
          }
          if (!textarea) {
            // Try contenteditable div
            const editable = document.querySelector('[contenteditable="true"]');
            if (editable) {
              editable.textContent = ${JSON.stringify(prompt)};
              editable.dispatchEvent(new Event('input', { bubbles: true }));
              return true;
            }
          }
          
          if (textarea) {
            textarea.value = ${JSON.stringify(prompt)};
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
            textarea.dispatchEvent(new Event('change', { bubbles: true }));
            
            // Try to find and click submit button
            setTimeout(() => {
              const submitButton = document.querySelector('button[type="submit"]') ||
                                   document.querySelector('button:has-text("Send")') ||
                                   document.querySelector('button:has-text("Submit")') ||
                                   document.querySelector('[aria-label*="Send"]') ||
                                   document.querySelector('[data-testid*="send"]');
              if (submitButton) {
                submitButton.click();
              }
            }, 100);
            return true;
          }
          return false;
        })();
      `);
      
      await electronAPI.markDeltaSent();
    } catch (err) {
      console.error('Error injecting text into webview:', err);
      // Fallback: use Electron's clipboard API (doesn't require focus)
      if (electronAPI && electronAPI.writeClipboard) {
        try {
          await electronAPI.writeClipboard(prompt);
        } catch (e) {
          console.error('Electron clipboard failed:', e);
        }
      }
    }
  } else {
    // Webview not available: use Electron's clipboard API
    if (electronAPI && electronAPI.writeClipboard) {
      try {
        await electronAPI.writeClipboard(prompt);
      } catch (e) {
        console.error('Electron clipboard failed:', e);
      }
    }
  }
}

// Handle hotkey events from main process
function setupHotkeyHandlers() {
  
  if (!electronAPI || !electronAPI.onHotkey) {
    console.error('✗ Cannot setup hotkey handlers - electronAPI.onHotkey not available');
    if (electronAPI) {
      console.error('Available electronAPI methods:', Object.keys(electronAPI));
    }
    return;
  }
  
  
  // Send to webview
  electronAPI.onHotkey('hotkey-send-to-webview', () => {
    sendToBrowser('answer');
  });
  
  // Window movement
  electronAPI.onHotkey('hotkey-move', (data) => {
    if (data && typeof data === 'object') {
      if (electronAPI && electronAPI.moveWindow) {
        electronAPI.moveWindow(data.dx || 0, data.dy || 0);
      }
    }
  });
  
  // Window resizing
  electronAPI.onHotkey('hotkey-resize', (data) => {
    if (data && typeof data === 'object') {
      if (electronAPI && electronAPI.resizeWindow) {
        electronAPI.resizeWindow(data.dw || 0, data.dh || 0);
      }
    }
  });
  
  // Opacity adjustment
  electronAPI.onHotkey('hotkey-opacity', (delta) => {
    if (typeof delta === 'number') {
      if (electronAPI && electronAPI.adjustOpacity) {
        electronAPI.adjustOpacity(delta);
      }
    }
  });
  
  // Toggle visibility
  electronAPI.onHotkey('hotkey-toggle-visibility', () => {
    if (electronAPI && electronAPI.toggleVisibility) {
      electronAPI.toggleVisibility();
    }
  });
  
  // Screenshot
  electronAPI.onHotkey('hotkey-screenshot', () => {
    console.log('🔥🔥🔥 HOTKEY TRIGGERED: hotkey-screenshot');
    if (electronAPI && electronAPI.takeScreenshot) {
      electronAPI.takeScreenshot();
    }
  });
  
  // Exit
  electronAPI.onHotkey('hotkey-exit', () => {
    if (electronAPI && electronAPI.exitApp) {
      electronAPI.exitApp();
    }
  });
  
}

// Control panel functions
function setCursorSystem(system) {
  currentCursorSystem = system;
  document.body.style.cursor = system === 'arrow' ? 'default' : system === 'caret' ? 'text' : 'default';
  
  // Update button states
  elements.arrowCursorButton.classList.toggle('active', system === 'arrow');
  elements.caretCursorButton.classList.toggle('active', system === 'caret');
  elements.normalCursorButton.classList.toggle('active', system === 'normal');
}

async function toggleLogging() {
  isLoggingEnabled = !isLoggingEnabled;
  elements.loggingButton.textContent = isLoggingEnabled ? '✓' : '📝';
  elements.loggingButton.title = `Debug Logging: ${isLoggingEnabled ? 'ON' : 'OFF'}`;
  elements.loggingButton.classList.toggle('active', isLoggingEnabled);
  
  if (config) {
    config.isLoggingEnabled = isLoggingEnabled;
    await electronAPI.saveConfig(config);
  }
}

async function toggleInterviewMode() {
  isInterviewMode = !isInterviewMode;
  elements.modeToggleButton.textContent = isInterviewMode ? 'I' : 'N';
  elements.modeToggleButton.title = isInterviewMode ? 'Interview Mode' : 'Normal Mode';
  elements.modeToggleButton.classList.toggle('active', isInterviewMode);
  
  if (config) {
    config.isInterviewMode = isInterviewMode;
    await electronAPI.saveConfig(config);
  }
}

function toggleHelp() {
  const isVisible = elements.helpWindow.style.display !== 'none';
  elements.helpWindow.style.display = isVisible ? 'none' : 'block';
  elements.helpButton.classList.toggle('active', !isVisible);
}

// Startup animation
function startStartupAnimation() {
  // Show startup label
  elements.startupLabel.classList.add('visible');
  
  setTimeout(() => {
    elements.startupLabel.classList.remove('visible');
    
    // Show content with animation
    setTimeout(() => {
      elements.contentGrid.style.opacity = '1';
      elements.buttonGroupsRow.style.opacity = '1';
      elements.controlPanel.style.opacity = '1';
    }, 300);
  }, 2000);
}

// Update UI
function updateUI() {
  // Set initial opacity
  elements.contentGrid.style.opacity = '0';
  elements.buttonGroupsRow.style.opacity = '0';
  elements.controlPanel.style.opacity = '0';
  elements.contentGrid.style.transition = 'opacity 0.6s ease-out';
  elements.buttonGroupsRow.style.transition = 'opacity 0.6s ease-out';
  elements.controlPanel.style.transition = 'opacity 0.6s ease-out';

  // Set cursor system
  setCursorSystem(currentCursorSystem);

  // Set logging state
  elements.loggingButton.textContent = isLoggingEnabled ? '✓' : '📝';
  elements.loggingButton.classList.toggle('active', isLoggingEnabled);

  // Set interview mode
  elements.modeToggleButton.textContent = isInterviewMode ? 'I' : 'N';
  elements.modeToggleButton.classList.toggle('active', isInterviewMode);
}


// Debug helper - expose to window for console access
window.debugApp = {
  toggleDevTools: () => {
    if (electronAPI && electronAPI.toggleDevTools) {
      electronAPI.toggleDevTools();
    }
  },
  log: (...args) => {
    if (electronAPI && electronAPI.log) {
      electronAPI.log(...args);
    } else {
      console.log(...args);
    }
  },
  getState: () => {
    return {
      isCapturing,
      transcript: transcript.substring(0, 100) + '...',
      config,
      isInterviewMode,
      isLoggingEnabled
    };
  }
};

  // Keyboard shortcut for DevTools (Ctrl+Shift+I or Cmd+Option+I)
  document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key.toLowerCase() === 'i') {
      e.preventDefault();
      if (electronAPI && electronAPI.toggleDevTools) {
        electronAPI.toggleDevTools();
      }
    }
    
    // Reload on Ctrl+R or Cmd+R (useful for debugging)
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'r' && e.shiftKey) {
      e.preventDefault();
      location.reload();
    }
  });

  // Initialize on load - ensure it only runs once
  let isInitialized = false;

  console.log('Setting up initialization handlers...');
  console.log('Document readyState:', document.readyState);

  document.addEventListener('DOMContentLoaded', () => {
    console.log('DOMContentLoaded event fired');
    if (!isInitialized) {
      isInitialized = true;
      console.log('Calling init() from DOMContentLoaded...');
      init().catch(error => {
        console.error('Error during initialization:', error);
        console.error('Error stack:', error.stack);
      });
    } else {
      console.log('Already initialized, skipping');
    }
  });

  // Also try to initialize if DOM is already loaded
  if (document.readyState === 'loading') {
    console.log('DOM is still loading, waiting for DOMContentLoaded...');
  } else {
    console.log('DOM is already loaded, initializing immediately...');
    if (!isInitialized) {
      isInitialized = true;
      console.log('Calling init() immediately...');
      init().catch(error => {
        console.error('Error during initialization:', error);
        console.error('Error stack:', error.stack);
      });
    }
  }

  // Add a fallback timeout
  setTimeout(() => {
    if (!isInitialized) {
      console.warn('Init not called after 2 seconds, forcing initialization...');
      isInitialized = true;
      init().catch(error => {
        console.error('Error during forced initialization:', error);
      });
    }
  }, 2000);
  
})(); // End IIFE - prevents duplicate declarations on hot reload

