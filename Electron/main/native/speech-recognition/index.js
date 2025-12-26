// Native module loader
let nativeModule = null;

try {
  // Try to load the native module
  const path = require('path');
  const addonPath = path.join(__dirname, 'build/Release/speech-recognition.node');
  nativeModule = require(addonPath);
} catch (error) {
  console.error('Failed to load native speech recognition module:', error);
  console.error('Make sure to run: npm run rebuild');
  // Export a fallback
  nativeModule = {
    SpeechRecognition: class FallbackSpeechRecognition {
      constructor() {
        console.warn('Using fallback speech recognition (not functional)');
      }
      start() { return false; }
      stop() { }
      isRunning() { return false; }
    }
  };
}

module.exports = nativeModule;

