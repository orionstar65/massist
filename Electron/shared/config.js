// Shared configuration
module.exports = {
  defaultConfig: {
    windowBounds: {
      width: 680,
      height: 650,
      x: undefined,
      y: undefined
    },
    opacity: 1.0,
    excludedFromCapture: true,
    isInterviewMode: true,
    isLoggingEnabled: false,
    fontSize: 15,
    serverUrl: '',
    username: '',
    authToken: '',
    prompts: []
  },

  browserUrls: {
    chatgpt: 'https://chatgpt.com',
    deepseek: 'https://chat.deepseek.com',
    perplexity: 'https://www.perplexity.ai',
    grok: 'https://x.com/i/grok'
  },

  hotkeys: {
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
  }
};

