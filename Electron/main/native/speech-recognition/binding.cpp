#include <napi.h>
#include "speech-recognition.h"

Napi::Object InitAll(Napi::Env env, Napi::Object exports) {
  SpeechRecognition::Init(env, exports);
  return exports;
}

NODE_API_MODULE(speech_recognition, InitAll)

