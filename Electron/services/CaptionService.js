const EventEmitter = require('events');
const path = require('path');

class CaptionService extends EventEmitter {
  constructor() {
    super();
    this.windowTextReader = null;
    this.isRunning = false;
    this.transcript = '';
    this.lastSentPos = 0;
    this.lastSnapshot = '';
    this.history = [];
    this.maxHistorySize = 51200; // ~100KB
    this.pollInterval = null;
    this.useWindowTextReader = false; // Toggle between Web Speech API and Window Text Reader
  }

  initialize() {
    // Try to load WindowTextReader native module
    try {
      const windowTextReaderPath = path.join(__dirname, '../main/native/window-text-reader');
      const WindowTextReaderModule = require(windowTextReaderPath);
      
      if (WindowTextReaderModule && WindowTextReaderModule.WindowTextReader) {
        this.windowTextReaderAvailable = true;
      } else {
        this.windowTextReaderAvailable = false;
      }
    } catch (error) {
      console.error('CaptionService: Failed to load WindowTextReader:', error);
      console.error('CaptionService: Will use Web Speech API from renderer');
      this.windowTextReaderAvailable = false;
    }
  }
  
  setMode(useWindowTextReader) {
    this.useWindowTextReader = useWindowTextReader && this.windowTextReaderAvailable;
  }

  createMockRecognizer() {
    // Mock implementation for development/testing
    return {
      SpeechRecognition: class MockSpeechRecognition {
        constructor() {
          this.isRunning = false;
        }
        
        start(callback) {
          this.isRunning = true;
          console.log('Mock: Speech recognition started');
          // Simulate transcript updates
          if (callback) {
            setTimeout(() => {
              callback('Mock transcript: This is a test.');
            }, 1000);
          }
          return true;
        }
        
        stop() {
          this.isRunning = false;
          console.log('Mock: Speech recognition stopped');
        }
        
        isRunning() {
          return this.isRunning;
        }
      }
    };
  }

  async start(config = {}) {
    if (this.isRunning) {
      return { success: true };
    }

    // Check which mode to use
    if (this.useWindowTextReader && this.windowTextReaderAvailable) {
      return await this.startWindowTextReader(config);
    } else {
      // Web Speech API is started in renderer process
      // This service just tracks state
      this.isRunning = true;
      return { success: true };
    }
  }
  
  async startWindowTextReader(config) {
    try {
      const WindowTextReaderModule = require(path.join(__dirname, '../main/native/window-text-reader'));
      const WindowTextReader = WindowTextReaderModule.WindowTextReader;
      
      // Configuration for finding the window
      const readerConfig = {
        windowTitle: config.windowTitle || 'Live captions', // Default to "Live captions" like Windows
        processName: config.processName || '',
        callback: (text) => {
          if (text) {
            this.onWindowTextReceived(text);
          }
        }
      };
      
      this.windowTextReader = new WindowTextReader(readerConfig);
      
      // Find the window - try multiple times with delays
      let found = this.windowTextReader.findWindow();
      
      if (!found) {
        // Wait a bit and try again (window might be loading)
        await new Promise(resolve => setTimeout(resolve, 1000));
        found = this.windowTextReader.findWindow();
      }
      
      if (!found) {
        // Try with just "caption" as search term
        readerConfig.windowTitle = 'caption';
        this.windowTextReader = new WindowTextReader(readerConfig);
        found = this.windowTextReader.findWindow();
      }
      
      if (!found) {
        return { 
          success: false, 
          error: `Window not found. Make sure:\n1. Chrome Live Captions is enabled and visible\n2. Accessibility permissions are granted\n3. The caption window is open\n\nLooking for: "${readerConfig.windowTitle || readerConfig.processName || 'Live captions'}"` 
        };
      }
      
      // Start reading
      const started = this.windowTextReader.start();
      if (started) {
        this.isRunning = true;
        return { success: true };
      } else {
        return { success: false, error: 'Failed to start WindowTextReader - window found but could not start reading' };
      }
    } catch (error) {
      console.error('Error starting WindowTextReader:', error);
      console.error('Error stack:', error.stack);
      return { success: false, error: error.message || 'Unknown error starting WindowTextReader' };
    }
  }

  stop() {
    if (!this.isRunning) {
      return;
    }

    if (this.useWindowTextReader && this.windowTextReader) {
      this.windowTextReader.stop();
      this.windowTextReader = null;
    }
    
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
    
    this.isRunning = false;
  }
  
  onWindowTextReceived(newText) {
    if (!newText || newText.trim() === '') {
      return;
    }
    
    // Normalize and process text (similar to Windows version)
    const normalized = this.normalize(newText);
    
    // Use similar logic to Windows version for text alignment
    this.processTextUpdate(normalized);
  }
  
  processTextUpdate(snapshot) {
    // Similar to Windows version's text alignment logic
    if (!snapshot || snapshot.trim() === '') {
      return;
    }
    
    // More sophisticated text alignment (like Windows version)
    const normalizedSnapshot = snapshot.trim();
    
    // Check if transcript ends with the snapshot (already aligned)
    if (this.transcript.endsWith(normalizedSnapshot)) {
      // Already aligned, no change
      this.lastSnapshot = normalizedSnapshot;
      return;
    }
    
    // Find the longest prefix match in the tail of transcript
    const searchWindow = Math.max(normalizedSnapshot.length * 2, 4096);
    const transcriptTail = this.transcript.slice(-searchWindow);
    const matchIndex = transcriptTail.lastIndexOf(normalizedSnapshot.substring(0, Math.min(50, normalizedSnapshot.length)));
    
    if (matchIndex >= 0) {
      // Found overlap - splice from match point
      const spliceStart = this.transcript.length - searchWindow + matchIndex;
      this.transcript = this.transcript.substring(0, spliceStart) + normalizedSnapshot;
    } else {
      // No overlap - append new text
      if (this.transcript.length > 0 && !this.transcript.endsWith('\n')) {
        this.transcript += '\n';
      }
      this.transcript += normalizedSnapshot;
    }
    
    // Trim if too long
    if (this.transcript.length > this.maxHistorySize) {
      this.transcript = this.transcript.slice(-this.maxHistorySize);
    }
    
    this.lastSnapshot = normalizedSnapshot;
    
    // Emit event for UI update
    this.emit('transcript-updated', this.transcript);
  }

  onTranscriptReceived(newTranscript) {
    if (!newTranscript || newTranscript.trim() === '') {
      return;
    }

    // Normalize transcript (similar to Windows version)
    const normalized = this.normalize(newTranscript);
    
    // Update transcript
    this.transcript = normalized;
    
    // Emit event for UI update
    this.emit('transcript-updated', this.transcript);
  }

  normalize(text) {
    if (!text) return '';
    
    // Normalize line endings and spaces
    let normalized = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
    normalized = normalized.replace(/[ ]+\n/g, '\n');
    
    return normalized;
  }

  getTranscript() {
    return this.transcript;
  }

  getDelta() {
    // Return new text since last send
    if (this.lastSentPos >= this.transcript.length) {
      return '';
    }
    return this.transcript.substring(this.lastSentPos);
  }

  markDeltaSent() {
    this.lastSentPos = this.transcript.length;
  }

  clear() {
    this.transcript = '';
    this.lastSentPos = 0;
    this.history = [];
    this.emit('transcript-updated', '');
  }

  cleanup() {
    this.stop();
    this.removeAllListeners();
  }
}

// Export singleton instance
const captionService = new CaptionService();
module.exports = captionService;

