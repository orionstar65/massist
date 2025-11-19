Rebuild and release this project as a single standalone executable file.
C:\Users\assas_8\Downloads\PowerView\StealthInterviewAssistant\bin\Release\net8.0-windows10.0.18362.0\win-x64\publish

Commands to execute:
1. dotnet build StealthInterviewAssistant/StealthInterviewAssistant.csproj -c Release
2. dotnet publish StealthInterviewAssistant/StealthInterviewAssistant.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "StealthInterviewAssistant\bin\Release\net8.0-windows10.0.18362.0\win-x64\publish"
3. Remove log files and XML documentation files if they exist in the publish directory

The output will be a single standalone executable file (StealthInterviewAssistant.exe ~181MB) that includes all dependencies and the .NET runtime, ready to run on Windows 10/11 without requiring .NET installation.

Note: The executable may extract some native libraries to a temporary directory on first run (required for WebView2), but the main executable is a single file bundle.
