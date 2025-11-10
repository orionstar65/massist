# Stealth Interview Assistant - Project Analysis

## 🎯 **Primary Purpose**

**Stealth Interview Assistant** is a Windows application designed to help users during video interviews or meetings by:

1. **Capturing live captions** from Windows Live Captions feature in real-time
2. **Displaying transcripts** in a dedicated interface
3. **Providing stealth functionality** - hiding itself from screen capture/sharing
4. **Enabling quick text copying** via hotkeys for reference during interviews

The "stealth" aspect means the application can hide itself from screen recording/sharing tools, making it useful for scenarios where you want to reference captions without revealing the assistant tool to interviewers.

---

## 🏗️ **Architecture Overview**

### **Technology Stack:**
- **.NET 8** with **WPF** (Windows Presentation Foundation)
- **MVVM-Lite Pattern** (Model-View-ViewModel without heavy frameworks)
- **UI Automation** (for accessing Windows Live Captions)
- **WebView2** (for embedded browser functionality)
- **P/Invoke** (for Windows API calls)

### **Project Structure:**
```
StealthInterviewAssistant/
├── Services/          # Business logic services
├── ViewModels/        # MVVM view models
├── Views/             # XAML UI definitions
├── Interop/           # P/Invoke native methods
└── Tests/             # Unit tests (xUnit)
```

---

## 🔧 **Core Functionalities**

### **1. Live Captions Capture (`LiveCaptionsService`)**

**Purpose:** Hooks into Windows Live Captions to capture real-time transcriptions.

**How it works:**
- Uses **UI Automation** to locate the Windows Live Captions window
- Finds the text container element that displays captions
- Hooks into `TextPattern.TextChangedEvent` to detect new text
- Processes and deduplicates text using a ring buffer (last 50 lines)
- Normalizes text (case-insensitive, whitespace-normalized) to avoid duplicates

**Key Features:**
- **Automatic window detection** with multiple fallback strategies
- **Line deduplication** - prevents showing the same caption line multiple times
- **Smart delta extraction** - only captures new text, not entire transcript
- **Ring buffer** - maintains hash of last 50 lines for duplicate detection
- **Auto-launch** - attempts to open Live Captions settings if not running

**Methods:**
- `StartAsync()` - Connects to Live Captions window and starts monitoring
- `GetDeltaAndAdvance()` - Returns new text since last capture point
- `GetAll()` - Returns entire transcript buffer
- `Clear()` - Clears the buffer and resets state

---

### **2. Stealth/Privacy Features (`NativeMethods`)**

**Purpose:** Hide the application window from screen capture/sharing tools.

**Implementation:**
Uses **TWO methods simultaneously** for maximum compatibility:

1. **DWM (Desktop Window Manager) Method:**
   - `DwmSetWindowAttribute(hwnd, DWMWA_EXCLUDED_FROM_CAPTURE, TRUE)`
   - Windows 10/11 native method

2. **Display Affinity Method:**
   - `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`
   - Alternative API for broader compatibility

**Why both?** Different screen capture tools respect different APIs. Using both ensures the window is hidden from most capture applications.

**Features:**
- Applies to main window AND WebView2 host window
- Toggleable via UI button
- Default state: **Excluded from capture** (stealth mode ON)

---

### **3. Global Hotkey System**

**Purpose:** Quick access to copy transcript delta without switching windows.

**Implementation:**
- Registers **Ctrl+Alt+C** as a global hotkey
- Works system-wide (even when app is in background)
- Copies only the "delta" (new text since last copy)

**Use Case:** During an interview, press Ctrl+Alt+C to quickly copy new captions to clipboard for reference.

---

### **4. Transcript Management**

**Features:**
- **Real-time display** - Captions appear as they're captured
- **Delta tracking** - Tracks what's new since last "capture point"
- **Copy Delta** - Copies only new text (not entire transcript)
- **Clear Buffer** - Resets transcript display
- **Deduplication** - Prevents duplicate lines from appearing

**Delta Concept:**
- When you click "Copy Delta" or press Ctrl+Alt+C, it copies only text added since the last copy
- Useful for copying just the latest question/response without the entire conversation

---

### **5. Embedded Browser (WebView2)**

**Purpose:** Provides a "stealth browser" panel for reference materials.

**Features:**
- Full WebView2 browser embedded in right panel
- Address bar with navigation
- Auto-prefixes `https://` if protocol missing
- Also excluded from capture (stealth mode)
- Can be used to view notes, documentation, or reference materials during interviews

---

## 🖥️ **User Interface**

### **Layout:**
```
┌─────────────────────────────────────────────────────────┐
│  Stealth Interview Assistant                            │
├──────────────────┬──────────┬──────────────────────────┤
│  Left Panel      │ Splitter │  Right Panel             │
│  (Captions)      │          │  (Browser)                │
│                  │          │                           │
│  [Start] [Copy]  │          │  [Address Bar] [Go]       │
│  [Clear] [Toggle]│          │                           │
│                  │          │  [WebView2 Browser]       │
│  Status: ✓       │          │                           │
│  Hotkey: Ctrl+   │          │                           │
│  Alt+C           │          │                           │
│                  │          │                           │
│  ┌─────────────┐ │          │                           │
│  │ Transcript  │ │          │                           │
│  │ Text Area   │ │          │                           │
│  │ (Scrollable)│ │          │                           │
│  └─────────────┘ │          │                           │
└──────────────────┴──────────┴──────────────────────────┘
```

### **UI Elements:**

**Left Panel:**
- **Start Button** - Initiates connection to Live Captions
- **Copy Delta Button** - Copies new text since last copy
- **Clear Button** - Clears transcript display
- **Toggle Exclude Button** - Enables/disables stealth mode
- **Status Message** - Shows connection status
- **Transcript Display** - Monospaced font, scrollable text area

**Right Panel:**
- **Address Bar** - URL input for browser
- **Go Button** - Navigate to URL
- **WebView2 Browser** - Full browser functionality

**Design:**
- Dark theme (VS Code-inspired colors)
- Rounded borders
- Modern, minimal aesthetic

---

## 🔄 **Workflow Example**

### **Typical Usage Scenario:**

1. **Start Interview:**
   - User opens the application
   - Window is automatically excluded from capture (stealth mode)
   - User clicks "Start" button

2. **Connection:**
   - App searches for Windows Live Captions window
   - If found, hooks into TextPattern events
   - Status shows "✓ Connected to Live Captions - Capturing..."

3. **During Interview:**
   - Live Captions transcribes audio (from meeting/interview)
   - App captures transcript in real-time
   - Text appears in left panel transcript area
   - Duplicate lines are automatically filtered

4. **Quick Reference:**
   - User presses **Ctrl+Alt+C** (or clicks "Copy Delta")
   - New text since last copy is copied to clipboard
   - User can paste into notes or reference document

5. **Stealth Mode:**
   - Application window is hidden from screen sharing
   - Interviewer cannot see the assistant tool
   - User can still see and use the application

---

## 🛠️ **Technical Details**

### **UI Automation Integration:**
- Uses `System.Windows.Automation` namespace
- Locates Live Captions window via multiple strategies:
  1. Exact name match ("Live captions")
  2. Name pattern matching
  3. AutomationId search
  4. Fallback: Any window with TextPattern
- Hooks `TextPattern.TextChangedEvent` for real-time updates

### **Text Processing:**
- **Delta Extraction:** Uses longest common prefix (LCP) algorithm
- **Deduplication:** Case-insensitive, whitespace-normalized hash comparison
- **Ring Buffer:** Maintains last 50 line hashes for duplicate detection

### **Window Management:**
- Minimizes Live Captions window after connection (doesn't move it)
- Applies capture exclusion to both main window and WebView2 host window
- Uses WindowInteropHelper for native window handle access

### **Hotkey System:**
- Registers hotkey in `SourceInitialized` event
- Handles `WM_HOTKEY` messages via `WndProc` hook
- Unregisters on window close

---

## 🎯 **Use Cases**

1. **Video Interviews:**
   - Capture interviewer questions in real-time
   - Reference transcript while formulating answers
   - Copy specific questions to notes

2. **Online Meetings:**
   - Transcribe important discussions
   - Capture action items or decisions
   - Maintain searchable transcript

3. **Accessibility:**
   - Visual reference for audio content
   - Review what was said
   - Reduce cognitive load

4. **Privacy-Conscious Usage:**
   - Use assistant tool without revealing it to others
   - Keep reference materials private
   - Maintain professional appearance

---

## ⚠️ **Current Issues & Limitations**

### **Known Problems:**
1. **Live Captions Connection:** The app may fail to connect to Live Captions window
   - Possible causes: Window name mismatch, permissions, TextPattern access restrictions
   - Status message shows connection state

2. **UI Automation Permissions:**
   - May require elevated permissions
   - Some system windows restrict programmatic access

3. **TextPattern Availability:**
   - Live Captions window structure may vary by Windows version
   - TextPattern might not be accessible in all scenarios

### **Limitations:**
- Requires Windows 10/11 with Live Captions feature
- Depends on Windows Live Captions being enabled
- WebView2 requires Edge WebView2 runtime
- Stealth mode effectiveness depends on screen capture tool implementation

---

## 🔍 **Why It Might Not Be Working**

Based on the code analysis, potential issues:

1. **Window Detection Failure:**
   - Live Captions window name might be different than expected
   - Window might not be accessible via UI Automation
   - Permissions might be insufficient

2. **TextPattern Access:**
   - Live Captions might restrict TextPattern access
   - Window structure might have changed in newer Windows versions
   - Security policies might block automation

3. **Event Handler Not Firing:**
   - TextChangedEvent might not be triggered
   - Event handler might be disconnected
   - TextPattern might not support events

### **Debugging Steps:**
1. Check status message after clicking "Start"
2. Verify Live Captions is running (Win+Ctrl+L)
3. Try running app as Administrator
4. Check if Live Captions window is visible and has text
5. Verify UI Automation permissions in Windows settings

---

## 📝 **Summary**

**Stealth Interview Assistant** is a sophisticated tool that:
- ✅ Captures Windows Live Captions in real-time
- ✅ Provides stealth mode to hide from screen capture
- ✅ Offers quick text copying via hotkeys
- ✅ Includes embedded browser for reference
- ✅ Deduplicates and processes transcript text
- ✅ Uses modern WPF UI with dark theme

The application is designed for users who need real-time transcription assistance during video calls while maintaining privacy and professionalism.

