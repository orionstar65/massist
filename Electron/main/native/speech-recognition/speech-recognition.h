#ifndef SPEECH_RECOGNITION_H
#define SPEECH_RECOGNITION_H

#include <napi.h>

class SpeechRecognition : public Napi::ObjectWrap<SpeechRecognition> {
 public:
  static Napi::Object Init(Napi::Env env, Napi::Object exports);
  SpeechRecognition(const Napi::CallbackInfo& info);
  ~SpeechRecognition();

 private:
  static Napi::FunctionReference constructor;
  
  Napi::Value Start(const Napi::CallbackInfo& info);
  Napi::Value Stop(const Napi::CallbackInfo& info);
  Napi::Value IsRunning(const Napi::CallbackInfo& info);
  
  void* nativeImpl;
};

#endif

