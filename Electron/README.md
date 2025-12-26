# Stealth Interview Assistant - macOS

Electron-based macOS version of the Stealth Interview Assistant - a powerful tool for interview preparation and assistance with real-time speech transcription and AI-powered answer generation.

## 🚀 Quick Start

```bash
# Install dependencies
npm install

# Build native module
cd main/native/speech-recognition && npm install && node-gyp rebuild && cd ../../..

# Run the app
npm start

# Or run in development mode (auto-opens DevTools)
npm run dev
```

## 📖 Documentation

- **[USER_GUIDE.md](USER_GUIDE.md)** - Complete user guide with detailed usage instructions
- **[DEBUG.md](DEBUG.md)** - Debugging guide and troubleshooting
- **[BUILD_AND_TEST.md](BUILD_AND_TEST.md)** - Build and testing procedures

## ✨ Features

### Core Features
- 🎤 **Live Speech Recognition** - Real-time transcription using macOS Speech Recognition
- 🤖 **AI Assistant Integration** - Built-in browser for ChatGPT, DeepSeek, Perplexity, Grok
- 📅 **Google Calendar Integration** - View and manage upcoming interviews
- ⌨️ **Global Hotkeys** - Quick actions without switching windows
- 🎯 **Stealth Mode** - Exclude window from screen recordings
- 🎨 **Customizable UI** - Adjustable opacity, positioning, and sizing

### Advanced Features
- 📝 **Smart Answer Generation** - Configure answer style, duration, and format
- 🔄 **Delta Tracking** - Only send new text since last send
- 🎛️ **Multiple Answer Styles** - Professional, Funny, STAR, Detailed, etc.
- 📊 **Interview Mode** - Optimized settings for interviews
- 🖱️ **Cursor Modes** - Arrow, Caret, or Normal cursor systems

## 🎯 Use Cases

### Primary Use Case: Interview Assistance
1. Start speech recognition during an interview
2. Questions are transcribed in real-time
3. Configure answer style and duration
4. Send to AI assistant for answer suggestions
5. Use generated answers as talking points

### Secondary Use Cases
- Meeting transcription
- Note-taking during calls
- Quick AI assistance
- Calendar event management

## ⌨️ Essential Hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Shift+/` | Send transcript to browser |
| `Ctrl+Shift+H` | Toggle visibility |
| `Ctrl+Shift+I` | Open DevTools (debugging) |
| `Ctrl+Shift+J/L/I/K` | Move window |
| `Ctrl+Shift+R/T/Q/A` | Resize window |
| `Ctrl+Shift+↑/↓` | Adjust opacity |
| `Ctrl+Shift+P` | Exit app |

## 📋 Requirements

- **macOS:** 10.15 (Catalina) or later
- **Node.js:** 18+ (for development)
- **Xcode Command Line Tools** (for building native module)
- **Microphone access** (required)
- **Internet connection** (for AI assistants)

## 🔧 Configuration

### Google Calendar Setup

1. Create OAuth credentials in [Google Cloud Console](https://console.cloud.google.com/)
2. Enable Google Calendar API
3. Add credentials to config file:
   ```json
   {
     "googleCalendar": {
       "clientId": "YOUR_CLIENT_ID.apps.googleusercontent.com",
       "clientSecret": "YOUR_CLIENT_SECRET"
     }
   }
   ```

Config file location: `~/Library/Application Support/stealth-interview-assistant-mac/config.json`

## 🏗️ Project Structure

```
Mac/
├── main/                 # Main process (Node.js)
│   ├── main.js          # Window management, IPC handlers
│   ├── preload.js       # Preload script (context bridge)
│   └── native/          # Native modules
│       └── speech-recognition/  # macOS Speech Recognition
├── renderer/            # Renderer process (HTML/CSS/JS)
│   ├── index.html      # Main UI
│   ├── styles.css      # Styling
│   └── main.js         # UI logic
├── services/            # Business logic
│   ├── CaptionService.js      # Speech recognition wrapper
│   ├── CalendarService.js     # Google Calendar integration
│   └── HotkeyService.js       # Global shortcuts
└── shared/             # Shared configuration
    └── config.js       # App configuration
```

## 🐛 Debugging

### Quick Debug Methods

1. **Open DevTools:** Press `Ctrl+Shift+I` (or `Cmd+Option+I`)
2. **Run in Dev Mode:** `npm run dev` (auto-opens DevTools)
3. **Debug Main Process:** `npm run debug`
4. **Check Console:** Use DevTools console or terminal

See [DEBUG.md](DEBUG.md) for detailed debugging guide.

## 📦 Building for Distribution

```bash
npm run build:mac
```

Creates a DMG file in the `dist/` directory.

## 🔐 Permissions

The app requires:
- **Microphone** - For speech recognition (required)
- **Screen Recording** - For screenshot feature (optional)

Permissions are requested automatically on first use.

## 🎓 How to Use

### Basic Workflow

1. **Start Captions** - Click ▶ button, grant microphone access
2. **Speak** - Your words appear in real-time
3. **Configure** - Select answer style and duration
4. **Send** - Click "Answer" button or press `Ctrl+Shift+/`
5. **Review** - AI generates answer in browser panel

### Detailed Instructions

See [USER_GUIDE.md](USER_GUIDE.md) for:
- Complete interface overview
- Step-by-step usage instructions
- Advanced features
- Workflow examples
- Troubleshooting guide

## 🛠️ Development

### Scripts

- `npm start` - Run app normally
- `npm run dev` - Run in development mode (auto-opens DevTools + **hot reload enabled**)
- `npm run debug` - Run with Node.js inspector
- `npm run build:mac` - Build for distribution
- `npm run rebuild` - Rebuild native modules

### Hot Reload (Dev Mode)

When running `npm run dev`, the app automatically reloads when you change:
- HTML files (`renderer/*.html`)
- CSS files (`renderer/*.css`)
- JavaScript files (`renderer/*.js`)

Just save your file and the window will reload automatically! No need to restart the app.

### Development Tips

- Use `npm run dev` for development (auto-opens DevTools)
- Check terminal for main process logs
- Use `debugApp` object in console for debugging
- See DEBUG.md for detailed debugging instructions

## 📝 Notes

- Native speech recognition module must be built before first run
- First launch will request microphone permissions
- Window exclusion uses Electron's `setContentProtection` API
- Some screen recording software may not respect exclusion settings

## 🤝 Contributing

This is a macOS port of the Windows WPF version. The architecture is similar but uses:
- Electron instead of WPF
- macOS Speech Recognition instead of Windows Live Captions
- Node.js services instead of C# services

## 📄 License

MIT License

## 👤 Author

ACC@2025

---

**For detailed usage instructions, see [USER_GUIDE.md](USER_GUIDE.md)**

