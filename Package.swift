// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "NCMBatchMP3",
    platforms: [
        .macOS(.v14)
    ],
    targets: [
        .executableTarget(
            name: "NCMConverter",
            path: "src"
        )
    ]
)