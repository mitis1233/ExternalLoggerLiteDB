using LiteDB;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;


namespace AvatarLogger
{
    internal class main
    {
        public static ProxyServer ProxyServer;

        public int ListeningPort => ProxyServer.ProxyEndPoints[0].Port;

        public static bool download = false;

        public static void SetupProxy()
        {
            ProxyServer = new ProxyServer();
            var endpoint = new ExplicitProxyEndPoint(IPAddress.Any, 9999, true);
            ProxyServer.AddEndPoint(endpoint);

            ProxyServer.Start();

            foreach (var endPoint in ProxyServer.ProxyEndPoints)
                Console.WriteLine("proxy listening on {0}:{1}", endPoint.IpAddress, endPoint.Port);
            endpoint.BeforeTunnelConnectResponse += ProcessConnect;

            ProxyServer.SetAsSystemHttpProxy(endpoint);
            ProxyServer.SetAsSystemHttpsProxy(endpoint);
        }



        public static void Main(string[] args)
        {
            Console.WriteLine("如果這是您第一次啟動，系統會提示您安裝根證書.");
            Console.WriteLine("安裝它來允許外部記錄器從 vrchat 解密 HTTPS 數據.\n");
            Console.WriteLine($"記錄的Avatar將保存到： \n{System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)}\\AvatarLog.db \n");

            SetupProxy();

            ProxyServer.BeforeRequest += ProcessRequest;
            ProxyServer.BeforeResponse += ProcessResponse;
            ProxyServer.ServerCertificateValidationCallback += ProcessCertValidation;

            Console.WriteLine("\n完成初始化，正在紀錄Avatar... 按Enter後可退出程序");

            Console.Read();

            Console.WriteLine("\n正在解除Proxy代理，關閉中...");
            Cleanup();
        }


        public static async Task ProcessRequest(object sender, SessionEventArgs e)
        {


            string url = e.HttpClient.Request.RequestUri.AbsoluteUri;
            if (!url.Contains("api.vrchat.cloud"))
            {
                return;
            }
        }
        private static readonly Regex fileRegex = new Regex("https://api.vrchat.cloud/api/1/file/file_[0-9a-fA-F]{8}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{12}");




        public static async Task ProcessResponse(object sender, SessionEventArgs e)
        {
            string url = e.HttpClient.Request.RequestUri.AbsoluteUri;
            if (url.Contains("https://api.vrchat.cloud/api/1/file/") && e.HttpClient.Response.StatusCode == 302)
            {
                var fileURL = "";
                foreach (Match match in fileRegex.Matches(url)) fileURL = (match.Value);

                var download_link = e.HttpClient.Response.Headers.Headers["Location"];
                var ext = System.IO.Path.GetExtension(download_link.ToString());
                if (ext == ".vrca")
                {
                    var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.0.0 Safari/537.36");
                    var avatarData = await httpClient.GetStringAsync(fileURL);
                    dynamic avatar = JsonConvert.DeserializeObject(avatarData);

                    string AvatarId = avatar.id;
                    if (string.IsNullOrEmpty(AvatarId))
                    {
                        return;
                    }
                    using (LiteDatabase liteDatabase = new LiteDatabase("AvatarLog.db", null))
                    {
                        ILiteCollection<Customer> collection = liteDatabase.GetCollection<Customer>("Avatar", BsonAutoId.ObjectId);
                        if (collection.Find((Customer x) => x.AvatarId == AvatarId, 0, 2147483647).DefaultIfEmpty(null).Single<Customer>() == null)
                        {
                            string name = avatar.name.ToString();
                            string assetUrl = fileURL;
                            string authorId = avatar.ownerId;



                            Customer entity = new Customer
                            {
                                Name = name,
                                assetUrl = assetUrl,
                                AvatarId = AvatarId,
                                AuthorId = authorId,
                            };
                            collection.Insert(entity);

                            collection.EnsureIndex<string>((Customer x) => x.AvatarId, false);

                        }
                    }
                }

            }
        }
        public class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string assetUrl { get; set; }
            public string AvatarId { get; set; }
            public string AuthorId { get; set; }
        }

        public static Task ProcessCertValidation(object sender, CertificateValidationEventArgs e)
        {
            if (e.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                e.IsValid = true;

            return Task.CompletedTask;
        }

        private static async Task ProcessConnect(object sender, TunnelConnectSessionEventArgs e)
        {
            string hostname = e.HttpClient.Request.RequestUri.Host;

            // this is to allow sites like google, youtube, mega, etc that use cert pinning to prevent MITM attacks.
            // solution does not fully work for browsers like firefox, and i can't see any info about fixing it.
            if (!hostname.Contains("api.vrchat.cloud"))
            {
                e.DecryptSsl = false;
            }
        }

        public static void Cleanup()
        {
            ProxyServer.BeforeRequest -= ProcessRequest;
            ProxyServer.BeforeResponse -= ProcessResponse;
            ProxyServer.RestoreOriginalProxySettings();
            ProxyServer.Stop();
            ProxyServer.Dispose();
        }

    }
}
