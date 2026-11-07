import Foundation

@main
struct PublicProbeSmoke {
    static func main() async throws {
        let port = try loopbackPort()
        let fixture = try Child(URL(fileURLWithPath: "/usr/bin/python3"),
                                ["-u", CommandLine.arguments[1], String(port)])
        defer { fixture.stop() }
        for _ in 0..<80 {
            if fixture.output.contains("ready") { break }
            try await Task.sleep(for: .milliseconds(50))
        }
        precondition(fixture.output.contains("ready"))
        let url = URL(string: "http://127.0.0.1:\(port)/")!
        func arguments(_ path: String) -> [String] {
            ["-q", "--noproxy", "*", "--silent", "--show-error", "--no-buffer",
             "--max-time", "10", "--range", "0-0", "--dump-header", "-", "--output", "/dev/null",
             url.appendingPathComponent(path).absoluteString]
        }
        let begin = Date()
        let headers = try await publicHeaderRequest(arguments(""), timeout: 3)
        precondition(Date().timeIntervalSince(begin) < 2)
        try validatePublicResponse(headers, url: url, marker: "slow-page-marker", allowRedirect: false)
        precondition((try? validatePublicResponse(headers, url: url, marker: "stale", allowRedirect: false)) == nil)
        precondition(completedPublicHeaders("HTTP/1.1 200 OK\r\nX-DemoMux-Verification: partial") == nil)
        precondition(completedPublicHeaders("HTTP/1.1 103 Early Hints\r\n\r\n") == nil)
        let informational = "HTTP/1.1 103 Early Hints\r\n\r\nHTTP/2 206\r\nX-DemoMux-Verification: expected\r\n\r\n"
        try validatePublicResponse(completedPublicHeaders(informational)!, url: url, marker: "expected", allowRedirect: false)
        guard case .handshake = publicTransportFailure(35), case .tls = publicTransportFailure(60),
              case .timeout = publicTransportFailure(28), case .connection = publicTransportFailure(7) else {
            fatalError("Transport errors must be distinguished")
        }
        let cancellable = Task { try await publicHeaderRequest(arguments("slow-header")) }
        try await Task.sleep(for: .milliseconds(150))
        let cancelBegin = Date()
        cancellable.cancel()
        do { _ = try await cancellable.value; fatalError("Expected cancellation") }
        catch is CancellationError { }
        precondition(Date().timeIntervalSince(cancelBegin) < 2)
        do { _ = try await publicHeaderRequest(arguments("slow-header"), timeout: 0.2); fatalError("Expected timeout") }
        catch PublicCheckError.timeout { }
        print("Passed: slow 1.4 MB GET ignores Range; headers finish without body, marker security, partial/1xx headers, transport classification, bounded timeout and prompt cancellation")
    }
}
