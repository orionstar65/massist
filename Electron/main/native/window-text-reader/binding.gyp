{
  "targets": [
    {
      "target_name": "window-text-reader",
      "cflags!": [ "-fno-exceptions" ],
      "cflags_cc!": [ "-fno-exceptions" ],
      "sources": [
        "window-text-reader.cpp",
        "window-text-reader.mm"
      ],
      "include_dirs": [
        "<!@(node -p \"require('node-addon-api').include\")"
      ],
      "dependencies": [
        "<!(node -p \"require('node-addon-api').gyp\")"
      ],
      "defines": [ "NAPI_DISABLE_CPP_EXCEPTIONS" ],
      "xcode_settings": {
        "GCC_ENABLE_CPP_EXCEPTIONS": "YES",
        "CLANG_CXX_LIBRARY": "libc++",
        "MACOSX_DEPLOYMENT_TARGET": "10.13"
      },
      "link_settings": {
        "libraries": [
          "-framework ApplicationServices",
          "-framework CoreFoundation",
          "-framework Cocoa"
        ]
      }
    }
  ]
}


