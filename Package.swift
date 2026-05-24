// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "FicheGen",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "FicheGen",
            path: "Sources/FicheGen",
            resources: [.process("Resources")]
        )
    ]
)
