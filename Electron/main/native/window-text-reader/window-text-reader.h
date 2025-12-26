#ifndef WINDOW_TEXT_READER_H
#define WINDOW_TEXT_READER_H

#include <napi.h>
#include <ApplicationServices/ApplicationServices.h>

class WindowTextReader : public Napi::ObjectWrap<WindowTextReader> {
public:
  static Napi::Object Init(Napi::Env env, Napi::Object exports);
  WindowTextReader(const Napi::CallbackInfo& info);
  ~WindowTextReader();

private:
  static Napi::FunctionReference constructor;
  
  Napi::Value Start(const Napi::CallbackInfo& info);
  Napi::Value Stop(const Napi::CallbackInfo& info);
  Napi::Value GetText(const Napi::CallbackInfo& info);
  Napi::Value FindWindow(const Napi::CallbackInfo& info);
  Napi::Value ListWindows(const Napi::CallbackInfo& info); // Debug function
  
  void StartPolling();
  void StopPolling();
  void PollForText();
  std::string ReadWindowText(AXUIElementRef window);
  AXUIElementRef FindWindowByTitle(const std::string& title);
  AXUIElementRef FindWindowByProcess(const std::string& processName);
  
  bool isRunning_;
  std::string lastText_;
  std::string windowTitle_;
  std::string processName_;
  AXUIElementRef targetWindow_;
  void* pollTimer_; // dispatch_source_t stored as void*
  Napi::ThreadSafeFunction callback_;
};

#endif

