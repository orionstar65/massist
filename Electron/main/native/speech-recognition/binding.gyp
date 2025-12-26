{
  "targets": [
    {
      "target_name": "speech-recognition",
      "sources": [
        "binding.cpp",
        "speech-recognition.mm"
      ],
      "include_dirs": [
        "<!@(node -p \"require('node-addon-api').include\")"
      ],
      "dependencies": [
        "<!(node -p \"require('node-addon-api').gyp\")"
      ],
      "cflags!": [ "-fno-exceptions" ],
      "cflags_cc!": [ "-fno-exceptions" ],
      "xcode_settings": {
        "GCC_ENABLE_CPP_EXCEPTIONS": "YES",
        "CLANG_CXX_LIBRARY": "libc++",
        "MACOSX_DEPLOYMENT_TARGET": "10.15"
      },
      "conditions": [
        ["OS=='mac'", {
          "frameworks": [
            "Speech",
            "AVFoundation",
            "AppKit"
          ],
          "link_settings": {
            "libraries": [
              "-framework Speech",
              "-framework AVFoundation",
              "-framework AppKit"
            ]
          }
        }]
      ]
    }
  ]
}

