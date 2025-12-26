const path = require('path');

let nativeModule = null;

try {
  const modulePath = path.join(__dirname, 'build/Release/window-text-reader.node');
  nativeModule = require(modulePath);
} catch (error) {
  console.error('Failed to load WindowTextReader native module:', error.message);
  throw error;
}

module.exports = nativeModule;


