// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "NCMBatchMP3",
    platforms: [
        .macOS(.v14) // 原作者用了较新的 SwiftUI 特性，最低设为 macOS 14
    ],
    targets: [
        .executableTarget(
            name: "NCMConverter",
            path: "src"
        )
    ]
)