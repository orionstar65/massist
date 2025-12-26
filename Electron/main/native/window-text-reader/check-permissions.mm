#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>

// Check if Accessibility permissions are granted
bool CheckAccessibilityPermissions() {
  // Try to create an accessibility element for the current process
  // If permissions are not granted, this will fail
  pid_t pid = getpid();
  AXUIElementRef app = AXUIElementCreateApplication(pid);
  
  if (app) {
    CFRelease(app);
    return true;
  }
  
  return false;
}

// Request Accessibility permissions (opens System Settings)
void RequestAccessibilityPermissions() {
  // Open System Settings to Accessibility pane
  NSURL* url = [NSURL URLWithString:@"x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"];
  [[NSWorkspace sharedWorkspace] openURL:url];
}


