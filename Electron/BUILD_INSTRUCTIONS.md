# Building the WindowTextReader Native Module

## Prerequisites

1. **Node.js** (v18+ recommended)
2. **Xcode Command Line Tools**:
   ```bash
   xcode-select --install
   ```
3. **node-gyp** (installed globally or via npx):
   ```bash
   npm install -g node-gyp
   ```

## Building the Module

1. Navigate to the native module directory:
   ```bash
   cd Mac/main/native/window-text-reader
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Build the module:
   ```bash
   npx node-gyp rebuild
   ```

   Or from the Mac directory root:
   ```bash
   cd Mac
   npm run rebuild
   ```

## Required Permissions

The WindowTextReader module requires **Accessibility permissions** on macOS:

1. Go to **System Settings** > **Privacy & Security** > **Accessibility**
2. Add your Electron app (or Terminal if running from command line)
3. Enable the checkbox for your app

## Troubleshooting

### Build Errors

If you get compilation errors:
- Make sure Xcode Command Line Tools are installed
- Check that you're using a compatible Node.js version
- Try cleaning and rebuilding:
  ```bash
  npx node-gyp clean
  npx node-gyp rebuild
  ```

### Runtime Errors

If the module fails to load:
- Check that Accessibility permissions are granted
- Verify the module was built successfully (check `build/Release/window-text-reader.node`)
- Check console logs for specific error messages

### Finding Windows

The module can find windows by:
- **Window Title**: Searches for windows containing the specified title (case-insensitive)
- **Process Name**: Searches for windows from applications matching the process name

Default configuration looks for windows with "Live captions" in the title.

## Testing

1. Start the Electron app
2. Select "Window" mode in the caption source toggle
3. Make sure a window with "Live captions" in the title is open
4. Click Start - it should find and read text from that window


