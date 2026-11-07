import Foundation

@main
enum RoutingSmoke {
    static func main() throws {
        let work = URL(fileURLWithPath: "/tmp/dmx-test-\(UUID().uuidString.prefix(8))", isDirectory: true)
        try privateDirectory(work)
        defer { try? fileManager.removeItem(at: work) }
        let guide = work.appendingPathComponent("guide")
        let sources = work.appendingPathComponent("source")
        let projectDirectory = sources.appendingPathComponent("folder")
        let nested = projectDirectory.appendingPathComponent("pages")
        let backend = work.appendingPathComponent("backend")
        for dir in [guide, nested, backend.appendingPathComponent("assets"), backend.appendingPathComponent("p/port-app")] {
            try privateDirectory(dir)
        }
        func write(_ text: String, _ url: URL) throws {
            try text.write(to: url, atomically: true, encoding: .utf8)
        }
        try write("guide-html", guide.appendingPathComponent("index.html"))
        let htmlFile = sources.appendingPathComponent("catalog.html")
        try write("single-html", htmlFile)
        try write("folder-html", nested.appendingPathComponent("demo.html"))
        try write("project-css", nested.appendingPathComponent("style.css"))
        try write("hidden", nested.appendingPathComponent(".secret"))
        try fileManager.createSymbolicLink(at: nested.appendingPathComponent("external.html"), withDestinationURL: htmlFile)
        try write("root-backend", backend.appendingPathComponent("index.html"))
        try write("absolute-asset", backend.appendingPathComponent("assets/app.js"))
        try write("path-backend", backend.appendingPathComponent("p/port-app/index.html"))

        let executable = Dependency.caddy.executable("caddy")
        let backendPort = try loopbackPort()
        let local = try loopbackPort()
        let extra = try loopbackPort()
        let last = try loopbackPort()
        _ = try loopbackPort(previewPort)
        var environment = ProcessInfo.processInfo.environment
        environment["XDG_DATA_HOME"] = work.path
        environment["XDG_CONFIG_HOME"] = work.path
        let socket = work.appendingPathComponent("admin.sock")
        let config = work.appendingPathComponent("gateway.json")
        let backendConfig = work.appendingPathComponent("backend.json")
        let backendSocket = work.appendingPathComponent("backend.sock")
        try JSONSerialization.data(withJSONObject: [
            "admin": ["listen": "unix/\(backendSocket.path)", "config": ["persist": false]],
            "apps": ["http": ["servers": ["fixture": [
                "listen": ["127.0.0.1:\(backendPort)"],
                "routes": [
                    ["match": [["path": ["/inspect", "/p/port-app/inspect"]]],
                     "handle": [["handler": "static_response", "body": "proto={http.request.header.X-Forwarded-Proto}"]],
                     "terminal": true],
                    ["handle": [["handler": "file_server", "root": backend.path]]],
                ],
            ]]]],
        ]).write(to: backendConfig)
        let fixture = try Child(executable, ["run", "--config", backendConfig.path], environment: environment)
        defer { fixture.stop() }

        var catalog = Catalog()
        let one = catalog.add(.html(file: htmlFile), name: "Catalog")
        let two = catalog.add(.folder(root: projectDirectory, entry: "pages/demo.html"), name: "Folder")
        let three = catalog.add(.port(backendPort), name: "Port App")
        let four = catalog.add(.html(file: htmlFile), name: "Spare")
        var publications = PublicationSlots()
        let sessionMarker = "gateway-test"
        let extraPorts = [8443: extra, 10000: last]
        var active: [UUID: Content] = [:]
        func configData() throws -> Data {
            try GatewayConfiguration(admin: socket, guide: guide, marker: sessionMarker,
                                     localPort: local, roots: extraPorts, projects: catalog.projects,
                                     active: active, publications: publications, publicHost: "fixture.ts.net").data()
        }
        func reload() throws {
            try configData().write(to: config, options: .atomic)
            _ = try command(executable, ["reload", "--config", config.path, "--address", "unix/\(socket.path)"])
        }
        func get(_ port: Int, _ path: String) throws -> String {
            try curl(["--fail", "http://127.0.0.1:\(port)\(path)"])
        }
        func expect(_ port: Int, _ path: String, _ body: String) throws {
            let actual = try get(port, path)
            precondition(actual == body, "\(path): expected \(body), got \(actual)")
        }
        func expectStatus(_ port: Int, _ path: String, _ status: Int) throws {
            let actual = try localHTTPStatus(port: port, path: path)
            precondition(actual == status, "\(path): expected HTTP \(status), got \(actual)")
        }
        func expectRedirect(_ path: String, _ target: String) throws {
            try expectStatus(previewPort, path, 307)
            let response = try curl(["-D", "-", "-o", "/dev/null",
                                     "http://127.0.0.1:\(previewPort)\(path)"])
            precondition(response.lowercased().contains("location: " + target.lowercased()))
            precondition(!response.contains("guide-html"))
        }

        try configData().write(to: config, options: .atomic)
        let gateway = try Child(executable, ["run", "--config", config.path], environment: environment)
        defer { gateway.stop() }
        for _ in 0..<30 {
            if (try? get(previewPort, guidePath)) == "guide-html" { break }
            Thread.sleep(forTimeInterval: 0.12)
        }
        guard gateway.running else { throw AppError(gateway.output) }
        try expect(previewPort, guidePath, "guide-html")
        let headers = try curl(["-D", "-", "-o", "/dev/null", "http://127.0.0.1:\(previewPort)\(guidePath)"])
        precondition(headers.lowercased().contains("x-demomux-verification: gateway-test"))

        active[one.id] = try one.source.prepare(in: work)
        active[two.id] = try two.source.prepare(in: work)
        active[three.id] = try three.source.prepare(in: work)
        try reload()
        try expect(local, one.localPath, "single-html")
        try expectStatus(previewPort, one.path, 404)
        try expectStatus(previewPort, one.localPath, 404)

        _ = try publications.reserve(one.id, publication: .path)
        _ = try publications.reserve(two.id, publication: .path)
        _ = try publications.reserve(three.id, publication: .root(8443))
        precondition((try? publications.reserve(four.id, publication: .path)) == nil)
        try reload()
        try expect(previewPort, one.path, "single-html")
        try expectStatus(previewPort, two.path, 307)
        try expect(previewPort, two.path + "pages/demo.html", "folder-html")
        try expect(previewPort, two.path + "pages/style.css", "project-css")
        try expectStatus(previewPort, two.path + "pages/.secret", 404)
        try expectStatus(previewPort, two.path + "pages/external.html", 404)
        try expect(extra, "/", "root-backend")
        try expect(extra, "/assets/app.js", "absolute-asset")
        try expect(extra, "/inspect", "proto=https")
        let spoof = try curl(["--header", "X-Forwarded-Proto: http", "http://127.0.0.1:\(extra)/inspect"])
        precondition(spoof == "proto=https")
        precondition(!loopbackConnectionRefused(backendPort))
        try expectRedirect(guidePath, one.path)
        try expectRedirect(guidePath + "index.html", one.path)
        try expectRedirect("/", one.path)
        precondition(publications.homeProjectID == one.id)
        try publications.selectHome(three.id)
        try reload()
        try expectRedirect(guidePath, "https://fixture.ts.net:8443/")
        precondition(publications.shares.count == 3)
        try expect(previewPort, one.path, "single-html")
        try expect(extra, "/assets/app.js", "absolute-asset")
        try publications.selectHome(one.id)
        try reload()

        let old = active[one.id]
        try write("edited-html", htmlFile)
        active[one.id] = try one.source.prepare(in: work)
        try reload()
        old?.discard()
        try expect(previewPort, one.path, "edited-html")

        _ = publications.remove(two.id)
        try reload()
        try expectStatus(previewPort, two.path, 404)
        try expect(previewPort, one.path, "edited-html")
        try expect(extra, "/", "root-backend")
        let moved = sources.appendingPathComponent("moved.html")
        try fileManager.moveItem(at: htmlFile, to: moved)
        precondition(!one.source.sourceExists)
        precondition((try? one.source.prepare(in: work)) == nil)
        _ = publications.remove(one.id)
        precondition(publications.homeProjectID == three.id)
        _ = publications.remove(three.id)
        active[one.id]?.discard()
        active[one.id] = nil
        active[four.id] = try ProjectSource.html(file: moved).prepare(in: work)
        _ = try publications.reserve(four.id, publication: .root(10000))
        _ = try publications.reserve(three.id, publication: .path)
        _ = try publications.reserve(two.id, publication: .root(443))
        try reload()
        try expectStatus(previewPort, "/", 307)
        try expect(previewPort, "/pages/demo.html", "folder-html")
        try expect(previewPort, three.path, "path-backend")
        try expect(previewPort, three.path + "inspect", "proto=https")
        try expectRedirect(guidePath, "/")
        precondition(publications.homeProjectID == two.id)
        precondition((try? publications.selectHome(four.id)) == nil)
        try expect(last, "/", "edited-html")
        try expectStatus(previewPort, one.path, 404)
        let remote = URL(string: "https://fixture.ts.net/p/folder/")!
        let verified = "HTTP/2 200\r\nX-DemoMux-Verification: expected\r\n\r\n200"
        try validatePublicResponse(verified, url: remote, marker: "expected", allowRedirect: false)
        precondition((try? validatePublicResponse(verified, url: remote, marker: "wrong", allowRedirect: false)) == nil)
        let redirect = "HTTP/2 307\r\nX-DemoMux-Verification: expected\r\nLocation: "
        try validatePublicResponse(redirect + "/p/folder/pages/demo.html\r\n\r\n307",
                                   url: remote, marker: "expected", allowRedirect: true)
        precondition((try? validatePublicResponse(redirect + "http://127.0.0.1:5000/\r\n\r\n307",
                                                  url: remote, marker: "expected", allowRedirect: true)) == nil)
        let entry = URL(string: "https://fixture.ts.net/.demomux/")!
        let rootTarget = URL(string: "https://fixture.ts.net:8443/")!
        try validatePublicResponse(redirect + rootTarget.absoluteString + "\r\n\r\n307",
                                  url: entry, marker: "expected", allowRedirect: true, expectedRedirect: rootTarget)
        precondition((try? validatePublicResponse(redirect + "https://fixture.ts.net:10000/\r\n\r\n307",
                                  url: entry, marker: "expected", allowRedirect: true, expectedRedirect: rootTarget)) == nil)
        publications = PublicationSlots()
        try reload()
        try expect(previewPort, guidePath, "guide-html")
        fixture.stop()
        precondition(loopbackConnectionRefused(backendPort))
        print("Passed: private preview, 3 slots, path/root routing, assets, reload, withdrawal, source loss, reuse, HTTPS forwarding, closed-port detection, homepage takeover, guide withdrawal and verified redirects.")
    }
}
