using Lampac.Engine;
using Lampac.Engine.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Newtonsoft.Json;
using Shared;
using Shared.Engine;
using Shared.Models.Module;
using Shared.Models.Module.Entrys;
using Shared.Models.SQL;
using Shared.PlaywrightCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lampac
{
    public class Startup
    {
        #region Startup
        static IApplicationBuilder _app = null;

        public static bool IsShutdown { get; private set; }

        public IConfiguration Configuration { get; }

        public static IServiceCollection serviceCollection { get; private set; }

        public static IMemoryCache memoryCache { get; private set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        #endregion

        #region ConfigureServices
        public void ConfigureServices(IServiceCollection services)
        {
            var init = AppInit.conf;
            var mods = init.BaseModule;

            serviceCollection = services;

            #region IHttpClientFactory
            services.AddHttpClient("proxy").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                UseCookies = false
            });

            services.AddHttpClient("proxyRedirect").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.None,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                UseCookies = false
            });

            services.AddHttpClient("proxyimg").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.None,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                UseCookies = false
            });

            services.AddHttpClient("base").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                UseCookies = false
            });

            services.AddHttpClient("baseNoRedirect").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                UseCookies = false
            });

            services.AddHttpClient("http2", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.AddHttpClient("http2proxyimg", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.None,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.AddHttpClient("http2NoRedirect", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.AddHttpClient("http3", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.RemoveAll<IHttpMessageHandlerBuilderFilter>();
            #endregion

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            if (init.listen.compression)
            {
                services.AddResponseCompression(options =>
                {
                    options.MimeTypes = AppInit.CompressionMimeTypes;
                });
            }

            services.AddMemoryCache(o =>
            {
                o.TrackStatistics = AppInit.conf.openstat.enable;
            });

            if (mods.ws)
            {
                services.AddSignalR(o =>
                {
                    o.EnableDetailedErrors = true;
                    o.MaximumParallelInvocationsPerClient = 2;
                    o.MaximumReceiveMessageSize = 1024 * 1024 * 10; // 10MB
                    o.StreamBufferCapacity = 1024 * 1024;           // 1MB
                });
            }

            services.AddSingleton<IActionDescriptorChangeProvider>(DynamicActionDescriptorChangeProvider.Instance);
            services.AddSingleton(DynamicActionDescriptorChangeProvider.Instance);

            services.AddDbContextFactory<HybridCacheContext>(HybridCacheContext.ConfiguringDbBuilder);
            services.AddDbContextFactory<ProxyLinkContext>(ProxyLinkContext.ConfiguringDbBuilder);

            if (mods.Sql.syncUser)
                services.AddDbContextFactory<SyncUserContext>(SyncUserContext.ConfiguringDbBuilder);

            if (mods.Sql.sisi)
                services.AddDbContextFactory<SisiContext>(SisiContext.ConfiguringDbBuilder);

            if (mods.Sql.externalids)
                services.AddDbContextFactory<ExternalidsContext>(ExternalidsContext.ConfiguringDbBuilder);

            IMvcBuilder mvcBuilder = services.AddControllersWithViews();

            mvcBuilder.AddJsonOptions(options => {
                //options.JsonSerializerOptions.IgnoreNullValues = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
            });

            #region module references
            string referencesPath = Path.Combine(Environment.CurrentDirectory, "module", "references");

            if (Directory.Exists(referencesPath))
            {
                var current = AppDomain.CurrentDomain.GetAssemblies();
                foreach (string dllFile in Directory.GetFiles(referencesPath, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        string loadedName = Path.GetFileNameWithoutExtension(dllFile);
                        if (current.Any(a => string.Equals(a.GetName().Name, loadedName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        Assembly loadedAssembly = Assembly.LoadFrom(dllFile);
                        mvcBuilder.AddApplicationPart(loadedAssembly);
                        Program.assemblieReferences.Add(MetadataReference.CreateFromFile(loadedAssembly.Location));

                        Console.WriteLine($"load reference: {Path.GetFileName(dllFile)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load reference {dllFile}: {ex.Message}");
                    }
                }
            }
            #endregion

            ModuleRepository.Configuration(mvcBuilder);

            Shared.Startup.Configure(Program.appReload, new NativeWebSocket(), new soks());
            BaseModControllers(mvcBuilder);

            #region compilation modules
            if (AppInit.modules != null)
            {
                // mod.dll
                foreach (var mod in AppInit.modules)
                {
                    try
                    {
                        Console.WriteLine("load module: " + mod.dll);
                        mvcBuilder.AddApplicationPart(mod.assembly);
                    }
                    catch (Exception ex) { Console.WriteLine(ex.Message + "\n"); }
                }
            }

            //  dll  source
            if (File.Exists("module/manifest.json"))
            {
                var jss = new JsonSerializerSettings
                {
                    Error = (se, ev) =>
                    {
                        ev.ErrorContext.Handled = true;
                        Console.WriteLine("module/manifest.json - " + ev.ErrorContext.Error + "\n\n");
                    }
                };

                var modules = JsonConvert.DeserializeObject<List<RootModule>>(File.ReadAllText("module/manifest.json"), jss);
                if (modules == null)
                    return;

                #region CompilationMod
                void CompilationMod(RootModule mod)
                {
                    if (!mod.enable || AppInit.modules.FirstOrDefault(i => i.dll == mod.dll) != null)
                        return;

                    if (mod.dll.EndsWith(".dll"))
                    {
                        try
                        {
                            mod.assembly = Assembly.LoadFrom(mod.dll);

                            AppInit.modules.Add(mod);
                            mvcBuilder.AddApplicationPart(mod.assembly);
                            Console.WriteLine($"load module: {Path.GetFileName(mod.dll)}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to load reference {mod.dll}: {ex.Message}");
                        }

                        return;
                    }

                    string path = Directory.Exists(mod.dll) ? mod.dll : $"{Environment.CurrentDirectory}/module/{mod.dll}";
                    if (Directory.Exists(path))
                    {
                        var syntaxTree = new List<SyntaxTree>();

                        foreach (string file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                        {
                            string _file = file.Replace("\\", "/").Replace(path.Replace("\\", "/"), "").Replace(Environment.CurrentDirectory.Replace("\\", "/"), "");
                            if (Regex.IsMatch(_file, "(\\.vs|bin|obj|Properties)/", RegexOptions.IgnoreCase))
                                continue;

                            syntaxTree.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file)));
                        }

                        if (mod.references != null)
                        {
                            foreach (string refns in mod.references)
                            {
                                string dlrns = Path.Combine(Environment.CurrentDirectory, "module", "references", refns);
                                if (!File.Exists(dlrns))
                                    dlrns = Path.Combine(Environment.CurrentDirectory, "module", mod.dll, refns);

                                if (File.Exists(dlrns) && Program.assemblieReferences.FirstOrDefault(a => Path.GetFileName(a.FilePath) == refns) == null)
                                {
                                    var assembly = Assembly.LoadFrom(dlrns);
                                    Program.assemblieReferences.Add(MetadataReference.CreateFromFile(assembly.Location));
                                }
                            }
                        }

                        CSharpCompilation compilation = CSharpCompilation.Create(Path.GetFileName(mod.dll), syntaxTree, references: Program.assemblieReferences, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                        using (var ms = new MemoryStream())
                        {
                            var result = compilation.Emit(ms);

                            if (!result.Success)
                            {
                                Console.WriteLine($"\ncompilation error: {mod.dll}");
                                foreach (var diagnostic in result.Diagnostics)
                                {
                                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                                        Console.WriteLine(diagnostic);
                                }
                                Console.WriteLine();
                            }
                            else
                            {
                                ms.Seek(0, SeekOrigin.Begin);
                                mod.assembly = Assembly.Load(ms.ToArray());

                                Console.WriteLine("compilation module: " + mod.dll);
                                mod.index = mod.index != 0 ? mod.index : (100 + AppInit.modules.Count);
                                AppInit.modules.Add(mod);
                                mvcBuilder.AddApplicationPart(mod.assembly);
                                WatchersDynamicModule(null, mvcBuilder, mod, path);
                            }
                        }
                    }
                }
                #endregion

                foreach (var mod in modules)
                    CompilationMod(mod);

                foreach (string folderMod in Directory.GetDirectories("module/"))
                {
                    string manifest = $"{Environment.CurrentDirectory}/{folderMod}/manifest.json";
                    if (!File.Exists(manifest))
                        continue;

                    var mod = JsonConvert.DeserializeObject<RootModule>(File.ReadAllText(manifest), jss);
                    if (mod != null)
                    {
                        if (mod.dll == null)
                            mod.dll = folderMod.Split("/")[1];
                        else if (mod.dll.EndsWith(".dll"))
                            mod.dll = Path.Combine(folderMod, mod.dll);

                        CompilationMod(mod);
                    }
                }

                if (Program.assemblieReferences != null)
                    CSharpEval.appReferences = Program.assemblieReferences;
            }

            if (AppInit.modules != null)
                AppInit.modules = AppInit.modules.OrderBy(i => i.index).ToList();

            Console.WriteLine();
            #endregion
        }
        #endregion


        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IMemoryCache memory, IHttpClientFactory httpClientFactory, IHostApplicationLifetime applicationLifetime)
        {
            _app = app;
            memoryCache = memory;
            var init = AppInit.conf;
            var mods = init.BaseModule;
            var midd = mods.Middlewares;

            #region IDbContextFactory
            HybridCacheContext.Factory = app.ApplicationServices.GetService<IDbContextFactory<HybridCacheContext>>();
            ProxyLinkContext.Factory = app.ApplicationServices.GetService<IDbContextFactory<ProxyLinkContext>>();

            if (mods.Sql.externalids)
                ExternalidsContext.Factory = app.ApplicationServices.GetService<IDbContextFactory<ExternalidsContext>>();

            if (mods.Sql.sisi)
                SisiContext.Factory = app.ApplicationServices.GetService<IDbContextFactory<SisiContext>>();

            if (mods.Sql.syncUser)
                SyncUserContext.Factory = app.ApplicationServices.GetService<IDbContextFactory<SyncUserContext>>();
            #endregion

            Shared.Startup.Configure(app, memory);
            HybridCache.Configure(memory);
            HybridFileCache.Configure(memory);
            ProxyManager.Configure(memory);

            Http.httpClientFactory = httpClientFactory;

            if (mods.nws)
            {
                NativeWebSocket.memoryCache = memoryCache;
                Http.nws = new NativeWebSocket();
            }

            if (mods.ws)
                Http.ws = new soks();

            #region modules loaded
            if (AppInit.modules != null)
            {
                foreach (var mod in AppInit.modules)
                {
                    try
                    {
                        if (mod.dll == "DLNA.dll")
                            mod.initspace = "DLNA.ModInit";

                        if (mod.dll == "SISI.dll")
                            mod.initspace = "SISI.ModInit";

                        if (mod.dll == "Tracks.dll" || mod.dll == "TorrServer.dll")
                            mod.version = 2;

                        LoadedModule(app, mod);
                    }
                    catch (Exception ex) { Console.WriteLine($"Module {mod.NamespacePath(mod.initspace)}: {ex.Message}\n\n"); }
                }
            }
            #endregion

            app.UseBaseMod();

            if (!init.multiaccess || init.useDeveloperExceptionPage)
                app.UseDeveloperExceptionPage();

            applicationLifetime.ApplicationStopping.Register(OnShutdown);

            applicationLifetime.ApplicationStarted.Register(() =>
            {
                if (!string.IsNullOrEmpty(init.listen.sock))
                    _ = Bash.Run($"while [ ! -S /var/run/{init.listen.sock}.sock ]; do sleep 1; done && chmod 666 /var/run/{init.listen.sock}.sock").ConfigureAwait(false);
            });

            #region UseForwardedHeaders
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardLimit = null,
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };

            if (init.KnownProxies != null && init.KnownProxies.Count > 0)
            {
                foreach (var k in init.KnownProxies)
                    forwarded.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(k.ip), k.prefixLength));
            }

            app.UseForwardedHeaders(forwarded);
            #endregion

            app.UseModHeaders();
            app.UseRequestInfo();

            if (mods.nws)
            {
                app.Map("/nws", nwsApp =>
                {
                    nwsApp.UseWAF();
                    nwsApp.UseWebSockets();
                    nwsApp.Run(NativeWebSocket.HandleWebSocketAsync);
                });
            }

            if (mods.ws)
            {
                app.Map("/ws", wsApp =>
                {
                    wsApp.UseWAF();
                    wsApp.UseRouting();
                    wsApp.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<soks>("");
                    });
                });
            }

            if (midd.staticache)
                app.UseStaticache();

            app.UseRouting();

            if (init.listen.compression)
                app.UseResponseCompression();

            if (midd.statistics)
                app.UseRequestStatistics();

            app.UseAnonymousRequest();

            app.UseAlwaysRjson();

            if (midd.module)
                app.UseModule(first: true);

            app.UseOverrideResponse(first: true);

            #region UseStaticFiles
            if (midd.staticFiles)
            {
                var contentTypeProvider = new FileExtensionContentTypeProvider();

                if (midd.staticFilesMappings != null)
                {
                    foreach (var mapping in midd.staticFilesMappings)
                        contentTypeProvider.Mappings[mapping.Key] = mapping.Value;
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    ServeUnknownFileTypes = midd.unknownStaticFiles,
                    DefaultContentType = "application/octet-stream",
                    ContentTypeProvider = contentTypeProvider
                });
            }
            #endregion

            app.UseWAF();
            app.UseAccsdb();

            if (midd.proxy)
            {
                app.MapWhen(context => context.Request.Path.Value.StartsWith("/proxy/") || context.Request.Path.Value.StartsWith("/proxy-dash/"), proxyApp =>
                {
                    proxyApp.UseProxyAPI();
                });
            }

            if (midd.proxyimg)
            {
                app.MapWhen(context => context.Request.Path.Value.StartsWith("/proxyimg"), proxyApp =>
                {
                    proxyApp.UseProxyIMG();
                });
            }

            if (midd.proxycub)
            {
                app.MapWhen(context => context.Request.Path.Value.StartsWith("/cub/"), proxyApp =>
                {
                    proxyApp.UseProxyCub();
                });
            }

            if (midd.proxytmdb)
            {
                app.MapWhen(context => context.Request.Path.Value.StartsWith("/tmdb/"), proxyApp =>
                {
                    proxyApp.UseProxyTmdb();
                });
            }

            if (midd.module)
                app.UseModule(first: false);

            app.UseOverrideResponse(first: false);

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }


        #region OnShutdown
        void OnShutdown()
        {
            if (Program._reload)
                return;

            IsShutdown = true;
            Shared.Startup.IsShutdown = true;

            Chromium.FullDispose();
            Firefox.FullDispose();
            NativeWebSocket.FullDispose();
            soks.FullDispose();

            DisposeModule(null);
        }
        #endregion

        #region BaseModControllers
        public static bool WebLogEnableController { get; private set; }

        public void BaseModControllers(IMvcBuilder mvcBuilder)
        {
            if (AppInit.conf?.BaseModule?.EnableControllers == null || 
                AppInit.conf.BaseModule.EnableControllers.Length == 0)
                return;

            WebLogEnableController = AppInit.conf.BaseModule.EnableControllers.Contains("WebLog");

            var syntaxTree = new List<SyntaxTree>();

            string patchcontrol = Path.Combine("basemod", "Controllers");
            if (!Directory.Exists(patchcontrol))
                patchcontrol = "../../../../../BaseModule/Controllers";

            foreach (string file in Directory.GetFiles(patchcontrol, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file).Replace("Controller.cs", "");

                if (name.Equals("Cmd", StringComparison.OrdinalIgnoreCase) && AppInit.conf.cmd.Count == 0)
                    continue;

                if (name.Equals("SyncApi", StringComparison.OrdinalIgnoreCase) && !AppInit.conf.sync.enable)
                    continue;

                if (AppInit.conf.BaseModule.EnableControllers.Contains(name, StringComparer.OrdinalIgnoreCase))
                    syntaxTree.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file)));
            }

            CSharpCompilation compilation = CSharpCompilation.Create("basemod", syntaxTree, references: Program.assemblieReferences, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                if (result.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    var assembly = Assembly.Load(ms.ToArray());
                    mvcBuilder.AddApplicationPart(assembly);
                }
                else
                {
                    Console.WriteLine($"\ncompilation error: basemod");
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        if (diagnostic.Severity == DiagnosticSeverity.Error)
                            Console.WriteLine(diagnostic);
                    }
                    Console.WriteLine();
                }
            }
        }
        #endregion

        #region WatchRebuildModule
        static readonly Dictionary<string, FileSystemWatcher> moduleWatchers = new();

        static readonly object moduleWatcherLock = new object();

        void WatchersDynamicModule(IApplicationBuilder app, IMvcBuilder mvcBuilder, RootModule mod, string path)
        {
            if (!mod.dynamic)
                return;

            path = Path.GetFullPath(path);

            lock (moduleWatcherLock)
            {
                if (moduleWatchers.ContainsKey(path))
                    return;

                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };

                watcher.Filters.Add("*.cs");
                watcher.Filters.Add("manifest.json");

                CancellationTokenSource debounceCts = null;
                object debounceLock = new object();

                void Recompile(object sender, FileSystemEventArgs e)
                {
                    string _file = e.FullPath.Replace("\\", "/").Replace(path.Replace("\\", "/"), "").Replace(Environment.CurrentDirectory.Replace("\\", "/"), "");
                    if (Regex.IsMatch(_file, "(\\.vs|bin|obj|Properties)/", RegexOptions.IgnoreCase))
                        return;

                    CancellationTokenSource cts;

                    lock (debounceLock)
                    {
                        debounceCts?.Cancel();
                        debounceCts = new CancellationTokenSource();
                        cts = debounceCts;
                    }

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

                        if (cts.IsCancellationRequested)
                            return;

                        watcher.EnableRaisingEvents = false;

                        try
                        {
                            var parts = mvcBuilder.PartManager.ApplicationParts
                                .OfType<AssemblyPart>()
                                .Where(p => p.Assembly == mod.assembly)
                                .ToList();

                            #region update manifest.json
                            string manifestPath = Path.Combine(path, "manifest.json");
                            RootModule manifestMod = null;

                            if (File.Exists(manifestPath))
                            {
                                try
                                {
                                    manifestMod = JsonConvert.DeserializeObject<RootModule>(File.ReadAllText(manifestPath));

                                    var excludedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        nameof(RootModule.dynamic),
                                        nameof(RootModule.index),
                                        nameof(RootModule.dll),
                                        nameof(RootModule.assembly),
                                        nameof(RootModule.initspace)
                                    };

                                    foreach (var property in typeof(RootModule).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                                    {
                                        if (!property.CanRead || !property.CanWrite || excludedProperties.Contains(property.Name))
                                            continue;

                                        property.SetValue(mod, property.GetValue(manifestMod));
                                    }
                                }
                                catch (Exception manifestEx)
                                {
                                    Console.WriteLine($"Failed to update manifest for {mod.dll}: {manifestEx.Message}");
                                }
                            }
                            #endregion

                            var assembly = CSharpEval.Compilation(mod);
                            if (assembly != null)
                            {
                                DisposeModule(mod);

                                foreach (var part in parts)
                                    mvcBuilder.PartManager.ApplicationParts.Remove(part);

                                if (manifestMod != null)
                                    mod.initspace = manifestMod.initspace;

                                mod.assembly = assembly;
                                LoadedModule(app, mod);

                                mvcBuilder.PartManager.ApplicationParts.Add(new AssemblyPart(mod.assembly));
                                DynamicActionDescriptorChangeProvider.Instance.NotifyChanges();

                                MiddlewaresModuleEntry.EnsureCache(forced: true);
                                OnlineModuleEntry.EnsureCache(forced: true);
                                SisiModuleEntry.EnsureCache(forced: true);

                                Console.WriteLine("rebuild module: " + mod.dll);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to rebuild module {mod.dll}: {ex.Message}");
                        }
                        finally
                        {
                            watcher.EnableRaisingEvents = true;
                        }
                    });
                }

                watcher.Changed += Recompile;
                watcher.Created += Recompile;
                watcher.Deleted += Recompile;
                watcher.Renamed += Recompile;

                watcher.EnableRaisingEvents = true;
                moduleWatchers[path] = watcher;
            }
        }
        #endregion

        #region LoadedModule
        void LoadedModule(IApplicationBuilder app, RootModule mod)
        {
            if (mod == null)
                return;

            if (mod.initspace != null && mod.assembly.GetType(mod.NamespacePath(mod.initspace)) is Type t && t.GetMethod("loaded") is MethodInfo m)
            {
                if (mod.version >= 2)
                {
                    m.Invoke(null, [
                        new InitspaceModel()
                        {
                            path = $"module/{mod.dll}",
                            soks = new soks(),
                            nws = new NativeWebSocket(),
                            memoryCache = memoryCache,
                            configuration = Configuration,
                            services = serviceCollection,
                            app = app ?? _app
                        }
                    ]);
                }
                else
                    m.Invoke(null, []);
            }
        }
        #endregion

        #region DisposeModule
        void DisposeModule(RootModule module)
        {
            if (AppInit.modules == null)
                return;

            if (module != null)
            {
                try
                {
                    if (module.initspace != null && module.assembly.GetType(module.NamespacePath(module.initspace)) is Type t && t.GetMethod("Dispose") is MethodInfo m)
                        m.Invoke(null, []);
                }
                catch { }
            }
            else
            {
                foreach (var mod in AppInit.modules)
                {
                    try
                    {
                        if (mod.initspace != null && mod.assembly.GetType(mod.NamespacePath(mod.initspace)) is Type t && t.GetMethod("Dispose") is MethodInfo m)
                            m.Invoke(null, []);
                    }
                    catch { }
                }
            }
        }
        #endregion
    }
}
