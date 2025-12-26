# Build and Test Instructions

## Build Status

✅ **Code Validation Complete**
- All JavaScript files have valid syntax
- Project structure is correct
- All required files are in place

## Build Steps

Due to sandbox restrictions, the actual npm install and build need to be run manually. Here are the steps:

### 1. Install Dependencies

```bash
cd /Volumes/Works/reserch/PowerInterviewAssistant/Mac
npm install
```

**Note:** If you encounter permission errors, you may need to:
- Run with appropriate permissions
- Or use `npm install --cache /tmp/npm-cache` to use a temporary cache

### 2. Build Native Module

The native speech recognition module needs to be built:

```bash
cd main/native/speech-recognition
npm install
npm run rebuild
# Or use node-gyp directly:
node-gyp rebuild
```

**Requirements:**
- Xcode Command Line Tools installed
- macOS 10.15+ (for Speech framework)

### 3. Test the Application

```bash
npm start
```

**Expected Behavior:**
- Window should appear (frameless, transparent, always on top)
- Startup animation should play
- UI should match Windows version's dark theme
- All buttons and controls should be visible

### 4. Test Features

#### Speech Recognition
1. Click the "Start" button (▶)
2. Grant microphone permissions when prompted
3. Speak into microphone
4. Transcript should appear in real-time

#### Hotkeys
Test all global shortcuts:
- `Ctrl+Shift+/` - Send to WebView
- `Ctrl+Shift+J/L` - Move left/right
- `Ctrl+Shift+I/K` - Move up/down
- `Ctrl+Shift+R/T` - Width decrease/increase
- `Ctrl+Shift+Q/A` - Height decrease/increase
- `Ctrl+Shift+↑/↓` - Opacity increase/decrease
- `Ctrl+Shift+H` - Toggle visibility
- `Ctrl+Shift+P` - Exit

#### Browser Integration
1. Click browser buttons (CG, D, P, G)
2. WebView should load corresponding URLs
3. Click "Answer" button
4. Text should be injected into the active webview

#### Calendar Integration
1. Click "Connect Calendar"
2. Complete OAuth flow in browser
3. Click "Refresh Calendar"
4. Upcoming interviews should appear

### 5. Build for Distribution

```bash
npm run build:mac
```

This will create a DMG file in the `dist/` directory.

## Known Issues & Notes

1. **Native Module**: The speech recognition native module must be built before use. If build fails, the app will use a mock implementation.

2. **Permissions**: 
   - Microphone access is required for speech recognition
   - Screen recording permission may be needed for screenshot feature

3. **Window Exclusion**: Uses Electron's `setContentProtection` API which may not work with all screen recording tools.

4. **Hotkeys**: Some hotkeys may conflict with system shortcuts. The app will log warnings if registration fails.

## Troubleshooting

### npm install fails
- Check Node.js version: `node --version` (should be 18+)
- Try clearing npm cache: `npm cache clean --force`
- Use `npm install --legacy-peer-deps` if peer dependency issues

### Native module build fails
- Ensure Xcode Command Line Tools: `xcode-select --install`
- Check macOS version (10.15+ required)
- Review build errors in `main/native/speech-recognition/build/`

### App won't start
- Check console for errors: `npm start` will show errors
- Verify all dependencies installed: `ls node_modules/electron`
- Check main.js syntax: `node -c main/main.js`

### Speech recognition not working
- Grant microphone permissions in System Preferences
- Check console for native module loading errors
- Verify native module was built: `ls main/native/speech-recognition/build/Release/`

## Code Quality

✅ All JavaScript files validated
✅ No syntax errors
✅ Project structure complete
✅ All services implemented
✅ UI components in place

## Next Steps

1. Run `npm install` to install dependencies
2. Build native module with `npm run rebuild`
3. Test with `npm start`
4. Build distribution package with `npm run build:mac`

