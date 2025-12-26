#import <Foundation/Foundation.h>
#import <Speech/Speech.h>
#import <AVFoundation/AVFoundation.h>
#import <napi.h>
#import "speech-recognition.h"

// Forward declaration
@interface SpeechRecognitionImpl : NSObject <SFSpeechRecognizerDelegate, SFSpeechRecognitionTaskDelegate>

@property (nonatomic, strong) SFSpeechRecognizer* recognizer;
@property (nonatomic, strong) AVAudioEngine* audioEngine;
@property (nonatomic, strong) SFSpeechRecognitionRequest* recognitionRequest;
@property (nonatomic, strong) SFSpeechRecognitionTask* recognitionTask;
@property (nonatomic, assign) BOOL isRunning;
@property (nonatomic, strong) NSMutableString* transcript;
@property (nonatomic, assign) Napi::ThreadSafeFunction* callback;

- (BOOL)start;
- (void)stop;
- (BOOL)isRunning;
- (NSString*)getTranscript;
- (void)clearTranscript;
- (void)setCallback:(Napi::ThreadSafeFunction*)callback;

@end

@implementation SpeechRecognitionImpl

- (instancetype)init {
    self = [super init];
    if (self) {
        self.recognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:@"en-US"]];
        self.recognizer.delegate = self;
        self.audioEngine = [[AVAudioEngine alloc] init];
        self.transcript = [[NSMutableString alloc] init];
        self.isRunning = NO;
        self.callback = nullptr;
    }
    return self;
}

- (void)dealloc {
    [self stop];
    if (self.callback) {
        self.callback->Release();
        self.callback = nullptr;
    }
}

- (BOOL)start {
    if (self.isRunning) {
        return YES;
    }

    // Request authorization
    [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {
        if (status != SFSpeechRecognizerAuthorizationStatusAuthorized) {
            NSLog(@"Speech recognition authorization denied");
            return;
        }

        dispatch_async(dispatch_get_main_queue(), ^{
            [self startRecognition];
        });
    }];

    return YES;
}

- (void)startRecognition {
    if (self.isRunning) {
        return;
    }

    // Stop any existing task
    if (self.recognitionTask) {
        [self.recognitionTask cancel];
        self.recognitionTask = nil;
    }

    // Create recognition request
    self.recognitionRequest = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
    self.recognitionRequest.shouldReportPartialResults = YES;

    // Setup audio engine (macOS doesn't need AVAudioSession)
    AVAudioInputNode* inputNode = self.audioEngine.inputNode;
    AVAudioFormat* recordingFormat = [inputNode outputFormatForBus:0];

    [inputNode installTapOnBus:0 bufferSize:1024 format:recordingFormat block:^(AVAudioPCMBuffer* buffer, AVAudioTime* when) {
        if (self.recognitionRequest) {
            [self.recognitionRequest appendAudioPCMBuffer:buffer];
        }
    }];

    // Start audio engine
    [self.audioEngine prepare];
    NSError* engineError = nil;
    BOOL started = [self.audioEngine startAndReturnError:&engineError];
    if (!started || engineError) {
        NSLog(@"Audio engine start error: %@", engineError.localizedDescription);
        return;
    }

    // Start recognition task
    self.recognitionTask = [self.recognizer recognitionTaskWithRequest:self.recognitionRequest delegate:self];
    self.isRunning = YES;
}

- (void)stop {
    if (!self.isRunning) {
        return;
    }

    if (self.audioEngine.isRunning) {
        [self.audioEngine stop];
        [self.audioEngine.inputNode removeTapOnBus:0];
    }

    if (self.recognitionRequest) {
        [self.recognitionRequest endAudio];
        self.recognitionRequest = nil;
    }

    if (self.recognitionTask) {
        [self.recognitionTask cancel];
        self.recognitionTask = nil;
    }

    self.isRunning = NO;
}

- (BOOL)isRunning {
    return self.isRunning;
}

- (NSString*)getTranscript {
    return [self.transcript copy];
}

- (void)clearTranscript {
    [self.transcript setString:@""];
}

- (void)setCallback:(Napi::ThreadSafeFunction*)callback {
    if (self.callback) {
        self.callback->Release();
    }
    self.callback = callback;
}

#pragma mark - SFSpeechRecognitionTaskDelegate

- (void)speechRecognitionTask:(SFSpeechRecognitionTask*)task didHypothesizeTranscription:(SFTranscription*)transcription {
    NSString* newText = transcription.formattedString;
    
    // Update transcript
    [self.transcript setString:newText];
    
    // Call JavaScript callback
    if (self.callback) {
        auto callback = [](Napi::Env env, Napi::Function jsCallback, const char* text) {
            jsCallback.Call({Napi::String::New(env, text)});
        };
        
        self.callback->NonBlockingCall(newText.UTF8String, callback);
    }
}

- (void)speechRecognitionTask:(SFSpeechRecognitionTask*)task didFinishRecognition:(SFSpeechRecognitionResult*)result {
    NSString* finalText = result.bestTranscription.formattedString;
    [self.transcript setString:finalText];
    
    if (self.callback) {
        auto callback = [](Napi::Env env, Napi::Function jsCallback, const char* text) {
            jsCallback.Call({Napi::String::New(env, text)});
        };
        
        self.callback->NonBlockingCall(finalText.UTF8String, callback);
    }
}

- (void)speechRecognitionTask:(SFSpeechRecognitionTask*)task didFinishSuccessfully:(BOOL)successfully {
    if (!successfully) {
        NSLog(@"Recognition task finished unsuccessfully");
    }
    self.isRunning = NO;
}

- (void)speechRecognitionTaskWasCancelled:(SFSpeechRecognitionTask*)task {
    self.isRunning = NO;
}

#pragma mark - SFSpeechRecognizerDelegate

- (void)speechRecognizer:(SFSpeechRecognizer*)speechRecognizer availabilityDidChange:(BOOL)available {
    if (!available) {
        NSLog(@"Speech recognizer became unavailable");
    }
}

@end

// C++ Wrapper
class SpeechRecognitionWrapper {
public:
    SpeechRecognitionImpl* impl;
    
    SpeechRecognitionWrapper() {
        impl = [[SpeechRecognitionImpl alloc] init];
    }
    
    ~SpeechRecognitionWrapper() {
        [impl release];
    }
};

// NAPI Implementation
Napi::FunctionReference SpeechRecognition::constructor;

Napi::Object SpeechRecognition::Init(Napi::Env env, Napi::Object exports) {
    Napi::Function func = DefineClass(env, "SpeechRecognition", {
        InstanceMethod("start", &SpeechRecognition::Start),
        InstanceMethod("stop", &SpeechRecognition::Stop),
        InstanceMethod("isRunning", &SpeechRecognition::IsRunning),
    });

    constructor = Napi::Persistent(func);
    constructor.SuppressDestruct();

    exports.Set("SpeechRecognition", func);
    return exports;
}

SpeechRecognition::SpeechRecognition(const Napi::CallbackInfo& info) : Napi::ObjectWrap<SpeechRecognition>(info) {
    nativeImpl = new SpeechRecognitionWrapper();
}

SpeechRecognition::~SpeechRecognition() {
    delete static_cast<SpeechRecognitionWrapper*>(nativeImpl);
}

Napi::Value SpeechRecognition::Start(const Napi::CallbackInfo& info) {
    Napi::Env env = info.Env();
    
    SpeechRecognitionWrapper* wrapper = static_cast<SpeechRecognitionWrapper*>(nativeImpl);
    
    // Set up callback
    if (info.Length() > 0 && info[0].IsFunction()) {
        Napi::Function callback = info[0].As<Napi::Function>();
        Napi::ThreadSafeFunction* tsf = new Napi::ThreadSafeFunction(
            Napi::ThreadSafeFunction::New(env, callback, "SpeechRecognition", 0, 1)
        );
        [wrapper->impl setCallback:tsf];
    }
    
    BOOL success = [wrapper->impl start];
    return Napi::Boolean::New(env, success);
}

Napi::Value SpeechRecognition::Stop(const Napi::CallbackInfo& info) {
    SpeechRecognitionWrapper* wrapper = static_cast<SpeechRecognitionWrapper*>(nativeImpl);
    [wrapper->impl stop];
    return info.Env().Undefined();
}

Napi::Value SpeechRecognition::IsRunning(const Napi::CallbackInfo& info) {
    Napi::Env env = info.Env();
    SpeechRecognitionWrapper* wrapper = static_cast<SpeechRecognitionWrapper*>(nativeImpl);
    BOOL running = [wrapper->impl isRunning];
    return Napi::Boolean::New(env, running);
}

