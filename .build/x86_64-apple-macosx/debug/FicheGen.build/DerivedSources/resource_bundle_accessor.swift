import Foundation

extension Foundation.Bundle {
    static let module: Bundle = {
        let mainPath = Bundle.main.bundleURL.appendingPathComponent("FicheGen_FicheGen.bundle").path
        let buildPath = "/Users/wal/.gemini/antigravity/worktrees/Fichegen/resume-fichegen-main-work/FicheGen-macOS/.build/x86_64-apple-macosx/debug/FicheGen_FicheGen.bundle"

        let preferredBundle = Bundle(path: mainPath)

        guard let bundle = preferredBundle ?? Bundle(path: buildPath) else {
            // Users can write a function called fatalError themselves, we should be resilient against that.
            Swift.fatalError("could not load resource bundle: from \(mainPath) or \(buildPath)")
        }

        return bundle
    }()
}