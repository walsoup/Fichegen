# FicheGen Native for macOS

FicheGen is a modern, native macOS application designed to revolutionize lesson planning for educators. Built entirely in Swift, FicheGen leverages the power of Google's Gemini API to instantly generate structured, comprehensive, and perfectly formatted lesson plans ("fiches pédagogiques") directly from your curriculum documents.

Gone are the days of tedious copying, formatting, and manual layout adjustments. FicheGen handles the heavy lifting, allowing educators to focus on what matters most: teaching.

## Key Features

✨ **Native macOS Experience**
Built from the ground up in Swift, FicheGen is blazingly fast and incredibly lightweight. It embraces modern macOS design paradigms, featuring beautiful `.ultraThinMaterial` glassmorphism, fluid spring animations, and a seamless native interface.

🤖 **AI-Powered Lesson Generation**
Provide the app with your class level, subject, lesson topic, and any extracted text or instructions. FicheGen's secure integration with the Gemini API instantly drafts a robust lesson plan tailored to your exact specifications.

🎨 **Graphic Style Builder**
Make your Fiches your own. With the built-in Style Builder, you can customize the primary and secondary colors, choose your preferred typography (Avenir, Helvetica, Georgia, etc.), and adjust global margins. See your changes instantly in a live interactive preview.

📑 **Template Engine**
Not a fan of the default layout? FicheGen allows you to upload custom Markdown (`.md`) structural templates. The AI will adopt your uploaded structure for all future generations, ensuring your lesson plans always match your school's official format.

💬 **Interactive AI Editor**
Need a quick tweak? Use the integrated AI chat command bar at the bottom of the result preview. Simply type *"Make the introduction shorter"* or *"Rewrite the conclusion to be more engaging"* and watch the document update natively on the fly.

📄 **Flawless Native PDF Export**
FicheGen features a robust, invisible WebKit engine that seamlessly renders your Markdown into HTML using your selected custom CSS styles, and leverages macOS `NSPrintOperation` to generate pixel-perfect PDFs directly to your drive. No Python dependencies, no external services required.

## Building from Source

FicheGen uses `XcodeGen` for project management, ensuring a clean and reproducible build environment.

1. **Clone the repository**
```bash
git clone https://github.com/yourusername/fichegen.git
cd fichegen
```

2. **Generate the Xcode Project**
```bash
xcodegen generate
```

3. **Build the App**
Open `FicheGen.xcodeproj` in Xcode and hit **Run** (⌘R), or build it directly from the command line:
```bash
xcodebuild -project FicheGen.xcodeproj -scheme FicheGen -configuration Release
```

## Requirements
- **macOS:** 14.0 or later
- **Xcode:** 15.0 or later (for building)
- **API Key:** A valid Google Gemini API key (can be securely configured in the app's Preferences).

## Privacy & Security
FicheGen communicates directly with the Gemini API via native Swift `URLSession`. Your API keys are securely stored in the macOS Keychain. No intermediate servers or analytics trackers are used.

---
*FicheGen: Empowering educators, one lesson plan at a time.*
