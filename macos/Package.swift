// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "mimi",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "mimi", targets: ["MimiCLI"])
    ],
    targets: [
        .executableTarget(name: "MimiCLI")
    ]
)
