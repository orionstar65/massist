#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>
#import <Cocoa/Cocoa.h>
#import "window-text-reader.h"
#import <napi.h>
#import <string>
#import <algorithm>
#import <cctype>

// Helper function to convert CFString to std::string
std::string CFStringToStdString(CFStringRef cfString) {
  if (!cfString) return "";
  
  CFIndex length = CFStringGetLength(cfString);
  CFIndex maxSize = CFStringGetMaximumSizeForEncoding(length, kCFStringEncodingUTF8) + 1;
  char* buffer = (char*)malloc(maxSize);
  
  if (CFStringGetCString(cfString, buffer, maxSize, kCFStringEncodingUTF8)) {
    std::string result(buffer);
    free(buffer);
    return result;
  }
  
  free(buffer);
  return "";
}

// Helper function to get window title
std::string GetWindowTitle(AXUIElementRef window) {
  CFStringRef titleRef = NULL;
  AXError error = AXUIElementCopyAttributeValue(window, kAXTitleAttribute, (CFTypeRef*)&titleRef);
  
  if (error == kAXErrorSuccess && titleRef) {
    std::string title = CFStringToStdString(titleRef);
    CFRelease(titleRef);
    return title;
  }
  
  return "";
}

// Helper function to get all text from a window element
std::string GetElementText(AXUIElementRef element) {
  if (!element) return "";
  
  // Try to get text value directly
  CFTypeRef valueRef = NULL;
  AXError error = AXUIElementCopyAttributeValue(element, kAXValueAttribute, (CFTypeRef*)&valueRef);
  
  if (error == kAXErrorSuccess && valueRef) {
    if (CFGetTypeID(valueRef) == CFStringGetTypeID()) {
      std::string text = CFStringToStdString((CFStringRef)valueRef);
      CFRelease(valueRef);
      if (!text.empty()) {
        return text;
      }
    }
    CFRelease(valueRef);
  }
  
  // Try to get description (some elements use description for text)
  CFTypeRef descRef = NULL;
  error = AXUIElementCopyAttributeValue(element, kAXDescriptionAttribute, (CFTypeRef*)&descRef);
  
  if (error == kAXErrorSuccess && descRef) {
    if (CFGetTypeID(descRef) == CFStringGetTypeID()) {
      std::string text = CFStringToStdString((CFStringRef)descRef);
      CFRelease(descRef);
      if (!text.empty()) {
        return text;
      }
    }
    CFRelease(descRef);
  }
  
  // Try to get text from children (recursive)
  CFArrayRef children = NULL;
  error = AXUIElementCopyAttributeValue(element, kAXChildrenAttribute, (CFTypeRef*)&children);
  
  if (error == kAXErrorSuccess && children) {
    CFIndex count = CFArrayGetCount(children);
    std::string result;
    
    for (CFIndex i = 0; i < count; i++) {
      AXUIElementRef child = (AXUIElementRef)CFArrayGetValueAtIndex(children, i);
      
      // Get role to prioritize text elements
      CFStringRef roleRef = NULL;
      AXUIElementCopyAttributeValue(child, kAXRoleAttribute, (CFTypeRef*)&roleRef);
      std::string role = roleRef ? CFStringToStdString(roleRef) : "";
      if (roleRef) CFRelease(roleRef);
      
      std::string childText = GetElementText(child);
      if (!childText.empty()) {
        // For text elements, add newline; for others, add space
        if (role == "AXStaticText" || role == "AXTextArea") {
          if (!result.empty() && result.back() != '\n') result += "\n";
          result += childText;
        } else {
          if (!result.empty()) result += " ";
          result += childText;
        }
      }
    }
    
    CFRelease(children);
    return result;
  }
  
  return "";
}

Napi::FunctionReference WindowTextReader::constructor;

Napi::Object WindowTextReader::Init(Napi::Env env, Napi::Object exports) {
  Napi::Function func = DefineClass(env, "WindowTextReader", {
    InstanceMethod("start", &WindowTextReader::Start),
    InstanceMethod("stop", &WindowTextReader::Stop),
    InstanceMethod("getText", &WindowTextReader::GetText),
    InstanceMethod("findWindow", &WindowTextReader::FindWindow),
    InstanceMethod("listWindows", &WindowTextReader::ListWindows)
  });
  
  constructor = Napi::Persistent(func);
  constructor.SuppressDestruct();
  
  exports.Set("WindowTextReader", func);
  return exports;
}

WindowTextReader::WindowTextReader(const Napi::CallbackInfo& info) 
  : Napi::ObjectWrap<WindowTextReader>(info),
    isRunning_(false),
    targetWindow_(NULL),
    pollTimer_(NULL),
    callback_(nullptr) {
  Napi::Env env = info.Env();
  
  if (info.Length() > 0 && info[0].IsObject()) {
    Napi::Object config = info[0].As<Napi::Object>();
    
    if (config.Has("windowTitle") && config.Get("windowTitle").IsString()) {
      windowTitle_ = config.Get("windowTitle").As<Napi::String>().Utf8Value();
    }
    
    if (config.Has("processName") && config.Get("processName").IsString()) {
      processName_ = config.Get("processName").As<Napi::String>().Utf8Value();
    }
    
    if (config.Has("callback") && config.Get("callback").IsFunction()) {
      callback_ = Napi::ThreadSafeFunction::New(
        env,
        config.Get("callback").As<Napi::Function>(),
        "WindowTextReaderCallback",
        0,
        1
      );
    }
  }
}

WindowTextReader::~WindowTextReader() {
  StopPolling();
  if (targetWindow_) {
    CFRelease(targetWindow_);
    targetWindow_ = NULL;
  }
  if (callback_) {
    callback_.Abort();
    callback_.Release();
  }
}

void WindowTextReader::StartPolling() {
  if (pollTimer_) return;
  
  // Use dispatch queue for polling
  dispatch_queue_t queue = dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0);
  dispatch_source_t timer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, queue);
  
  if (timer) {
    uint64_t interval = (uint64_t)(0.16 * NSEC_PER_SEC); // 160ms
    dispatch_time_t start = dispatch_time(DISPATCH_TIME_NOW, interval);
    
    // Capture 'this' pointer
    WindowTextReader* reader = this;
    dispatch_source_set_timer(timer, start, interval, 0);
    dispatch_source_set_event_handler(timer, ^{
      if (reader && reader->isRunning_) {
        reader->PollForText();
      }
    });
    
    dispatch_resume(timer);
    pollTimer_ = (void*)timer; // Store timer pointer
  }
}

void WindowTextReader::StopPolling() {
  if (pollTimer_) {
    dispatch_source_t timer = (dispatch_source_t)pollTimer_;
    dispatch_source_cancel(timer);
    dispatch_release(timer);
    pollTimer_ = NULL;
  }
}

void WindowTextReader::PollForText() {
  if (!targetWindow_ || !isRunning_) return;
  
  std::string currentText = ReadWindowText(targetWindow_);
  
  // Only update if text changed and is not empty
  if (currentText != lastText_ && !currentText.empty()) {
    lastText_ = currentText;
    
    if (callback_) {
      std::string textToSend = currentText;
      // Use NonBlockingCall for async callback
      napi_status status = callback_.NonBlockingCall([textToSend](Napi::Env env, Napi::Function jsCallback) {
        if (jsCallback) {
          try {
            jsCallback.Call({Napi::String::New(env, textToSend)});
          } catch (...) {
            // Ignore callback errors
          }
        }
      });
      
      if (status != napi_ok) {
        // If NonBlockingCall fails, try BlockingCall
        callback_.BlockingCall([textToSend](Napi::Env env, Napi::Function jsCallback) {
          if (jsCallback) {
            try {
              jsCallback.Call({Napi::String::New(env, textToSend)});
            } catch (...) {
              // Ignore callback errors
            }
          }
        });
      }
    }
  }
}

std::string WindowTextReader::ReadWindowText(AXUIElementRef window) {
  if (!window) return "";
  
  // Strategy: Chrome Live Captions typically has text in specific elements
  // Try multiple approaches to find the caption text
  
  // 1. Try to get text directly from the window
  std::string directText = GetElementText(window);
  if (!directText.empty() && directText.length() > 3) {
    return directText;
  }
  
  // 2. Look for text containers in children
  CFArrayRef children = NULL;
  AXError error = AXUIElementCopyAttributeValue(window, kAXChildrenAttribute, (CFTypeRef*)&children);
  
  if (error == kAXErrorSuccess && children) {
    CFIndex count = CFArrayGetCount(children);
    std::string result;
    std::string longestText = ""; // Track the longest text found (likely the main caption)
    
    // First pass: look for text elements
    for (CFIndex i = 0; i < count; i++) {
      AXUIElementRef child = (AXUIElementRef)CFArrayGetValueAtIndex(children, i);
      
      // Get role
      CFStringRef roleRef = NULL;
      AXUIElementCopyAttributeValue(child, kAXRoleAttribute, (CFTypeRef*)&roleRef);
      
      std::string role = roleRef ? CFStringToStdString(roleRef) : "";
      if (roleRef) CFRelease(roleRef);
      
      // Prioritize text elements
      if (role == "AXStaticText" || role == "AXTextArea" || role == "AXTextField") {
        std::string text = GetElementText(child);
        if (!text.empty() && text.length() > longestText.length()) {
          longestText = text;
        }
      }
      
      // Also check groups and scroll areas (Chrome might use these)
      if (role == "AXGroup" || role == "AXScrollArea" || role == "AXWebArea") {
        std::string text = GetElementText(child);
        if (!text.empty() && text.length() > longestText.length()) {
          longestText = text;
        }
      }
    }
    
    // If we found a long text, use it
    if (!longestText.empty() && longestText.length() > 5) {
      CFRelease(children);
      return longestText;
    }
    
    // Second pass: recursively search all children
    for (CFIndex i = 0; i < count; i++) {
      AXUIElementRef child = (AXUIElementRef)CFArrayGetValueAtIndex(children, i);
      std::string childText = ReadWindowText(child);
      if (!childText.empty() && childText.length() > longestText.length()) {
        longestText = childText;
      }
    }
    
    CFRelease(children);
    
    if (!longestText.empty()) {
      // Clean up the result - preserve newlines but normalize spaces
      std::string cleaned;
      bool lastWasSpace = false;
      bool lastWasNewline = false;
      for (char c : longestText) {
        if (c == '\n') {
          if (!lastWasNewline) {
            cleaned += '\n';
            lastWasNewline = true;
            lastWasSpace = false;
          }
        } else if (c == ' ' || c == '\t') {
          if (!lastWasSpace && !lastWasNewline) {
            cleaned += ' ';
            lastWasSpace = true;
          }
        } else {
          cleaned += c;
          lastWasSpace = false;
          lastWasNewline = false;
        }
      }
      return cleaned;
    }
  }
  
  return "";
}

AXUIElementRef WindowTextReader::FindWindowByTitle(const std::string& title) {
  // Get all running applications
  NSWorkspace* workspace = [NSWorkspace sharedWorkspace];
  NSArray* apps = [workspace runningApplications];
  
  // Try multiple title variations for Chrome Live Captions
  std::vector<std::string> searchTerms = {
    title,  // Original search term
    "Live captions",
    "Live Captions",
    "live caption",
    "caption"
  };
  
  for (NSRunningApplication* app in apps) {
    NSString* bundleId = [app bundleIdentifier];
    NSString* appName = [app localizedName];
    
    // Prioritize Chrome/Chromium browsers
    bool isChrome = false;
    if (bundleId) {
      std::string bundleIdStr = std::string([bundleId UTF8String]);
      std::string lowerBundleId = bundleIdStr;
      std::transform(lowerBundleId.begin(), lowerBundleId.end(), lowerBundleId.begin(), ::tolower);
      
      if (lowerBundleId.find("chrome") != std::string::npos ||
          lowerBundleId.find("chromium") != std::string::npos ||
          lowerBundleId.find("com.google.chrome") != std::string::npos) {
        isChrome = true;
      }
    }
    
    pid_t pid = [app processIdentifier];
    AXUIElementRef appRef = AXUIElementCreateApplication(pid);
    
    if (appRef) {
      CFArrayRef windows = NULL;
      AXError error = AXUIElementCopyAttributeValues(
        appRef,
        kAXWindowsAttribute,
        0,
        100,
        (CFArrayRef*)&windows
      );
      
      if (error == kAXErrorSuccess && windows) {
        CFIndex count = CFArrayGetCount(windows);
        
        // First pass: look for exact matches in Chrome
        if (isChrome) {
          for (CFIndex i = 0; i < count; i++) {
            AXUIElementRef window = (AXUIElementRef)CFArrayGetValueAtIndex(windows, i);
            std::string windowTitle = GetWindowTitle(window);
            
            for (const auto& searchTerm : searchTerms) {
              std::string lowerTitle = windowTitle;
              std::string lowerSearch = searchTerm;
              std::transform(lowerTitle.begin(), lowerTitle.end(), lowerTitle.begin(), ::tolower);
              std::transform(lowerSearch.begin(), lowerSearch.end(), lowerSearch.begin(), ::tolower);
              
              if (lowerTitle.find(lowerSearch) != std::string::npos) {
                // Verify it has text content (likely a caption window)
                std::string testText = ReadWindowText(window);
                if (!testText.empty() || windowTitle.find("caption") != std::string::npos) {
                  CFRetain(window);
                  CFRelease(appRef);
                  CFRelease(windows);
                  return window;
                }
              }
            }
          }
        }
        
        // Second pass: look in all apps
        for (CFIndex i = 0; i < count; i++) {
          AXUIElementRef window = (AXUIElementRef)CFArrayGetValueAtIndex(windows, i);
          std::string windowTitle = GetWindowTitle(window);
          
          for (const auto& searchTerm : searchTerms) {
            std::string lowerTitle = windowTitle;
            std::string lowerSearch = searchTerm;
            std::transform(lowerTitle.begin(), lowerTitle.end(), lowerTitle.begin(), ::tolower);
            std::transform(lowerSearch.begin(), lowerSearch.end(), lowerSearch.begin(), ::tolower);
            
            if (lowerTitle.find(lowerSearch) != std::string::npos) {
              CFRetain(window);
              CFRelease(appRef);
              CFRelease(windows);
              return window;
            }
          }
        }
        
        CFRelease(windows);
      }
      
      CFRelease(appRef);
    }
  }
  
  return NULL;
}

AXUIElementRef WindowTextReader::FindWindowByProcess(const std::string& processName) {
  NSWorkspace* workspace = [NSWorkspace sharedWorkspace];
  NSArray* apps = [workspace runningApplications];
  
  std::string lowerProcessName = processName;
  std::transform(lowerProcessName.begin(), lowerProcessName.end(), lowerProcessName.begin(), ::tolower);
  
  for (NSRunningApplication* app in apps) {
    NSString* bundleId = [app bundleIdentifier];
    NSString* name = [app localizedName];
    
    std::string appName = name ? std::string([name UTF8String]) : "";
    std::string bundleIdStr = bundleId ? std::string([bundleId UTF8String]) : "";
    
    std::string lowerAppName = appName;
    std::string lowerBundleId = bundleIdStr;
    std::transform(lowerAppName.begin(), lowerAppName.end(), lowerAppName.begin(), ::tolower);
    std::transform(lowerBundleId.begin(), lowerBundleId.end(), lowerBundleId.begin(), ::tolower);
    
    if (lowerAppName.find(lowerProcessName) != std::string::npos ||
        lowerBundleId.find(lowerProcessName) != std::string::npos) {
      pid_t pid = [app processIdentifier];
      AXUIElementRef appRef = AXUIElementCreateApplication(pid);
      
      if (appRef) {
        CFArrayRef windows = NULL;
        AXError error = AXUIElementCopyAttributeValues(
          appRef,
          kAXWindowsAttribute,
          0,
          100,
          (CFArrayRef*)&windows
        );
        
        if (error == kAXErrorSuccess && windows && CFArrayGetCount(windows) > 0) {
          // Return the first window
          AXUIElementRef window = (AXUIElementRef)CFArrayGetValueAtIndex(windows, 0);
          CFRetain(window);
          CFRelease(appRef);
          CFRelease(windows);
          return window;
        }
        
        CFRelease(appRef);
        if (windows) CFRelease(windows);
      }
    }
  }
  
  return NULL;
}

Napi::Value WindowTextReader::FindWindow(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  
  // Try to find window
  if (!windowTitle_.empty()) {
    targetWindow_ = FindWindowByTitle(windowTitle_);
  } else if (!processName_.empty()) {
    targetWindow_ = FindWindowByProcess(processName_);
  } else {
    // Default: try to find "Live captions" window
    targetWindow_ = FindWindowByTitle("Live captions");
  }
  
  if (targetWindow_) {
    // Test reading text to verify it's the right window
    std::string testText = ReadWindowText(targetWindow_);
    if (testText.empty()) {
      // Window found but no text - might not be the right one
      CFRelease(targetWindow_);
      targetWindow_ = NULL;
    }
  }
  
  return Napi::Boolean::New(env, targetWindow_ != NULL);
}

Napi::Value WindowTextReader::Start(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  
  if (isRunning_) {
    return Napi::Boolean::New(env, true);
  }
  
  // Find window if not already found
  if (!targetWindow_) {
    if (!windowTitle_.empty()) {
      targetWindow_ = FindWindowByTitle(windowTitle_);
    } else if (!processName_.empty()) {
      targetWindow_ = FindWindowByProcess(processName_);
    } else {
      // Default: try to find "Live captions" window
      targetWindow_ = FindWindowByTitle("Live captions");
    }
  }
  
  if (!targetWindow_) {
    // Try one more time with empty search to find any caption window
    targetWindow_ = FindWindowByTitle("caption");
  }
  
  if (!targetWindow_) {
    return Napi::Boolean::New(env, false);
  }
  
  // Verify window still has text
  std::string testText = ReadWindowText(targetWindow_);
  if (testText.empty()) {
    // Window found but no readable text - release and return false
    CFRelease(targetWindow_);
    targetWindow_ = NULL;
    return Napi::Boolean::New(env, false);
  }
  
  isRunning_ = true;
  lastText_ = "";
  StartPolling();
  
  return Napi::Boolean::New(env, true);
}

Napi::Value WindowTextReader::Stop(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  
  StopPolling();
  isRunning_ = false;
  
  return Napi::Boolean::New(env, true);
}

Napi::Value WindowTextReader::GetText(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  return Napi::String::New(env, lastText_);
}

Napi::Value WindowTextReader::ListWindows(const Napi::CallbackInfo& info) {
  Napi::Env env = info.Env();
  Napi::Array result = Napi::Array::New(env);
  
  NSWorkspace* workspace = [NSWorkspace sharedWorkspace];
  NSArray* apps = [workspace runningApplications];
  
  int index = 0;
  for (NSRunningApplication* app in apps) {
    NSString* bundleId = [app bundleIdentifier];
    NSString* name = [app localizedName];
    
    pid_t pid = [app processIdentifier];
    AXUIElementRef appRef = AXUIElementCreateApplication(pid);
    
    if (appRef) {
      CFArrayRef windows = NULL;
      AXError error = AXUIElementCopyAttributeValues(
        appRef,
        kAXWindowsAttribute,
        0,
        100,
        (CFArrayRef*)&windows
      );
      
      if (error == kAXErrorSuccess && windows) {
        CFIndex count = CFArrayGetCount(windows);
        
        for (CFIndex i = 0; i < count; i++) {
          AXUIElementRef window = (AXUIElementRef)CFArrayGetValueAtIndex(windows, i);
          std::string windowTitle = GetWindowTitle(window);
          
          if (!windowTitle.empty()) {
            Napi::Object windowInfo = Napi::Object::New(env);
            windowInfo.Set("title", Napi::String::New(env, windowTitle));
            windowInfo.Set("app", Napi::String::New(env, name ? std::string([name UTF8String]) : ""));
            windowInfo.Set("bundleId", Napi::String::New(env, bundleId ? std::string([bundleId UTF8String]) : ""));
            
            // Try to read text from this window
            std::string text = ReadWindowText(window);
            windowInfo.Set("hasText", Napi::Boolean::New(env, !text.empty()));
            windowInfo.Set("textLength", Napi::Number::New(env, text.length()));
            if (!text.empty() && text.length() < 200) {
              windowInfo.Set("textPreview", Napi::String::New(env, text));
            }
            
            result.Set(index++, windowInfo);
          }
        }
        
        CFRelease(windows);
      }
      
      CFRelease(appRef);
    }
  }
  
  return result;
}

Napi::Object Init(Napi::Env env, Napi::Object exports) {
  return WindowTextReader::Init(env, exports);
}

NODE_API_MODULE(window_text_reader, Init)

