using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace P3Orbit
{
    internal sealed class HttpRequest
    {
        public string Method;
        public string Path;
        public string RawQuery;
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = new byte[0];
        public string Origin;

        public string BodyText { get { return Encoding.UTF8.GetString(Body); } }
    }

    internal sealed class HttpResponse
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];
        public Dictionary<string, string> ExtraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool Cacheable;

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        internal static HttpResponse Json(object payload, int status = 200)
        {
            return new HttpResponse
            {
                Status = status,
                ContentType = "application/json; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(Serializer.Serialize(payload))
            };
        }

        internal static HttpResponse Error(string code, string message, int status = 400)
        {
            return Json(new Dictionary<string, object>
            {
                { "ok", false },
                { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } }
            }, status);
        }

        internal static HttpResponse File(string path)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            return new HttpResponse
            {
                ContentType = MimeFor(path),
                Body = bytes,
                Cacheable = true
            };
        }

        internal static string MimeFor(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".ico": return "image/x-icon";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                case ".map": return "application/json; charset=utf-8";
                case ".txt": return "text/plain; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }

    // Small blocking HTTP/1.1 server on a TcpListener. HttpListener would need a
    // URL ACL for some prefixes; loopback TCP needs nothing and keeps the
    // dependency surface at zero.
    internal static class HttpServer
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int MaxBodyBytes = 4 * 1024 * 1024;

        internal static void Serve(int port, Func<HttpRequest, HttpResponse> handler, Action<string> log)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            log("Bridge listening on http://127.0.0.1:" + port + "/");

            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { continue; }

                // One slow or half-open socket must not stall the polling UI.
                TcpClient captured = client;
                ThreadPool.QueueUserWorkItem(delegate { HandleClient(captured, handler, log); });
            }
        }

        private static void HandleClient(TcpClient client, Func<HttpRequest, HttpResponse> handler, Action<string> log)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 10000;
                    using (var stream = client.GetStream())
                    {
                        HttpRequest request = ReadRequest(stream);
                        if (request == null) return;
                        HttpResponse response;
                        try { response = handler(request); }
                        catch (Exception ex)
                        {
                            log("Request failed: " + ex.Message);
                            response = HttpResponse.Error("bridge_error", ex.Message, 500);
                        }
                        WriteResponse(stream, request, response);
                    }
                }
            }
            catch (Exception ex)
            {
                log("Connection dropped: " + ex.Message);
            }
        }

        private static HttpRequest ReadRequest(Stream stream)
        {
            var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int headerEnd = -1;

            while (headerEnd < 0)
            {
                int read = stream.Read(chunk, 0, chunk.Length);
                if (read <= 0) return null;
                buffer.Write(chunk, 0, read);
                headerEnd = IndexOf(buffer.ToArray(), new byte[] { 13, 10, 13, 10 });
                if (buffer.Length > MaxHeaderBytes) throw new InvalidDataException("Request headers are too large.");
            }

            byte[] all = buffer.ToArray();
            string head = Encoding.ASCII.GetString(all, 0, headerEnd);
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 3) throw new InvalidDataException("Malformed request line.");

            var request = new HttpRequest
            {
                Method = requestLine[0].ToUpperInvariant(),
                Path = requestLine[1]
            };

            int queryAt = request.Path.IndexOf('?');
            if (queryAt >= 0)
            {
                request.RawQuery = request.Path.Substring(queryAt + 1);
                request.Path = request.Path.Substring(0, queryAt);
            }
            request.Path = Uri.UnescapeDataString(request.Path);

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                request.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            request.Headers.TryGetValue("Origin", out request.Origin);

            int contentLength = 0;
            string rawLength;
            if (request.Headers.TryGetValue("Content-Length", out rawLength) &&
                !Int32.TryParse(rawLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength))
                throw new InvalidDataException("Invalid Content-Length.");

            if (contentLength < 0) throw new InvalidDataException("Invalid Content-Length.");
            if (contentLength > MaxBodyBytes) throw new InvalidDataException("Request body is too large.");

            int bodyStart = headerEnd + 4;
            int alreadyRead = all.Length - bodyStart;
            if (alreadyRead > contentLength) alreadyRead = contentLength;

            var body = new byte[contentLength];
            if (alreadyRead > 0) Buffer.BlockCopy(all, bodyStart, body, 0, alreadyRead);
            int offset = alreadyRead;
            while (offset < contentLength)
            {
                int read = stream.Read(body, offset, contentLength - offset);
                if (read <= 0) throw new InvalidDataException("Request body ended early.");
                offset += read;
            }
            request.Body = body;
            return request;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        private static void WriteResponse(Stream stream, HttpRequest request, HttpResponse response)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n");
            head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
            head.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
            head.Append("Connection: close\r\n");
            head.Append("X-Content-Type-Options: nosniff\r\n");
            head.Append("Referrer-Policy: no-referrer\r\n");
            head.Append(response.Cacheable ? "Cache-Control: no-cache\r\n" : "Cache-Control: no-store\r\n");

            if (!String.IsNullOrEmpty(request.Origin) && IsLocalOrigin(request.Origin))
            {
                head.Append("Access-Control-Allow-Origin: ").Append(request.Origin).Append("\r\n");
                head.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                head.Append("Access-Control-Allow-Headers: Content-Type\r\n");
                head.Append("Access-Control-Max-Age: 600\r\n");
                head.Append("Vary: Origin\r\n");
            }

            foreach (var pair in response.ExtraHeaders)
                head.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");
            head.Append("\r\n");

            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            if (response.Body.Length > 0) stream.Write(response.Body, 0, response.Body.Length);
            stream.Flush();
        }

        internal static bool IsLocalOrigin(string origin)
        {
            return origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://[::1]:", StringComparison.OrdinalIgnoreCase)
                || origin.Equals("http://localhost", StringComparison.OrdinalIgnoreCase)
                || origin.Equals("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || origin.Equals("http://[::1]", StringComparison.OrdinalIgnoreCase);
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 409: return "Conflict";
                case 422: return "Unprocessable Entity";
                case 500: return "Internal Server Error";
                default: return "Response";
            }
        }
    }

    internal static class JsonText
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        internal static T Parse<T>(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return default(T);
            return Serializer.Deserialize<T>(text);
        }

        internal static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }

        internal static Dictionary<string, object> Object(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();
            var parsed = Serializer.DeserializeObject(text) as Dictionary<string, object>;
            if (parsed == null) throw new InvalidDataException("Expected a JSON object.");
            return parsed;
        }
    }
}

