using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace P3Orbit
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int port = 8787;
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            bool openBrowser = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                        if (i + 1 < args.Length) Int32.TryParse(args[++i], out port);
                        break;
                    case "--root":
                        if (i + 1 < args.Length) root = Path.GetFullPath(args[++i]);
                        break;
                    case "--open":
                        openBrowser = true;
                        break;
                    case "--help":
                    case "-h":
                        Console.WriteLine("P3 OrBit bridge");
                        Console.WriteLine("  --port <n>   listen port (default 8787)");
                        Console.WriteLine("  --root <dir> folder holding catalog/ and web/ (default: parent of bridge/)");
                        Console.WriteLine("  --open       open the UI in the default browser");
                        return 0;
                }
            }

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("P3 OrBit bridge 0.1.0");
            Console.WriteLine("root: " + root);

            App app;
            try { app = new App(root, port); }
            catch (Exception ex)
            {
                Console.WriteLine("Startup failed: " + ex.Message);
                return 1;
            }

            Console.CancelKeyPress += delegate
            {
                Console.WriteLine("Closing adapter...");
                app.Shutdown();
            };
            AppDomain.CurrentDomain.ProcessExit += delegate { app.Shutdown(); };

            string webRoot = Path.Combine(root, "web", "dist");
            if (!Directory.Exists(webRoot))
                Console.WriteLine("web/dist not found - run 'npm run dev' in web/ for the live UI, or 'npm run build' for a single-port build.");

            if (openBrowser) OpenBrowser("http://127.0.0.1:" + port + "/");
            Console.WriteLine("API: http://127.0.0.1:" + port + "/api/state");

            try
            {
                HttpServer.Serve(port, request => Route(app, request, webRoot), text => { });
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Console.WriteLine("Could not listen on port " + port + ": " + ex.Message);
                Console.WriteLine("Another bridge is probably running. Close it, or use --port <n>.");
                app.Shutdown();
                return 2;
            }
            return 0;
        }

        private static HttpResponse Route(App app, HttpRequest request, string webRoot)
        {
            try
            {
                if (request.Method == "OPTIONS") return new HttpResponse { Status = 204 };

                // A random web page can still POST to loopback even when CORS
                // hides the response. Reject browser state changes that come
                // from a non-local origin. Non-browser clients send no Origin.
                if (request.Method != "GET" && request.Method != "HEAD" &&
                    !String.IsNullOrEmpty(request.Origin) && !HttpServer.IsLocalOrigin(request.Origin))
                    return HttpResponse.Error("bad_origin", "State changes are only accepted from a local page.", 403);

                if (!request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && request.Path != "/api")
                    return ServeStatic(request, webRoot);

                string path = request.Path.Substring(4).Trim('/').ToLowerInvariant();
                string method = request.Method;
                Dictionary<string, object> body = null;

                Func<Dictionary<string, object>> json = () =>
                {
                    body = JsonText.Object(request.BodyText);
                    return body;
                };

                switch (method + " /" + path)
                {
                    case "GET /state":
                        return HttpResponse.Json(app.State());

                    case "GET /log":
                        return HttpResponse.Json(app.Logs(QueryLong(request, "since", 0)));

                    case "GET /adapters":
                        return HttpResponse.Json(app.State());

                    case "POST /adapters/refresh":
                        return HttpResponse.Json(app.RefreshAdapters());

                    case "POST /adapter/select":
                        return HttpResponse.Json(app.SelectAdapter(Required(json(), "library")));

                    case "POST /mode":
                        return HttpResponse.Json(app.SetMode(Required(json(), "mode")));

                    case "POST /connect":
                        return HttpResponse.Json(app.Connect());

                    case "POST /disconnect":
                        return HttpResponse.Json(app.Disconnect());

                    case "POST /read":
                        return HttpResponse.Json(app.Read());

                    case "POST /dump/import":
                        return HttpResponse.Json(app.ImportDump(Required(json(), "hex")));

                    case "POST /plan":
                    {
                        var payload = json();
                        object editsRaw;
                        if (!payload.TryGetValue("edits", out editsRaw))
                            throw new ApiException("no_edits", "Send an edits array.", 422);
                        if (!(editsRaw is System.Collections.IList))
                            throw new ApiException("bad_edits", "Send edits as an array of { id, value } objects.", 422);
                        List<Edit> edits;
                        try { edits = JsonText.Parse<List<Edit>>(JsonText.Serialize(editsRaw)); }
                        catch (Exception ex)
                        {
                            throw new ApiException("bad_edits",
                                "Send edits as an array of { id, value } objects. " + ex.Message, 422);
                        }
                        if (edits == null)
                            throw new ApiException("bad_edits", "Send edits as an array of { id, value } objects.", 422);
                        return HttpResponse.Json(app.PlanEdits(edits));
                    }

                    case "POST /apply":
                    {
                        var payload = json();
                        object planId, confirm;
                        payload.TryGetValue("planId", out planId);
                        payload.TryGetValue("confirm", out confirm);
                        return HttpResponse.Json(app.Apply(planId as string, confirm as string));
                    }

                    case "POST /discard":
                        return HttpResponse.Json(app.Discard());

                    case "POST /undo":
                        return HttpResponse.Json(app.Undo());

                    case "POST /catalog/reload":
                        return HttpResponse.Json(app.ReloadCatalogFromDisk());

                    case "POST /catalog/import":
                    {
                        var payload = json();
                        object text;
                        payload.TryGetValue("text", out text);
                        object jsonNode;
                        string fresh = payload.TryGetValue("json", out jsonNode) ? JsonText.Serialize(jsonNode) : text as string;
                        return HttpResponse.Json(app.ImportCatalog(fresh));
                    }
                }

                return HttpResponse.Error("not_found", "No route for " + method + " " + request.Path + ".", 404);
            }
            catch (ApiException ex)
            {
                return HttpResponse.Error(ex.Code, ex.Message, ex.Status);
            }
            catch (InvalidDataException ex)
            {
                return HttpResponse.Error("invalid_data", ex.Message, 422);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled: " + ex);
                return HttpResponse.Error("bridge_error", ex.Message, 500);
            }
        }

        private static string Required(Dictionary<string, object> body, string key)
        {
            object value;
            if (!body.TryGetValue(key, out value) || value == null || Convert.ToString(value).Length == 0)
                throw new ApiException("missing_field", "'" + key + "' is required.", 422);
            return Convert.ToString(value);
        }

        private static long QueryLong(HttpRequest request, string key, long fallback)
        {
            if (String.IsNullOrEmpty(request.RawQuery)) return fallback;
            foreach (string pair in request.RawQuery.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (String.Equals(pair.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                {
                    long parsed;
                    if (Int64.TryParse(pair.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        return parsed;
                }
            }
            return fallback;
        }

        private static HttpResponse ServeStatic(HttpRequest request, string webRoot)
        {
            if (!Directory.Exists(webRoot))
            {
                return new HttpResponse
                {
                    ContentType = "text/html; charset=utf-8",
                    Body = Encoding.UTF8.GetBytes(
                        "<!doctype html><meta charset=utf-8><title>P3 OrBit</title>" +
                        "<body style=\"font:15px/1.5 system-ui;background:#0d1117;color:#c9d1d9;padding:40px\">" +
                        "<h1>Bridge is running</h1><p>The UI has not been built yet.</p>" +
                        "<p>In <code>web/</code> run <code>npm run dev</code> (Vite on :5173) " +
                        "or <code>npm run build</code> and reload this page.</p>" +
                        "<p>API: <a style=\"color:#58a6ff\" href=\"/api/state\">/api/state</a></p>")
                };
            }

            string relative = request.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0) relative = "index.html";
            string rootFull = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(rootFull, relative));

            // Compare against the root plus a separator so a sibling directory
            // such as web/dist-old cannot satisfy a prefix test.
            string rootPrefix = rootFull + Path.DirectorySeparatorChar;
            if (!String.Equals(candidate, rootFull, StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Error("not_found", "Not found.", 404);

            if (!File.Exists(candidate) && Path.GetExtension(candidate).Length == 0)
                candidate = Path.Combine(webRoot, "index.html");

            if (!File.Exists(candidate)) return HttpResponse.Error("not_found", "Not found.", 404);
            return HttpResponse.File(candidate);
        }

        private static void OpenBrowser(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Console.WriteLine("Could not open a browser: " + ex.Message); }
        }
    }
}
