using System.Diagnostics;
using System.Net;
using System.Text;

namespace ffxiv_dresser_analyze_client
{
    internal class WebServer(StaticData staticData, DresserData dresserData)
    {
        private readonly byte[] html = File.ReadAllBytes(
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "ffxiv-dresser-analyze.web"));
        
        private HttpListener listener = new();

        public async Task HandleRequests()
        {
            while (true)
            {
                HttpListenerContext ctx = await listener.GetContextAsync();
                var req = ctx.Request;
                var res = ctx.Response;

                // 允许商城用户脚本从 localhost 读取只读数据；实际请求仍只暴露本地游戏状态。
                res.AddHeader("Access-Control-Allow-Origin", "*");
                res.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = 204;
                    res.Close();
                    continue;
                }

                var path = req.Url!.AbsolutePath;
                if (path == "/data/dresser")
                {
                    dresserData.Read();
                    if (req.Headers.Get("If-Modified-Since") == dresserData.LastModified)
                    {
                        res.AddHeader("X-Not-Modified", "Not Modified");
                        res.StatusCode = 304;
                    }
                    else
                    {
                        byte[] resContent;
                        if (dresserData.Loaded)
                        {
                            resContent = dresserData.Json;
                            res.AddHeader("X-Data-Loaded", "true");
                            res.AddHeader("Last-Modified", dresserData.LastModified);
                        }
                        else
                        {
                            resContent = [0x5b, 0x5d];  // "[]"
                            res.AddHeader("X-Data-Loaded", "false");
                        }
                        res.AddHeader("Cache-Control", "max-age=0");
                        res.ContentType = "application/json";
                        res.ContentEncoding = Encoding.UTF8;
                        res.ContentLength64 = resContent.Length;
                        await res.OutputStream.WriteAsync(resContent);
                    }
                }
                else if (path == "/data/cabinet")
                {
                    dresserData.Read();
                    if (dresserData.CabinetLoaded && dresserData.LastCabinetModified != null &&
                        req.Headers.Get("If-Modified-Since") == dresserData.LastCabinetModified)
                    {
                        res.AddHeader("X-Not-Modified", "Not Modified");
                        res.StatusCode = 304;
                        res.Close();
                        continue;
                    }
                    var resContent = dresserData.CabinetJson;
                    res.AddHeader("Cache-Control", "max-age=0");
                    if (dresserData.CabinetLoaded && dresserData.LastCabinetModified != null)
                    {
                        res.AddHeader("Last-Modified", dresserData.LastCabinetModified);
                    }
                    res.ContentType = "application/json";
                    res.ContentEncoding = Encoding.UTF8;
                    res.ContentLength64 = resContent.Length;
                    await res.OutputStream.WriteAsync(resContent);
                }
                else if (path == "/")
                {
                    res.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate");
                    res.ContentType = "text/html";
                    res.ContentEncoding = Encoding.UTF8;
                    res.ContentLength64 = html.Length;
                    await res.OutputStream.WriteAsync(html);
                }
                else if (path.StartsWith("/icon/"))
                {
                    var hq = path.EndsWith("hq");
                    byte[]? pixels = null;
                    if (uint.TryParse(path[6..^(hq ? 2 : 0)], out var id))
                    {
                        pixels = staticData.GetIcon(id, hq);
                    }
                    if (pixels != null)
                    {
                        res.AddHeader("Cache-Control", "max-age=86400");
                        res.ContentType = "image/bmp";
                        res.ContentLength64 = StaticData.BmpHeader.Length + pixels.Length;
                        await res.OutputStream.WriteAsync(StaticData.BmpHeader);
                        await res.OutputStream.WriteAsync(pixels);
                    }
                    else
                    {
                        res.StatusCode = 404;
                    }

                }
                else if (staticData.Jsons.TryGetValue(path, out var json))
                {
                    res.ContentType = "application/json";
                    res.ContentEncoding = Encoding.UTF8;
                    res.ContentLength64 = json.Length;
                    await res.OutputStream.WriteAsync(json);
                }
                else
                {
                    res.StatusCode = 404;
                }
                res.Close();
            }
        }

        public void Run()
        {
            var port = 8014;
            while (true)
            {
                var host = $"http://localhost:{port}/";
                listener.Prefixes.Add(host);
                try
                {
                    listener.Start();
                    Console.WriteLine($"已就绪，请在浏览器中访问 {host}");
#if !DEBUG
                    try
                    {
                        Process.Start(new ProcessStartInfo(host) { UseShellExecute = true });
                    }
                    catch { }
#endif
                    break;
                }
                catch (HttpListenerException ex)
                {
                    if (ex.ErrorCode != 32) throw;
                    Console.Write($"端口 {port} 被占用，请关闭之前运行的本程序或输入一个新的端口号：");
                    var input = Console.ReadLine()!;
                    if (input != "")
                    {
                        port = ushort.Parse(input);
                    }
                    listener = new();
                }
            }

            var task = HandleRequests();
            task.GetAwaiter().GetResult();
            listener.Close();
        }
    }
}
