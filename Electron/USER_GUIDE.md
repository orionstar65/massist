# Stealth Interview Assistant - User Guide

## Overview

**Stealth Interview Assistant** is a macOS desktop application designed to help you during interviews by providing real-time speech-to-text transcription and AI-powered answer assistance. The app runs discretely in the background, capturing audio from your microphone and displaying transcripts that you can quickly send to AI assistants like ChatGPT, DeepSeek, Perplexity, or Grok.

### Key Features

- 🎤 **Live Speech Recognition** - Real-time transcription using macOS built-in Speech Recognition
- 🤖 **AI Assistant Integration** - Built-in browser for ChatGPT, DeepSeek, Perplexity, and Grok
- 📅 **Google Calendar Integration** - View upcoming interviews and events
- ⌨️ **Global Hotkeys** - Quick actions without switching windows
- 🎯 **Stealth Mode** - Exclude window from screen recordings
- 🎨 **Customizable UI** - Adjustable opacity, positioning, and sizing
- 📝 **Smart Answer Generation** - Configure answer style, duration, and format

---

## Installation & Setup

### Prerequisites

- macOS 10.15 (Catalina) or later
- Microphone access (required for speech recognition)
- Internet connection (for AI assistants and calendar)

### Installation Steps

1. **Install Dependencies**
   ```bash
   cd Mac
   npm install
   ```

2. **Build Native Module**
   ```bash
   cd main/native/speech-recognition
   npm install
   node-gyp rebuild
   cd ../../..
   ```

3. **Run the App**
   ```bash
   npm start
   ```

4. **Grant Permissions**
   - When prompted, grant **Microphone** access
   - Optionally grant **Screen Recording** access (for screenshot feature)

---

## Interface Overview

### Main Window Layout

The app window is divided into several sections:

```
┌─────────────────────────────────────────────────────────┐
│  [Main Border - Dashed Outline]                         │
│  ┌──────────────────┬────┬──────────────────┐          │
│  │                  │    │                  │          │
│  │  Caption Panel   │    │  Browser Panel   │          │
│  │  (Left)          │    │  (Right)         │          │
│  │                  │    │                  │          │
│  └──────────────────┴────┴──────────────────┘          │
│  ┌──────────────────────────────────────────────────┐  │
│  │  [Toggle Buttons] [Duration] [Action Buttons]   │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
│  [Control Panel - Right Side]                          │
```

### Panel Descriptions

#### 1. **Caption Panel (Left)**
- Displays real-time transcript from speech recognition
- Contains controls for starting/stopping captions
- Shows calendar events (when connected)
- Font size adjustable with slider

#### 2. **Browser Panel (Right)**
- Embedded web browser for AI assistants
- Quick-launch buttons for different services
- Text injection for sending prompts

#### 3. **Button Groups Row (Bottom)**
- **Left**: Toggle buttons for answer style modifiers
- **Middle**: Radio buttons for answer duration
- **Right**: Action buttons (FollowUp, Clarify, Answer)

#### 4. **Control Panel (Right Side)**
- Cursor mode selection
- Logging toggle
- Interview mode toggle
- Help button
- Exit button

---

## How to Use

### Step 1: Start Speech Recognition

1. Click the **▶ (Play)** button in the caption panel
2. Grant microphone permissions when prompted
3. The status light will turn **green** when ready
4. Start speaking - your words will appear in real-time

**Status Indicators:**
- 🔴 **Gray**: Not started
- 🟠 **Orange**: Connecting/Searching
- 🟢 **Green**: Active and capturing

### Step 2: Configure Answer Settings

#### Answer Style Toggles (Left Button Group)

Select one or more style modifiers:

- **Professional** - Formal, business-appropriate tone
- **Funny** - Light, humorous responses
- **STAR** - Situation-Task-Action-Result format
- **Sharp** - Concise, direct answers
- **Creative** - Innovative, out-of-the-box thinking
- **Specific** - Mention specific technologies/examples
- **Story mode** - Narrative-style responses
- **Impactful** - Emphasize results and achievements
- **Tech stack versions** - Include version numbers
- **Detailed** - Comprehensive explanations
- **Step-by-Step** - Break down into steps

#### Answer Duration (Middle Button Group)

Choose how long the answer should be:
- **1 min** - Brief, concise answer
- **1-2mins** - Medium-length response
- **2-3mins** - Detailed, comprehensive answer

### Step 3: Send to AI Assistant

#### Method 1: Using Action Buttons

1. **Answer Button** - Generates a complete answer to the question
2. **FollowUp Button** - Creates a thoughtful follow-up question
3. **Clarify Button** - Asks for clarification on a point

The app will:
- Extract new text since last send (delta)
- Format it with your selected modifiers
- Inject it into the active browser
- Automatically submit (if possible)

#### Method 2: Using Hotkey

1. Press **`Ctrl+Shift+/`** (or `Cmd+Shift+/` on Mac)
2. This sends the transcript delta to the active browser
3. Works even when the app window is not focused

### Step 4: Use AI Assistants

#### Quick Launch Buttons

Click the buttons in the address bar:
- **CG** - Opens ChatGPT
- **D** - Opens DeepSeek
- **P** - Opens Perplexity
- **G** - Opens Grok

#### Manual Navigation

The browser panel works like a normal browser:
- Navigate to any URL
- Interact with websites normally
- Text injection works on most input fields

---

## Advanced Features

### Google Calendar Integration

#### Connect Calendar

1. Click **"📅 Connect Calendar"** button
2. Browser will open for Google OAuth
3. Sign in and grant permissions
4. Status will show "✓ Connected"

#### View Upcoming Interviews

- Connected calendar shows upcoming events
- Filters for interview-related keywords:
  - interview, meeting, call, zoom, teams
  - google meet, hiring, recruiter
- Displays event title, time, and calendar name

#### Refresh Calendar

- Click **🔄** button to refresh events
- Updates automatically show new interviews

### Window Management

#### Positioning

Use hotkeys to move the window:
- **`Ctrl+Shift+J`** - Move left
- **`Ctrl+Shift+L`** - Move right
- **`Ctrl+Shift+I`** - Move up
- **`Ctrl+Shift+K`** - Move down

Hold the keys for continuous movement.

#### Resizing

- **`Ctrl+Shift+R`** - Decrease width
- **`Ctrl+Shift+T`** - Increase width
- **`Ctrl+Shift+Q`** - Decrease height
- **`Ctrl+Shift+A`** - Increase height

#### Opacity Control

- **`Ctrl+Shift+↑`** - Increase opacity
- **`Ctrl+Shift+↓`** - Decrease opacity

Make the window more or less transparent for better visibility.

#### Visibility Toggle

- **`Ctrl+Shift+H`** - Hide/Show window
- Useful for temporarily hiding the app

### Stealth Features

#### Exclude from Screen Recording

The app can be excluded from screen recordings:
- Enabled by default
- Prevents the window from appearing in recordings
- Uses macOS `setContentProtection` API

**Note:** Not all recording software respects this setting.

#### Hide from Dock

- App runs without Dock icon (LSUIElement)
- Cleaner desktop experience
- Still accessible via window

### Cursor Modes

Control how the cursor appears:

- **A (Arrow)** - Standard arrow cursor
- **I (Caret)** - Text caret cursor
- **N (Normal)** - Default system cursors

### Interview Mode

Toggle between:
- **I (Interview Mode)** - Optimized for interviews
- **N (Normal Mode)** - General use

### Debug Logging

Enable detailed logging:
- Click **📝** button in control panel
- Toggles to **✓** when enabled
- Logs saved to console and terminal

---

## Global Hotkeys Reference

All hotkeys use `Ctrl+Shift` (or `Cmd+Shift` on Mac) + key:

| Hotkey | Action |
|--------|--------|
| `/` | Send transcript delta to WebView |
| `J` | Move window left |
| `L` | Move window right |
| `I` | Move window up |
| `K` | Move window down |
| `R` | Decrease window width |
| `T` | Increase window width |
| `Q` | Decrease window height |
| `A` | Increase window height |
| `↑` | Increase opacity |
| `↓` | Decrease opacity |
| `H` | Toggle visibility (hide/show) |
| `P` | Exit application |
| `.` | Take screenshot |

**Note:** Hotkeys work globally, even when the app window is not focused.

---

## Workflow Examples

### Example 1: Answering a Technical Question

1. **Start Captions** - Click ▶ button
2. **Listen** - Interviewer asks: "Tell me about your experience with React"
3. **Configure** - Select toggles: Professional, Detailed, Tech stack versions
4. **Set Duration** - Choose "1-2mins"
5. **Send** - Click "Answer" button
6. **Review** - AI generates answer in browser
7. **Copy/Read** - Use the generated answer as reference

### Example 2: Asking a Follow-Up

1. **Continue Captions** - Keep recording
2. **Listen** - Interviewer asks a follow-up question
3. **Send FollowUp** - Click "FollowUp" button
4. **AI Suggests** - Get a thoughtful follow-up question
5. **Use It** - Ask the suggested question

### Example 3: Clarifying a Point

1. **Capture** - Record the point that needs clarification
2. **Click Clarify** - Use "Clarify" button
3. **Get Response** - AI generates clarification request
4. **Ask** - Use it to clarify during the interview

### Example 4: Quick Reference During Interview

1. **Keep Captions Running** - Continuous transcription
2. **Use Hotkey** - Press `Ctrl+Shift+/` to send to browser
3. **Get Quick Answer** - AI responds instantly
4. **Reference** - Use answer as talking points

---

## Tips & Best Practices

### For Best Transcription Quality

1. **Speak Clearly** - Enunciate words properly
2. **Reduce Background Noise** - Use in quiet environment
3. **Check Microphone** - Ensure good microphone quality
4. **Monitor Status** - Watch the status light (should be green)

### For Optimal AI Responses

1. **Combine Modifiers** - Use multiple toggles for better results
   - Example: Professional + Detailed + STAR
2. **Match Duration** - Choose duration that fits the question
3. **Review Before Sending** - Check transcript is accurate
4. **Use FollowUp** - Generate thoughtful questions

### Window Management Tips

1. **Position Strategically** - Place where it won't block important content
2. **Adjust Opacity** - Make semi-transparent if needed
3. **Resize for Comfort** - Make it large enough to read easily
4. **Hide When Needed** - Use `Ctrl+Shift+H` to hide temporarily

### Performance Tips

1. **Close Unused Tabs** - Keep browser panel clean
2. **Clear Transcript** - Use ✖ button to clear old text
3. **Monitor Memory** - Check DevTools if app feels slow
4. **Restart if Needed** - Close and reopen for fresh start

---

## Troubleshooting

### Speech Recognition Not Working

**Problem:** Status light stays orange or red

**Solutions:**
1. Check microphone permissions in System Preferences
2. Ensure microphone is not muted
3. Try restarting the app
4. Check console for errors (DevTools)

### Hotkeys Not Working

**Problem:** Hotkeys don't respond

**Solutions:**
1. Check if hotkey conflicts with system shortcuts
2. Ensure app has focus (try clicking window)
3. Check terminal for registration errors
4. Restart the app

### Browser Not Loading

**Problem:** WebView shows blank or error

**Solutions:**
1. Check internet connection
2. Try different browser button (CG, D, P, G)
3. Reload page (right-click → Reload)
4. Check if website is accessible

### Calendar Not Connecting

**Problem:** Calendar connection fails

**Solutions:**
1. Verify Google Calendar API credentials in config
2. Check internet connection
3. Ensure OAuth consent screen is configured
4. Check terminal for error messages

### Window Too Small/Large

**Problem:** Window size is uncomfortable

**Solutions:**
1. Use resize hotkeys (`Ctrl+Shift+R/T/Q/A`)
2. Drag window edges (if enabled)
3. Reset by closing and reopening app

### Transcript Not Updating

**Problem:** No text appears in caption panel

**Solutions:**
1. Verify captions are started (green status light)
2. Check microphone is working
3. Speak louder or closer to microphone
4. Check console for errors

---

## Configuration

### Config File Location

Configuration is stored in:
```
~/Library/Application Support/stealth-interview-assistant-mac/config.json
```

### Configurable Options

```json
{
  "windowBounds": {
    "width": 680,
    "height": 650,
    "x": null,
    "y": null
  },
  "opacity": 1.0,
  "excludedFromCapture": true,
  "isInterviewMode": true,
  "isLoggingEnabled": false,
  "googleCalendar": {
    "clientId": "YOUR_CLIENT_ID.apps.googleusercontent.com",
    "clientSecret": "YOUR_CLIENT_SECRET"
  }
}
```

### Google Calendar Setup

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select existing
3. Enable **Google Calendar API**
4. Create **OAuth 2.0 credentials**
5. Add credentials to config file
6. Add your email as test user (if in testing mode)

---

## Keyboard Shortcuts Summary

### App Controls
- `Ctrl+Shift+I` - Open DevTools (debugging)
- `Ctrl+Shift+R` - Reload window
- `Ctrl+Shift+H` - Toggle visibility
- `Ctrl+Shift+P` - Exit app

### Window Movement
- `Ctrl+Shift+J/L/I/K` - Move window
- `Ctrl+Shift+R/T/Q/A` - Resize window
- `Ctrl+Shift+↑/↓` - Adjust opacity

### Content
- `Ctrl+Shift+/` - Send to browser
- `Ctrl+Shift+.` - Screenshot

---

## Support & Resources

### Debugging

See `DEBUG.md` for detailed debugging instructions.

### Building

See `BUILD_AND_TEST.md` for build and test procedures.

### Common Issues

Check the Troubleshooting section above for solutions to common problems.

---

## Version Information

- **Version:** 1.0.0
- **Platform:** macOS 10.15+
- **Framework:** Electron
- **Speech Recognition:** macOS Speech Framework
- **Author:** ACC@2025

---

## License

MIT License

